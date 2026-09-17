namespace NovaWallet.Api.Outbox;

public class LoggingEventPublisher(ILogger<LoggingEventPublisher> logger) : IEventPublisher
{
    public Task PublishAsync(string type, string payload, CancellationToken ct)
    {
        logger.LogInformation("Outbox event {Type}: {Payload}", type, payload);
        return Task.CompletedTask;
    }
}
