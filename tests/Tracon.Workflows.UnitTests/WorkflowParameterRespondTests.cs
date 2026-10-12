using Tracon.Workflows.UnitTests.Fakes;

namespace Tracon.Workflows.UnitTests;

/// <summary>
/// Answering a waiting workflow continues it with the values it started with.
/// </summary>
/// <remarks>
/// <para>
/// 🚨 F-294: a response opens a NEW run row (K-130) and resumes from the source
/// run's checkpoints. The values live only in the source run's
/// <c>run_inputs</c> row; if the continuation did not read them it would either
/// run on the defaults or lose them for the NEXT answer in the chain.
/// </para>
/// <para>
/// A Magentic plan review is the one human-in-the-loop shape a stored (and so
/// parameterized) definition can have; a code-defined workflow carries no
/// schema. The cases use the in-process runner with a stand-in manager, which
/// is enough: the subject is the runner's own bookkeeping, not the model.
/// </para>
/// </remarks>
public sealed class WorkflowParameterRespondTests
{
    private const string Name = "magentic";

    [Fact]
    public async Task The_waiting_run_records_the_final_values_beside_its_row()
    {
        var host = new WorkflowTestHost("manager", "worker");

        await SaveAsync(host, Tone(required: true), Voice());

        var waiting = await StartWaitingRunAsync(host, runner => runner, ("tone", "formal"));

        var record = await host.RunInputs.GetAsync(host.TenantContext.TenantId, waiting.Id);

        record.ShouldNotBeNull();
        record.Messages.ShouldBeEmpty("a workflow row stores no message: the content guard never saw a copy of it");
        record.Parameters.ShouldNotBeNull();
        Render(record.Parameters).ShouldBe(["tone=formal", "voice=alloy"]);
    }

    [Fact]
    public async Task The_answer_continues_with_the_same_values_and_passes_them_on()
    {
        var host = new WorkflowTestHost("manager", "worker");

        await SaveAsync(host, Tone(required: true), Voice());

        var runner = host.CreateRunner();
        var first = await StartWaitingRunAsync(host, _ => runner, ("tone", "formal"));

        var second = await AnswerAsync(host, runner, first);

        // The chain: every link carries its own copy, so the NEXT answer finds it.
        var record = await host.RunInputs.GetAsync(host.TenantContext.TenantId, second.Id);

        record.ShouldNotBeNull();
        record.Parameters.ShouldNotBeNull();
        Render(record.Parameters).ShouldBe(["tone=formal", "voice=alloy"]);

        if (second.Status == RunStatus.AwaitingInput)
        {
            var third = await AnswerAsync(host, runner, second);

            Render((await host.RunInputs.GetAsync(host.TenantContext.TenantId, third.Id))!.Parameters!)
                .ShouldBe(["tone=formal", "voice=alloy"]);
        }
    }

    [Fact]
    public async Task A_run_that_recorded_nothing_cannot_be_answered_when_the_workflow_declares_parameters()
    {
        // 🚨 Fail closed (K-890's spirit): answering it on the defaults would
        // change the instructions the agents were given halfway through the work.
        var host = new WorkflowTestHost("manager", "worker") { RecordsRunInput = false };

        await SaveAsync(host, Tone(required: true), Voice());

        var runner = host.CreateRunner();
        var waiting = await StartWaitingRunAsync(host, _ => runner, ("tone", "formal"));
        var pending = (await runner.ListPendingRequestsAsync(waiting.Id)).Single();

        var refusal = await Should.ThrowAsync<TraconException>(async () =>
        {
            await foreach (var _ in runner.RespondStreamingAsync(new WorkflowRespondRequest
            {
                RunId = waiting.Id,
                RequestId = pending.RequestId,
                Approved = true,
            }))
            {
                // Nothing is expected.
            }
        });

        refusal.Message.ShouldContain("did not record the values it started with", Case.Sensitive);
        refusal.Message.ShouldContain("RecordRunInput", Case.Sensitive);

        // No second row: the refusal came before a run was opened.
        (await host.RunStore.QueryRunsAsync(new RunQuery { Take = 10 })).Count.ShouldBe(1);
    }

    [Fact]
    public async Task A_workflow_that_declares_no_parameters_is_answered_exactly_as_before()
    {
        var host = new WorkflowTestHost("manager", "worker") { RecordsRunInput = false };

        await SaveAsync(host);

        var runner = host.CreateRunner();
        var waiting = await StartWaitingRunAsync(host, _ => runner);

        var second = await AnswerAsync(host, runner, waiting);

        second.WorkflowName.ShouldBe(Name);
    }

    [Fact]
    public async Task A_schema_that_gained_a_required_name_refuses_the_answer_and_names_it()
    {
        var host = new WorkflowTestHost("manager", "worker");

        await SaveAsync(host, Tone(required: true));

        var runner = host.CreateRunner();
        var waiting = await StartWaitingRunAsync(host, _ => runner, ("tone", "formal"));
        var pending = (await runner.ListPendingRequestsAsync(waiting.Id)).Single();

        await SaveAsync(host, Tone(required: true), new AgentParameter { Name = "audience", Kind = AgentParameterKind.Text, Required = true });

        var refusal = await Should.ThrowAsync<TraconException>(async () =>
        {
            await foreach (var _ in runner.RespondStreamingAsync(new WorkflowRespondRequest
            {
                RunId = waiting.Id,
                RequestId = pending.RequestId,
                Approved = true,
            }))
            {
                // Nothing is expected.
            }
        });

        refusal.Message.ShouldContain("changed its parameters", Case.Sensitive);
        refusal.Message.ShouldContain("'audience'", Case.Sensitive);
    }

    [Fact]
    public async Task A_name_the_schema_no_longer_declares_is_dropped_not_refused()
    {
        var host = new WorkflowTestHost("manager", "worker");

        await SaveAsync(host, Tone(required: true), Voice());

        var runner = host.CreateRunner();
        var waiting = await StartWaitingRunAsync(host, _ => runner, ("tone", "formal"));

        // The operator removes 'voice'. A value no instruction can reach is
        // harmless; refusing the answer for it would strand a waiting run.
        await SaveAsync(host, Tone(required: true));

        var second = await AnswerAsync(host, runner, waiting);

        Render((await host.RunInputs.GetAsync(host.TenantContext.TenantId, second.Id))!.Parameters!)
            .ShouldBe(["tone=formal"]);
    }

    [Fact]
    public async Task A_resume_from_a_checkpoint_carries_the_values_too()
    {
        var host = new WorkflowTestHost("manager", "worker");

        await SaveAsync(host, Tone(required: true), Voice());

        var runner = host.CreateRunner();
        var waiting = await StartWaitingRunAsync(host, _ => runner, ("tone", "formal"));

        await foreach (var _ in runner.ResumeStreamingAsync(new WorkflowResumeRequest { RunId = waiting.Id }))
        {
            // The events are not the point of this case.
        }

        var resumed = (await host.RunStore.QueryRunsAsync(new RunQuery { Take = 10 }))
            .Single(run => run.Id != waiting.Id);

        Render((await host.RunInputs.GetAsync(host.TenantContext.TenantId, resumed.Id))!.Parameters!)
            .ShouldBe(["tone=formal", "voice=alloy"]);
    }

    [Fact]
    public async Task The_runner_refuses_a_missing_value_itself_before_any_model_call()
    {
        // The runner is public API: a consumer can skip HTTP, and then the
        // endpoint's early 400 is not there to catch the mistake.
        var host = new WorkflowTestHost("manager", "worker");

        await SaveAsync(host, Tone(required: true));

        var runner = host.CreateRunner();

        await foreach (var _ in runner.RunStreamingAsync(new WorkflowRunRequest { WorkflowName = Name, Message = "go" }))
        {
            // Drain.
        }

        var run = (await host.RunStore.QueryRunsAsync(new RunQuery { Take = 10, OnlyRootRuns = false })).Single();

        run.Status.ShouldBe(RunStatus.Failed);
        run.Error.ShouldNotBeNull().Message.ShouldContain("Missing required parameter 'tone'", Case.Sensitive);
        (await host.RunStore.QueryRunsAsync(new RunQuery { Take = 10, OnlyRootRuns = false }))
            .ShouldNotContain(candidate => candidate.ParentRunId == run.Id, "no step started");
        (await host.RunInputs.GetAsync(host.TenantContext.TenantId, run.Id)).ShouldBeNull();
    }

    [Fact]
    public async Task The_runner_refuses_an_undeclared_name_itself()
    {
        var host = new WorkflowTestHost("manager", "worker");

        await SaveAsync(host, Tone(required: true));

        var runner = host.CreateRunner();

        await foreach (var _ in runner.RunStreamingAsync(new WorkflowRunRequest
        {
            WorkflowName = Name,
            Message = "go",
            Parameters = Values(("tone", "formal"), ("tonne", "typo")),
        }))
        {
            // Drain.
        }

        var run = (await host.RunStore.QueryRunsAsync(new RunQuery { Take = 10 })).Single();

        run.Status.ShouldBe(RunStatus.Failed);
        run.Error.ShouldNotBeNull().Message.ShouldContain("Unknown parameter 'tonne'", Case.Sensitive);
    }

    [Fact]
    public async Task A_failing_input_store_does_not_stop_the_run()
    {
        // The record is best effort, like the agent path's: the run continues
        // and the consequence (a later answer cannot find the values) is the
        // fail-closed refusal above, with a message that says so.
        var host = new WorkflowTestHost("manager", "worker");

        await SaveAsync(host, Tone(required: true));

        var runner = host.CreateRunnerWithRunInputStore(new ThrowingRunInputStore());

        await foreach (var _ in runner.RunStreamingAsync(new WorkflowRunRequest
        {
            WorkflowName = Name,
            Message = "go",
            Parameters = Values(("tone", "formal")),
        }))
        {
            // Drain.
        }

        var run = (await host.RunStore.QueryRunsAsync(new RunQuery { Take = 10 })).Single();

        run.Status.ShouldBe(RunStatus.AwaitingInput, run.Error?.Message);
    }

    private static async Task SaveAsync(WorkflowTestHost host, params AgentParameter[] parameters)
        => await host.SaveAsync(new WorkflowDefinition
        {
            Name = Name,
            Kind = WorkflowKind.Magentic,
            AgentNames = ["worker"],
            ManagerAgentName = "manager",
            MaxIterations = 2,
            RequirePlanApproval = true,
            Parameters = parameters,
        });

    private static AgentParameter Tone(bool required) => new()
    {
        Name = "tone",
        Kind = AgentParameterKind.Text,
        Required = required,
    };

    private static AgentParameter Voice() => new()
    {
        Name = "voice",
        Kind = AgentParameterKind.Text,
        DefaultValue = "alloy",
    };

    private static Dictionary<string, string> Values(params (string Name, string Value)[] pairs)
        => pairs.ToDictionary(static pair => pair.Name, static pair => pair.Value, StringComparer.Ordinal);

    private static List<string> Render(IReadOnlyDictionary<string, string> values)
        => [.. values.OrderBy(static pair => pair.Key, StringComparer.Ordinal).Select(static pair => $"{pair.Key}={pair.Value}")];

    private static async Task<RunRecord> StartWaitingRunAsync(
        WorkflowTestHost host,
        Func<WorkflowRunner, WorkflowRunner> select,
        params (string Name, string Value)[] values)
    {
        var runner = select(host.CreateRunner());

        await foreach (var _ in runner.RunStreamingAsync(new WorkflowRunRequest
        {
            WorkflowName = Name,
            Message = "prepare the report",
            Parameters = values.Length == 0 ? null : Values(values),
        }))
        {
            // The first round only builds the plan.
        }

        var run = (await host.RunStore.QueryRunsAsync(new RunQuery { Take = 10 })).Single();

        run.Status.ShouldBe(RunStatus.AwaitingInput, run.Error?.Message);

        return run;
    }

    private static async Task<RunRecord> AnswerAsync(WorkflowTestHost host, WorkflowRunner runner, RunRecord source)
    {
        var pending = (await runner.ListPendingRequestsAsync(source.Id)).Single();

        await foreach (var _ in runner.RespondStreamingAsync(new WorkflowRespondRequest
        {
            RunId = source.Id,
            RequestId = pending.RequestId,
            Approved = true,
        }))
        {
            // Drain.
        }

        return (await host.RunStore.QueryRunsAsync(new RunQuery { Take = 20 }))
            .Where(run => run.Id != source.Id && run.StartedAt >= source.StartedAt)
            .OrderByDescending(static run => run.StartedAt)
            .First();
    }
}
