using System.Net;
using System.Net.Http.Json;

namespace NovaWallet.Tests;

[Collection(nameof(ApiCollection))]
public class IdempotencyTests(ApiFixture fixture) : ApiTestBase(fixture)
{
    [Fact]
    public async Task Same_idempotency_key_parallel_only_debits_once()
    {
        var client = Client;
        var senderToken = await TestHttp.TokenAsync(client, "idem-sender", "customer");
        var receiverToken = await TestHttp.TokenAsync(client, "idem-receiver", "customer");

        var from = await TestHttp.CreateWalletAsync(client, senderToken);
        var to = await TestHttp.CreateWalletAsync(client, receiverToken);
        await TestHttp.CreditAsync(client, from, 1_000);

        var tasks = Enumerable.Range(0, 10)
            .Select(_ => TestHttp.TransferAsync(client, senderToken, from, to, 100, "same-key"))
            .ToArray();

        var responses = await Task.WhenAll(tasks);
        Assert.All(responses, r => Assert.Equal(HttpStatusCode.Created, r.StatusCode));

        var bodies = await Task.WhenAll(responses.Select(r => r.Content.ReadFromJsonAsync<TransferResult>()));
        Assert.All(bodies, b => Assert.Equal(bodies[0]!.TransactionId, b!.TransactionId));
        Assert.Equal(900, await TestHttp.GetBalanceAsync(client, senderToken, from));
        Assert.Equal(100, await TestHttp.GetBalanceAsync(client, receiverToken, to));
    }

    [Fact]
    public async Task Reused_key_with_different_body_returns_422()
    {
        var client = Client;
        var senderToken = await TestHttp.TokenAsync(client, "idem-reuse", "customer");
        var receiverToken = await TestHttp.TokenAsync(client, "idem-reuse-b", "customer");

        var from = await TestHttp.CreateWalletAsync(client, senderToken);
        var to = await TestHttp.CreateWalletAsync(client, receiverToken);
        await TestHttp.CreditAsync(client, from, 1_000);

        var first = await TestHttp.TransferAsync(client, senderToken, from, to, 100, "reuse-key");
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        var second = await TestHttp.TransferAsync(client, senderToken, from, to, 200, "reuse-key");
        Assert.Equal(HttpStatusCode.UnprocessableEntity, second.StatusCode);
    }

    private record TransferResult(Guid TransactionId, long FromBalanceKobo, long ToBalanceKobo, bool Cached);
}
