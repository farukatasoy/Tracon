using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;
// K-269: Tracon.Testing.TraconTestHost and this project's own host share a name, so the package is imported one
// type at a time.
using FakeModelProvider = Tracon.Testing.FakeModelProvider;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// Phase 270: the stall and reset limits of a stored Magentic workflow reach the framework's orchestrator.
/// </summary>
/// <remarks>
/// <para>
/// The orchestrator is built by the framework and keeps its limits private, so the proof is what a run does. The
/// manager is scripted to see no progress in every round: with <c>MaxStalls = 1</c> it replans in the second round,
/// and with <c>MaxResets = 1</c> the round after that replan does not run. The cases differ by one limit each, so a
/// limit that stopped reaching the orchestrator changes the message a run ends with. Each ledger is a model call the
/// manager answers from a queue; a ledger the manager cannot parse is asked again by the framework, so the scripts
/// are long enough to outlast those retries.
/// </para>
/// <para>
/// 🚨 Measured against Microsoft Agent Framework 1.26.0: a run that reaches the reset limit does not fail and does
/// not wait. It ends <c>Completed</c> and its output is a message that says it stopped at the limit, in place of the
/// manager's final answer. A consumer that wants the failure reads that message; the docs say so.
/// </para>
/// </remarks>
public sealed class WorkflowMagenticLimitsTests
{
    private const string ResetStop = "maximum reset count limit";
    private const string RoundStop = "maximum round count limit";

    [Fact]
    public async Task Both_limits_together_end_the_task_at_the_reset_limit_before_the_round_limit_is_reached()
    {
        // MaxStalls = 1: the second round without progress is the first one above the limit, so the manager replans
        // there. MaxResets = 1: the round after that replan does not run. That is round 3, below MaxIterations = 4.
        // Under the framework's own stall value (3) the replan would come at round 4, where the round limit is
        // reached first (the test below), so only a MaxStalls that reached the orchestrator gives this message.
        await using var host = await StartAsync(StalledManager());

        await SaveAsync(host, new WorkflowSaveRequest
        {
            Kind = WorkflowKind.Magentic,
            ManagerAgentName = "manager",
            AgentNames = ["writer"],
            MaxStalls = 1,
            MaxResets = 1,
            MaxIterations = 4,
        });

        var body = await RunAsync(host);

        body.ShouldContain(ResetStop, Case.Sensitive);

        var run = await ReadRunAsync(host);

        run.Status.ShouldBe(RunStatus.Completed, run.Error?.Message);
        run.Error.ShouldBeNull();
    }

    [Fact]
    public async Task A_reset_limit_alone_does_not_stop_a_task_whose_replan_comes_after_the_round_limit()
    {
        // The framework's stall value is 3, so a manager that sees no progress replans at round 4. With
        // MaxIterations = 4 the round limit is reached at that moment and its message wins; MaxResets = 1 cannot
        // show. It is the contrast of the test above: the difference is MaxStalls and nothing else.
        await using var host = await StartAsync(StalledManager());

        await SaveAsync(host, new WorkflowSaveRequest
        {
            Kind = WorkflowKind.Magentic,
            ManagerAgentName = "manager",
            AgentNames = ["writer"],
            MaxResets = 1,
            MaxIterations = 4,
        });

        var body = await RunAsync(host);

        body.ShouldContain(RoundStop, Case.Sensitive);
        body.ShouldNotContain(ResetStop, Case.Sensitive);
    }

    [Fact]
    public async Task A_stall_limit_alone_replans_but_never_stops_the_task_at_a_reset_limit()
    {
        await using var host = await StartAsync(StalledManager());

        await SaveAsync(host, new WorkflowSaveRequest
        {
            Kind = WorkflowKind.Magentic,
            ManagerAgentName = "manager",
            AgentNames = ["writer"],
            MaxStalls = 1,
            MaxIterations = 4,
        });

        var body = await RunAsync(host);

        body.ShouldContain(RoundStop, Case.Sensitive);
        body.ShouldNotContain(ResetStop, Case.Sensitive);
    }

    [Fact]
    public async Task A_definition_that_sets_neither_limit_runs_to_the_round_limit()
    {
        await using var host = await StartAsync(StalledManager());

        await SaveAsync(host, new WorkflowSaveRequest
        {
            Kind = WorkflowKind.Magentic,
            ManagerAgentName = "manager",
            AgentNames = ["writer"],
            MaxIterations = 3,
        });

        var body = await RunAsync(host);

        body.ShouldContain(RoundStop, Case.Sensitive);
        body.ShouldNotContain(ResetStop, Case.Sensitive);
    }

    /// <summary>A manager that plans, then sees no progress in every round it is asked about.</summary>
    private static FakeModelProvider StalledManager()
    {
        var provider = new FakeModelProvider("magentic-provider");

        provider.ForModel("manager-model", static cfg =>
        {
            cfg.RespondsWith("FACTS: the task is open.", "PLAN: the writer tries.");

            // Plenty of no-progress ledgers, each followed by the two answers a replan asks the manager for.
            for (var round = 0; round < 12; round++)
            {
                cfg.RespondsWith(NoProgressLedger(), "FACTS: still open.", "PLAN: try again.");
            }
        });
        provider.ForModel("writer-model", static cfg => cfg.RespondsWith("I tried.").EchoesUserMessage());

        return provider;
    }

    private static string NoProgressLedger()
        => """
           {"is_request_satisfied":{"reason":"r","answer":false},
            "is_in_loop":{"reason":"r","answer":false},
            "is_progress_being_made":{"reason":"r","answer":false},
            "next_speaker":{"reason":"r","answer":"writer"},
            "instruction_or_question":{"reason":"r","answer":"Try the task."}}
           """;

    private static Task<TraconTestHost> StartAsync(FakeModelProvider provider)
        => TraconTestHost.StartAsync(builder => builder
            .AddModelProvider(provider)
            .AddAgent(Agent("manager", "manager-model"))
            .AddAgent(Agent("writer", "writer-model"))
            .UseWorkflows());

    private static AgentDefinition Agent(string name, string model)
        => new()
        {
            Name = name,
            Description = $"The {name}.",
            Instructions = $"You are the {name}.",
            Model = new ModelBinding { Provider = "magentic-provider", Model = model },
            Origin = AgentDefinitionOrigin.Code,
        };

    private static async Task SaveAsync(TraconTestHost host, WorkflowSaveRequest request)
    {
        using var saved = await host.Client.PutAsJsonAsync(new Uri("/tracon/api/workflows/plan", UriKind.Relative), request);

        saved.StatusCode.ShouldBe(HttpStatusCode.OK, await saved.Content.ReadAsStringAsync());
    }

    private static async Task<string> RunAsync(TraconTestHost host)
    {
        using var response = await host.Client.PostAsJsonAsync(
            new Uri("/tracon/api/workflows/plan/run", UriKind.Relative),
            new WorkflowRunHttpRequest { Message = "do the task" });

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        return await response.Content.ReadAsStringAsync();
    }

    private static async Task<RunRecord> ReadRunAsync(TraconTestHost host)
    {
        var runs = await host.Services.GetRequiredService<IRunStore>()
            .QueryRunsAsync(new RunQuery { Take = 50, OnlyRootRuns = false });

        return runs.Single(static run => run.Kind == RunKind.Workflow);
    }
}
