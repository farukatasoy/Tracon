using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// A workflow step that cannot deliver its agent's answer fails the run.
/// </summary>
/// <remarks>
/// <para>
/// Every workflow participant is a <see cref="ChildAgentInvoker"/>,
/// which for agent DELEGATION returns a wait limit, a budget refusal or a
/// pending approval as the sub-agent's answer, so the calling model can react.
/// A workflow step has no calling model: the refusal sentence became the next
/// node's input and the run ended <c>Completed</c>. The site promises that a
/// node whose call never came back ends the run <c>Failed</c>.
/// </para>
/// <para>
/// The cases run through the real host — DI, the ambient run scope, the MAF
/// graph and the run store — because that is where the refusal turned into
/// content; a unit test of the invoker alone cannot see the graph carry it on.
/// </para>
/// </remarks>
public sealed class WorkflowStepRefusalTests
{
    private const string SlowAgent = "slow";
    private const string FastAgent = "fast";

    [Theory]
    [InlineData(WorkflowKind.Sequential)]
    [InlineData(WorkflowKind.Concurrent)]
    [InlineData(WorkflowKind.GroupChat)]
    [InlineData(WorkflowKind.Handoff)]
    public async Task An_agent_step_past_its_wait_limit_fails_the_workflow(WorkflowKind kind)
    {
        var hanging = new HangingModelProvider("hanging", respectsCancellation: true);

        await using var host = await StartAsync(hanging);

        await SaveAsync(host, new WorkflowSaveRequest { Kind = kind, AgentNames = [SlowAgent, FastAgent] });

        var workflow = await RunAsync(host);

        workflow.Status.ShouldBe(RunStatus.Failed);
        workflow.Error.ShouldNotBeNull();
        workflow.Error.Type.ShouldBe(TraconChildRunTimeoutException.ChildRunTimeoutErrorType);
        workflow.Error.Class.ShouldBe(RunErrorClass.Timeout);
        workflow.Error.Message.ShouldContain($"Agent '{SlowAgent}' did not respond in time", Case.Sensitive);

        (await RootEventTypesAsync(host, workflow.Id)).ShouldContain(RunEventType.ChildRunTimedOut);
    }

    [Fact]
    public async Task A_node_after_a_step_past_its_wait_limit_never_runs()
    {
        var hanging = new HangingModelProvider("hanging", respectsCancellation: true);
        var spoken = 0;

        await using var host = await StartAsync(
            hanging,
            builder => builder
                .UseWorkflows()
                .AddWorkflowFunction<List<ChatMessage>, List<ChatMessage>>(
                    "speak",
                    _ => (messages, _, _) =>
                    {
                        Interlocked.Increment(ref spoken);

                        return new ValueTask<List<ChatMessage>>(messages);
                    }));

        await SaveAsync(host, new WorkflowSaveRequest
        {
            Kind = WorkflowKind.Sequential,
            Nodes =
            [
                new WorkflowNodeReference { Name = SlowAgent, Kind = WorkflowNodeKind.Agent },
                new WorkflowNodeReference { Name = "speak", Kind = WorkflowNodeKind.Function },
            ],
        });

        var workflow = await RunAsync(host);

        spoken.ShouldBe(0, "the refusal sentence reached the next node as its input");
        workflow.Status.ShouldBe(RunStatus.Failed);
        workflow.Error.ShouldNotBeNull().Class.ShouldBe(RunErrorClass.Timeout);
    }

    [Fact]
    public async Task A_step_the_tree_budget_refuses_fails_the_workflow_as_quota_exceeded()
    {
        await using var host = await StartAsync(
            new HangingModelProvider("hanging", respectsCancellation: true),
            configureGraph: static graph => graph.MaxTotalRuns = 1);

        await SaveAsync(host, new WorkflowSaveRequest
        {
            Kind = WorkflowKind.Sequential,
            AgentNames = [FastAgent, "fast-second"],
        });

        var workflow = await RunAsync(host);

        workflow.Status.ShouldBe(RunStatus.Failed);
        workflow.Error.ShouldNotBeNull();
        workflow.Error.Type.ShouldBe(TraconRunBudgetExceededException.RunBudgetExceededErrorType);
        workflow.Error.Class.ShouldBe(RunErrorClass.QuotaExceeded);
    }

    [Fact]
    public async Task A_step_whose_provider_never_answered_fails_the_workflow_with_the_provider_fault()
    {
        // the distinction holds in a workflow too: no wait limit fired, so
        // the step fails with the provider's own fault, not with the limit.
        var timingOut = new TimingOutModelProvider("timing-out");

        await using var host = await TraconTestHost.StartAsync(
            builder => builder
                .AddModelProvider(timingOut)
                .AddAgent(TestData.Definition(SlowAgent) with
                {
                    Model = new ModelBinding { Provider = timingOut.Name, Model = "timing-out-1" },
                })
                .AddAgent(TestData.Definition(FastAgent))
                .UseWorkflows());

        await SaveAsync(host, new WorkflowSaveRequest { Kind = WorkflowKind.Sequential, AgentNames = [SlowAgent, FastAgent] });

        var workflow = await RunAsync(host);

        workflow.Status.ShouldBe(RunStatus.Failed);
        workflow.Error.ShouldNotBeNull();
        string.Equals(workflow.Error.Type, TraconChildRunTimeoutException.ChildRunTimeoutErrorType, StringComparison.Ordinal)
            .ShouldBeFalse("a provider that never answered is not the step's wait limit");
        (await RootEventTypesAsync(host, workflow.Id)).ShouldNotContain(RunEventType.ChildRunTimedOut);

        // The workflow row names the step's own fault exactly as the step's row
        // does. This fake throws HttpClient's raw TaskCanceledException;
        // a real SDK wraps it and the provider normalizer files it as Timeout.
        var step = (await host.Services.GetRequiredService<IRunStore>()
                .QueryRunsAsync(new RunQuery { Take = 20, OnlyRootRuns = false }))
            .Single(run => run.Kind == RunKind.Agent && run.ParentRunId == workflow.Id);

        step.Status.ShouldBe(RunStatus.Failed);
        workflow.Error.Type.ShouldBe(step.Error.ShouldNotBeNull().Type);
        workflow.Error.Class.ShouldBe(step.Error.Class);
    }

    private static Task<TraconTestHost> StartAsync(
        HangingModelProvider hanging,
        Action<ITraconBuilder>? configure = null,
        Action<TraconAgentGraphOptions>? configureGraph = null)
        => TraconTestHost.StartAsync(
            builder =>
            {
                builder
                    .AddModelProvider(hanging)
                    .AddAgent(TestData.Definition(SlowAgent) with
                    {
                        Model = new ModelBinding { Provider = hanging.Name, Model = "hanging-1" },
                    })
                    .AddAgent(TestData.Definition(FastAgent))
                    .AddAgent(TestData.Definition("fast-second"));

                if (configure is null)
                {
                    builder.UseWorkflows();
                }
                else
                {
                    configure(builder);
                }
            },
            configureServices: services => services.Configure<TraconOptions>(options =>
            {
                options.AgentGraph.ChildDeadline = TimeSpan.FromMilliseconds(200);
                options.AgentGraph.WaitTimeout = TimeSpan.FromSeconds(10);
                configureGraph?.Invoke(options.AgentGraph);
            }));

    private static async Task SaveAsync(TraconTestHost host, WorkflowSaveRequest request)
    {
        using var saved = await host.Client.PutAsJsonAsync("/tracon/api/workflows/steps", request);

        saved.StatusCode.ShouldBe(HttpStatusCode.OK, await saved.Content.ReadAsStringAsync());
    }

    private static async Task<RunRecord> RunAsync(TraconTestHost host)
    {
        using (var response = await host.Client.PostAsJsonAsync(
                   "/tracon/api/workflows/steps/run",
                   new WorkflowRunHttpRequest { Message = "write the episode" }))
        {
            _ = await response.Content.ReadAsStringAsync();
        }

        var runs = await host.Services.GetRequiredService<IRunStore>()
            .QueryRunsAsync(new RunQuery { Take = 20, OnlyRootRuns = false });

        return runs.Single(static run => run.Kind == RunKind.Workflow);
    }

    private static async Task<List<RunEventType>> RootEventTypesAsync(TraconTestHost host, Guid runId)
    {
        var types = new List<RunEventType>();

        await foreach (var runEvent in host.Services.GetRequiredService<IRunStore>().ReadEventsAsync(runId))
        {
            types.Add(runEvent.Type);
        }

        return types;
    }
}
