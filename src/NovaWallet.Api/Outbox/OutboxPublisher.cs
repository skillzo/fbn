using Microsoft.EntityFrameworkCore;
using NovaWallet.Api.Data;

namespace NovaWallet.Api.Outbox;

public class OutboxPublisher(
    IServiceScopeFactory scopeFactory,
    IEventPublisher publisher,
    TimeProvider time,
    IConfiguration config,
    ILogger<OutboxPublisher> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = config.GetValue("Outbox:PollSeconds", 3);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessBatchAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Outbox batch failed");
            }

            await Task.Delay(TimeSpan.FromSeconds(interval), stoppingToken);
        }
    }

    private async Task ProcessBatchAsync(CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var batch = await db.Outbox
            .FromSqlRaw("""
                SELECT id, type, payload, created_at, processed_at
                FROM outbox
                WHERE processed_at IS NULL
                ORDER BY created_at
                LIMIT 50
                FOR UPDATE SKIP LOCKED
                """)
            .ToListAsync(ct);

        if (batch.Count == 0)
        {
            await tx.RollbackAsync(ct);
            return;
        }

        var now = time.GetUtcNow();
        foreach (var message in batch)
        {
            await publisher.PublishAsync(message.Type, message.Payload, ct);
            message.ProcessedAt = now;
        }

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }
}
