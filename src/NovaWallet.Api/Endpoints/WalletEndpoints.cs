using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using NovaWallet.Api.Auth;
using NovaWallet.Api.Errors;
using NovaWallet.Api.Services;

namespace NovaWallet.Api.Endpoints;

public static class WalletEndpoints
{
    public record CreditRequest(long AmountKobo);

    public static RouteGroupBuilder MapWalletEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/wallets").WithTags("Wallets");

        group.MapPost("/", async (
            WalletService wallets,
            ClaimsPrincipal user,
            CancellationToken ct) =>
        {
            var result = await wallets.CreateWalletAsync(user.GetCustomerId(), ct);
            return Results.Created($"/wallets/{result.Id}/balance", result);
        });

        group.MapGet("/{id:guid}/transactions", async (
            Guid id,
            int page,
            int pageSize,
            WalletService wallets,
            ClaimsPrincipal user,
            CancellationToken ct) =>
        {
            page = page == 0 ? 1 : page;
            pageSize = pageSize == 0 ? 20 : pageSize;
            var result = await wallets.GetStatementAsync(id, user.GetCustomerId(), page, pageSize, ct);
            return Results.Ok(result);
        });

        group.MapGet("/{id:guid}/balance", async (
            Guid id,
            WalletService wallets,
            ClaimsPrincipal user,
            CancellationToken ct) =>
        {
            var result = await wallets.GetBalanceAsync(id, user.GetCustomerId(), ct);
            return Results.Ok(result);
        });

        group.MapPost("/{id:guid}/credit", async (
            Guid id,
            CreditRequest body,
            [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
            WalletService wallets,
            ClaimsPrincipal user,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(idempotencyKey))
                throw new AppException(400, "idempotency_key_required", "Idempotency-Key header is required");

            if (idempotencyKey.Length > 100)
                throw new AppException(400, "idempotency_key_invalid", "Idempotency-Key must be at most 100 characters");

            var actor = user.GetCustomerId();
            var result = await wallets.CreditAsync(id, body.AmountKobo, idempotencyKey, actor, ct);
            return Results.Json(result, statusCode: StatusCodes.Status201Created);
        })
        .RequireAuthorization(AuthExtensions.SystemOnlyPolicy);

        return group;
    }
}
