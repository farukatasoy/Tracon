using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;
using static Tracon.AspNetCore.FunctionalTests.Infrastructure.NodeUsageHost;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// What the check at the start of a node costs the store: the reads it adds, counted on the real store.
/// </summary>
/// <remarks>
/// <para>
/// The check reads the tenant's rules, and the current periods' usage when there are rules: one read for a tenant with
/// no rule, two for a tenant with one. The count is exact and asserted. It is measured as a slope, the difference between
/// a chain of 21 nodes and a chain of one, so the reads the door and the run's close make (the same for both) cancel.
/// </para>
/// <para>
/// The time a chain takes is not a test's to measure: it depends on the machine, and the cost of a store round trip is the
/// store's. A test that only wrote it down asserted nothing about the product, and it was removed with its stopwatch.
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
}
