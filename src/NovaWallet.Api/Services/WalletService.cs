using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NovaWallet.Api.Data;
using NovaWallet.Api.Errors;

namespace NovaWallet.Api.Services;

public class WalletService(AppDbContext db, TimeProvider time)
{
    public record CreateWalletResult(Guid Id, string CustomerId, string Currency);
    public record BalanceResult(long BalanceKobo, string Currency);
    public record CreditResult(Guid TransactionId, long BalanceKobo, bool Cached = false);

    private sealed class BalanceRow
    {
        public long Balance { get; set; }
    }

    public record StatementItem(
        Guid Id,
        string Type,
        Guid? FromWalletId,
        Guid ToWalletId,
        long AmountKobo,
        DateTimeOffset CreatedAt);
    public record StatementResult(IReadOnlyList<StatementItem> Items, int Page, int PageSize, int TotalCount);

    public async Task<CreateWalletResult> CreateWalletAsync(string customerId, CancellationToken ct)
    {
        if (await db.Wallets.AnyAsync(w => w.CustomerId == customerId, ct))
            throw new AppException(409, "wallet_exists", "Customer already has a wallet");

        var wallet = new Wallet
        {
            Id = Guid.NewGuid(),
            CustomerId = customerId,
            Balance = 0,
            Currency = "NGN",
            CreatedAt = time.GetUtcNow(),
        };

        db.Wallets.Add(wallet);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            throw new AppException(409, "wallet_exists", "Customer already has a wallet");
        }

        return new CreateWalletResult(wallet.Id, wallet.CustomerId, wallet.Currency);
    }

    public async Task<BalanceResult> GetBalanceAsync(Guid walletId, string customerId, CancellationToken ct)
    {
        var wallet = await db.Wallets.AsNoTracking()
            .FirstOrDefaultAsync(w => w.Id == walletId, ct);

        if (wallet is null || wallet.CustomerId != customerId)
            throw new AppException(404, "wallet_not_found", "Wallet not found");

        return new BalanceResult(wallet.Balance, wallet.Currency);
    }

    public async Task<StatementResult> GetStatementAsync(
        Guid walletId,
        string customerId,
        int page,
        int pageSize,
        CancellationToken ct)
    {
        if (page < 1)
            throw new AppException(400, "invalid_page", "page must be at least 1");

        if (pageSize < 1 || pageSize > 100)
            throw new AppException(400, "invalid_page_size", "pageSize must be between 1 and 100");

        var owned = await db.Wallets.AsNoTracking()
            .AnyAsync(w => w.Id == walletId && w.CustomerId == customerId, ct);
        if (!owned)
            throw new AppException(404, "wallet_not_found", "Wallet not found");

        var query = db.Transactions.AsNoTracking()
            .Where(t => t.FromWalletId == walletId || t.ToWalletId == walletId)
            .OrderByDescending(t => t.CreatedAt);

        var total = await query.CountAsync(ct);
        var rows = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(t => new StatementItem(
                t.Id,
                t.Type,
                t.FromWalletId,
                t.ToWalletId,
                t.Amount,
                t.CreatedAt))
            .ToListAsync(ct);

        return new StatementResult(rows, page, pageSize, total);
    }

    public async Task<CreditResult> CreditAsync(
        Guid walletId,
        long amountKobo,
        string idempotencyKey,
        string actor,
        CancellationToken ct)
    {
        if (amountKobo <= 0)
            throw new AppException(400, "invalid_amount", "amountKobo must be greater than zero");

        // Scope system credits by actor so NIP retries share the same key space.
        var scope = $"system:{actor}";
        var requestHash = HashCredit(walletId, amountKobo);

        await using var dbTx = await db.Database.BeginTransactionAsync(ct);

        if (!await TryClaimIdempotencyKeyAsync(scope, idempotencyKey, requestHash, ct))
        {
            await dbTx.RollbackAsync(ct);
            db.ChangeTracker.Clear();
            return await WaitForCachedCreditAsync(scope, idempotencyKey, requestHash, ct);
        }

        var rows = await db.Database.SqlQuery<BalanceRow>($"""
            UPDATE wallets
            SET balance = balance + {amountKobo}
            WHERE id = {walletId}
            RETURNING balance AS "Balance"
            """).ToListAsync(ct);

        if (rows.Count == 0)
            throw new AppException(404, "wallet_not_found", "Wallet not found");

        var balanceAfter = rows[0].Balance;
        var balanceBefore = balanceAfter - amountKobo;
        var transactionId = Guid.NewGuid();
        var now = time.GetUtcNow();

        db.Transactions.Add(new WalletTransaction
        {
            Id = transactionId,
            Type = "CREDIT",
            FromWalletId = null,
            ToWalletId = walletId,
            Amount = amountKobo,
            CreatedAt = now,
        });

        db.AuditLog.Add(new AuditLogEntry
        {
            WalletId = walletId,
            TransactionId = transactionId,
            Action = "CREDIT",
            Amount = amountKobo,
            BalanceBefore = balanceBefore,
            BalanceAfter = balanceAfter,
            Actor = actor,
            CreatedAt = now,
        });

        var result = new CreditResult(transactionId, balanceAfter);
        var keyRow = await db.IdempotencyKeys
            .FirstAsync(x => x.CustomerId == scope && x.Key == idempotencyKey, ct);
        keyRow.ResponseBody = JsonSerializer.Serialize(result);

        await db.SaveChangesAsync(ct);
        await dbTx.CommitAsync(ct);

        return result;
    }

    private async Task<bool> TryClaimIdempotencyKeyAsync(
        string scope,
        string key,
        string requestHash,
        CancellationToken ct)
    {
        db.IdempotencyKeys.Add(new IdempotencyKey
        {
            CustomerId = scope,
            Key = key,
            RequestHash = requestHash,
            CreatedAt = time.GetUtcNow(),
        });

        try
        {
            await db.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            db.ChangeTracker.Clear();
            return false;
        }
    }

    private async Task<CreditResult> WaitForCachedCreditAsync(
        string scope,
        string key,
        string requestHash,
        CancellationToken ct)
    {
        for (var i = 0; i < 60; i++)
        {
            var row = await db.IdempotencyKeys.AsNoTracking()
                .FirstOrDefaultAsync(x => x.CustomerId == scope && x.Key == key, ct);

            if (row is null)
                throw new AppException(409, "idempotency_in_progress", "Credit is still processing");

            if (row.RequestHash != requestHash)
                throw new AppException(422, "idempotency_conflict", "Idempotency-Key was used with a different request");

            if (row.ResponseBody is not null)
            {
                var cached = JsonSerializer.Deserialize<CreditResult>(row.ResponseBody)
                    ?? throw new InvalidOperationException("Invalid idempotency payload");
                return cached with { Cached = true };
            }

            await Task.Delay(50, ct);
        }

        throw new AppException(409, "idempotency_in_progress", "Credit is still processing");
    }

    private static string HashCredit(Guid walletId, long amountKobo)
    {
        var payload = $"{walletId}|{amountKobo}";
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is PostgresException pg && pg.SqlState == PostgresErrorCodes.UniqueViolation;
}
