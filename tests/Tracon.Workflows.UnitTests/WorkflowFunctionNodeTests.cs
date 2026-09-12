using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using Tracon.Workflows.UnitTests.Fakes;

namespace Tracon.Workflows.UnitTests;

/// <summary>
/// Function nodes (phase 71): a Sequential chain that mixes agents from the
/// catalog with a plain function registered in code.
/// </summary>
public sealed class WorkflowFunctionNodeTests
{
    [Fact]
    public void Registering_the_same_function_name_twice_fails_at_registry_construction()
    {
        var host = new WorkflowTestHost();

        host.AddFunction<string, string>("uppercase", static (input, _, _) => new ValueTask<string>(input.ToUpperInvariant()));
        host.AddFunction<string, string>("uppercase", static (input, _, _) => new ValueTask<string>(input.ToUpperInvariant()));

        var exception = Should.Throw<TraconException>(() => host.Functions);

        exception.Message.ShouldContain("uppercase", Case.Sensitive);
        exception.Message.ShouldContain("More than one workflow function", Case.Sensitive);
    }

    [Fact]
    public void Function_catalog_lists_registered_functions_ordered_by_name()
    {
        var host = new WorkflowTestHost();

        host.AddFunction<string, string>("zebra", static (input, _, _) => new ValueTask<string>(input), "z");
        host.AddFunction<string, string>("alpha", static (input, _, _) => new ValueTask<string>(input), "a");

        var descriptors = host.Functions.List();

        descriptors.Select(descriptor => descriptor.Name).ShouldBe(["alpha", "zebra"]);
        descriptors[0].Description.ShouldBe("a");
        descriptors[0].InputType.ShouldBe(typeof(string));
        descriptors[0].OutputType.ShouldBe(typeof(string));
    }

    [Fact]
    public async Task Definition_pointing_to_an_unregistered_function_fails_at_compile_time()
    {
        var host = new WorkflowTestHost("writer");

        var exception = await Should.ThrowAsync<TraconException>(async () =>
            await host.Compiler.CompileAsync(new WorkflowDefinition
            {
                Name = "chain",
                Kind = WorkflowKind.Sequential,
                Nodes =
                [
                    new WorkflowNodeReference { Name = "writer", Kind = WorkflowNodeKind.Agent },
                    new WorkflowNodeReference { Name = "missing-function", Kind = WorkflowNodeKind.Function },
                ],
            }));

        exception.Message.ShouldContain("'missing-function'", Case.Sensitive);
        exception.Message.ShouldContain("no workflow function registered", Case.Sensitive);
    }

    [Fact]
    public async Task Mixed_chain_compiles_and_the_function_node_is_classified_correctly_in_the_graph()
    {
        var host = new WorkflowTestHost("writer");
        host.AddFunction<string, string>("uppercase", static (input, _, _) => new ValueTask<string>(input.ToUpperInvariant()));

        var workflow = await host.Compiler.CompileAsync(new WorkflowDefinition
        {
            Name = "chain",
            Kind = WorkflowKind.Sequential,
            Nodes =
            [
                new WorkflowNodeReference { Name = "writer", Kind = WorkflowNodeKind.Agent },
                new WorkflowNodeReference { Name = "uppercase", Kind = WorkflowNodeKind.Function },
            ],
        });

        var graph = WorkflowGraphReader.Read("chain", workflow);
        var functionNode = graph.Nodes.Single(candidate => string.Equals(candidate.Id, "uppercase", StringComparison.Ordinal));

        functionNode.Kind.ShouldBe(WorkflowNodeKind.Function);
        functionNode.AgentName.ShouldBeNull();
        functionNode.Label.ShouldBe("uppercase");

        // 🚨 Regression check (phase 71): an agent step in a mixed chain is
        // bound as a FunctionExecutor subtype too (see the compiler's remarks
        // on WorkflowAgentStepExecutor), so it must stay classified as Agent
        // and keep its AgentName - not fall through to Function just because
        // its runtime executor type is FunctionExecutor-derived.
        var agentNode = graph.Nodes.Single(candidate => string.Equals(candidate.Id, "writer", StringComparison.Ordinal));

        agentNode.Kind.ShouldBe(WorkflowNodeKind.Agent);
        agentNode.AgentName.ShouldBe("writer");
    }

    [Fact]
    public async Task Agent_function_agent_chain_runs_end_to_end()
    {
        var host = new WorkflowTestHost("one", "two");

        host.AddFunction<List<ChatMessage>, List<ChatMessage>>(
            "uppercase",
            static (messages, _, _) =>
            {
                var text = messages.LastOrDefault()?.Text ?? string.Empty;

                return new ValueTask<List<ChatMessage>>([new ChatMessage(ChatRole.User, text.ToUpperInvariant())]);
            },
            "Uppercases the last message.");

        await host.SaveAsync(new WorkflowDefinition
        {
            Name = "mixed-chain",
            Kind = WorkflowKind.Sequential,
            Nodes =
            [
                new WorkflowNodeReference { Name = "one", Kind = WorkflowNodeKind.Agent },
                new WorkflowNodeReference { Name = "uppercase", Kind = WorkflowNodeKind.Function },
                new WorkflowNodeReference { Name = "two", Kind = WorkflowNodeKind.Agent },
            ],
        });

        var runner = host.CreateRunner();
        var events = await Collect(runner, "mixed-chain", "hi");

        // EchoAgent "one" tags "hi" -> "[one]hi"; the function upper-cases it
        // -> "[ONE]HI"; EchoAgent "two" tags that -> "[two][ONE]HI". If the
        // function node were skipped or its output not forwarded, the middle
        // segment would still be lowercase.
        var output = events.Single(runEvent => runEvent.Type == RunEventType.WorkflowOutput);

        output.Text.ShouldNotBeNull();
        output.Text!.ShouldContain("[two][ONE]HI", Case.Sensitive);

        // ExecutorInvoked/ExecutorCompleted are keyed by executor id, and the
        // function node's id is its own name (fixed, no agent-derived suffix).
        events.ShouldContain(runEvent =>
            runEvent.Type == RunEventType.ExecutorInvoked &&
            string.Equals(runEvent.Text, "uppercase", StringComparison.Ordinal));
        events.ShouldContain(runEvent =>
            runEvent.Type == RunEventType.ExecutorCompleted &&
            string.Equals(runEvent.Text, "uppercase", StringComparison.Ordinal));

        var runs = await host.RunStore.QueryRunsAsync(new RunQuery { OnlyRootRuns = false, Take = 100 });
        var root = runs.Single(run => run.Kind == RunKind.Workflow);

        root.Status.ShouldBe(RunStatus.Completed);

        // The function node opens no `runs` row of its own: only the two
        // agent steps do. This is what keeps its cost contribution at zero
        // (phase 71 DoD) without any special-casing in the cost rollup.
        runs.Count(run => run.Kind == RunKind.Agent).ShouldBe(2);
    }

    [Fact]
    public async Task A_function_node_that_throws_fails_the_run_without_leaking_the_raw_message()
    {
        var host = new WorkflowTestHost("one");

        host.AddFunction<List<ChatMessage>, List<ChatMessage>>(
            "explode",
            static (_, _, _) => throw new InvalidOperationException("function node blew up"));

        await host.SaveAsync(new WorkflowDefinition
        {
            Name = "failing-chain",
            Kind = WorkflowKind.Sequential,
            Nodes =
            [
                new WorkflowNodeReference { Name = "one", Kind = WorkflowNodeKind.Agent },
                new WorkflowNodeReference { Name = "explode", Kind = WorkflowNodeKind.Function },
            ],
        });

        var runner = host.CreateRunner();
        var events = await Collect(runner, "failing-chain", "hi");

        var runs = await host.RunStore.QueryRunsAsync(new RunQuery { OnlyRootRuns = false, Take = 100 });
        var root = runs.Single(run => run.Kind == RunKind.Workflow);

        root.Status.ShouldBe(RunStatus.Failed);
        root.Error.ShouldNotBeNull();

        // Phase 119 (BL-027/BL-037): a foreign exception's own message never reaches a
        // persistent field; only its type name and a correlation id do.
        root.Error!.Message.ShouldContain(nameof(InvalidOperationException), Case.Sensitive);
        root.Error.Message.ShouldNotContain("function node blew up", Case.Sensitive);

        events.ShouldContain(runEvent => runEvent.Type == RunEventType.RunFailed);
    }

    [Fact]
    public async Task Cancellation_requested_inside_a_function_node_still_records_Canceled()
    {
        // Same shape as WorkflowCancellationTests.Cancellation_during_a_step_records_Canceled_not_Completed:
        // MAF does not honor a mid-step cancellation (K-432), so the runner
        // enforces it itself at the next super-step boundary - a mechanism
        // that lives in WorkflowRunner.PumpAsync and does not care what kind
        // of node just ran. This proves that also holds for a function node.
        using var trigger = new CancellationTokenSource();

        var host = new WorkflowTestHost("one");

        host.AddFunction<List<ChatMessage>, List<ChatMessage>>(
            "cancel-mid-flight",
            (messages, _, _) =>
            {
                trigger.Cancel();

                return new ValueTask<List<ChatMessage>>(messages);
            });

        await host.SaveAsync(new WorkflowDefinition
        {
            Name = "chain",
            Kind = WorkflowKind.Sequential,
            Nodes =
            [
                new WorkflowNodeReference { Name = "cancel-mid-flight", Kind = WorkflowNodeKind.Function },
                new WorkflowNodeReference { Name = "one", Kind = WorkflowNodeKind.Agent },
            ],
        });

        var runner = host.CreateRunner();

        try
        {
            await foreach (var _ in runner.RunStreamingAsync(
                new WorkflowRunRequest { WorkflowName = "chain", Message = "hello" },
                trigger.Token))
            {
            }
        }
        catch (OperationCanceledException)
        {
        }

        var runs = await host.RunStore.QueryRunsAsync(new RunQuery { OnlyRootRuns = false, Take = 100 });
        var root = runs.Single(run => run.Kind == RunKind.Workflow);

        root.Status.ShouldBe(RunStatus.Canceled);
    }

    [Fact]
    public async Task Resuming_an_ALREADY_COMPLETED_mixed_chain_does_NOT_re_run_its_function_node()
    {
        // Measured (phase 71): resuming from the LATEST checkpoint of an
        // already-Completed run (no explicit checkpointId - what
        // WorkflowResumeRequest defaults to) does not invoke the function
        // node a second time. The checkpoint already captures the graph at
        // its finished state, so there is nothing left to execute - MAF
        // does not "replay" super-steps that already ran, it only continues
        // FROM the saved point forward.
        var host = new WorkflowTestHost("one");
        var invocations = 0;

        host.AddFunction<List<ChatMessage>, List<ChatMessage>>(
            "count-calls",
            (messages, _, _) =>
            {
                Interlocked.Increment(ref invocations);

                return new ValueTask<List<ChatMessage>>(messages);
            });

        await host.SaveAsync(new WorkflowDefinition
        {
            Name = "chain",
            Kind = WorkflowKind.Sequential,
            Nodes =
            [
                new WorkflowNodeReference { Name = "one", Kind = WorkflowNodeKind.Agent },
                new WorkflowNodeReference { Name = "count-calls", Kind = WorkflowNodeKind.Function },
            ],
        });

        var runner = host.CreateRunner();
        await Collect(runner, "chain", "hello");

        invocations.ShouldBe(1);

        var first = (await host.RunStore.QueryRunsAsync(new RunQuery { OnlyRootRuns = false, Take = 100 }))
            .Single(run => run.Kind == RunKind.Workflow);

        await foreach (var _ in runner.ResumeStreamingAsync(new WorkflowResumeRequest { RunId = first.Id }))
        {
        }

        invocations.ShouldBe(1);
    }

    [Fact]
    public async Task Resuming_from_an_EARLIER_checkpoint_RE_RUNS_the_function_node_that_follows_it()
    {
        // 🚨 Measured (phase 71, the plan's own "riskiest failure mode"):
        // an explicit checkpointId from BEFORE the function node's super-step
        // (the shape a real crash-recovery resume takes - the process died
        // before the LATEST checkpoint was written) replays that super-step,
        // and the function runs again. This is the concrete case the phase
        // doc's idempotency requirement is about: a function node registered
        // with AddWorkflowFunction MUST be idempotent, because resuming from
        // a checkpoint older than its own completion WILL invoke it again.
        var host = new WorkflowTestHost("one");
        var invocations = 0;

        host.AddFunction<List<ChatMessage>, List<ChatMessage>>(
            "count-calls",
            (messages, _, _) =>
            {
                Interlocked.Increment(ref invocations);

                return new ValueTask<List<ChatMessage>>(messages);
            });

        await host.SaveAsync(new WorkflowDefinition
        {
            Name = "chain",
            Kind = WorkflowKind.Sequential,
            Nodes =
            [
                new WorkflowNodeReference { Name = "one", Kind = WorkflowNodeKind.Agent },
                new WorkflowNodeReference { Name = "count-calls", Kind = WorkflowNodeKind.Function },
            ],
        });

        var runner = host.CreateRunner();
        await Collect(runner, "chain", "hello");

        invocations.ShouldBe(1);

        var first = (await host.RunStore.QueryRunsAsync(new RunQuery { OnlyRootRuns = false, Take = 100 }))
            .Single(run => run.Kind == RunKind.Workflow);

        var checkpoints = await host.CheckpointStore.ListByRunAsync(host.TenantContext.TenantId, first.Id);

        // The checkpoint written right after "one" (before "count-calls" ran)
        // is not the last one - the run wrote a second checkpoint after the
        // function completed too.
        checkpoints.Count.ShouldBeGreaterThanOrEqualTo(2);
        var beforeFunctionRan = checkpoints[0];

        await foreach (var _ in runner.ResumeStreamingAsync(new WorkflowResumeRequest
        {
            RunId = first.Id,
            CheckpointId = beforeFunctionRan.CheckpointId,
        }))
        {
        }

        invocations.ShouldBe(2);
    }

    private static async Task<List<RunEvent>> Collect(WorkflowRunner runner, string name, string message)
    {
        var events = new List<RunEvent>();

        await foreach (var runEvent in runner.RunStreamingAsync(new WorkflowRunRequest
        {
            WorkflowName = name,
            Message = message,
        }))
        {
            events.Add(runEvent);
        }

        return events;
    }
}
