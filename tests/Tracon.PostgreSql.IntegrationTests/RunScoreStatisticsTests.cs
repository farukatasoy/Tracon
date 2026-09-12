using Tracon.PostgreSql.IntegrationTests.Infrastructure;

namespace Tracon.PostgreSql.IntegrationTests;

/// <summary>
/// Verifies the hand-written SQL (FILTER/CASE, scalar subquery) behind the
/// <see cref="RunStatistics.ScoredRuns"/>/<see cref="RunStatistics.PositiveRate"/>
/// computation on PostgreSQL, against a real database (Phase 31).
/// </summary>
public sealed class RunScoreStatisticsTests(PostgresFixture fixture) : IAsyncLifetime
{
    private PostgresTestContext _context = null!;

    /// <inheritdoc />
    public async ValueTask InitializeAsync() => _context = await PostgresTestContext.CreateAsync(fixture);

    /// <inheritdoc />
    public async ValueTask DisposeAsync() => await _context.DisposeAsync();

    [Fact]
    public async Task Binary_scores_positive_rate_is_computed_correctly()
    {
        var tenant = _context.TenantContext.TenantId;
        var scoredRunId = TraconId.NewId();
        var unscoredRunId = TraconId.NewId();

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
        var evalRunId = TraconId.NewId();

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
        var runId = TraconId.NewId();

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

    /// <remarks>
    /// 🚨 A null value records that NO MEASUREMENT was made and stays out of the
    /// DENOMINATOR too; otherwise a run scored only positively reads as half
    /// positive.
    /// </remarks>
    [Fact]
    public async Task A_binary_score_with_no_measurement_leaves_the_rate_ALONE()
    {
        var tenant = _context.TenantContext.TenantId;
        var runId = TraconId.NewId();

        await _context.Runs.StartRunAsync(Start(runId, tenant));

        await _context.RunScores.UpsertAsync(Score(runId, tenant, "alice", 1));
        await _context.RunScores.UpsertAsync(Score(runId, tenant, "bob", 0) with { Value = null });

        var stats = await _context.Runs.GetStatisticsAsync(new RunStatisticsQuery { TenantId = tenant });

        stats.ScoredRuns.ShouldBe(1);
        stats.PositiveRate.ShouldBe(1.0);
    }

    [Fact]
    public async Task A_run_scored_ONLY_without_a_measurement_has_NO_rate()
    {
        var tenant = _context.TenantContext.TenantId;
        var runId = TraconId.NewId();

        await _context.Runs.StartRunAsync(Start(runId, tenant));
        await _context.RunScores.UpsertAsync(Score(runId, tenant, "alice", 0) with { Value = null });

        var stats = await _context.Runs.GetStatisticsAsync(new RunStatisticsQuery { TenantId = tenant });

        stats.ScoredRuns.ShouldBe(1);
        stats.PositiveRate.ShouldBeNull();
    }

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
