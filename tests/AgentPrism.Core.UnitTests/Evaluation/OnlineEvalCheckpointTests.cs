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
        var judge = new CountingJudge("good", static _ => new RunJudgment { Score = 70 });
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
        var judge = new CountingJudge("good", static _ => new RunJudgment { Score = 70 });
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
        var judgeA = new CountingJudge("a", static _ => new RunJudgment { Score = 70 });
        var judgeB = new CountingJudge("b", static _ => new RunJudgment { Score = 50 });
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
        var judge = new CountingJudge("unsure", static _ => new RunJudgment { Score = null, Reason = "unclear" });
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
        var judge = new CountingJudge("out-of-range", static _ => new RunJudgment { Score = 101 });
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
    public async Task A_judge_name_containing_a_colon_round_trips_through_the_checkpoint()
    {
        var runs = new InMemoryRunStore(tenantContext: new FixedTenantContext(Tenant));
        var inputs = new InMemoryRunInputStore();
        var runId = await SeedRunAsync(runs, inputs);
        var scores = new InMemoryRunScoreStore();
        var judge = new CountingJudge("a:b", static _ => new RunJudgment { Score = 55 });
        var handler = BuildHandler(runs, inputs, scores, [judge]);
        var run = await runs.GetRunAsync(runId);

        await handler.JudgeRunAsync(run!);
        var (retryScores, _) = await handler.JudgeRunAsync(run!, skipAlreadyScored: true);

        judge.CallCount.ShouldBe(1);
        var saved = retryScores.ShouldHaveSingleItem();
        saved.Author.ShouldBe("judge:a:b");
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
            Kind = RunScoreKind.Binary,
            Value = 1,
            Source = "human",
            Author = "operator@example.com",
            CreatedAt = DateTimeOffset.UtcNow,
        });

        var judge = new CountingJudge("good", static _ => new RunJudgment { Score = 70 });
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
        var goodJudge = new CountingJudge("good", static _ => new RunJudgment { Score = 70 });
        var otherJudge = new CountingJudge("other", static _ => new RunJudgment { Score = 40 });
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
        var judge = new CountingJudge("good", static _ => new RunJudgment { Score = 70 });
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
            return new RunJudgment { Score = 91 };
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
        Kind = JobKind.OnlineEval,
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
    }
}
