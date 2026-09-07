using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

namespace AgentPrism.Core.UnitTests.Evaluation;

/// <summary>
/// Tests for the per-judge retry checkpoint (Phase 118): a judge that already
/// wrote a score for a run is not called again on a retry attempt.
/// </summary>
public sealed class OnlineEvalCheckpointTests
{
    private const string Tenant = "acme";
    private const string Agent = "support";

    [Fact]
    public async Task ExecuteAsync_does_not_skip_on_the_first_attempt_even_if_a_score_already_exists()
    {
        var runs = new InMemoryRunStore(tenantContext: new FixedTenantContext(Tenant));
        var inputs = new InMemoryRunInputStore();
        var runId = await SeedRunAsync(runs, inputs);
        var scores = new InMemoryRunScoreStore();
        var judge = new CountingJudge("good", static _ => JudgeVerdict.Headline("good", 70));
        var handler = BuildHandler(runs, inputs, scores, [judge]);

        // A pre-existing row, as if left over from something unrelated to this attempt.
        await handler.JudgeRunAsync((await runs.GetRunAsync(runId))!);

        var reported = new List<JobItemResult>();
        await handler.ExecuteAsync(ExecutionContext(runId, reported, attempt: 1));

        judge.CallCount.ShouldBe(2);
    }

    [Fact]
    public async Task ExecuteAsync_skips_an_already_scored_judge_on_a_retry_attempt()
    {
        var runs = new InMemoryRunStore(tenantContext: new FixedTenantContext(Tenant));
        var inputs = new InMemoryRunInputStore();
        var runId = await SeedRunAsync(runs, inputs);
        var scores = new InMemoryRunScoreStore();
        var judge = new CountingJudge("good", static _ => JudgeVerdict.Headline("good", 70));
        var handler = BuildHandler(runs, inputs, scores, [judge]);

        await handler.JudgeRunAsync((await runs.GetRunAsync(runId))!);

        var reported = new List<JobItemResult>();
        await handler.ExecuteAsync(ExecutionContext(runId, reported, attempt: 2));

        judge.CallCount.ShouldBe(1);
        reported.ShouldHaveSingleItem().Status.ShouldBe(JobItemStatus.Completed);
    }

    [Fact]
    public async Task ExecuteAsync_completes_the_item_when_every_judge_is_already_scored_on_retry()
    {
        var runs = new InMemoryRunStore(tenantContext: new FixedTenantContext(Tenant));
        var inputs = new InMemoryRunInputStore();
        var runId = await SeedRunAsync(runs, inputs);
        var scores = new InMemoryRunScoreStore();
        var judgeA = new CountingJudge("a", static _ => JudgeVerdict.Headline("a", 70));
        var judgeB = new CountingJudge("b", static _ => JudgeVerdict.Headline("b", 50));
        var handler = BuildHandler(runs, inputs, scores, [judgeA, judgeB]);

        await handler.JudgeRunAsync((await runs.GetRunAsync(runId))!);

        var reported = new List<JobItemResult>();
        await handler.ExecuteAsync(ExecutionContext(runId, reported, attempt: 2));

        reported.ShouldHaveSingleItem().Status.ShouldBe(JobItemStatus.Completed);
        judgeA.CallCount.ShouldBe(1);
        judgeB.CallCount.ShouldBe(1);
    }

    [Fact]
    public async Task A_failed_judge_is_not_checkpointed_and_runs_again_when_skipping_is_requested()
    {
        var runs = new InMemoryRunStore(tenantContext: new FixedTenantContext(Tenant));
        var inputs = new InMemoryRunInputStore();
        var runId = await SeedRunAsync(runs, inputs);
        var scores = new InMemoryRunScoreStore();
        var judge = new ThrowingJudge("bad", new InvalidOperationException("model crashed"));
        var handler = BuildHandler(runs, inputs, scores, [judge]);
        var run = await runs.GetRunAsync(runId);

        await handler.JudgeRunAsync(run!);
        var (retryScores, retryFailures) = await handler.JudgeRunAsync(run!, skipAlreadyScored: true);

        judge.CallCount.ShouldBe(2);
        retryScores.ShouldBeEmpty();
        retryFailures.ShouldHaveSingleItem().ErrorType.ShouldBe("judge_failed");
        (await scores.ListAsync(Tenant, runId)).ShouldBeEmpty();
    }

    [Fact]
    public async Task A_judge_that_timed_out_is_not_checkpointed_and_runs_again_when_skipping_is_requested()
    {
        var runs = new InMemoryRunStore(tenantContext: new FixedTenantContext(Tenant));
        var inputs = new InMemoryRunInputStore();
        var runId = await SeedRunAsync(runs, inputs);
        var scores = new InMemoryRunScoreStore();
        var judge = new CountingAsyncJudge("slow", static async (_, token) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return new RunJudgment();
        });
        var handler = BuildHandler(
            runs,
            inputs,
            scores,
            [judge],
            new OnlineEvaluationOptions { JudgeTimeout = TimeSpan.FromMilliseconds(10) });
        var run = await runs.GetRunAsync(runId);

        await handler.JudgeRunAsync(run!);
        var (retryScores, retryFailures) = await handler.JudgeRunAsync(run!, skipAlreadyScored: true);

        judge.CallCount.ShouldBe(2);
        retryScores.ShouldBeEmpty();
        retryFailures.ShouldHaveSingleItem().ErrorType.ShouldBe("judge_timeout");
    }

    [Fact]
    public async Task An_abstaining_judge_is_not_checkpointed_and_runs_again_when_skipping_is_requested()
    {
        var runs = new InMemoryRunStore(tenantContext: new FixedTenantContext(Tenant));
        var inputs = new InMemoryRunInputStore();
        var runId = await SeedRunAsync(runs, inputs);
        var scores = new InMemoryRunScoreStore();
        var judge = new CountingJudge("unsure", static _ => JudgeVerdict.Headline("unsure", null, "unclear"));
        var handler = BuildHandler(runs, inputs, scores, [judge]);
        var run = await runs.GetRunAsync(runId);

        await handler.JudgeRunAsync(run!);
        var (retryScores, retryFailures) = await handler.JudgeRunAsync(run!, skipAlreadyScored: true);

        judge.CallCount.ShouldBe(2);
        retryScores.ShouldBeEmpty();
        retryFailures.ShouldBeEmpty();
        (await scores.ListAsync(Tenant, runId)).ShouldBeEmpty();
    }

    [Fact]
    public async Task A_judge_with_an_out_of_range_score_is_not_checkpointed_and_runs_again_when_skipping_is_requested()
    {
        var runs = new InMemoryRunStore(tenantContext: new FixedTenantContext(Tenant));
        var inputs = new InMemoryRunInputStore();
        var runId = await SeedRunAsync(runs, inputs);
        var scores = new InMemoryRunScoreStore();
        var judge = new CountingJudge("out-of-range", static _ => JudgeVerdict.Headline("out-of-range", 101));
        var handler = BuildHandler(runs, inputs, scores, [judge]);
        var run = await runs.GetRunAsync(runId);

        await handler.JudgeRunAsync(run!);
        var (retryScores, retryFailures) = await handler.JudgeRunAsync(run!, skipAlreadyScored: true);

        judge.CallCount.ShouldBe(2);
        retryScores.ShouldBeEmpty();
        retryFailures.ShouldHaveSingleItem().ErrorType.ShouldBe("judge_contract");
        (await scores.ListAsync(Tenant, runId)).ShouldBeEmpty();
    }

    [Fact]
    public async Task A_judge_name_containing_a_separator_round_trips_through_the_checkpoint()
    {
        // The `judge:` prefix is stripped by length, not by splitting on ':',
        // so a name carrying the legal separators must survive the round trip.
        var runs = new InMemoryRunStore(tenantContext: new FixedTenantContext(Tenant));
        var inputs = new InMemoryRunInputStore();
        var runId = await SeedRunAsync(runs, inputs);
        var scores = new InMemoryRunScoreStore();
        var judge = new CountingJudge("a.b-c_d", static _ => JudgeVerdict.Headline("a.b-c_d", 55));
        var handler = BuildHandler(runs, inputs, scores, [judge]);
        var run = await runs.GetRunAsync(runId);

        await handler.JudgeRunAsync(run!);
        var (retryScores, _) = await handler.JudgeRunAsync(run!, skipAlreadyScored: true);

        judge.CallCount.ShouldBe(1);
        var saved = retryScores.ShouldHaveSingleItem();
        saved.Author.ShouldBe("judge:a.b-c_d");
        saved.Name.ShouldBe("a.b-c_d");
    }

    [Fact]
    public async Task A_score_name_the_startup_gate_would_reject_is_refused_before_the_write()
    {
        // RunJudgeSet fails the host before any run when a judge name breaks
        // [A-Za-z0-9._-]{1,64}, so this is unreachable in a composed host.
        // Reached anyway -- a directly constructed handler -- the judgment is
        // refused BEFORE the first write rather than throwing out of the job:
        // one bad judge must not take the whole run down with it, and the
        // caller of POST /api/runs/{id}/judge learns which judge was wrong.
        var runs = new InMemoryRunStore(tenantContext: new FixedTenantContext(Tenant));
        var inputs = new InMemoryRunInputStore();
        var runId = await SeedRunAsync(runs, inputs);
        var scores = new InMemoryRunScoreStore();
        var judge = new CountingJudge("a:b", static _ => JudgeVerdict.Headline("a:b", 55));
        var handler = BuildHandler(runs, inputs, scores, [judge]);
        var run = await runs.GetRunAsync(runId);

        var (written, failures) = await handler.JudgeRunAsync(run!);

        written.ShouldBeEmpty();
        var failure = failures.ShouldHaveSingleItem();
        failure.JudgeName.ShouldBe("a:b");
        failure.ErrorType.ShouldBe("judge_contract");
        failure.IsRetryable.ShouldBeFalse();
        (await scores.ListAsync(Tenant, runId)).ShouldBeEmpty();
    }

    [Fact]
    public async Task A_judgment_that_repeats_a_name_writes_nothing()
    {
        // The name is part of the uniqueness key, so the second row would
        // silently overwrite the first. The whole judgment is refused instead,
        // and refused BEFORE the first write -- a partial set is worse than none.
        var runs = new InMemoryRunStore(tenantContext: new FixedTenantContext(Tenant));
        var inputs = new InMemoryRunInputStore();
        var runId = await SeedRunAsync(runs, inputs);
        var scores = new InMemoryRunScoreStore();
        var judge = new CountingJudge("twin", static _ => new RunJudgment
        {
            Scores =
            [
                new JudgeScore { Name = "twin.same", Kind = RunScoreKind.Numeric, Value = 10 },
                new JudgeScore { Name = "twin.same", Kind = RunScoreKind.Numeric, Value = 90 },
            ],
        });
        var handler = BuildHandler(runs, inputs, scores, [judge]);
        var run = await runs.GetRunAsync(runId);

        var (written, failures) = await handler.JudgeRunAsync(run!);

        written.ShouldBeEmpty();
        failures.ShouldHaveSingleItem().ErrorType.ShouldBe("judge_contract");
        (await scores.ListAsync(Tenant, runId)).ShouldBeEmpty();
    }

    [Fact]
    public async Task A_judge_that_wrote_several_rows_is_skipped_on_a_retry_and_reports_all_of_them()
    {
        // The checkpoint asks "does a row with this judge's author exist", so a
        // judge writing N rows must still count as scored -- and the caller must
        // get back every row it wrote, not one of them.
        var runs = new InMemoryRunStore(tenantContext: new FixedTenantContext(Tenant));
        var inputs = new InMemoryRunInputStore();
        var runId = await SeedRunAsync(runs, inputs);
        var scores = new InMemoryRunScoreStore();
        var judge = new CountingJudge("multi", static _ => new RunJudgment
        {
            Scores =
            [
                new JudgeScore { Name = "multi.a", Kind = RunScoreKind.Numeric, Value = 10 },
                new JudgeScore { Name = "multi.b", Kind = RunScoreKind.Numeric, Value = 20 },
                new JudgeScore { Name = "multi.c", Kind = RunScoreKind.Numeric, Value = 30 },
            ],
        });
        var handler = BuildHandler(runs, inputs, scores, [judge]);
        var run = await runs.GetRunAsync(runId);

        var first = await handler.JudgeRunAsync(run!);
        first.Scores.Count.ShouldBe(3);

        var second = await handler.JudgeRunAsync(run!, skipAlreadyScored: true);

        judge.CallCount.ShouldBe(1);
        second.Scores.Select(static score => score.Name)
            .ShouldBe(["multi.a", "multi.b", "multi.c"], ignoreOrder: true);
        (await scores.ListAsync(Tenant, runId)).Count.ShouldBe(3);
    }

    [Fact]
    public async Task A_store_failure_partway_through_a_set_leaves_nothing_behind()
    {
        // 🚨 A HALF-written set is worse than none: the checkpoint keys on the
        // author alone, so one surviving row would make the retry skip the judge
        // and freeze the missing metrics forever.
        var runs = new InMemoryRunStore(tenantContext: new FixedTenantContext(Tenant));
        var inputs = new InMemoryRunInputStore();
        var runId = await SeedRunAsync(runs, inputs);
        var scores = new FailAfterRunScoreStore(new InMemoryRunScoreStore(), failOnWrite: 2);
        var judge = new CountingJudge("multi", static _ => new RunJudgment
        {
            Scores =
            [
                new JudgeScore { Name = "multi.a", Kind = RunScoreKind.Numeric, Value = 10 },
                new JudgeScore { Name = "multi.b", Kind = RunScoreKind.Numeric, Value = 20 },
                new JudgeScore { Name = "multi.c", Kind = RunScoreKind.Numeric, Value = 30 },
            ],
        });
        var handler = BuildHandler(runs, inputs, scores, [judge]);
        var run = await runs.GetRunAsync(runId);

        var (written, failures) = await handler.JudgeRunAsync(run!);

        written.ShouldBeEmpty();
        var failure = failures.ShouldHaveSingleItem();
        failure.JudgeName.ShouldBe("multi");
        failure.IsRetryable.ShouldBeTrue();

        // Nothing survives, so the retry sees an unscored run and calls the
        // judge again instead of skipping it.
        (await scores.ListAsync(Tenant, runId)).ShouldBeEmpty();

        scores.StopFailing();
        await handler.JudgeRunAsync(run!, skipAlreadyScored: true);

        judge.CallCount.ShouldBe(2);
        (await scores.ListAsync(Tenant, runId)).Count.ShouldBe(3);
    }

    [Fact]
    public async Task A_non_numeric_headline_score_stays_out_of_the_zero_to_hundred_window()
    {
        // A judge is free to give its overall verdict as Stars. A 5 on a 1-5
        // scale must not be averaged into a 0-100 window whose low-score
        // threshold is 60, or the alarm fires on every healthy run.
        var runs = new InMemoryRunStore(tenantContext: new FixedTenantContext(Tenant));
        var inputs = new InMemoryRunInputStore();
        var runId = await SeedRunAsync(runs, inputs);
        var scores = new InMemoryRunScoreStore();
        var summary = new OnlineEvalSummaryService(
            runs,
            new FixedOptionsMonitor(new OnlineEvaluationOptions()));
        var judge = new CountingJudge("stars", static _ => new RunJudgment
        {
            Scores = [new JudgeScore { Name = "stars", Kind = RunScoreKind.Stars, Value = 5 }],
        });
        var handler = new OnlineEvalJobHandler(
            runs,
            inputs,
            scores,
            [judge],
            new FixedTenantContext(Tenant),
            summaryService: summary);
        var run = await runs.GetRunAsync(runId);

        await handler.JudgeRunAsync(run!);

        // The row is written and readable...
        (await scores.ListAsync(Tenant, runId)).ShouldHaveSingleItem().Value.ShouldBe(5);

        // ...but it never entered the 0-100 average.
        (await summary.GetSummaryAsync(Tenant)).SampleCount.ShouldBe(0);
    }

    /// <summary>A store that fails on the Nth write, then can be told to behave.</summary>
    private sealed class FailAfterRunScoreStore(IRunScoreStore inner, int failOnWrite) : IRunScoreStore
    {
        private int _writes;
        private bool _stopped;

        public void StopFailing() => _stopped = true;

        public async ValueTask<RunScore> UpsertAsync(RunScore score, CancellationToken cancellationToken = default)
        {
            if (!_stopped && ++_writes == failOnWrite)
            {
                throw new InvalidOperationException("simulated store failure");
            }

            return await inner.UpsertAsync(score, cancellationToken).ConfigureAwait(false);
        }

        public ValueTask<IReadOnlyList<RunScore>> ListAsync(string tenantId, Guid runId, CancellationToken cancellationToken = default)
            => inner.ListAsync(tenantId, runId, cancellationToken);

        public ValueTask<bool> DeleteAsync(string tenantId, Guid scoreId, CancellationToken cancellationToken = default)
            => inner.DeleteAsync(tenantId, scoreId, cancellationToken);

        public ValueTask<RunScoreSummary> SummarizeAsync(RunScoreQuery query, CancellationToken cancellationToken = default)
            => inner.SummarizeAsync(query, cancellationToken);
    }

    [Fact]
    public async Task A_human_authored_score_is_not_mistaken_for_a_judge_checkpoint()
    {
        var runs = new InMemoryRunStore(tenantContext: new FixedTenantContext(Tenant));
        var inputs = new InMemoryRunInputStore();
        var runId = await SeedRunAsync(runs, inputs);
        var scores = new InMemoryRunScoreStore();

        await scores.UpsertAsync(new RunScore
        {
            TenantId = Tenant,
            RunId = runId,
            Name = RunScoreRules.DefaultName,
            Kind = RunScoreKind.Binary,
            Value = 1,
            Source = "human",
            Author = "operator@example.com",
            CreatedAt = DateTimeOffset.UtcNow,
        });

        var judge = new CountingJudge("good", static _ => JudgeVerdict.Headline("good", 70));
        var handler = BuildHandler(runs, inputs, scores, [judge]);
        var run = await runs.GetRunAsync(runId);

        var (retryScores, retryFailures) = await handler.JudgeRunAsync(run!, skipAlreadyScored: true);

        judge.CallCount.ShouldBe(1);
        retryFailures.ShouldBeEmpty();
        retryScores.ShouldHaveSingleItem().Source.ShouldBe("judge:good");
        (await scores.ListAsync(Tenant, runId)).Count.ShouldBe(2);
    }

    [Fact]
    public async Task A_score_read_failure_does_not_skip_any_judge_and_does_not_fail_the_call()
    {
        var runs = new InMemoryRunStore(tenantContext: new FixedTenantContext(Tenant));
        var inputs = new InMemoryRunInputStore();
        var runId = await SeedRunAsync(runs, inputs);
        var goodJudge = new CountingJudge("good", static _ => JudgeVerdict.Headline("good", 70));
        var otherJudge = new CountingJudge("other", static _ => JudgeVerdict.Headline("other", 40));
        var scores = new ThrowingListRunScoreStore(new InMemoryRunScoreStore());
        var handler = BuildHandler(runs, inputs, scores, [goodJudge, otherJudge]);
        var run = await runs.GetRunAsync(runId);

        var (retryScores, retryFailures) = await handler.JudgeRunAsync(run!, skipAlreadyScored: true);

        retryFailures.ShouldBeEmpty();
        retryScores.Count.ShouldBe(2);
        goodJudge.CallCount.ShouldBe(1);
        otherJudge.CallCount.ShouldBe(1);
    }

    [Fact]
    public async Task Cancellation_during_the_checkpoint_read_propagates_and_is_not_swallowed()
    {
        var runs = new InMemoryRunStore(tenantContext: new FixedTenantContext(Tenant));
        var inputs = new InMemoryRunInputStore();
        var runId = await SeedRunAsync(runs, inputs);
        var scores = new CancelingRunScoreStore(new InMemoryRunScoreStore());
        var judge = new CountingJudge("good", static _ => JudgeVerdict.Headline("good", 70));
        var handler = BuildHandler(runs, inputs, scores, [judge]);
        var run = await runs.GetRunAsync(runId);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Should.ThrowAsync<OperationCanceledException>(
            () => handler.JudgeRunAsync(run!, skipAlreadyScored: true, cancellationToken: cancellation.Token).AsTask());

        judge.CallCount.ShouldBe(0);
    }

    [Fact]
    public async Task A_judge_whose_body_completes_after_its_timeout_does_not_leave_a_row_a_retry_would_read()
    {
        var runs = new InMemoryRunStore(tenantContext: new FixedTenantContext(Tenant));
        var inputs = new InMemoryRunInputStore();
        var runId = await SeedRunAsync(runs, inputs);
        var scores = new InMemoryRunScoreStore();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var judge = new CountingAsyncJudge("slow", async (_, _) =>
        {
            started.TrySetResult();
            await release.Task;
            return JudgeVerdict.Headline("slow", 91);
        });

        var handler = BuildHandler(
            runs,
            inputs,
            scores,
            [judge],
            new OnlineEvaluationOptions { JudgeTimeout = TimeSpan.FromMilliseconds(20) });
        var run = await runs.GetRunAsync(runId);

        var firstAttempt = handler.JudgeRunAsync(run!).AsTask();
        await started.Task.WaitAsync(TimeSpan.FromSeconds(1));

        var (firstScores, firstFailures) = await firstAttempt;
        firstScores.ShouldBeEmpty();
        firstFailures.ShouldHaveSingleItem().ErrorType.ShouldBe("judge_timeout");

        // The abandoned body from the first attempt completes late; per K-621
        // its result is discarded and it writes nothing.
        release.TrySetResult();

        var (retryScores, retryFailures) = await handler.JudgeRunAsync(run!, skipAlreadyScored: true);

        judge.CallCount.ShouldBe(2);
        retryFailures.ShouldBeEmpty();
        retryScores.ShouldHaveSingleItem().Value.ShouldBe(91);
        (await scores.ListAsync(Tenant, runId)).ShouldHaveSingleItem();
    }

    private static async Task<Guid> SeedRunAsync(InMemoryRunStore runs, InMemoryRunInputStore inputs)
    {
        var runId = AgentPrismId.NewId();
        var now = DateTimeOffset.UtcNow;

        await runs.StartRunAsync(new RunStartInfo
        {
            RunId = runId,
            AgentName = Agent,
            TenantId = Tenant,
            StartedAt = now,
        });

        await inputs.SaveAsync(new RunInputRecord
        {
            RunId = runId,
            TenantId = Tenant,
            Messages = [new ChatMessage(ChatRole.User, "hello")],
            CreatedAt = now,
        });

        await runs.AppendEventAsync(new RunEvent
        {
            RunId = runId,
            Sequence = 0,
            Type = RunEventType.MessageCompleted,
            Timestamp = now,
            Text = "answer text",
        });

        await runs.CompleteRunAsync(new RunCompletion
        {
            RunId = runId,
            Status = RunStatus.Completed,
            CompletedAt = now,
        });

        return runId;
    }

    private static JobContext ExecutionContext(Guid runId, List<JobItemResult> reported, int attempt) => new()
    {
        Job = Job(attempt),
        Items = [Item(runId)],
        ReportItemAsync = (result, _) =>
        {
            reported.Add(result);
            return default;
        },
        IsCancelledAsync = _ => new ValueTask<bool>(false),
    };

    private static JobRecord Job(int attempt) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = Tenant,
        HandlerKey = JobHandlerKeys.OnlineEval,
        TargetName = Agent,
        Status = JobStatus.Running,
        Attempt = attempt,
        ScheduledFor = DateTimeOffset.UtcNow,
        CreatedAt = DateTimeOffset.UtcNow,
    };

    private static JobItemRecord Item(Guid runId)
        => new() { Id = Guid.NewGuid(), JobId = Guid.NewGuid(), Seq = 0, Input = runId.ToString(), Status = JobItemStatus.Pending };

    private static OnlineEvalJobHandler BuildHandler(
        InMemoryRunStore runs,
        InMemoryRunInputStore inputs,
        IRunScoreStore scores,
        IReadOnlyList<IRunJudge> judges,
        OnlineEvaluationOptions? options = null)
        => new(
            runs,
            inputs,
            scores,
            judges,
            new FixedTenantContext(Tenant),
            optionsMonitor: options is null ? null : new FixedOptionsMonitor(options));

    private sealed class FixedTenantContext(string tenantId) : ITenantContext
    {
        public string TenantId => tenantId;
    }

    private sealed class FixedOptionsMonitor(OnlineEvaluationOptions value) : IOptionsMonitor<OnlineEvaluationOptions>
    {
        public OnlineEvaluationOptions CurrentValue => value;

        public OnlineEvaluationOptions Get(string? name) => value;

        public IDisposable? OnChange(Action<OnlineEvaluationOptions, string?> listener) => null;
    }

    private sealed class CountingJudge(string name, Func<RunJudgeContext, RunJudgment> respond) : IRunJudge
    {
        private int _callCount;

        public string Name => name;

        public int CallCount => _callCount;

        public ValueTask<RunJudgment> JudgeAsync(RunJudgeContext context, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _callCount);
            return new ValueTask<RunJudgment>(respond(context));
        }
    }

    private sealed class CountingAsyncJudge(string name, Func<RunJudgeContext, CancellationToken, ValueTask<RunJudgment>> respond) : IRunJudge
    {
        private int _callCount;

        public string Name => name;

        public int CallCount => _callCount;

        public ValueTask<RunJudgment> JudgeAsync(RunJudgeContext context, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _callCount);
            return respond(context, cancellationToken);
        }
    }

    private sealed class ThrowingJudge(string name, Exception exception) : IRunJudge
    {
        private int _callCount;

        public string Name => name;

        public int CallCount => _callCount;

        public ValueTask<RunJudgment> JudgeAsync(RunJudgeContext context, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _callCount);
            throw exception;
        }
    }

    private sealed class ThrowingListRunScoreStore(IRunScoreStore inner) : IRunScoreStore
    {
        public ValueTask<RunScore> UpsertAsync(RunScore score, CancellationToken cancellationToken = default)
            => inner.UpsertAsync(score, cancellationToken);

        public ValueTask<IReadOnlyList<RunScore>> ListAsync(string tenantId, Guid runId, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("The score store is unavailable.");

        public ValueTask<bool> DeleteAsync(string tenantId, Guid scoreId, CancellationToken cancellationToken = default)
            => inner.DeleteAsync(tenantId, scoreId, cancellationToken);

        public ValueTask<RunScoreSummary> SummarizeAsync(RunScoreQuery query, CancellationToken cancellationToken = default)
            => inner.SummarizeAsync(query, cancellationToken);
    }

    private sealed class CancelingRunScoreStore(IRunScoreStore inner) : IRunScoreStore
    {
        public ValueTask<RunScore> UpsertAsync(RunScore score, CancellationToken cancellationToken = default)
            => inner.UpsertAsync(score, cancellationToken);

        public ValueTask<IReadOnlyList<RunScore>> ListAsync(string tenantId, Guid runId, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return inner.ListAsync(tenantId, runId, cancellationToken);
        }

        public ValueTask<bool> DeleteAsync(string tenantId, Guid scoreId, CancellationToken cancellationToken = default)
            => inner.DeleteAsync(tenantId, scoreId, cancellationToken);

        public ValueTask<RunScoreSummary> SummarizeAsync(RunScoreQuery query, CancellationToken cancellationToken = default)
            => inner.SummarizeAsync(query, cancellationToken);
    }
}
