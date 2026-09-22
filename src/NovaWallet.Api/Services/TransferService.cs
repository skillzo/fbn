using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NovaWallet.Api.Data;
using NovaWallet.Api.Errors;

namespace NovaWallet.Api.Services;

public class TransferService(AppDbContext db, TimeProvider time, IConfiguration config)
{
    public record TransferRequest(Guid FromWalletId, Guid ToWalletId, long AmountKobo);
    public record TransferResult(Guid TransactionId, long FromBalanceKobo, long ToBalanceKobo, bool Cached = false);

    private sealed class BalanceRow
    {
        public long Balance { get; set; }
    }

    private long DailyLimitKobo => config.GetValue("Transfers:DailyLimitKobo", 50_000_000L);

    public async Task<TransferResult> TransferAsync(
        TransferRequest request,
        string customerId,
        string idempotencyKey,
        string actor,
        CancellationToken ct)
    {
        if (request.AmountKobo <= 0)
            throw new AppException(400, "invalid_amount", "amountKobo must be greater than zero");

        if (request.FromWalletId == request.ToWalletId)
            throw new AppException(400, "invalid_transfer", "from and to wallet must differ");

        var from = await db.Wallets.AsNoTracking()
            .FirstOrDefaultAsync(w => w.Id == request.FromWalletId, ct);
        if (from is null || from.CustomerId != customerId)
            throw new AppException(404, "wallet_not_found", "Source wallet not found");

        var toExists = await db.Wallets.AsNoTracking()
            .AnyAsync(w => w.Id == request.ToWalletId, ct);
        if (!toExists)
            throw new AppException(404, "wallet_not_found", "Destination wallet not found");

        var requestHash = HashRequest(request);

        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                return await RunTransferAsync(request, customerId, idempotencyKey, requestHash, actor, ct);
            }
            catch (Exception ex) when (IsTransient(ex))
            {
                if (attempt == 2)
                    throw;
            }
        }

        throw new InvalidOperationException("unreachable");
    }

    private async Task<TransferResult> RunTransferAsync(
        TransferRequest request,
        string customerId,
        string idempotencyKey,
        string requestHash,
        string actor,
        CancellationToken ct)
    {
        await using var dbTx = await db.Database.BeginTransactionAsync(ct);

        if (!await TryClaimIdempotencyKeyAsync(customerId, idempotencyKey, requestHash, ct))
        {
            await dbTx.RollbackAsync(ct);
            db.ChangeTracker.Clear();
            return await WaitForCachedResponseAsync(customerId, idempotencyKey, requestHash, ct);
        }

        await ApplyDailyLimitAsync(request.FromWalletId, request.AmountKobo, ct);

        long fromBalanceAfter;
        long toBalanceAfter;

        if (request.FromWalletId.CompareTo(request.ToWalletId) < 0)
        {
            fromBalanceAfter = await DebitAsync(request.FromWalletId, request.AmountKobo, ct);
            toBalanceAfter = await CreditAsync(request.ToWalletId, request.AmountKobo, ct);
        }
        else
        {
            toBalanceAfter = await CreditAsync(request.ToWalletId, request.AmountKobo, ct);
            fromBalanceAfter = await DebitAsync(request.FromWalletId, request.AmountKobo, ct);
        }

        var transactionId = Guid.NewGuid();
        var now = time.GetUtcNow();
        var fromBefore = fromBalanceAfter + request.AmountKobo;
        var toBefore = toBalanceAfter - request.AmountKobo;

        db.Transactions.Add(new WalletTransaction
        {
            Id = transactionId,
            Type = "TRANSFER",
            FromWalletId = request.FromWalletId,
            ToWalletId = request.ToWalletId,
            Amount = request.AmountKobo,
            CreatedAt = now,
        });

        db.AuditLog.Add(new AuditLogEntry
        {
            WalletId = request.FromWalletId,
            TransactionId = transactionId,
            Action = "DEBIT",
            Amount = request.AmountKobo,
            BalanceBefore = fromBefore,
            BalanceAfter = fromBalanceAfter,
            Actor = actor,
            CreatedAt = now,
        });

        db.AuditLog.Add(new AuditLogEntry
        {
            WalletId = request.ToWalletId,
            TransactionId = transactionId,
            Action = "CREDIT",
            Amount = request.AmountKobo,
            BalanceBefore = toBefore,
            BalanceAfter = toBalanceAfter,
            Actor = actor,
            CreatedAt = now,
        });

        db.Outbox.Add(new OutboxMessage
        {
            Id = Guid.NewGuid(),
            Type = "TransferCompleted",
            Payload = JsonSerializer.Serialize(new
            {
                eventId = transactionId,
                transactionId,
                fromWalletId = request.FromWalletId,
                toWalletId = request.ToWalletId,
                amountKobo = request.AmountKobo,
                completedAt = now,
            }),
            CreatedAt = now,
        });

        var result = new TransferResult(transactionId, fromBalanceAfter, toBalanceAfter);
        await SetIdempotencyResponseAsync(customerId, idempotencyKey, result, ct);

        await db.SaveChangesAsync(ct);
        await dbTx.CommitAsync(ct);

        return result;
    }

    private async Task<bool> TryClaimIdempotencyKeyAsync(
        string customerId,
        string key,
        string requestHash,
        CancellationToken ct)
    {
        db.IdempotencyKeys.Add(new IdempotencyKey
        {
            CustomerId = customerId,
            Key = key,
            RequestHash = requestHash,
            CreatedAt = time.GetUtcNow(),
        });

        try
        {
            await db.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateException ex) when (IsUniqueIdempotency(ex))
        {
            db.ChangeTracker.Clear();
            return false;
        }
    }

    private async Task<TransferResult> WaitForCachedResponseAsync(
        string customerId,
        string key,
        string requestHash,
        CancellationToken ct)
    {
        for (var i = 0; i < 60; i++)
        {
            var row = await db.IdempotencyKeys.AsNoTracking()
                .FirstOrDefaultAsync(x => x.CustomerId == customerId && x.Key == key, ct);

            if (row is null)
                throw new AppException(409, "idempotency_in_progress", "Transfer is still processing");

            if (row.RequestHash != requestHash)
                throw new AppException(422, "idempotency_conflict", "Idempotency-Key was used with a different request");

            if (row.ResponseBody is not null)
                return DeserializeResult(row.ResponseBody).WithCached();

            await Task.Delay(50, ct);
        }

        throw new AppException(409, "idempotency_in_progress", "Transfer is still processing");
    }

    private async Task SetIdempotencyResponseAsync(
        string customerId,
        string key,
        TransferResult result,
        CancellationToken ct)
    {
        var row = await db.IdempotencyKeys
            .FirstAsync(x => x.CustomerId == customerId && x.Key == key, ct);
        row.ResponseBody = JsonSerializer.Serialize(result);
    }

    private async Task ApplyDailyLimitAsync(Guid walletId, long amountKobo, CancellationToken ct)
    {
        var limit = DailyLimitKobo;
        if (amountKobo > limit)
            throw new AppException(422, "daily_limit_exceeded", "Transfer exceeds daily outbound limit");

        var day = WatDay(time.GetUtcNow());
        var rows = await db.Database.SqlQuery<BalanceRow>($"""
            INSERT INTO daily_limits (wallet_id, day, total)
            VALUES ({walletId}, {day}, {amountKobo})
            ON CONFLICT (wallet_id, day) DO UPDATE
            SET total = daily_limits.total + {amountKobo}
            WHERE daily_limits.total + {amountKobo} <= {limit}
            RETURNING total AS "Balance"
            """).ToListAsync(ct);

        if (rows.Count == 0)
            throw new AppException(422, "daily_limit_exceeded", "Transfer exceeds daily outbound limit");
    }

    private static DateOnly WatDay(DateTimeOffset utcNow) =>
        DateOnly.FromDateTime(utcNow.AddHours(1).UtcDateTime);

    private static string HashRequest(TransferRequest request)
    {
        var payload = $"{request.FromWalletId}|{request.ToWalletId}|{request.AmountKobo}";
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static TransferResult DeserializeResult(string json)
    {
        var result = JsonSerializer.Deserialize<TransferResult>(json);
        return result ?? throw new InvalidOperationException("Invalid idempotency payload");
    }

    private async Task<long> DebitAsync(Guid walletId, long amountKobo, CancellationToken ct)
    {
        var rows = await db.Database.SqlQuery<BalanceRow>($"""
            UPDATE wallets
            SET balance = balance - {amountKobo}
            WHERE id = {walletId} AND balance >= {amountKobo}
            RETURNING balance AS "Balance"
            """).ToListAsync(ct);

        if (rows.Count == 0)
            throw new AppException(422, "insufficient_funds", "Insufficient balance");

        return rows[0].Balance;
    }

    private async Task<long> CreditAsync(Guid walletId, long amountKobo, CancellationToken ct)
    {
        var rows = await db.Database.SqlQuery<BalanceRow>($"""
            UPDATE wallets
            SET balance = balance + {amountKobo}
            WHERE id = {walletId}
            RETURNING balance AS "Balance"
            """).ToListAsync(ct);

        if (rows.Count == 0)
            throw new AppException(404, "wallet_not_found", "Destination wallet not found");

        return rows[0].Balance;
    }

    private static bool IsUniqueIdempotency(DbUpdateException ex) =>
        ex.InnerException is PostgresException pg && pg.SqlState == PostgresErrorCodes.UniqueViolation;

    private static bool IsTransient(Exception ex) =>
        ex switch
        {
            PostgresException pg => pg.SqlState is PostgresErrorCodes.DeadlockDetected
                or PostgresErrorCodes.SerializationFailure,
            _ => ex.InnerException is not null && IsTransient(ex.InnerException),
        };
}

file static class TransferResultExtensions
{
    public static TransferService.TransferResult WithCached(this TransferService.TransferResult r) =>
        r with { Cached = true };
}
