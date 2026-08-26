using AgentPrism.PostgreSql.IntegrationTests.Infrastructure;

namespace AgentPrism.PostgreSql.IntegrationTests;

/// <summary>
/// The <c>runs_v1</c> read contract view (Phase 111): a consumer's own SQL
/// query against the published view must see exactly what
/// <c>IRunStore</c> sees, through the store's own public surface -- not a
/// duplicated hand-computed expectation.
/// </summary>
/// <remarks>
/// Builds its OWN schema with <c>EnableReadViews = true</c>
/// (<see cref="PostgresTestContext.CreateAsync(PostgresFixture, string, bool, int, bool, bool)"/>),
/// not the shared <see cref="Infrastructure.PostgresSchemaFixture"/>: the view
/// is opt-in and most contract test classes must not pay for it.
/// </remarks>
public sealed class ReadViewContractTests(PostgresFixture fixture) : IAsyncLifetime
{
    private PostgresTestContext _context = null!;

    public async ValueTask InitializeAsync()
        => _context = await PostgresTestContext.CreateAsync(fixture, applyMigrations: true, enableReadViews: true);

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

        var viewTotalCost = await _context.ScalarAsync<decimal?>(
            $"SELECT total_cost FROM {_context.SchemaName}.runs_v1 WHERE run_id = '{runId}';");

        var stats = await _context.Runs.GetStatisticsAsync(new RunStatisticsQuery
        {
            TenantId = tenant,
            AgentName = agentName,
        });

        viewTotalCost.ShouldBe(0.5m + 0.25m + 0.05m);
        viewTotalCost.ShouldBe(stats.TotalCost);
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

        // No Cost given -- the model was never priced (Source: PricingSource.Unknown
        // is not even reached; Cost itself stays null, same as a code agent).
        await _context.Runs.CompleteRunAsync(new RunCompletion
        {
            RunId = runId,
            Status = RunStatus.Completed,
            CompletedAt = startedAt.AddSeconds(1),
            Usage = new RunUsage { InputTokens = 10, OutputTokens = 5, TotalTokens = 15 },
            TenantId = tenant,
        });

        var viewTotalCost = await _context.ScalarAsync<decimal?>(
            $"SELECT total_cost FROM {_context.SchemaName}.runs_v1 WHERE run_id = '{runId}';");

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
            $"SELECT status_name FROM {_context.SchemaName}.runs_v1 WHERE run_id = '{runId}';");

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
            $"SELECT tenant_id FROM {_context.SchemaName}.runs_v1 WHERE run_id IN ('{runIdA}', '{runIdB}');"))
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
