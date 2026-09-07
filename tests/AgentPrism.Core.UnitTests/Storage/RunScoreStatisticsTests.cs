namespace AgentPrism.Core.UnitTests.Storage;

/// <summary>
/// Tests for the <see cref="RunStatistics.ScoredRuns"/> and
/// <see cref="RunStatistics.PositiveRate"/> calculation.
/// </summary>
/// <remarks>
/// <see cref="InMemoryRunStore"/> uses the <see cref="IRunScoreStore"/> given to
/// its constructor when computing the summary; these tests share the same
/// instance to verify that writing a score and reading statistics see the SAME
/// store.
/// </remarks>
public sealed class RunScoreStatisticsTests
{
    private const string Tenant = "test";

    [Fact]
    public async Task Scored_run_is_counted_in_ScoredRuns_and_the_rate_is_computed()
    {
        var scores = new InMemoryRunScoreStore();
        var runs = new InMemoryRunStore(scores);

        var scoredRunId = AgentPrismId.NewId();
        var unscoredRunId = AgentPrismId.NewId();

        await runs.StartRunAsync(Start(scoredRunId));
        await runs.StartRunAsync(Start(unscoredRunId));

        // Two different authors score the same run: 1 positive, 1 negative.
        await scores.UpsertAsync(Score(scoredRunId, "alice", 1));
        await scores.UpsertAsync(Score(scoredRunId, "bob", 0));

        var stats = await runs.GetStatisticsAsync(new RunStatisticsQuery { TenantId = Tenant });

        stats.ScoredRuns.ShouldBe(1);
        stats.PositiveRate.ShouldBe(0.5);
    }

    [Fact]
    public async Task Eval_run_score_never_enters_the_summary()
    {
        // K-141's exclusion (RunKind.Eval is excluded) also applies to scores.
        var scores = new InMemoryRunScoreStore();
        var runs = new InMemoryRunStore(scores);

        var evalRunId = AgentPrismId.NewId();
        await runs.StartRunAsync(Start(evalRunId) with { Kind = RunKind.Eval });
        await scores.UpsertAsync(Score(evalRunId, "alice", 1));

        var stats = await runs.GetStatisticsAsync(new RunStatisticsQuery { TenantId = Tenant });

        stats.TotalRuns.ShouldBe(0);
        stats.ScoredRuns.ShouldBe(0);
        stats.PositiveRate.ShouldBeNull();
    }

    [Fact]
    public async Task Star_score_is_counted_in_ScoredRuns_but_does_NOT_enter_the_rate()
    {
        // Averaging a star score with a binary score is meaningless; the rate
        // is computed only from Binary scores (see
        // docs/arsiv/fazlar/31-GERI-BILDIRIM-VE-PUANLAMA.md, risks).
        var scores = new InMemoryRunScoreStore();
        var runs = new InMemoryRunStore(scores);

        var runId = AgentPrismId.NewId();
        await runs.StartRunAsync(Start(runId));
        await scores.UpsertAsync(Score(runId, "alice", 5) with { Kind = RunScoreKind.Stars });

        var stats = await runs.GetStatisticsAsync(new RunStatisticsQuery { TenantId = Tenant });

        stats.ScoredRuns.ShouldBe(1);
        stats.PositiveRate.ShouldBeNull();
    }

    [Fact]
    public async Task No_scores_at_all_means_ScoredRuns_is_zero_and_the_rate_is_null()
    {
        var scores = new InMemoryRunScoreStore();
        var runs = new InMemoryRunStore(scores);

        await runs.StartRunAsync(Start(AgentPrismId.NewId()));

        var stats = await runs.GetStatisticsAsync(new RunStatisticsQuery { TenantId = Tenant });

        stats.ScoredRuns.ShouldBe(0);
        stats.PositiveRate.ShouldBeNull();
    }

    private static RunStartInfo Start(Guid runId)
        => new()
        {
            RunId = runId,
            AgentName = "test-agent",
            StartedAt = DateTimeOffset.UtcNow,
            TenantId = Tenant,
        };

    /// <remarks>
    /// 🚨 A null value records that NO MEASUREMENT was made. Counting it in the
    /// DENOMINATOR would report a run scored only positively as half positive —
    /// exactly the reading `JudgeScore.Value` refuses to produce.
    /// </remarks>
    [Fact]
    public async Task A_binary_score_with_no_measurement_leaves_the_rate_ALONE()
    {
        var scores = new InMemoryRunScoreStore();
        var runs = new InMemoryRunStore(scores);
        var runId = AgentPrismId.NewId();

        await runs.StartRunAsync(Start(runId));

        await scores.UpsertAsync(Score(runId, "alice", 1));
        await scores.UpsertAsync(Score(runId, "bob", 0) with { Value = null });

        var stats = await runs.GetStatisticsAsync(new RunStatisticsQuery { TenantId = Tenant });

        stats.ScoredRuns.ShouldBe(1);
        stats.PositiveRate.ShouldBe(1.0);
    }

    [Fact]
    public async Task A_run_scored_ONLY_without_a_measurement_has_NO_rate()
    {
        var scores = new InMemoryRunScoreStore();
        var runs = new InMemoryRunStore(scores);
        var runId = AgentPrismId.NewId();

        await runs.StartRunAsync(Start(runId));
        await scores.UpsertAsync(Score(runId, "alice", 0) with { Value = null });

        var stats = await runs.GetStatisticsAsync(new RunStatisticsQuery { TenantId = Tenant });

        stats.ScoredRuns.ShouldBe(1);
        stats.PositiveRate.ShouldBeNull();
    }

    private static RunScore Score(Guid runId, string author, int value)
        => new()
        {
            TenantId = Tenant,
            RunId = runId,
            Name = RunScoreRules.DefaultName,
            Kind = RunScoreKind.Binary,
            Value = value,
            Source = "human",
            Author = author,
            CreatedAt = DateTimeOffset.UtcNow,
        };
}
