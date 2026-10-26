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
/// fails the step by name before a model is called. A run parameter can choose the version instead of the
/// graph (phase 249, <c>AgentVersionParameter</c>): the value is read each time the step runs, so one graph
/// serves every version.
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
    private const string VersionParameter = "agentVersion";

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
        var message = run.Error.ShouldNotBeNull().Message;

        message.ShouldContain("Version 7", Case.Sensitive);
        message.ShouldContain("'writer'", Case.Sensitive);
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

    [Fact]
    public async Task A_run_parameter_picks_the_version_the_step_runs_and_its_row_records_it()
    {
        var provider = Pinning();
        await using var host = await StartSelectingAsync(provider);

        await CreateAgentAsync(host, "ALPHA text");
        await UpdateAgentAsync(host, "BETA text");
        await UpdateAgentAsync(host, "GAMMA text");

        var run = await RunAsync(host, Values((VersionParameter, "1")));

        run.Status.ShouldBe(RunStatus.Completed, run.Error?.Message);

        var instructions = provider.Requests.Select(static request => request.Options!.Instructions).ToList();

        instructions.ShouldContain(static text => text!.Contains("ALPHA text", StringComparison.Ordinal));
        instructions.ShouldNotContain(static text => text!.Contains("GAMMA text", StringComparison.Ordinal));
        (await ChildRowsAsync(host, run.Id)).ShouldHaveSingleItem().AgentVersion.ShouldBe(1);
    }

    [Fact]
    public async Task One_graph_serves_every_version_because_the_value_is_read_each_time_the_step_runs()
    {
        // The factory builds the graph before a run's values are bound and the wrapper is shared by every run:
        // had the first value been captured, the second and third run would repeat it.
        var provider = Pinning();
        await using var host = await StartSelectingAsync(provider);

        await CreateAgentAsync(host, "ALPHA text");
        await UpdateAgentAsync(host, "BETA text");
        await UpdateAgentAsync(host, "GAMMA text");

        var first = await RunAsync(host, Values((VersionParameter, "1")));
        var second = await RunAsync(host, Values((VersionParameter, "2")));
        var third = await RunAsync(host, Values((VersionParameter, "3")));

        foreach (var (run, version) in new[] { (first, 1), (second, 2), (third, 3) })
        {
            run.Status.ShouldBe(RunStatus.Completed, run.Error?.Message);
            (await ChildRowsAsync(host, run.Id)).ShouldHaveSingleItem().AgentVersion.ShouldBe(version);
        }

        provider.Requests.Select(static request => request.Options!.Instructions).ToList().ShouldBe(
        [
            "ALPHA text",
            "BETA text",
            "GAMMA text",
        ]);
    }

    [Fact]
    public async Task The_selected_version_decides_whether_the_agent_takes_the_workflows_values()
    {
        // Version 1 declares no parameter; the current version 2 declares {{tone}}. The step must read the schema of the
        // version the run asked for: the current one would bind the tone and compile version 2's text.
        var provider = Pinning();
        var tone = new[] { new AgentParameter { Name = "tone", Kind = AgentParameterKind.Text, DefaultValue = "plain" } };

        await using var host = await StartSelectingAsync(
            provider,
            parameters:
            [
                new AgentParameter { Name = VersionParameter, Kind = AgentParameterKind.Number },
                new AgentParameter { Name = "tone", Kind = AgentParameterKind.Text, DefaultValue = "plain" },
            ]);

        await CreateAgentAsync(host, "ALPHA text");
        await UpdateAgentAsync(host, "BETA in a {{tone}} tone", tone);

        var run = await RunAsync(host, Values((VersionParameter, "1"), ("tone", "formal")));

        run.Status.ShouldBe(RunStatus.Completed, run.Error?.Message);
        provider.Requests.ShouldHaveSingleItem().Options!.Instructions.ShouldBe("ALPHA text");
        (await ChildRowsAsync(host, run.Id)).ShouldHaveSingleItem().AgentVersion.ShouldBe(1);
    }

    [Fact]
    public async Task A_step_whose_agent_declares_parameters_runs_the_selected_version_when_the_workflow_supplies_none_of_them()
    {
        // The workflow's only parameter is the version: nothing of its values reaches the agent, and the agent's own
        // defaults are bound. That branch resolves the agent again, and it must keep the selected version.
        var provider = Pinning();
        var tone = new[] { new AgentParameter { Name = "tone", Kind = AgentParameterKind.Text, DefaultValue = "plain" } };

        await using var host = await StartSelectingAsync(provider);

        await CreateAgentAsync(host, "ALPHA in a {{tone}} tone", tone);
        await UpdateAgentAsync(host, "BETA in a {{tone}} tone", tone);

        var run = await RunAsync(host, Values((VersionParameter, "1")));

        run.Status.ShouldBe(RunStatus.Completed, run.Error?.Message);
        provider.Requests.ShouldHaveSingleItem().Options!.Instructions.ShouldBe("ALPHA in a plain tone");
        (await ChildRowsAsync(host, run.Id)).ShouldHaveSingleItem().AgentVersion.ShouldBe(1);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task A_run_that_leaves_the_version_parameter_empty_or_out_runs_the_current_version(string? value)
    {
        var provider = Pinning();
        await using var host = await StartSelectingAsync(provider);

        await CreateAgentAsync(host, "ALPHA text");
        await UpdateAgentAsync(host, "BETA text");

        var run = await RunAsync(host, value is null ? null : Values((VersionParameter, value)));

        run.Status.ShouldBe(RunStatus.Completed, run.Error?.Message);
        provider.Requests.ShouldHaveSingleItem().Options!.Instructions.ShouldBe("BETA text");
        (await ChildRowsAsync(host, run.Id)).ShouldHaveSingleItem().AgentVersion.ShouldBe(2);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("1.5")]
    [InlineData("+2")]
    [InlineData(" 2")]
    [InlineData("99999999999")]
    public async Task A_value_that_is_not_a_version_number_fails_the_step_by_name_before_a_model_is_called(string value)
    {
        var provider = Pinning();
        await using var host = await StartSelectingAsync(provider);

        await CreateAgentAsync(host, "ALPHA text");

        var run = await RunAsync(host, Values((VersionParameter, value)));

        run.Status.ShouldBe(RunStatus.Failed);

        var message = run.Error.ShouldNotBeNull().Message;

        message.ShouldContain($"'{VersionParameter}'", Case.Sensitive);
        message.ShouldContain($"'{Writer}'", Case.Sensitive);

        // The value is the caller's content: the text that reaches a run's error and a log line names the parameter only.
        message.ShouldNotContain(value, Case.Sensitive);
        provider.Requests.ShouldBeEmpty("a model was called although the version value cannot be held");
        (await ChildRowsAsync(host, run.Id)).ShouldBeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_name_the_workflow_does_not_declare_fails_the_step_by_name_before_a_model_is_called(bool declaresOtherName)
    {
        // The second case is the quiet one: the workflow declares a parameter, only not the one the step reads, so the
        // final value set is not empty and a missing name looks the same as a declared parameter nobody gave.
        var provider = Pinning();

        await using var host = await StartSelectingAsync(
            provider,
            parameters: declaresOtherName ? [new AgentParameter { Name = "agentVerison", Kind = AgentParameterKind.Number }] : []);

        await CreateAgentAsync(host, "ALPHA text");

        var run = await RunAsync(host, declaresOtherName ? Values(("agentVerison", "1")) : null);

        run.Status.ShouldBe(RunStatus.Failed);

        var message = run.Error.ShouldNotBeNull().Message;

        message.ShouldContain($"'{VersionParameter}'", Case.Sensitive);
        message.ShouldContain($"'{Writer}'", Case.Sensitive);
        message.ShouldContain($"'{Flow}'", Case.Sensitive);
        provider.Requests.ShouldBeEmpty("a model was called although the step reads a parameter the workflow does not declare");
        (await ChildRowsAsync(host, run.Id)).ShouldBeEmpty();
    }

    [Fact]
    public async Task A_version_the_agent_does_not_have_fails_the_step_by_name_before_a_model_is_called_when_a_parameter_names_it()
    {
        var provider = Pinning();
        await using var host = await StartSelectingAsync(provider);

        await CreateAgentAsync(host, "ALPHA text");

        var run = await RunAsync(host, Values((VersionParameter, "7")));

        run.Status.ShouldBe(RunStatus.Failed);

        var message = run.Error.ShouldNotBeNull().Message;

        message.ShouldContain("Version 7", Case.Sensitive);
        message.ShouldContain("'writer'", Case.Sensitive);
        provider.Requests.ShouldBeEmpty("a model was called although the version does not exist");
        (await ChildRowsAsync(host, run.Id)).ShouldBeEmpty();
    }

    [Fact]
    public async Task An_agent_defined_in_code_runs_when_the_parameter_is_empty_and_fails_the_step_when_it_names_a_version()
    {
        // History belongs to the database: a code agent has a current text and nothing to pin.
        var provider = Pinning();

        await using var host = await StartSelectingAsync(
            provider,
            configure: static builder => builder.AddAgent(new AgentDefinition
            {
                Name = Writer,
                Instructions = "From code.",
                Model = new ModelBinding { Provider = "pin-provider", Model = "pin-model" },
                Origin = AgentDefinitionOrigin.Code,
            }));

        var current = await RunAsync(host, null);

        current.Status.ShouldBe(RunStatus.Completed, current.Error?.Message);
        provider.Requests.ShouldHaveSingleItem().Options!.Instructions.ShouldBe("From code.");

        var pinned = await RunAsync(host, Values((VersionParameter, "1")));

        pinned.Status.ShouldBe(RunStatus.Failed);
        pinned.Error.ShouldNotBeNull().Message.ShouldContain("does not keep version history", Case.Sensitive);
        provider.Requests.Count.ShouldBe(1, "the failed run did not call a model");
        (await ChildRowsAsync(host, pinned.Id)).ShouldBeEmpty();
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

    private static Task<TraconTestHost> StartSelectingAsync(
        FakeModelProvider provider,
        IReadOnlyList<AgentParameter>? parameters = null,
        Action<ITraconBuilder>? configure = null)
        => TraconTestHost.StartAsync(builder =>
        {
            builder.AddModelProvider(provider);
            builder.UseWorkflows();
            builder.AddWorkflow(
                Flow,
                services => Single(services, Writer, step => step.AgentVersionParameter = VersionParameter),
                options => options.Parameters = parameters ?? [new AgentParameter { Name = VersionParameter, Kind = AgentParameterKind.Number }]);
            configure?.Invoke(builder);
        });

    private static Dictionary<string, string> Values(params (string Name, string Value)[] values)
        => values.ToDictionary(static value => value.Name, static value => value.Value, StringComparer.Ordinal);

    private static Workflow Single(IServiceProvider services, string agent, Action<WorkflowAgentStepOptions>? configure)
    {
        var step = configure is null
            ? services.GetWorkflowAgentStep(Flow, agent)
            : services.GetWorkflowAgentStep(Flow, agent, configure);

        return new WorkflowBuilder(step).WithOutputFrom(step).WithName(Flow).Build();
    }

    private static async Task CreateAgentAsync(TraconTestHost host, string instructions, IReadOnlyList<AgentParameter>? parameters = null)
    {
        using var created = await host.Client.PostAsJsonAsync("/tracon/api/agents", Request(instructions, parameters));

        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
    }

    private static async Task UpdateAgentAsync(TraconTestHost host, string instructions, IReadOnlyList<AgentParameter>? parameters = null)
    {
        using var updated = await host.Client.PutAsJsonAsync($"/tracon/api/agents/{Writer}", Request(instructions, parameters));

        updated.StatusCode.ShouldBe(HttpStatusCode.OK, await updated.Content.ReadAsStringAsync());
    }

    private static AgentDefinitionRequest Request(string instructions, IReadOnlyList<AgentParameter>? parameters = null)
        => new()
        {
            Name = Writer,
            Instructions = instructions,
            Model = new ModelBinding { Provider = "pin-provider", Model = "pin-model" },
            Parameters = parameters ?? [],
        };

    private static async Task<RunRecord> RunAsync(TraconTestHost host, Dictionary<string, string>? parameters = null)
    {
        using var response = await host.Client.PostAsJsonAsync(
            $"/tracon/api/workflows/{Flow}/run",
            new WorkflowRunHttpRequest { Message = "Episode 12", Parameters = parameters });

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
