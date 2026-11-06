using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// With the audit chain key switched off after entries were signed, the log names each tenant that writes an
/// unsigned entry - up to a limit - and then says once that more were left out.
/// </summary>
/// <remarks>
/// <para>
/// The tenant comes from a request, so the set of tenants already warned about is bounded. The set itself is tested
/// alone; this case runs the real store on SQLite, because what an operator reads is the log line the store writes at
/// the moment the set turns a tenant away, and that call site had no test.
/// </para>
/// <para>
/// The limit is 1024 tenants, so the case writes for 1025: every tenant has a signed entry first (the key is on) and
/// an unsigned one after the host restarts without the id that selects the key.
/// </para>
/// </remarks>
public sealed class AuditChainKeyWarningLimitTests
{
    private const string KeyId = "2026-10";
    private const int WarnedTenantLimit = 1024;
    private const int Tenants = WarnedTenantLimit + 1;

    private static readonly string KeyValue =
        Convert.ToBase64String(Enumerable.Range(1, 32).Select(static i => (byte)i).ToArray());

    [Fact]
    public async Task The_tenant_after_the_limit_is_not_named_and_the_log_says_once_that_names_were_left_out()
    {
        using var database = new TempSqliteDatabase("audit-key-off-limit");

        await using (var signing = await StartAsync(database, activeKey: true))
        {
            for (var tenant = 0; tenant < Tenants; tenant++)
            {
                await WriteAsync(signing, TenantName(tenant));
            }
        }

        await using var restarted = await StartAsync(database, activeKey: false);

        for (var tenant = 0; tenant < Tenants; tenant++)
        {
            await WriteAsync(restarted, TenantName(tenant));
        }

        // The tenant that was turned away writes again: the line that says names were left out is not repeated.
        await WriteAsync(restarted, TenantName(Tenants - 1));
        await WriteAsync(restarted, TenantName(0));

        var warnings = restarted.Logs.Entries
            .Where(static entry => entry.StartsWith("Warning ", StringComparison.Ordinal))
            .ToList();

        var named = warnings.Where(static entry => entry.Contains("was signed with key", StringComparison.Ordinal)).ToList();
        var leftOut = warnings.Where(static entry => entry.Contains("More than", StringComparison.Ordinal)).ToList();

        named.Count.ShouldBe(WarnedTenantLimit, "each of the first tenants is named once");

        for (var tenant = 0; tenant < WarnedTenantLimit; tenant++)
        {
            named.Count(entry => entry.Contains($"tenant {TenantName(tenant)} was signed", StringComparison.Ordinal))
                .ShouldBe(1, $"tenant {tenant}");
        }

        named.ShouldNotContain(
            entry => entry.Contains(TenantName(Tenants - 1), StringComparison.Ordinal),
            "the tenant after the limit is not named");

        leftOut.Count.ShouldBe(1);
        leftOut[0].ShouldContain($"More than {WarnedTenantLimit} tenants");
        leftOut[0].ShouldContain("Tracon:Audit:ChainKeyId");
    }

    private static string TenantName(int index) => $"tenant-{index:D4}";

    private static async Task<KeyedHost> StartAsync(TempSqliteDatabase database, bool activeKey)
    {
        var logs = new RecordingLoggerProvider();
        var builder = WebApplication.CreateSlimBuilder();

        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Logging.AddProvider(logs);
        builder.Logging.SetMinimumLevel(LogLevel.Warning);

        // The value stays in configuration so the signed entries can still be read; only the id that selects the
        // signing key goes when the key is switched off.
        var configuration = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [$"Tracon:AuditChainKeys:{KeyId}"] = KeyValue,
        };

        if (activeKey)
        {
            configuration["Tracon:Audit:ChainKeyId"] = KeyId;
        }

        builder.Configuration.AddInMemoryCollection(configuration);
        builder.AddTracon().UseSqlite(database.ConnectionString);

        var app = builder.Build();

        app.MapTracon();

        await app.StartAsync();

        return new KeyedHost(app, logs);
    }

    private static async Task WriteAsync(KeyedHost host, string tenant)
        => await host.Services.GetRequiredService<IAuditLog>().WriteAsync(
            new AuditEntry
            {
                Id = TraconId.NewId(),
                TenantId = tenant,
                Actor = "user-1",
                Action = "agent.update",
                Entity = "agent:one",
                Before = null,
                After = """{"version":2}""",
                CreatedAt = DateTimeOffset.UtcNow,
            },
            TestContext.Current.CancellationToken);

    /// <summary>A running host with the log lines it wrote.</summary>
    private sealed class KeyedHost(WebApplication app, RecordingLoggerProvider logs) : IAsyncDisposable
    {
        public RecordingLoggerProvider Logs { get; } = logs;

        public IServiceProvider Services => app.Services;

        public async ValueTask DisposeAsync() => await app.DisposeAsync();
    }
}
