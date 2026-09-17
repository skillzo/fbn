namespace NovaWallet.Api.Outbox;

public interface IEventPublisher
{
    Task PublishAsync(string type, string payload, CancellationToken ct);
}
