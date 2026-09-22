using System.Net;
using System.Net.Http.Json;

namespace NovaWallet.Tests;

[Collection(nameof(ApiCollection))]
public class ValidationTests(ApiFixture fixture) : ApiTestBase(fixture)
{
    [Fact]
    public async Task Self_transfer_is_rejected()
    {
        var client = Client;
        var token = await TestHttp.TokenAsync(client, "self-xfer", "customer");
        var wallet = await TestHttp.CreateWalletAsync(client, token);
        await TestHttp.CreditAsync(client, wallet, 1_000);

        var response = await TestHttp.TransferAsync(client, token, wallet, wallet, 100, "self-key");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Transfer_to_missing_wallet_returns_404()
    {
        var client = Client;
        var token = await TestHttp.TokenAsync(client, "missing-dest", "customer");
        var from = await TestHttp.CreateWalletAsync(client, token);
        await TestHttp.CreditAsync(client, from, 1_000);

        var response = await TestHttp.TransferAsync(
            client,
            token,
            from,
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            100,
            "missing-dest-key");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Transfer_from_wallet_you_do_not_own_returns_404()
    {
        var client = Client;
        var owner = await TestHttp.TokenAsync(client, "not-owner-src", "customer");
        var other = await TestHttp.TokenAsync(client, "not-owner-other", "customer");
        var from = await TestHttp.CreateWalletAsync(client, owner);
        var to = await TestHttp.CreateWalletAsync(client, other);
        await TestHttp.CreditAsync(client, from, 1_000);

        var response = await TestHttp.TransferAsync(client, other, from, to, 100, "not-owner-key");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Single_transfer_over_daily_limit_returns_422()
    {
        var client = Client;
        var sender = await TestHttp.TokenAsync(client, "over-limit", "customer");
        var receiver = await TestHttp.TokenAsync(client, "over-limit-b", "customer");
        var from = await TestHttp.CreateWalletAsync(client, sender);
        var to = await TestHttp.CreateWalletAsync(client, receiver);

        // Default daily limit is 50_000_000 kobo; a single larger transfer must fail.
        await TestHttp.CreditAsync(client, from, 60_000_000);
        var response = await TestHttp.TransferAsync(client, sender, from, to, 50_000_001, "over-limit-key");
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Credit_replay_with_same_idempotency_key_does_not_double_credit()
    {
        var client = Client;
        var token = await TestHttp.TokenAsync(client, "credit-idem", "customer");
        var wallet = await TestHttp.CreateWalletAsync(client, token);

        var first = await TestHttp.CreditRawAsync(client, wallet, 500, "nip-ref-1");
        var second = await TestHttp.CreditRawAsync(client, wallet, 500, "nip-ref-1");

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Created, second.StatusCode);
        Assert.Equal(500, await TestHttp.GetBalanceAsync(client, token, wallet));
    }
}
