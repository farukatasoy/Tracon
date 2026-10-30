using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;
using FakeModelProvider = Tracon.Testing.FakeModelProvider;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// An agent node can pin the version of its agent (failure modes 14 and 16): the save
/// refuses a pin that cannot hold, a run of the chain runs the pinned text while the agent is
/// edited, and every path a step is resolved through honours the pin.
/// </summary>
/// <remarks>
/// The agents here are stored agents (created through the management API), because only a stored
/// agent keeps versions. The model provider records the instructions each call reached it with, so
/// "the pinned version ran" is proved by the text the model was given, not by a row's label.
/// </remarks>
public sealed class WorkflowAgentVersionPinTests
{
    private const string Writer = "writer";
    private const string Editor = "editor";
    private const string Workflow = "podcast";

    // --- The save refuses a pin that cannot hold (16) ---

    [Fact]
    public async Task A_pin_to_a_version_the_agent_has_is_saved_and_read_back()
    {
        var provider = Provider();
        await using var host = await StartAsync(provider);

        await CreateAgentAsync(host, Writer, "ALPHA");
        await UpdateAgentAsync(host, Writer, "BETA");

        using var saved = await SaveAsync(host, Workflow, Pinned(Writer, 1), Plain(Editor));

        saved.StatusCode.ShouldBe(HttpStatusCode.OK, await saved.Content.ReadAsStringAsync());

        using var read = await host.Client.GetAsync($"/tracon/api/workflows/{Workflow}");
        var nodes = (await TraconTestHost.ReadJsonAsync(read)).GetProperty("nodes");

        nodes[0].GetProperty("agentVersion").GetInt32().ShouldBe(1);
        nodes[1].TryGetProperty("agentVersion", out var none).ShouldBeTrue();
        none.ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task A_version_below_one_is_refused_and_nothing_is_written(int version)
    {
        await using var host = await StartAsync(Provider());

        await CreateAgentAsync(host, Writer, "ALPHA");

        using var response = await SaveAsync(host, Workflow, Pinned(Writer, version));

        await AssertInvalidAsync(response, "has an 'agentVersion' of " + version);
        await AssertNothingStoredAsync(host);
    }

    [Fact]
    public async Task A_function_node_that_carries_a_version_is_refused_by_name()
    {
        await using var host = await StartAsync(Provider());

        await CreateAgentAsync(host, Writer, "ALPHA");

        using var response = await SaveAsync(
            host,
            Workflow,
            Plain(Writer),
            new { name = "tts", kind = "Function", agentVersion = 1 });

        await AssertInvalidAsync(response, "Node 'tts' in workflow 'podcast' is a function node but carries an 'agentVersion'");
        await AssertNothingStoredAsync(host);
    }

    [Fact]
    public async Task A_version_the_agent_does_not_have_is_refused_and_names_the_node_and_the_version()
    {
        await using var host = await StartAsync(Provider());

        await CreateAgentAsync(host, Writer, "ALPHA");

        using var response = await SaveAsync(host, Workflow, Pinned(Writer, 7));

        await AssertInvalidAsync(response, "Node 'writer' in workflow 'podcast' pins agent 'writer' to version 7");
        await AssertNothingStoredAsync(host);
    }

    [Fact]
    public async Task An_agent_defined_in_code_cannot_be_pinned_because_it_keeps_no_history()
    {
        await using var host = await StartAsync(
            Provider(),
            static builder => builder.AddAgent(new AgentDefinition
            {
                Name = "coded",
                Instructions = "From code.",
                Model = new ModelBinding { Provider = "pin-provider", Model = "pin-model" },
                Origin = AgentDefinitionOrigin.Code,
            }));

        using var response = await SaveAsync(host, Workflow, Pinned("coded", 1));

        await AssertInvalidAsync(response, "'coded' is defined in code and keeps no version history");
        await AssertNothingStoredAsync(host);
    }

    [Fact]
    public async Task An_agent_that_does_not_exist_yet_may_still_be_named_with_a_pin()
    {
        // Agent NAMES are not judged at save (the agent may be defined later); a version cannot be
        // judged without the agent. The compiler reports the missing agent by name at run time.
        await using var host = await StartAsync(Provider());

        using var response = await SaveAsync(host, Workflow, Pinned("later", 2));

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }

    // --- A run runs the pinned version (14) ---

    [Fact]
    public async Task A_pinned_step_runs_the_pinned_text_while_the_agent_is_edited()
    {
        var provider = Provider();
        await using var host = await StartAsync(provider);

        await CreateAgentAsync(host, Writer, "ALPHA text");
        await UpdateAgentAsync(host, Writer, "BETA text");
        await CreateAgentAsync(host, Editor, "EDIT text");

        (await SaveAsync(host, Workflow, Pinned(Writer, 1), Plain(Editor))).StatusCode.ShouldBe(HttpStatusCode.OK);

        var run = await RunAsync(host, Workflow);

        run.Status.ShouldBe(RunStatus.Completed, run.Error?.Message);

        var instructions = provider.Requests.Select(static request => request.Options!.Instructions).ToList();

        instructions.ShouldContain(static text => text!.Contains("ALPHA text", StringComparison.Ordinal));
        instructions.ShouldNotContain(static text => text!.Contains("BETA text", StringComparison.Ordinal));
        instructions.ShouldContain(static text => text!.Contains("EDIT text", StringComparison.Ordinal));

        // The child rows tell the same story: the pinned step recorded version 1, the free step the
        // agent's only version.
        var rows = await ChildRowsAsync(host, run.Id);

        rows.Single(static row => string.Equals(row.AgentName, Writer, StringComparison.Ordinal)).AgentVersion.ShouldBe(1);
        rows.Single(static row => string.Equals(row.AgentName, Editor, StringComparison.Ordinal)).AgentVersion.ShouldBe(1);
    }

    [Fact]
    public async Task A_step_with_no_pin_follows_the_agent_to_its_new_version()
    {
        var provider = Provider();
        await using var host = await StartAsync(provider);

        await CreateAgentAsync(host, Writer, "ALPHA text");
        (await SaveAsync(host, Workflow, Plain(Writer))).StatusCode.ShouldBe(HttpStatusCode.OK);
        await UpdateAgentAsync(host, Writer, "BETA text");

        var run = await RunAsync(host, Workflow);

        run.Status.ShouldBe(RunStatus.Completed, run.Error?.Message);
        provider.Requests.Select(static request => request.Options!.Instructions).ShouldContain(
            static text => text!.Contains("BETA text", StringComparison.Ordinal));
        (await ChildRowsAsync(host, run.Id)).Single().AgentVersion.ShouldBe(2);
    }

    [Fact]
    public async Task A_pinned_step_that_binds_the_workflows_parameters_still_runs_its_pinned_version()
    {
        // The parameterized path compiles the definition itself, outside the catalog's cache: a pin
        // that only the cached path honoured would be dropped exactly when values are supplied.
        var provider = Provider();
        await using var host = await StartAsync(provider);

        await CreateAgentAsync(host, Writer, "ALPHA in a {{tone}} tone.", Tone());
        await UpdateAgentAsync(host, Writer, "BETA in a {{tone}} tone.", Tone());

        using var saved = await host.Client.PutAsJsonAsync(
            $"/tracon/api/workflows/{Workflow}",
            new WorkflowSaveRequest
            {
                Kind = WorkflowKind.Sequential,
                Nodes = [new WorkflowNodeReference { Name = Writer, Kind = WorkflowNodeKind.Agent, AgentVersion = 1 }],
                Parameters = [Tone()],
            });

        saved.StatusCode.ShouldBe(HttpStatusCode.OK, await saved.Content.ReadAsStringAsync());

        var run = await RunAsync(host, Workflow, new Dictionary<string, string>(StringComparer.Ordinal) { ["tone"] = "formal" });

        run.Status.ShouldBe(RunStatus.Completed, run.Error?.Message);

        var instructions = provider.Requests.Select(static request => request.Options!.Instructions).ToList();

        instructions.ShouldContain(static text => text!.Contains("ALPHA in a formal tone.", StringComparison.Ordinal));
        instructions.ShouldNotContain(static text => text!.Contains("BETA", StringComparison.Ordinal));
        (await ChildRowsAsync(host, run.Id)).Single().AgentVersion.ShouldBe(1);
    }

    [Fact]
    public async Task A_pin_that_stopped_holding_after_the_save_fails_the_run_before_any_step_runs()
    {
        // The agent was deleted and created again: its history went with the delete, so the pinned version
        // 3 names nothing. The compiler says so, by node, before the first model call.
        var provider = Provider();
        await using var host = await StartAsync(provider);

        await CreateAgentAsync(host, Writer, "ONE");
        await UpdateAgentAsync(host, Writer, "TWO");
        await UpdateAgentAsync(host, Writer, "THREE");
        await CreateAgentAsync(host, Editor, "EDIT text");

        (await SaveAsync(host, Workflow, Plain(Editor), Pinned(Writer, 3))).StatusCode.ShouldBe(HttpStatusCode.OK);

        using (var deleted = await host.Client.DeleteAsync($"/tracon/api/agents/{Writer}"))
        {
            deleted.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        }

        await CreateAgentAsync(host, Writer, "FRESH");

        var run = await RunAsync(host, Workflow);

        run.Status.ShouldBe(RunStatus.Failed);
        run.Error.ShouldNotBeNull().Message.ShouldContain("pins agent 'writer' to version 3", Case.Sensitive);
        provider.Requests.ShouldBeEmpty("the editor, which runs first, never started");
        (await ChildRowsAsync(host, run.Id)).ShouldBeEmpty();
    }

    [Fact]
    public async Task A_pin_does_not_reach_the_content_of_an_agent_created_again_under_the_same_name()
    {
        // A version number is an identity. Before it, the numbering restarted at 1 when the agent
        // was created again, so the pin to version 2 named the NEW agent's second save and ran its text.
        var provider = Provider();
        await using var host = await StartAsync(provider);

        await CreateAgentAsync(host, Writer, "OLD ONE");
        await UpdateAgentAsync(host, Writer, "OLD TWO");
        await CreateAgentAsync(host, Editor, "EDIT text");

        (await SaveAsync(host, Workflow, Plain(Editor), Pinned(Writer, 2))).StatusCode.ShouldBe(HttpStatusCode.OK);

        using (var deleted = await host.Client.DeleteAsync($"/tracon/api/agents/{Writer}"))
        {
            deleted.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        }

        await CreateAgentAsync(host, Writer, "NEW ONE");
        await UpdateAgentAsync(host, Writer, "NEW TWO");

        // The new era holds versions 3 and 4; number 2 names nothing.
        using (var versions = await host.Client.GetAsync($"/tracon/api/agents/{Writer}/versions"))
        {
            (await TraconTestHost.ReadJsonAsync(versions)).EnumerateArray()
                .Select(static version => version.GetProperty("version").GetInt32())
                .ShouldBe([4, 3]);
        }

        var run = await RunAsync(host, Workflow);

        run.Status.ShouldBe(RunStatus.Failed);
        run.Error.ShouldNotBeNull().Message.ShouldContain("pins agent 'writer' to version 2", Case.Sensitive);
        provider.Requests.ShouldBeEmpty("the editor, which runs first, never started and no step ran the new agent's text");
        (await ChildRowsAsync(host, run.Id)).ShouldBeEmpty();
    }

    // --- Helpers ---------------------------------------------------------

    private static FakeModelProvider Provider()
        => new FakeModelProvider("pin-provider")
            .WithModel(new ModelDescriptor { Name = "pin-model" })
            .EchoesUserMessage();

    private static Task<TraconTestHost> StartAsync(FakeModelProvider provider, Action<ITraconBuilder>? configure = null)
        => TraconTestHost.StartAsync(builder =>
        {
            builder.AddModelProvider(provider).UseWorkflows();
            configure?.Invoke(builder);
        });

    private static AgentParameter Tone()
        => new() { Name = "tone", Kind = AgentParameterKind.Text, DefaultValue = "casual" };

    private static async Task CreateAgentAsync(TraconTestHost host, string name, string instructions, params AgentParameter[] parameters)
    {
        using var created = await host.Client.PostAsJsonAsync("/tracon/api/agents", Request(name, instructions, parameters));

        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
    }

    private static async Task UpdateAgentAsync(TraconTestHost host, string name, string instructions, params AgentParameter[] parameters)
    {
        using var updated = await host.Client.PutAsJsonAsync($"/tracon/api/agents/{name}", Request(name, instructions, parameters));

        updated.StatusCode.ShouldBe(HttpStatusCode.OK, await updated.Content.ReadAsStringAsync());
    }

    private static AgentDefinitionRequest Request(string name, string instructions, AgentParameter[] parameters)
        => new()
        {
            Name = name,
            Instructions = instructions,
            Model = new ModelBinding { Provider = "pin-provider", Model = "pin-model" },
            Parameters = parameters,
        };

    private static object Pinned(string name, int version) => new { name, kind = "Agent", agentVersion = version };

    private static object Plain(string name) => new { name, kind = "Agent" };

    private static Task<HttpResponseMessage> SaveAsync(TraconTestHost host, string name, params object[] nodes)
        => host.Client.PutAsJsonAsync($"/tracon/api/workflows/{name}", new { kind = "Sequential", nodes });

    private static async Task<RunRecord> RunAsync(
        TraconTestHost host,
        string name,
        IReadOnlyDictionary<string, string>? parameters = null)
    {
        using var response = await host.Client.PostAsJsonAsync(
            $"/tracon/api/workflows/{name}/run",
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

    private static async Task AssertInvalidAsync(HttpResponseMessage response, string expectedDetail)
    {
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var problem = await TraconTestHost.ReadJsonAsync(response);

        problem.GetProperty("title").GetString().ShouldBe("Workflow definition invalid");
        problem.GetProperty("detail").GetString().ShouldNotBeNull().ShouldContain(expectedDetail, Case.Sensitive);
    }

    private static async Task AssertNothingStoredAsync(TraconTestHost host)
    {
        using var read = await host.Client.GetAsync($"/tracon/api/workflows/{Workflow}");

        read.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }
}
