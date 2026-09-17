using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NovaWallet.Api.Data;

namespace NovaWallet.Tests;

[Collection(nameof(ApiCollection))]
public class AuditTests(ApiFixture fixture) : ApiTestBase(fixture)
{
    [Fact]
    public async Task Audit_log_rejects_updates()
    {
        var client = Client;
        var token = await TestHttp.TokenAsync(client, "audit-user", "customer");
        var walletId = await TestHttp.CreateWalletAsync(client, token);
        await TestHttp.CreditAsync(client, walletId, 100);

        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var entry = await db.AuditLog.FirstAsync();

        entry.Action = "TAMPER";
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }
}
