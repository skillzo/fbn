using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Testcontainers.PostgreSql;

namespace NovaWallet.Tests;

public sealed class ApiFixture : IAsyncLifetime
{
    private PostgreSqlContainer? _postgres;

    public string PostgresConnectionString => _postgres?.GetConnectionString()
        ?? throw new InvalidOperationException("Postgres container is not started");

    public WebApplicationFactory<Program> Factory { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        _postgres = new PostgreSqlBuilder().WithImage("postgres:16").Build();
        await _postgres.StartAsync();
        Factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            QuietLogging(builder);
            builder.UseSetting("ConnectionStrings:Default", _postgres.GetConnectionString());
            builder.UseSetting("Jwt:Key", "test-only-jwt-key-32-chars-min!!");
            builder.UseSetting("Jwt:Issuer", "NovaWallet");
            builder.UseSetting("Jwt:Audience", "NovaWallet.Api");
            builder.UseSetting("DevAuth:Enabled", "true");
            builder.UseSetting("Swagger:Enabled", "false");
            builder.UseSetting("RateLimiting:TransfersPerMinute", "10000");
            builder.UseSetting("Outbox:PollSeconds", "1");
        });
    }

    public async Task DisposeAsync()
    {
        if (_postgres is not null)
            await _postgres.DisposeAsync();
    }

    public WebApplicationFactory<Program> CreateFactory(
        Action<IServiceCollection>? configureServices = null,
        Action<IDictionary<string, string?>>? configureSettings = null)
    {
        var settings = new Dictionary<string, string?>
        {
            ["ConnectionStrings:Default"] = PostgresConnectionString,
            ["Jwt:Key"] = "test-only-jwt-key-32-chars-min!!",
            ["Jwt:Issuer"] = "NovaWallet",
            ["Jwt:Audience"] = "NovaWallet.Api",
            ["DevAuth:Enabled"] = "true",
            ["Swagger:Enabled"] = "false",
            ["RateLimiting:TransfersPerMinute"] = "10000",
            ["Outbox:PollSeconds"] = "1",
        };
        configureSettings?.Invoke(settings);

        return new CustomWebApplicationFactory(settings, configureServices);
    }

    private static void QuietLogging(IWebHostBuilder builder) =>
        builder.ConfigureLogging(logging =>
        {
            logging.ClearProviders();
            logging.SetMinimumLevel(LogLevel.None);
        });

    private sealed class CustomWebApplicationFactory(
        Dictionary<string, string?> settings,
        Action<IServiceCollection>? configureServices) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            QuietLogging(builder);
            foreach (var (key, value) in settings)
                builder.UseSetting(key, value);

            if (configureServices is not null)
                builder.ConfigureTestServices(configureServices);
        }
    }
}

[CollectionDefinition(nameof(ApiCollection))]
public class ApiCollection : ICollectionFixture<ApiFixture>;

[Collection(nameof(ApiCollection))]
public abstract class ApiTestBase(ApiFixture fixture)
{
    protected HttpClient Client => fixture.Factory.CreateClient();
}
