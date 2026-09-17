using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NovaWallet.Api.Data;
using System.Net;

namespace NovaWallet.Tests;

[Collection(nameof(ApiCollection))]
public class OutboxTests(ApiFixture fixture) : ApiTestBase(fixture)
{
    [Fact]
    public async Task Successful_transfer_writes_one_outbox_row()
    {
        var client = Client;
        var senderToken = await TestHttp.TokenAsync(client, "outbox-sender", "customer");
        var receiverToken = await TestHttp.TokenAsync(client, "outbox-receiver", "customer");

        var from = await TestHttp.CreateWalletAsync(client, senderToken);
        var to = await TestHttp.CreateWalletAsync(client, receiverToken);
        await TestHttp.CreditAsync(client, from, 500);

        var response = await TestHttp.TransferAsync(client, senderToken, from, to, 100, "outbox-ok");
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var rows = await db.Outbox.Where(o => o.Type == "TransferCompleted").ToListAsync();
        Assert.Contains(rows, r => r.Payload.Contains(from.ToString(), StringComparison.Ordinal));
    }

    [Fact]
    public async Task Failed_transfer_writes_no_outbox_row()
    {
        var client = Client;
        var senderToken = await TestHttp.TokenAsync(client, "outbox-fail", "customer");
        var receiverToken = await TestHttp.TokenAsync(client, "outbox-fail-b", "customer");

        var from = await TestHttp.CreateWalletAsync(client, senderToken);
        var to = await TestHttp.CreateWalletAsync(client, receiverToken);

        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var before = await db.Outbox.CountAsync();

        var response = await TestHttp.TransferAsync(client, senderToken, from, to, 100, "outbox-fail-key");
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal(before, await db.Outbox.CountAsync());
    }

    [Fact]
    public async Task Publisher_marks_outbox_row_processed()
    {
        var client = Client;
        var senderToken = await TestHttp.TokenAsync(client, "outbox-pub", "customer");
        var receiverToken = await TestHttp.TokenAsync(client, "outbox-pub-b", "customer");

        var from = await TestHttp.CreateWalletAsync(client, senderToken);
        var to = await TestHttp.CreateWalletAsync(client, receiverToken);
        await TestHttp.CreditAsync(client, from, 200);

        var response = await TestHttp.TransferAsync(client, senderToken, from, to, 50, "outbox-pub-key");
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        await Task.Delay(TimeSpan.FromSeconds(6));

        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var row = await db.Outbox.OrderByDescending(o => o.CreatedAt).FirstAsync();
        Assert.NotNull(row.ProcessedAt);
    }
}
