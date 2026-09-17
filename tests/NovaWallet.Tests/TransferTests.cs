using System.Diagnostics;

namespace NovaWallet.Tests;

[Collection(nameof(ApiCollection))]
public class TransferTests(ApiFixture fixture) : ApiTestBase(fixture)
{
    [Fact]
    public async Task Concurrent_transfers_only_spend_available_balance()
    {
        const long funded = 50_000;
        const int parallel = 1000;
        const long amountEach = 100;

        var client = Client;
        var senderToken = await TestHttp.TokenAsync(client, "sender", "customer");
        var receiverToken = await TestHttp.TokenAsync(client, "receiver", "customer");

        var from = await TestHttp.CreateWalletAsync(client, senderToken);
        var to = await TestHttp.CreateWalletAsync(client, receiverToken);
        await TestHttp.CreditAsync(client, from, funded);

        var sw = Stopwatch.StartNew();
        var tasks = Enumerable.Range(0, parallel)
            .Select(i => TestHttp.TransferAsync(client, senderToken, from, to, amountEach, $"key-{i}"))
            .ToArray();

        var responses = await Task.WhenAll(tasks);
        sw.Stop();

        var success = responses.Count(r => r.StatusCode == System.Net.HttpStatusCode.Created);
        var rejected = responses.Count(r => (int)r.StatusCode == 422);
        var sourceBal = await TestHttp.GetBalanceAsync(client, senderToken, from);
        var destBal = await TestHttp.GetBalanceAsync(client, receiverToken, to);

        var summary = $"""
            --- Concurrent transfer load ---
            Funded:        {funded:N0} kobo
            Parallel:      {parallel:N0}
            Amount each:   {amountEach:N0}
            Succeeded:     {success:N0}
            Rejected 422:  {rejected:N0}
            Source bal:    {sourceBal:N0}
            Dest bal:      {destBal:N0}
            Elapsed:       {sw.Elapsed.TotalSeconds:0.00}s
            --------------------------------
            """;

        Console.WriteLine(summary);

        var outDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        await File.WriteAllTextAsync(Path.Combine(outDir, "load-summary.txt"), summary);
        await File.WriteAllTextAsync(Path.Combine(AppContext.BaseDirectory, "load-summary.txt"), summary);

        Assert.Equal(500, success);
        Assert.Equal(500, rejected);
        Assert.Equal(0, sourceBal);
        Assert.Equal(funded, destBal);
    }

    [Fact]
    public async Task Opposing_transfers_do_not_corrupt_totals()
    {
        var client = Client;
        var aToken = await TestHttp.TokenAsync(client, "opp-a", "customer");
        var bToken = await TestHttp.TokenAsync(client, "opp-b", "customer");

        var walletA = await TestHttp.CreateWalletAsync(client, aToken);
        var walletB = await TestHttp.CreateWalletAsync(client, bToken);
        await TestHttp.CreditAsync(client, walletA, 10_000);
        await TestHttp.CreditAsync(client, walletB, 10_000);

        var tasks = new List<Task<HttpResponseMessage>>();
        for (var i = 0; i < 50; i++)
        {
            tasks.Add(TestHttp.TransferAsync(client, aToken, walletA, walletB, 100, $"ab-{i}"));
            tasks.Add(TestHttp.TransferAsync(client, bToken, walletB, walletA, 100, $"ba-{i}"));
        }

        var responses = await Task.WhenAll(tasks);
        Assert.DoesNotContain(responses, r => (int)r.StatusCode >= 500);

        var total = await TestHttp.GetBalanceAsync(client, aToken, walletA)
                    + await TestHttp.GetBalanceAsync(client, bToken, walletB);
        Assert.Equal(20_000, total);
    }
}
