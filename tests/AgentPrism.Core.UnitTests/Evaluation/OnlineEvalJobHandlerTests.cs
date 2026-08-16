using Microsoft.Extensions.AI;

namespace AgentPrism.Core.UnitTests.Evaluation;

/// <summary>Tests for the online evaluation job handler (Phase 49).</summary>
public sealed class OnlineEvalJobHandlerTests
{
    private const string Tenant = "acme";
    private const string Agent = "support";

    [Fact]
    public void Kind_is_OnlineEval()
    {
        var handler = BuildHandler(new InMemoryRunStore(tenantContext: new FixedTenantContext(Tenant)), new InMemoryRunInputStore(), new InMemoryRunScoreStore(), []);
        handler.Kind.ShouldBe(JobKind.OnlineEval);
    }

    [Fact]
    public async Task Missing_run_produces_no_error_and_writes_no_score()
    {
        var runs = new InMemoryRunStore(tenantContext: new FixedTenantContext(Tenant));
        var scores = new InMemoryRunScoreStore();
        var handler = BuildHandler(runs, new InMemoryRunInputStore(), scores, [new ScriptedJudge("model", static _ => new RunJudgment { Score = 80 })]);

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
        var handler = BuildHandler(runs, new InMemoryRunInputStore(), scores, [new ScriptedJudge("model", static _ => new RunJudgment { Score = 80 })]);
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
        var handler = BuildHandler(runs, inputs, scores, [new ScriptedJudge("model", static _ => new RunJudgment { Score = 80 })]);
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
            [new ScriptedJudge("model", static _ => new RunJudgment { Score = 42, Reason = "reason" })]);
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
            [new ScriptedJudge("model", static _ => new RunJudgment { Score = null, Reason = "unclear" })]);
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

        var okJudge = new ScriptedJudge("good", static _ => new RunJudgment { Score = 70 });
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

        var judge = new ScriptedJudge("model", static _ => new RunJudgment { Score = 55 });
        var scores = new InMemoryRunScoreStore();
        var handler = BuildHandler(runs, inputs, scores, [judge]);

        var run = await runs.GetRunAsync(runId);

        await handler.JudgeRunAsync(run!);
        await handler.JudgeRunAsync(run!);

        (await scores.ListAsync(Tenant, runId)).Count.ShouldBe(1);
    }

    private static async Task<Guid> SeedRunAsync(
        InMemoryRunStore runs,
        bool withOutput,
        InMemoryRunInputStore? inputs = null)
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
        Kind = JobKind.OnlineEval,
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
        IReadOnlyList<IRunJudge> judges)
        => new(runs, inputs, scores, judges, new FixedTenantContext(Tenant));

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
}
