namespace NovaWallet.Api.Data;

public class Wallet
{
    public Guid Id { get; set; }
    public string CustomerId { get; set; } = "";
    public long Balance { get; set; }
    public string Currency { get; set; } = "NGN";
    public DateTimeOffset CreatedAt { get; set; }
}

public class WalletTransaction
{
    public Guid Id { get; set; }
    public string Type { get; set; } = "";
    public Guid? FromWalletId { get; set; }
    public Guid ToWalletId { get; set; }
    public long Amount { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public class IdempotencyKey
{
    public string CustomerId { get; set; } = "";
    public string Key { get; set; } = "";
    public string RequestHash { get; set; } = "";
    public string? ResponseBody { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public class DailyLimit
{
    public Guid WalletId { get; set; }
    public DateOnly Day { get; set; }
    public long Total { get; set; }
}

public class OutboxMessage
{
    public Guid Id { get; set; }
    public string Type { get; set; } = "";
    public string Payload { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ProcessedAt { get; set; }
}

public class AuditLogEntry
{
    public long Id { get; set; }
    public Guid WalletId { get; set; }
    public Guid TransactionId { get; set; }
    public string Action { get; set; } = "";
    public long Amount { get; set; }
    public long BalanceBefore { get; set; }
    public long BalanceAfter { get; set; }
    public string Actor { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
}
