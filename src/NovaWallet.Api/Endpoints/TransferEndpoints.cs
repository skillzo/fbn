using Microsoft.AspNetCore.Mvc;
using NovaWallet.Api.Services;
using NovaWallet.Api.Auth;
using NovaWallet.Api.Errors;

namespace NovaWallet.Api.Endpoints;

public static class TransferEndpoints
{
    public record TransferBody(Guid FromWalletId, Guid ToWalletId, long AmountKobo);

    public static RouteGroupBuilder MapTransferEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/transfers").WithTags("Transfers");

        group.MapPost("/", async (
            TransferBody body,
            [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
            TransferService transfers,
            HttpContext http,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(idempotencyKey))
                throw new AppException(400, "idempotency_key_required", "Idempotency-Key header is required");

            if (idempotencyKey.Length > 100)
                throw new AppException(400, "idempotency_key_invalid", "Idempotency-Key must be at most 100 characters");

            var customerId = http.User.GetCustomerId();
            var request = new TransferService.TransferRequest(body.FromWalletId, body.ToWalletId, body.AmountKobo);
            var result = await transfers.TransferAsync(request, customerId, idempotencyKey, customerId, ct);
            return Results.Json(result, statusCode: StatusCodes.Status201Created);
        })
        .RequireRateLimiting("transfers");

        return group;
    }
}
