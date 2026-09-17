using System.Net;
using System.Net.Http.Json;

namespace NovaWallet.Tests;

[Collection(nameof(ApiCollection))]
public class AuthTests(ApiFixture fixture) : ApiTestBase(fixture)
{
    [Fact]
    public async Task Missing_token_returns_401()
    {
        var response = await Client.GetAsync("/wallets/00000000-0000-0000-0000-000000000001/balance");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Customer_cannot_credit_wallet()
    {
        var client = Client;
        var token = await TestHttp.TokenAsync(client, "auth-cust", "customer");
        var walletId = await TestHttp.CreateWalletAsync(client, token);

        using var request = new HttpRequestMessage(HttpMethod.Post, $"/wallets/{walletId}/credit")
        {
            Content = JsonContent.Create(new { amountKobo = 100 }),
        };
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Other_users_wallet_returns_404_on_balance()
    {
        var client = Client;
        var owner = await TestHttp.TokenAsync(client, "auth-owner", "customer");
        var other = await TestHttp.TokenAsync(client, "auth-other", "customer");
        var walletId = await TestHttp.CreateWalletAsync(client, owner);

        using var request = new HttpRequestMessage(HttpMethod.Get, $"/wallets/{walletId}/balance");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", other);

        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
