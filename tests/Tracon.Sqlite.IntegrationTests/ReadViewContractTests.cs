using Tracon.Sqlite.IntegrationTests.Infrastructure;

namespace Tracon.Sqlite.IntegrationTests;

/// <summary>
/// The <c>{prefix}runs_v1</c> read contract view (Phase 111): a consumer's
/// own SQL query against the published view must see exactly what
/// <c>IRunStore</c> sees, through the store's own public surface -- not a
/// duplicated hand-computed expectation.
/// </summary>
/// <remarks>
/// Builds its OWN table prefix with <c>EnableReadViews = true</c>, not the
/// shared schema fixture used by other contract test classes: the view is
/// opt-in and most contract test classes must not pay for it.
/// </remarks>
public sealed class ReadViewContractTests(SqliteFixture fixture) : IAsyncLifetime
{
    private SqliteTestContext _context = null!;

    /// <summary>
    /// SQLite stores a uuid as UPPERCASE text (K-191); a raw SQL literal built
    /// from the default (lowercase) <see cref="Guid.ToString()"/> never
    /// matches a stored row -- ordinal string comparison, no case folding.
    /// </summary>
    private static string SqlGuid(Guid id) => id.ToString("D").ToUpperInvariant();

    public async ValueTask InitializeAsync()
        => _context = await SqliteTestContext.CreateAsync(fixture, applyMigrations: true, enableReadViews: true);

    public async ValueTask DisposeAsync() => await _context.DisposeAsync();

    [Fact]
    public async Task Total_cost_in_the_view_matches_the_stores_own_statistics()
    {
        const string tenant = "tenant-view-cost";
        const string agentName = "read-view-cost-agent";
        var runId = Guid.NewGuid();
        var startedAt = DateTimeOffset.UtcNow;

        await _context.Runs.StartRunAsync(new RunStartInfo
        {
            RunId = runId,
            AgentName = agentName,
            StartedAt = startedAt,
            TenantId = tenant,
        });

        await _context.Runs.CompleteRunAsync(new RunCompletion
        {
            RunId = runId,
            Status = RunStatus.Completed,
            CompletedAt = startedAt.AddSeconds(1),
            Usage = new RunUsage { InputTokens = 100, OutputTokens = 50, CachedInputTokens = 10, TotalTokens = 150 },
            Cost = new RunCost
            {
                InputCost = 0.5m,
                OutputCost = 0.25m,
                CachedInputCost = 0.05m,
                Currency = "USD",
                Source = PricingSource.Catalog,
            },
            TenantId = tenant,
        });

        // 🚨 input_cost/output_cost are TEXT affinity, cached_input_cost is
        // NUMERIC (docs/hafiza/sql-saglayicilari.md); the view CASTs each to
        // REAL, so the value round-trips through double -- test amounts are
        // chosen to be exactly representable in binary floating point.
        var viewTotalCost = await _context.ScalarAsync<double?>(
            $"SELECT total_cost FROM {_context.TablePrefix}runs_v1 WHERE run_id = '{SqlGuid(runId)}';");

        var stats = await _context.Runs.GetStatisticsAsync(new RunStatisticsQuery
        {
            TenantId = tenant,
            AgentName = agentName,
        });

        viewTotalCost.ShouldBe(0.5 + 0.25 + 0.05);
        viewTotalCost.ShouldBe((double?)stats.TotalCost);
    }

    [Fact]
    public async Task Total_cost_is_null_not_zero_when_pricing_is_undefined()
    {
        const string tenant = "tenant-view-unpriced";
        var runId = Guid.NewGuid();
        var startedAt = DateTimeOffset.UtcNow;

        await _context.Runs.StartRunAsync(new RunStartInfo
        {
            RunId = runId,
            AgentName = "read-view-unpriced-agent",
            StartedAt = startedAt,
            TenantId = tenant,
        });

        await _context.Runs.CompleteRunAsync(new RunCompletion
        {
            RunId = runId,
            Status = RunStatus.Completed,
            CompletedAt = startedAt.AddSeconds(1),
            Usage = new RunUsage { InputTokens = 10, OutputTokens = 5, TotalTokens = 15 },
            TenantId = tenant,
        });

        var viewTotalCost = await _context.ScalarAsync<double?>(
            $"SELECT total_cost FROM {_context.TablePrefix}runs_v1 WHERE run_id = '{SqlGuid(runId)}';");

        viewTotalCost.ShouldBeNull();
    }

    [Fact]
    public async Task Status_name_mirrors_RunStatus_for_a_failed_run()
    {
        const string tenant = "tenant-view-status";
        var runId = Guid.NewGuid();
        var startedAt = DateTimeOffset.UtcNow;

        await _context.Runs.StartRunAsync(new RunStartInfo
        {
            RunId = runId,
            AgentName = "read-view-status-agent",
            StartedAt = startedAt,
            TenantId = tenant,
        });

        await _context.Runs.CompleteRunAsync(new RunCompletion
        {
            RunId = runId,
            Status = RunStatus.Failed,
            CompletedAt = startedAt.AddSeconds(1),
            Error = new RunError { Type = "boom", Message = "boom" },
            TenantId = tenant,
        });

        var statusName = await _context.ScalarAsync<string>(
            $"SELECT status_name FROM {_context.TablePrefix}runs_v1 WHERE run_id = '{SqlGuid(runId)}';");

        statusName.ShouldBe(nameof(RunStatus.Failed));
    }

    /// <summary>
    /// 111.1: the view is NOT a tenant boundary. A consumer that queries it
    /// without its own <c>WHERE tenant_id = ...</c> clause sees every
    /// tenant's rows -- the manual acceptance case 5 this test automates.
    /// </summary>
    [Fact]
    public async Task View_carries_every_tenants_rows_unfiltered()
    {
        var tenantA = "tenant-view-a-" + Guid.NewGuid().ToString("N");
        var tenantB = "tenant-view-b-" + Guid.NewGuid().ToString("N");
        var runIdA = Guid.NewGuid();
        var runIdB = Guid.NewGuid();
        var startedAt = DateTimeOffset.UtcNow;

        await _context.Runs.StartRunAsync(new RunStartInfo
        {
            RunId = runIdA,
            AgentName = "read-view-tenant-agent",
            StartedAt = startedAt,
            TenantId = tenantA,
        });
        await _context.Runs.StartRunAsync(new RunStartInfo
        {
            RunId = runIdB,
            AgentName = "read-view-tenant-agent",
            StartedAt = startedAt,
            TenantId = tenantB,
        });

        var visibleTenantIds = new List<string>();

        await using (var command = _context.DataSource.CreateCommand(
            $"SELECT tenant_id FROM {_context.TablePrefix}runs_v1 WHERE run_id IN ('{SqlGuid(runIdA)}', '{SqlGuid(runIdB)}');"))
        await using (var reader = await command.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                visibleTenantIds.Add(reader.GetString(0));
            }
        }

        visibleTenantIds.ShouldBe([tenantA, tenantB], ignoreOrder: true);
    }
}
