using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

namespace Tracon.Core.UnitTests.Evaluation;

/// <summary>Tests for the online evaluation job handler (Phase 49).</summary>
public sealed class OnlineEvalJobHandlerTests
{
    private const string Tenant = "acme";
    private const string Agent = "support";

    [Fact]
    public async Task Missing_run_produces_no_error_and_writes_no_score()
    {
        var runs = new InMemoryRunStore(tenantContext: new FixedTenantContext(Tenant));
        var scores = new InMemoryRunScoreStore();
        var handler = BuildHandler(runs, new InMemoryRunInputStore(), scores, [new ScriptedJudge("model", static _ => JudgeVerdict.Headline("model", 80))]);

        var runId = Guid.NewGuid();
        var reported = new List<JobItemResult>();

        await handler.ExecuteAsync(ExecutionContext(runId, reported));

        reported.Single().Status.ShouldBe(JobItemStatus.Completed);
        (await scores.ListAsync(Tenant, runId)).ShouldBeEmpty();
    }

    [Fact]
    public async Task No_sampling_and_no_error_when_run_inputs_record_is_missing()
    {
        var runs = new InMemoryRunStore(tenantContext: new FixedTenantContext(Tenant));
        var runId = await SeedRunAsync(runs, withOutput: true);

        var scores = new InMemoryRunScoreStore();
        var handler = BuildHandler(runs, new InMemoryRunInputStore(), scores, [new ScriptedJudge("model", static _ => JudgeVerdict.Headline("model", 80))]);
        var reported = new List<JobItemResult>();

        await handler.ExecuteAsync(ExecutionContext(runId, reported));

        reported.Single().Status.ShouldBe(JobItemStatus.Completed);
        (await scores.ListAsync(Tenant, runId)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Nothing_is_written_when_no_judge_is_registered()
    {
        var runs = new InMemoryRunStore(tenantContext: new FixedTenantContext(Tenant));
        var inputs = new InMemoryRunInputStore();
        var runId = await SeedRunAsync(runs, withOutput: true, inputs: inputs);

        var scores = new InMemoryRunScoreStore();
        var handler = BuildHandler(runs, inputs, scores, []);
        var reported = new List<JobItemResult>();

        await handler.ExecuteAsync(ExecutionContext(runId, reported));

        reported.Single().Status.ShouldBe(JobItemStatus.Completed);
        (await scores.ListAsync(Tenant, runId)).ShouldBeEmpty();
    }

    [Fact]
    public async Task No_score_is_written_when_the_output_cannot_be_read()
    {
        var runs = new InMemoryRunStore(tenantContext: new FixedTenantContext(Tenant));
        var inputs = new InMemoryRunInputStore();
        var runId = await SeedRunAsync(runs, withOutput: false, inputs: inputs);

        var scores = new InMemoryRunScoreStore();
        var handler = BuildHandler(runs, inputs, scores, [new ScriptedJudge("model", static _ => JudgeVerdict.Headline("model", 80))]);
        var reported = new List<JobItemResult>();

        await handler.ExecuteAsync(ExecutionContext(runId, reported));

        reported.Single().Status.ShouldBe(JobItemStatus.Completed);
        (await scores.ListAsync(Tenant, runId)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Score_is_written_with_the_correct_fields()
    {
        var runs = new InMemoryRunStore(tenantContext: new FixedTenantContext(Tenant));
        var inputs = new InMemoryRunInputStore();
        var runId = await SeedRunAsync(runs, withOutput: true, inputs: inputs);

        var scores = new InMemoryRunScoreStore();
        var handler = BuildHandler(
            runs,
            inputs,
            scores,
            [new ScriptedJudge("model", static _ => JudgeVerdict.Headline("model", 42, "reason"))]);
        var reported = new List<JobItemResult>();

        await handler.ExecuteAsync(ExecutionContext(runId, reported));

        reported.Single().Status.ShouldBe(JobItemStatus.Completed);

        var saved = (await scores.ListAsync(Tenant, runId)).Single();
        saved.Kind.ShouldBe(RunScoreKind.Numeric);
        saved.Value.ShouldBe(42);
        saved.Comment.ShouldBe("reason");
        saved.Source.ShouldBe("judge:model");
        saved.Author.ShouldBe("judge:model");
    }

    [Fact]
    public async Task Judge_that_cannot_decide_does_not_silently_write_zero()
    {
        var runs = new InMemoryRunStore(tenantContext: new FixedTenantContext(Tenant));
        var inputs = new InMemoryRunInputStore();
        var runId = await SeedRunAsync(runs, withOutput: true, inputs: inputs);

        var scores = new InMemoryRunScoreStore();
        var handler = BuildHandler(
            runs,
            inputs,
            scores,
            [new ScriptedJudge("model", static _ => JudgeVerdict.Headline("model", null, "unclear"))]);
        var reported = new List<JobItemResult>();

        await handler.ExecuteAsync(ExecutionContext(runId, reported));

        (await scores.ListAsync(Tenant, runId)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Job_is_retried_with_backoff_when_one_judge_fails_and_the_other_still_writes()
    {
        var runs = new InMemoryRunStore(tenantContext: new FixedTenantContext(Tenant));
        var inputs = new InMemoryRunInputStore();
        var runId = await SeedRunAsync(runs, withOutput: true, inputs: inputs);

        var okJudge = new ScriptedJudge("good", static _ => JudgeVerdict.Headline("good", 70));
        var badJudge = new ScriptedJudge("bad", static _ => throw new InvalidOperationException("model crashed"));

        var scores = new InMemoryRunScoreStore();
        var handler = BuildHandler(runs, inputs, scores, [okJudge, badJudge]);
        var reported = new List<JobItemResult>();

        var exception = await Should.ThrowAsync<JobRetryException>(
            () => handler.ExecuteAsync(ExecutionContext(runId, reported)).AsTask());

        exception.RetryAfter.ShouldNotBeNull();
        exception.RetryAfter!.Value.ShouldBeGreaterThan(TimeSpan.Zero);

        // The successful judge's score is RECORDED even though the job is retried.
        var saved = (await scores.ListAsync(Tenant, runId)).Single();
        saved.Source.ShouldBe("judge:good");

        // Because the job failed, the item was never reported (stays Pending).
        reported.ShouldBeEmpty();
    }

    [Fact]
    public async Task JudgeRunAsync_called_twice_updates_the_same_judges_row_instead_of_duplicating()
    {
        var runs = new InMemoryRunStore(tenantContext: new FixedTenantContext(Tenant));
        var inputs = new InMemoryRunInputStore();
        var runId = await SeedRunAsync(runs, withOutput: true, inputs: inputs);

        var judge = new ScriptedJudge("model", static _ => JudgeVerdict.Headline("model", 55));
        var scores = new InMemoryRunScoreStore();
        var handler = BuildHandler(runs, inputs, scores, [judge]);

        var run = await runs.GetRunAsync(runId);

        await handler.JudgeRunAsync(run!);
        await handler.JudgeRunAsync(run!);

        (await scores.ListAsync(Tenant, runId)).Count.ShouldBe(1);
    }

    [Fact]
    public async Task Invalid_score_is_a_terminal_contract_failure_and_is_not_persisted()
    {
        var runs = new InMemoryRunStore(tenantContext: new FixedTenantContext(Tenant));
        var inputs = new InMemoryRunInputStore();
        var runId = await SeedRunAsync(runs, withOutput: true, inputs: inputs);
        var scores = new InMemoryRunScoreStore();
        var handler = BuildHandler(runs, inputs, scores, [new ScriptedJudge("invalid", static _ => JudgeVerdict.Headline("invalid", 101))]);
        var reported = new List<JobItemResult>();

        await handler.ExecuteAsync(ExecutionContext(runId, reported));

        reported.Single().Status.ShouldBe(JobItemStatus.Failed);
        reported.Single().Error.ShouldBe("invalid (judge_contract)");
        (await scores.ListAsync(Tenant, runId)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Judge_timeout_is_retryable_and_does_not_write_a_score()
    {
        var runs = new InMemoryRunStore(tenantContext: new FixedTenantContext(Tenant));
        var inputs = new InMemoryRunInputStore();
        var runId = await SeedRunAsync(runs, withOutput: true, inputs: inputs);
        var scores = new InMemoryRunScoreStore();
        var handler = BuildHandler(
            runs,
            inputs,
            scores,
            [new AsyncJudge("slow", static async (_, token) =>
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
                return new RunJudgment();
            })],
            new OnlineEvaluationOptions { JudgeTimeout = TimeSpan.FromMilliseconds(10) });

        await Should.ThrowAsync<JobRetryException>(() => handler.ExecuteAsync(ExecutionContext(runId, [])).AsTask());

        (await scores.ListAsync(Tenant, runId)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Judge_timeout_cuts_off_the_wait_when_the_judge_ignores_cancellation()
    {
        var runs = new InMemoryRunStore(tenantContext: new FixedTenantContext(Tenant));
        var inputs = new InMemoryRunInputStore();
        var runId = await SeedRunAsync(runs, withOutput: true, inputs: inputs);
        var scores = new InMemoryRunScoreStore();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = BuildHandler(
            runs,
            inputs,
            scores,
            [new AsyncJudge("ignores-cancellation", async (_, _) =>
            {
                started.SetResult();
                await release.Task;
                return JudgeVerdict.Headline("ignores-cancellation", 91);
            })],
            new OnlineEvaluationOptions { JudgeTimeout = TimeSpan.FromMilliseconds(20) });
        var run = await runs.GetRunAsync(runId);

        var judging = handler.JudgeRunAsync(run!).AsTask();
        await started.Task.WaitAsync(TimeSpan.FromSeconds(1));

        try
        {
            var winner = await Task.WhenAny(judging, Task.Delay(TimeSpan.FromSeconds(1)));

            winner.ShouldBe(judging);
            var result = await judging;
            result.Scores.ShouldBeEmpty();
            result.Failures.ShouldHaveSingleItem().ErrorType.ShouldBe("judge_timeout");
        }
        finally
        {
            release.TrySetResult();
        }

        await release.Task;
        (await scores.ListAsync(Tenant, runId)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Caller_cancellation_is_propagated_without_normalizing_it_as_a_judge_failure()
    {
        var runs = new InMemoryRunStore(tenantContext: new FixedTenantContext(Tenant));
        var inputs = new InMemoryRunInputStore();
        var runId = await SeedRunAsync(runs, withOutput: true, inputs: inputs);
        var handler = BuildHandler(
            runs,
            inputs,
            new InMemoryRunScoreStore(),
            [new AsyncJudge("slow", static async (_, token) =>
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
                return new RunJudgment();
            })]);
        var run = await runs.GetRunAsync(runId);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Should.ThrowAsync<OperationCanceledException>(() => handler.JudgeRunAsync(run!, cancellationToken: cancellation.Token).AsTask());
    }

    private static async Task<Guid> SeedRunAsync(
        InMemoryRunStore runs,
        bool withOutput,
        InMemoryRunInputStore? inputs = null)
    {
        var runId = TraconId.NewId();
        var now = DateTimeOffset.UtcNow;

        await runs.StartRunAsync(new RunStartInfo
        {
            RunId = runId,
            AgentName = Agent,
            TenantId = Tenant,
            StartedAt = now,
        });

        if (inputs is not null)
        {
            await inputs.SaveAsync(new RunInputRecord
            {
                RunId = runId,
                TenantId = Tenant,
                Messages = [new ChatMessage(ChatRole.User, "hello")],
                CreatedAt = now,
            });
        }

        if (withOutput)
        {
            await runs.AppendEventAsync(new RunEvent
            {
                RunId = runId,
                Sequence = 0,
                Type = RunEventType.MessageCompleted,
                Timestamp = now,
                Text = "answer text",
            });
        }

        await runs.CompleteRunAsync(new RunCompletion
        {
            RunId = runId,
            Status = RunStatus.Completed,
            CompletedAt = now,
        });

        return runId;
    }

    private static JobContext ExecutionContext(Guid runId, List<JobItemResult> reported) => new()
    {
        Job = Job(),
        Items = [Item(runId)],
        ReportItemAsync = (result, _) =>
        {
            reported.Add(result);
            return default;
        },
        IsCancelledAsync = _ => new ValueTask<bool>(false),
    };

    private static JobRecord Job() => new()
    {
        Id = Guid.NewGuid(),
        TenantId = Tenant,
        HandlerKey = JobHandlerKeys.OnlineEval,
        TargetName = Agent,
        Status = JobStatus.Running,
        Attempt = 1,
        ScheduledFor = DateTimeOffset.UtcNow,
        CreatedAt = DateTimeOffset.UtcNow,
    };

    private static JobItemRecord Item(Guid runId)
        => new() { Id = Guid.NewGuid(), JobId = Guid.NewGuid(), Seq = 0, Input = runId.ToString(), Status = JobItemStatus.Pending };

    private static OnlineEvalJobHandler BuildHandler(
        InMemoryRunStore runs,
        InMemoryRunInputStore inputs,
        InMemoryRunScoreStore scores,
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

    private sealed class ScriptedJudge(string name, Func<RunJudgeContext, RunJudgment> respond) : IRunJudge
    {
        public string Name => name;

        public ValueTask<RunJudgment> JudgeAsync(RunJudgeContext context, CancellationToken cancellationToken = default)
            => new(respond(context));
    }

    private sealed class AsyncJudge(string name, Func<RunJudgeContext, CancellationToken, ValueTask<RunJudgment>> respond) : IRunJudge
    {
        public string Name => name;

        public ValueTask<RunJudgment> JudgeAsync(RunJudgeContext context, CancellationToken cancellationToken = default)
            => respond(context, cancellationToken);
    }

    private sealed class FixedOptionsMonitor(OnlineEvaluationOptions value) : IOptionsMonitor<OnlineEvaluationOptions>
    {
        public OnlineEvaluationOptions CurrentValue => value;

        public OnlineEvaluationOptions Get(string? name) => value;

        public IDisposable? OnChange(Action<OnlineEvaluationOptions, string?> listener) => null;
    }
}
