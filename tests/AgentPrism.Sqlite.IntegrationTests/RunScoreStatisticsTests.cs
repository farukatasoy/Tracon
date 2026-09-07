using AgentPrism.Sqlite.IntegrationTests.Infrastructure;

namespace AgentPrism.Sqlite.IntegrationTests;

/// <summary>
/// Verifies the hand-written SQL (FILTER/CASE, scalar subquery) behind the
/// <see cref="RunStatistics.ScoredRuns"/>/<see cref="RunStatistics.PositiveRate"/>
/// computation on SQLite, against a real database (Phase 31).
/// </summary>
public sealed class RunScoreStatisticsTests(SqliteFixture fixture) : IAsyncLifetime
{
    private SqliteTestContext _context = null!;

    /// <inheritdoc />
    public async ValueTask InitializeAsync() => _context = await SqliteTestContext.CreateAsync(fixture);

    /// <inheritdoc />
    public async ValueTask DisposeAsync() => await _context.DisposeAsync();

    [Fact]
    public async Task Binary_scores_positive_rate_is_computed_correctly()
    {
        var tenant = _context.TenantContext.TenantId;
        var scoredRunId = AgentPrismId.NewId();
        var unscoredRunId = AgentPrismId.NewId();

        await _context.Runs.StartRunAsync(Start(scoredRunId, tenant));
        await _context.Runs.StartRunAsync(Start(unscoredRunId, tenant));

        await _context.RunScores.UpsertAsync(Score(scoredRunId, tenant, "alice", 1));
        await _context.RunScores.UpsertAsync(Score(scoredRunId, tenant, "bob", 0));

        var stats = await _context.Runs.GetStatisticsAsync(new RunStatisticsQuery { TenantId = tenant });

        stats.ScoredRuns.ShouldBe(1);
        stats.PositiveRate.ShouldBe(0.5);
    }

    [Fact]
    public async Task Eval_runs_score_NEVER_enters_the_summary()
    {
        var tenant = _context.TenantContext.TenantId;
        var evalRunId = AgentPrismId.NewId();

        await _context.Runs.StartRunAsync(Start(evalRunId, tenant) with { Kind = RunKind.Eval });
        await _context.RunScores.UpsertAsync(Score(evalRunId, tenant, "alice", 1));

        var stats = await _context.Runs.GetStatisticsAsync(new RunStatisticsQuery { TenantId = tenant });

        stats.TotalRuns.ShouldBe(0);
        stats.ScoredRuns.ShouldBe(0);
        stats.PositiveRate.ShouldBeNull();
    }

    [Fact]
    public async Task Star_score_is_counted_in_ScoredRuns_but_does_NOT_JOIN_the_rate()
    {
        var tenant = _context.TenantContext.TenantId;
        var runId = AgentPrismId.NewId();

        await _context.Runs.StartRunAsync(Start(runId, tenant));
        await _context.RunScores.UpsertAsync(Score(runId, tenant, "alice", 5) with { Kind = RunScoreKind.Stars });

        var stats = await _context.Runs.GetStatisticsAsync(new RunStatisticsQuery { TenantId = tenant });

        stats.ScoredRuns.ShouldBe(1);
        stats.PositiveRate.ShouldBeNull();
    }

    private static RunStartInfo Start(Guid runId, string tenant)
        => new()
        {
            RunId = runId,
            AgentName = "test-agent",
            StartedAt = DateTimeOffset.UtcNow,
            TenantId = tenant,
        };

    private static RunScore Score(Guid runId, string tenant, string author, int value)
        => new()
        {
            TenantId = tenant,
            RunId = runId,
            Name = RunScoreRules.DefaultName,
            Kind = RunScoreKind.Binary,
            Value = value,
            Source = "human",
            Author = author,
            CreatedAt = DateTimeOffset.UtcNow,
        };
}
