using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace NovaWallet.Tests;

[Collection(nameof(ApiCollection))]
public class DailyLimitTests(ApiFixture fixture) : ApiTestBase(fixture)
{
    [Fact]
    public async Task Daily_limit_resets_after_wat_midnight()
    {
        var start = new DateTimeOffset(2025, 1, 1, 21, 0, 0, TimeSpan.Zero);
        var time = new FakeTimeProvider(start);

        using var factory = fixture.CreateFactory(
            services =>
            {
                services.RemoveAll(typeof(TimeProvider));
                services.AddSingleton<TimeProvider>(time);
            },
            settings => settings["Transfers:DailyLimitKobo"] = "5000");

        var client = factory.CreateClient();
        var senderToken = await TestHttp.TokenAsync(client, "limit-sender", "customer");
        var receiverToken = await TestHttp.TokenAsync(client, "limit-receiver", "customer");

        var from = await TestHttp.CreateWalletAsync(client, senderToken);
        var to = await TestHttp.CreateWalletAsync(client, receiverToken);
        await TestHttp.CreditAsync(client, from, 20_000);

        var ok = await TestHttp.TransferAsync(client, senderToken, from, to, 3_000, "limit-1");
        Assert.Equal(HttpStatusCode.Created, ok.StatusCode);

        var blocked = await TestHttp.TransferAsync(client, senderToken, from, to, 3_000, "limit-2");
        Assert.Equal(HttpStatusCode.UnprocessableEntity, blocked.StatusCode);

        time.SetUtcNow(new DateTimeOffset(2025, 1, 1, 23, 0, 0, TimeSpan.Zero));

        var afterMidnight = await TestHttp.TransferAsync(client, senderToken, from, to, 3_000, "limit-3");
        Assert.Equal(HttpStatusCode.Created, afterMidnight.StatusCode);
    }
}
