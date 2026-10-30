using System.Diagnostics;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;
using static Tracon.AspNetCore.FunctionalTests.Infrastructure.NodeUsageHost;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// What the check at the start of a node costs the store: the reads it adds, counted on the real store, and the
/// time a chain takes with the quota on and off.
/// </summary>
/// <remarks>
/// <para>
/// The check reads the tenant's rules, and the current periods' usage when there are rules: one read for a tenant with
/// no rule, two for a tenant with one. The count is exact and asserted. It is measured as a slope, the difference between
/// a chain of 21 nodes and a chain of one, so the reads the door and the run's close make (the same for both) cancel.
/// </para>
/// <para>
/// The time is written to the test output and not asserted: it depends on the machine, and the cost of a store round trip
/// is the store's (the measurement here is a local SQLite file, the cheapest a round trip gets).
/// </para>
/// </remarks>
public sealed class QuotaGateRoundTripTests
{
    private const int Nodes = 21;

    [Fact]
    public async Task A_node_start_reads_the_tenants_rules_once_when_it_has_none()
    {
        var (lists, usages, _) = await SlopeAsync(withRule: false);

        lists.ShouldBe(1);
        usages.ShouldBe(0);
    }

    [Fact]
    public async Task A_node_start_reads_the_rules_and_the_usage_when_the_tenant_has_a_rule()
    {
        var (lists, usages, _) = await SlopeAsync(withRule: true);

        lists.ShouldBe(1);
        usages.ShouldBe(1);
    }

    [Fact]
    public async Task The_time_a_chain_takes_with_the_quota_on_and_off_is_written_down()
    {
        using var off = await ElapsedAsync(quotaEnabled: false, withRule: false);
        using var empty = await ElapsedAsync(quotaEnabled: true, withRule: false);
        using var ruled = await ElapsedAsync(quotaEnabled: true, withRule: true);

        // Medians of five runs of a 21-node chain on a local SQLite file, written for the change's record.
        Console.WriteLine($"QUOTA-GATE-TIME chain={Nodes} nodes: quota off {off.Median:F1} ms, quota on and no rule {empty.Median:F1} ms, quota on and a rule {ruled.Median:F1} ms");
        Console.WriteLine($"QUOTA-GATE-TIME per node: no rule {(empty.Median - off.Median) / Nodes:F2} ms, a rule {(ruled.Median - off.Median) / Nodes:F2} ms");

        off.Median.ShouldBeGreaterThan(0);
    }

    /// <summary>The reads one node start adds: the difference between a long chain and a short one, per node.</summary>
    private static async Task<(int Lists, int Usages, double Milliseconds)> SlopeAsync(bool withRule)
    {
        var (shortLists, shortUsages) = await CountAsync(nodes: 1, withRule);
        var (longLists, longUsages) = await CountAsync(Nodes, withRule);

        var extra = Nodes - 1;

        (longLists - shortLists).ShouldBe(extra * ((longLists - shortLists) / extra), "the reads do not grow evenly with the nodes");
        (longUsages - shortUsages).ShouldBe(extra * ((longUsages - shortUsages) / extra));

        return ((longLists - shortLists) / extra, (longUsages - shortUsages) / extra, 0);
    }

    private static async Task<(int Lists, int Usages)> CountAsync(int nodes, bool withRule)
    {
        CallCounter? lists = null;
        CallCounter? usages = null;
        using var database = new TempSqliteDatabase("quota-gate-round-trips");

        await using var host = await Start(
            database,
            configureServices: null,
            configureTracon: builder =>
            {
                // After the store is registered: the proxy wraps the registration that is there.
                lists = builder.Services.CountCalls<IQuotaStore>(nameof(IQuotaStore.ListAsync));
                usages = builder.Services.CountCalls<IQuotaStore>(nameof(IQuotaStore.GetUsageAsync));
            });

        await PrepareAsync(host, nodes, withRule);
        await RunAsync(host);

        return (lists!.Count, usages!.Count);
    }

    private static async Task<Timings> ElapsedAsync(bool quotaEnabled, bool withRule)
    {
        var database = new TempSqliteDatabase("quota-gate-time");

        await using var host = await Start(
            database,
            services => services.Configure<TraconQuotaOptions>(options => options.Enabled = quotaEnabled),
            configureTracon: null);

        await PrepareAsync(host, Nodes, withRule);
        await RunAsync(host);

        var samples = new List<double>();

        for (var index = 0; index < 5; index++)
        {
            var watch = Stopwatch.StartNew();

            await RunAsync(host);

            samples.Add(watch.Elapsed.TotalMilliseconds);
        }

        samples.Sort();

        return new Timings(samples[2], database);
    }

    private static Task<TraconTestHost> Start(
        TempSqliteDatabase database,
        Action<IServiceCollection>? configureServices,
        Action<ITraconBuilder>? configureTracon)
        => TraconTestHost.StartAsync(
            configureTracon: builder =>
            {
                builder.UseSqlite(database.ConnectionString);
                builder.UseWorkflows();

                for (var index = 0; index < Nodes; index++)
                {
                    builder.AddWorkflowFunction<List<ChatMessage>, List<ChatMessage>>($"pass-{index:00}", _ => (messages, _, _) => new ValueTask<List<ChatMessage>>(messages));
                }

                configureTracon?.Invoke(builder);
            },
            configureServices: services =>
            {
                services.Configure<TraconOptions>(options => options.Pricing.Currency = "USD");
                configureServices?.Invoke(services);
            });

    private static async Task PrepareAsync(TraconTestHost host, int nodes, bool withRule)
    {
        if (withRule)
        {
            await host.Services.GetRequiredService<IQuotaStore>().SaveAsync(new QuotaDefinition
            {
                Id = Guid.NewGuid(),
                TenantId = host.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<TraconOptions>>().Value.DefaultTenantId,
                Period = QuotaPeriod.Daily,
                MaxCost = 1_000_000m,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
            });
        }

        await SaveAsync(host, Workflow, [.. Enumerable.Range(0, nodes).Select(static index => Function($"pass-{index:00}"))]);
    }

    /// <summary>The median of the samples, and the database that is deleted with it.</summary>
    private sealed record Timings(double Median, TempSqliteDatabase Database) : IDisposable
    {
        public void Dispose() => Database.Dispose();
    }
}
