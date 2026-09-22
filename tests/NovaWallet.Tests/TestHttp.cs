using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace NovaWallet.Tests;

internal static class TestHttp
{
    private record DevTokenResponse(string Token, DateTimeOffset ExpiresAt);
    private record WalletResponse(Guid Id, string CustomerId, string Currency);
    private record BalanceResponse(long BalanceKobo, string Currency);

    public static async Task<string> TokenAsync(HttpClient client, string sub, string role)
    {
        var response = await client.PostAsJsonAsync("/dev/token", new { sub, role });
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<DevTokenResponse>();
        return body!.Token;
    }

    public static async Task<Guid> CreateWalletAsync(HttpClient client, string token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/wallets");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<WalletResponse>();
        return body!.Id;
    }

    public static async Task CreditAsync(
        HttpClient client,
        Guid walletId,
        long amountKobo,
        string? idempotencyKey = null)
    {
        var sys = await TokenAsync(client, "system", "system");
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/wallets/{walletId}/credit")
        {
            Content = JsonContent.Create(new { amountKobo }),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", sys);
        request.Headers.Add("Idempotency-Key", idempotencyKey ?? Guid.NewGuid().ToString("N"));
        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
    }

    public static async Task<HttpResponseMessage> CreditRawAsync(
        HttpClient client,
        Guid walletId,
        long amountKobo,
        string idempotencyKey,
        string? systemToken = null)
    {
        var sys = systemToken ?? await TokenAsync(client, "system", "system");
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/wallets/{walletId}/credit")
        {
            Content = JsonContent.Create(new { amountKobo }),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", sys);
        request.Headers.Add("Idempotency-Key", idempotencyKey);
        return await client.SendAsync(request);
    }

    public static async Task<long> GetBalanceAsync(HttpClient client, string token, Guid walletId)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/wallets/{walletId}/balance");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<BalanceResponse>();
        return body!.BalanceKobo;
    }

    public static async Task<HttpResponseMessage> TransferAsync(
        HttpClient client,
        string token,
        Guid from,
        Guid to,
        long amountKobo,
        string idempotencyKey)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/transfers")
        {
            Content = JsonContent.Create(new { fromWalletId = from, toWalletId = to, amountKobo }),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Add("Idempotency-Key", idempotencyKey);
        return await client.SendAsync(request);
    }
}
