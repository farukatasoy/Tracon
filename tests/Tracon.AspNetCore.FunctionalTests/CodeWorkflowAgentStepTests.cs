using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;
using FakeModelProvider = Tracon.Testing.FakeModelProvider;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// An agent step of a graph built in code (phase 211, <c>GetWorkflowAgentStep</c>): its own timeout replaces
/// <c>ChildDeadline</c>, its version pin runs the pinned text of a stored agent, and a pin that cannot hold
/// fails the step by name before a model is called.
/// </summary>
/// <remarks>
/// <para>
/// The same two settings a stored chain gives an agent node (<c>WorkflowAgentNodeTimeoutTests</c>,
/// <c>WorkflowAgentVersionPinTests</c>), through the same wrapper cache and the same invoker; what is new is the
/// way in: a code graph's factory is synchronous and runs on every run, so nothing can check the pin when the graph
/// is built and the step is where it is checked.
/// </para>
/// </remarks>
public sealed class CodeWorkflowAgentStepTests
{
    private const string Flow = "code-steps";
    private const string Writer = "writer";
    private const string Slow = "slow";

    [Fact]
    public async Task A_steps_timeout_replaces_the_installations_child_deadline_for_that_step()
    {
        var hanging = new HangingModelProvider("hanging", respectsCancellation: true);

        // The installation's deadline is far away: only the step's can fire.
        await using var host = await StartHangingAsync(hanging, childDeadline: TimeSpan.FromMinutes(10), step => step.Timeout = TimeSpan.FromMilliseconds(300));

        var run = await RunAsync(host);

        run.Status.ShouldBe(RunStatus.Failed);

        var error = run.Error.ShouldNotBeNull();

        error.Type.ShouldBe(TraconChildRunTimeoutException.ChildRunTimeoutErrorType);
        error.Class.ShouldBe(RunErrorClass.Timeout);
        error.Message.ShouldContain("00:00:00.3", Case.Sensitive);
        error.Message.ShouldContain($"the 'timeout' of node '{Slow}' in workflow '{Flow}'", Case.Sensitive);
    }

    [Fact]
    public async Task A_step_without_a_timeout_keeps_the_installations_child_deadline()
    {
        var hanging = new HangingModelProvider("hanging", respectsCancellation: true);

        await using var host = await StartHangingAsync(hanging, childDeadline: TimeSpan.FromMilliseconds(300), configure: null);

        var run = await RunAsync(host);

        run.Status.ShouldBe(RunStatus.Failed);

        var error = run.Error.ShouldNotBeNull();

        error.Type.ShouldBe(TraconChildRunTimeoutException.ChildRunTimeoutErrorType);
        error.Message.ShouldContain("'Tracon:AgentGraph:ChildDeadline'", Case.Sensitive);
    }

    [Fact]
    public async Task A_pinned_step_runs_the_pinned_text_while_the_agent_is_edited()
    {
        var provider = Pinning();
        await using var host = await StartPinnedAsync(provider, version: 1);

        await CreateAgentAsync(host, "ALPHA text");
        await UpdateAgentAsync(host, "BETA text");

        var run = await RunAsync(host);

        run.Status.ShouldBe(RunStatus.Completed, run.Error?.Message);

        var instructions = provider.Requests.Select(static request => request.Options!.Instructions).ToList();

        instructions.ShouldContain(static text => text!.Contains("ALPHA text", StringComparison.Ordinal));
        instructions.ShouldNotContain(static text => text!.Contains("BETA text", StringComparison.Ordinal));

        // The step's own row records the version that ran.
        (await ChildRowsAsync(host, run.Id)).ShouldHaveSingleItem().AgentVersion.ShouldBe(1);
    }

    [Fact]
    public async Task A_pin_to_a_version_the_agent_does_not_have_fails_the_step_by_name_before_a_model_is_called()
    {
        var provider = Pinning();
        await using var host = await StartPinnedAsync(provider, version: 7);

        await CreateAgentAsync(host, "ALPHA text");

        var run = await RunAsync(host);

        run.Status.ShouldBe(RunStatus.Failed);
        run.Error.ShouldNotBeNull().Message.ShouldContain("7", Case.Sensitive);
        provider.Requests.ShouldBeEmpty("a model was called although the pinned version does not exist");
    }

    [Fact]
    public async Task A_pin_to_an_agent_defined_in_code_fails_the_step_because_it_does_not_keep_history()
    {
        var provider = Pinning();

        await using var host = await StartPinnedAsync(
            provider,
            version: 1,
            static builder => builder.AddAgent(new AgentDefinition
            {
                Name = Writer,
                Instructions = "From code.",
                Model = new ModelBinding { Provider = "pin-provider", Model = "pin-model" },
                Origin = AgentDefinitionOrigin.Code,
            }));

        var run = await RunAsync(host);

        run.Status.ShouldBe(RunStatus.Failed);
        run.Error.ShouldNotBeNull().Message.ShouldContain("does not keep version history", Case.Sensitive);
        provider.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_step_that_asks_for_an_impossible_timeout_or_version_is_refused_when_the_graph_is_built()
    {
        await using var host = await StartPinnedAsync(Pinning(), version: null);

        Should.Throw<ArgumentOutOfRangeException>(
            () => host.Services.GetWorkflowAgentStep(Flow, Writer, step => step.Timeout = TimeSpan.Zero));

        Should.Throw<ArgumentOutOfRangeException>(
            () => host.Services.GetWorkflowAgentStep(Flow, Writer, step => step.Timeout = TimeSpan.FromDays(60)))
            .Message.ShouldContain("must be from 1 to", Case.Sensitive);

        Should.Throw<ArgumentOutOfRangeException>(
                () => host.Services.GetWorkflowAgentStep(Flow, Writer, step => step.AgentVersion = 0))
            .Message.ShouldContain("numbered from 1", Case.Sensitive);
    }

    private static FakeModelProvider Pinning()
        => new FakeModelProvider("pin-provider")
            .WithModel(new ModelDescriptor { Name = "pin-model" })
            .EchoesUserMessage();

    private static Task<TraconTestHost> StartHangingAsync(
        HangingModelProvider hanging,
        TimeSpan childDeadline,
        Action<WorkflowAgentStepOptions>? configure)
        => TraconTestHost.StartAsync(
            builder =>
            {
                builder.AddModelProvider(hanging);

                builder.AddAgent(TestData.Definition(Slow) with
                {
                    Model = new ModelBinding { Provider = hanging.Name, Model = "hanging-1" },
                });

                builder.UseWorkflows();
                builder.AddWorkflow(Flow, services => Single(services, Slow, configure));
            },
            configureServices: services => services.Configure<TraconOptions>(options =>
            {
                options.AgentGraph.ChildDeadline = childDeadline;
                options.AgentGraph.WaitTimeout = childDeadline + TimeSpan.FromSeconds(30);
            }));

    private static Task<TraconTestHost> StartPinnedAsync(FakeModelProvider provider, int? version, Action<ITraconBuilder>? configure = null)
        => TraconTestHost.StartAsync(builder =>
        {
            builder.AddModelProvider(provider);
            builder.UseWorkflows();
            builder.AddWorkflow(Flow, services => Single(services, Writer, version is null ? null : step => step.AgentVersion = version));
            configure?.Invoke(builder);
        });

    private static Workflow Single(IServiceProvider services, string agent, Action<WorkflowAgentStepOptions>? configure)
    {
        var step = configure is null
            ? services.GetWorkflowAgentStep(Flow, agent)
            : services.GetWorkflowAgentStep(Flow, agent, configure);

        return new WorkflowBuilder(step).WithOutputFrom(step).WithName(Flow).Build();
    }

    private static async Task CreateAgentAsync(TraconTestHost host, string instructions)
    {
        using var created = await host.Client.PostAsJsonAsync("/tracon/api/agents", Request(instructions));

        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
    }

    private static async Task UpdateAgentAsync(TraconTestHost host, string instructions)
    {
        using var updated = await host.Client.PutAsJsonAsync($"/tracon/api/agents/{Writer}", Request(instructions));

        updated.StatusCode.ShouldBe(HttpStatusCode.OK, await updated.Content.ReadAsStringAsync());
    }

    private static AgentDefinitionRequest Request(string instructions)
        => new()
        {
            Name = Writer,
            Instructions = instructions,
            Model = new ModelBinding { Provider = "pin-provider", Model = "pin-model" },
        };

    private static async Task<RunRecord> RunAsync(TraconTestHost host)
    {
        using var response = await host.Client.PostAsJsonAsync(
            $"/tracon/api/workflows/{Flow}/run",
            new WorkflowRunHttpRequest { Message = "Episode 12" });

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());

        var frames = await SseReader.ReadAllAsync(await response.Content.ReadAsStreamAsync());
        var accepted = frames.First(static frame => string.Equals(frame.Event, "run", StringComparison.Ordinal));
        var runId = JsonDocument.Parse(accepted.Data).RootElement.GetProperty("runId").GetGuid();

        return (await host.Services.GetRequiredService<IRunStore>().GetRunAsync(runId))!;
    }

    private static async Task<IReadOnlyList<RunRecord>> ChildRowsAsync(TraconTestHost host, Guid rootRunId)
        => (await host.Services.GetRequiredService<IRunStore>().QueryRunsAsync(new RunQuery { OnlyRootRuns = false, Take = 100 }))
            .Where(run => run.ParentRunId == rootRunId)
            .ToList();
}
