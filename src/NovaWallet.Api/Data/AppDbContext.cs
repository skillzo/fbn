using Microsoft.EntityFrameworkCore;

namespace NovaWallet.Api.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Wallet> Wallets => Set<Wallet>();
    public DbSet<WalletTransaction> Transactions => Set<WalletTransaction>();
    public DbSet<IdempotencyKey> IdempotencyKeys => Set<IdempotencyKey>();
    public DbSet<DailyLimit> DailyLimits => Set<DailyLimit>();
    public DbSet<OutboxMessage> Outbox => Set<OutboxMessage>();
    public DbSet<AuditLogEntry> AuditLog => Set<AuditLogEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Wallet>(e =>
        {
            e.ToTable("wallets");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.HasIndex(x => x.CustomerId).IsUnique();
            e.Property(x => x.CustomerId).HasColumnName("customer_id");
            e.Property(x => x.Balance).HasColumnName("balance");
            e.Property(x => x.Currency).HasColumnName("currency").HasMaxLength(3).IsFixedLength();
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.ToTable(t =>
            {
                t.HasCheckConstraint("CK_wallets_balance_nonneg", "balance >= 0");
                t.HasCheckConstraint("CK_wallets_currency_ngn", "currency = 'NGN'");
            });
        });

        modelBuilder.Entity<WalletTransaction>(e =>
        {
            e.ToTable("transactions");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.Type).HasColumnName("type");
            e.Property(x => x.FromWalletId).HasColumnName("from_wallet_id");
            e.Property(x => x.ToWalletId).HasColumnName("to_wallet_id");
            e.Property(x => x.Amount).HasColumnName("amount");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.HasIndex(x => new { x.FromWalletId, x.CreatedAt }).IsDescending(false, true);
            e.HasIndex(x => new { x.ToWalletId, x.CreatedAt }).IsDescending(false, true);
            e.ToTable(t => t.HasCheckConstraint("CK_transactions_amount_positive", "amount > 0"));
        });

        modelBuilder.Entity<IdempotencyKey>(e =>
        {
            e.ToTable("idempotency_keys");
            e.HasKey(x => new { x.CustomerId, x.Key });
            e.Property(x => x.CustomerId).HasColumnName("customer_id");
            e.Property(x => x.Key).HasColumnName("key");
            e.Property(x => x.RequestHash).HasColumnName("request_hash");
            e.Property(x => x.ResponseBody).HasColumnName("response_body").HasColumnType("jsonb");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
        });

        modelBuilder.Entity<DailyLimit>(e =>
        {
            e.ToTable("daily_limits");
            e.HasKey(x => new { x.WalletId, x.Day });
            e.Property(x => x.WalletId).HasColumnName("wallet_id");
            e.Property(x => x.Day).HasColumnName("day");
            e.Property(x => x.Total).HasColumnName("total");
        });

        modelBuilder.Entity<OutboxMessage>(e =>
        {
            e.ToTable("outbox");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.Type).HasColumnName("type");
            e.Property(x => x.Payload).HasColumnName("payload").HasColumnType("jsonb");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.Property(x => x.ProcessedAt).HasColumnName("processed_at");
            e.HasIndex(x => x.CreatedAt)
                .HasFilter("processed_at IS NULL")
                .HasDatabaseName("IX_outbox_unprocessed_created_at");
        });

        modelBuilder.Entity<AuditLogEntry>(e =>
        {
            e.ToTable("audit_log");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id").UseIdentityAlwaysColumn();
            e.Property(x => x.WalletId).HasColumnName("wallet_id");
            e.Property(x => x.TransactionId).HasColumnName("transaction_id");
            e.Property(x => x.Action).HasColumnName("action");
            e.Property(x => x.Amount).HasColumnName("amount");
            e.Property(x => x.BalanceBefore).HasColumnName("balance_before");
            e.Property(x => x.BalanceAfter).HasColumnName("balance_after");
            e.Property(x => x.Actor).HasColumnName("actor");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
        });
    }
}
