using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;
using FakeModelProvider = Tracon.Testing.FakeModelProvider;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// A callable agent can be pinned to a version of the callee: the callee runs that version in
/// every call mode, the model reads the description of that version, a pinned parent runs the callee versions
/// ITS definition names, and the save refuses a pin that cannot hold.
/// </summary>
/// <remarks>
/// <para>
/// The agents are stored agents created through the management API, because only a stored agent keeps versions.
/// The model provider records what each model was shown, so "the pinned version ran" is proved by the text the
/// model was given and by the version the child run row records, not by a label on the definition.
/// </para>
/// <para>
/// The router's model calls the callee the way a real one does: with its own tool in
/// <see cref="SubAgentCallMode.Tool"/> mode and with the framework's task tools in <see cref="SubAgentCallMode.Background"/>
/// mode. Each mode builds its children in the same place, so each is proved on its own.
/// </para>
/// </remarks>
public sealed class CallableAgentVersionPinTests
{
    private const string ProviderName = "pin-routing";
    private const string RouterModel = "router-model";
    private const string ResearcherModel = "researcher-model";
    private const string Router = "router";
    private const string Researcher = "researcher";

    // Microsoft Agent Framework's background task tools.
    private const string StartTask = "background_agents_start_task";
    private const string WaitForCompletion = "background_agents_wait_for_first_completion";
    private const string GetResults = "background_agents_get_task_results";

    // --- The pinned version is the one that runs, in every call mode ---

    [Theory]
    [InlineData(SubAgentCallMode.Tool, false)]
    [InlineData(SubAgentCallMode.Tool, true)]
    [InlineData(SubAgentCallMode.Background, false)]
    [InlineData(SubAgentCallMode.Background, true)]
    public async Task A_pinned_callee_runs_its_pinned_version_while_the_agent_is_edited(SubAgentCallMode mode, bool streaming)
    {
        var provider = Provider(mode);
        await using var host = await StartAsync(provider);

        await CreateResearcherAsync(host, "ALPHA text", "ALPHA description");
        await UpdateResearcherAsync(host, "BETA text", "BETA description");
        await CreateRouterAsync(host, mode, new CallableAgentReference { Name = Researcher, Version = 1 });

        await RunRouterAsync(host, streaming);

        // What the callee's model was given: the pinned version's text, never the current one's.
        var researcherText = InstructionsOf(provider, ResearcherModel);

        researcherText.ShouldNotBeEmpty();
        researcherText.ShouldAllBe(static text => text.Contains("ALPHA text", StringComparison.Ordinal));
        researcherText.ShouldNotContain(static text => text.Contains("BETA", StringComparison.Ordinal));

        // What the router's model was shown about the callee: the pinned version's description.
        var shown = ShownToRouter(provider);

        shown.ShouldContain("ALPHA description", Case.Sensitive);
        shown.ShouldNotContain("BETA description", Case.Sensitive);

        (await ChildRowAsync(host)).AgentVersion.ShouldBe(1);
    }

    [Theory]
    [InlineData(SubAgentCallMode.Tool)]
    [InlineData(SubAgentCallMode.Background)]
    public async Task A_callee_with_no_pin_follows_the_agent_to_its_new_version(SubAgentCallMode mode)
    {
        var provider = Provider(mode);
        await using var host = await StartAsync(provider);

        await CreateResearcherAsync(host, "ALPHA text", "ALPHA description");
        await CreateRouterAsync(host, mode, new CallableAgentReference { Name = Researcher });
        await UpdateResearcherAsync(host, "BETA text", "BETA description");

        await RunRouterAsync(host, streaming: false);

        InstructionsOf(provider, ResearcherModel).ShouldAllBe(static text => text.Contains("BETA text", StringComparison.Ordinal));
        ShownToRouter(provider).ShouldContain("BETA description", Case.Sensitive);
        (await ChildRowAsync(host)).AgentVersion.ShouldBe(2);
    }

    // --- A parent pinned to a version of its own runs the callee versions that version names ---

    [Fact]
    public async Task A_parent_resolved_at_a_version_runs_the_callee_versions_that_version_names()
    {
        var provider = Provider(SubAgentCallMode.Tool);
        await using var host = await StartAsync(provider);

        await CreateResearcherAsync(host, "ALPHA text", "ALPHA description");
        await UpdateResearcherAsync(host, "BETA text", "BETA description");

        // Router version 1 pins the callee to 1; router version 2 follows the callee.
        await CreateRouterAsync(host, SubAgentCallMode.Tool, new CallableAgentReference { Name = Researcher, Version = 1 });
        await UpdateRouterAsync(host, SubAgentCallMode.Tool, new CallableAgentReference { Name = Researcher });

        var catalog = host.Services.GetRequiredService<IAgentCatalog>();
        var pinnedParent = (await catalog.ResolveAsync(Router, version: 1, culture: null, CancellationToken.None)).ShouldNotBeNull();

        _ = await pinnedParent.RunAsync("start");

        var runs = host.Services.GetRequiredService<IRunStore>();
        var first = (await runs.QueryRunsAsync(new RunQuery { OnlyRootRuns = false })).Where(static run => run.Depth == 1).ToList();

        first.ShouldHaveSingleItem().AgentVersion.ShouldBe(1);

        // The router's model calls the callee once more, then answers as before.
        provider.ForModel(RouterModel, static router => router.CallsTool(Researcher, new { query = "sub task" }));

        var currentParent = (await catalog.ResolveAsync(Router, culture: null, CancellationToken.None)).ShouldNotBeNull();

        _ = await currentParent.RunAsync("start");

        var all = (await runs.QueryRunsAsync(new RunQuery { OnlyRootRuns = false })).Where(static run => run.Depth == 1).ToList();

        all.Count.ShouldBe(2);
        all.Select(static run => run.AgentVersion).Order().ShouldBe([1, 2]);
    }

    [Fact]
    public async Task A_pin_that_stopped_holding_fails_the_caller_by_name_instead_of_running_another_version()
    {
        // The callee was deleted and created again: its history went with the delete, and the numbering goes on
        // So version 1 names nothing and no later save can take its place.
        var provider = Provider(SubAgentCallMode.Tool);
        await using var host = await StartAsync(provider);

        await CreateResearcherAsync(host, "ALPHA text", "ALPHA description");
        await CreateRouterAsync(host, SubAgentCallMode.Tool, new CallableAgentReference { Name = Researcher, Version = 1 });

        using (var deleted = await host.Client.DeleteAsync($"/tracon/api/agents/{Researcher}"))
        {
            deleted.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        }

        await CreateResearcherAsync(host, "FRESH text", "FRESH description");

        var catalog = host.Services.GetRequiredService<IAgentCatalog>();
        var failure = await Should.ThrowAsync<TraconCompilationException>(
            async () => await catalog.ResolveAsync(Router, culture: null, CancellationToken.None));

        failure.AgentName.ShouldBe(Router);
        failure.Message.ShouldContain("pins callable agent 'researcher' to version 1", Case.Sensitive);
        provider.Requests.ShouldBeEmpty("no model was called");
    }

    [Fact]
    public async Task A_replay_without_live_tools_calls_no_callee_pinned_or_not()
    {
        // Replay clears the callable agents with the skills and the extensions, because a callee runs through a
        // context provider the replay transform cannot reach: it would run live and bill again.
        var provider = Provider(SubAgentCallMode.Tool);
        await using var host = await StartAsync(provider);

        await CreateResearcherAsync(host, "ALPHA text", "ALPHA description");
        await CreateRouterAsync(host, SubAgentCallMode.Tool, new CallableAgentReference { Name = Researcher, Version = 1 });
        await RunRouterAsync(host, streaming: false);

        var runs = host.Services.GetRequiredService<IRunStore>();
        var root = (await runs.QueryRunsAsync(new RunQuery())).ShouldHaveSingleItem();

        using var replay = await host.Client.PostAsJsonAsync($"/tracon/api/runs/{root.Id}/replay", new { toolMode = "NoTools" });

        replay.StatusCode.ShouldBe(HttpStatusCode.OK, await replay.Content.ReadAsStringAsync());

        var replayId = (await TraconTestHost.ReadJsonAsync(replay)).GetProperty("runId").GetGuid();

        (await runs.GetRunAsync(replayId))!.ChildRunCount.ShouldBe(0);
        (await runs.QueryRunsAsync(new RunQuery { OnlyRootRuns = false })).Count(static run => run.Depth == 1).ShouldBe(1, "only the first run's callee ran");

        var replayed = provider.Requests.Last(static request => string.Equals(request.Options?.ModelId, RouterModel, StringComparison.Ordinal));

        (replayed.Options!.Tools ?? []).Select(static tool => tool.Name).ShouldNotContain(static name => string.Equals(name, Researcher, StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_workflow_step_pinned_to_a_parent_version_runs_the_callee_versions_that_version_names()
    {
        // The step is a ChildAgentInvoker over the router; the router it resolves is version 1, whose own entry pins the
        // callee to version 1. The router's current version follows the callee, so only the parent pin separates them.
        var provider = Provider(SubAgentCallMode.Tool);
        await using var host = await TraconTestHost.StartAsync(builder => builder.AddModelProvider(provider).UseWorkflows());

        await CreateResearcherAsync(host, "ALPHA text", "ALPHA description");
        await UpdateResearcherAsync(host, "BETA text", "BETA description");
        await CreateRouterAsync(host, SubAgentCallMode.Tool, new CallableAgentReference { Name = Researcher, Version = 1 });
        await UpdateRouterAsync(host, SubAgentCallMode.Tool, new CallableAgentReference { Name = Researcher });

        using (var saved = await host.Client.PutAsJsonAsync(
            "/tracon/api/workflows/chain",
            new { kind = "Sequential", nodes = new object[] { new { name = Router, kind = "Agent", agentVersion = 1 } } }))
        {
            saved.StatusCode.ShouldBe(HttpStatusCode.OK, await saved.Content.ReadAsStringAsync());
        }

        using var response = await host.Client.PostAsJsonAsync("/tracon/api/workflows/chain/run", new WorkflowRunHttpRequest { Message = "Episode 12" });

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        _ = await SseReader.ReadAllAsync(await response.Content.ReadAsStreamAsync());

        var rows = await host.Services.GetRequiredService<IRunStore>().QueryRunsAsync(new RunQuery { OnlyRootRuns = false, Take = 100 });

        rows.Single(static run => string.Equals(run.AgentName, Router, StringComparison.Ordinal)).AgentVersion.ShouldBe(1);
        rows.Single(static run => string.Equals(run.AgentName, Researcher, StringComparison.Ordinal)).AgentVersion.ShouldBe(1);
        InstructionsOf(provider, ResearcherModel).ShouldAllBe(static text => text.Contains("ALPHA text", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_cycle_that_exists_only_through_a_pinned_version_is_saved_and_the_depth_limit_stops_the_run()
    {
        // The call graph reads the names of the callees' CURRENT versions. cycle-b's current version calls nobody, so
        // cycle-a -> cycle-b@1 -> cycle-a is not seen when cycle-a is saved. The run-time depth limit is the second line.
        var provider = new FakeModelProvider(ProviderName);

        provider.ForModel("cycle-a-model", static agent => agent
            .CallsTool("cycle_b", new { query = "go" })
            .CallsTool("cycle_b", new { query = "go" })
            .EchoesLastToolResult("A: "));
        provider.ForModel("cycle-b-model", static agent => agent
            .CallsTool("cycle_a", new { query = "go" })
            .CallsTool("cycle_a", new { query = "go" })
            .EchoesLastToolResult("B: "));

        await using var host = await StartAsync(provider);

        static AgentDefinitionRequest Definition(string name, string model, params CallableAgentReference[] callable)
            => new()
            {
                Name = name,
                Instructions = "Delegate.",
                Model = Binding(model),
                CallableAgents = callable,
                SubAgents = new SubAgentSettings { CallMode = SubAgentCallMode.Tool },
            };

        async Task SendAsync(HttpMethod method, string path, AgentDefinitionRequest body, HttpStatusCode expected)
        {
            using var request = new HttpRequestMessage(method, path) { Content = JsonContent.Create(body) };
            using var sent = await host.Client.SendAsync(request);

            sent.StatusCode.ShouldBe(expected, await sent.Content.ReadAsStringAsync());
        }

        await SendAsync(HttpMethod.Post, "/tracon/api/agents", Definition("cycle-a", "cycle-a-model"), HttpStatusCode.Created);
        await SendAsync(HttpMethod.Post, "/tracon/api/agents", Definition("cycle-b", "cycle-b-model", "cycle-a"), HttpStatusCode.Created);
        await SendAsync(HttpMethod.Put, "/tracon/api/agents/cycle-b", Definition("cycle-b", "cycle-b-model"), HttpStatusCode.OK);

        // Saved: the names of the current versions form no cycle.
        await SendAsync(
            HttpMethod.Put,
            "/tracon/api/agents/cycle-a",
            Definition("cycle-a", "cycle-a-model", new CallableAgentReference { Name = "cycle-b", Version = 1 }),
            HttpStatusCode.OK);

        var catalog = host.Services.GetRequiredService<IAgentCatalog>();
        var root = (await catalog.ResolveAsync("cycle-a", culture: null, CancellationToken.None)).ShouldNotBeNull();
        var answer = await root.RunAsync("start");

        answer.Text.ShouldContain("call depth limit was exceeded", Case.Sensitive);

        var rows = await host.Services.GetRequiredService<IRunStore>().QueryRunsAsync(new RunQuery { OnlyRootRuns = false, Take = 100 });

        rows.Count.ShouldBe(4, "cycle-a, cycle-b, cycle-a and cycle-b: the fifth call is refused");
        rows.Max(static run => run.Depth).ShouldBe(3);
        rows.ShouldAllBe(static run => run.Status == RunStatus.Completed);
    }

    // --- The save refuses a pin that cannot hold ---

    [Fact]
    public async Task A_pin_to_a_version_the_callee_has_is_saved_and_read_back_in_the_definition_and_its_history()
    {
        await using var host = await StartAsync(Provider(SubAgentCallMode.Tool));

        await CreateResearcherAsync(host, "ALPHA text", "ALPHA description");
        await UpdateResearcherAsync(host, "BETA text", "BETA description");
        await CreateRouterAsync(host, SubAgentCallMode.Tool, new CallableAgentReference { Name = Researcher, Version = 1 });

        using var read = await host.Client.GetAsync($"/tracon/api/agents/{Router}");
        var callable = (await TraconTestHost.ReadJsonAsync(read)).GetProperty("definition").GetProperty("callableAgents");

        callable.GetArrayLength().ShouldBe(1);
        callable[0].GetProperty("name").GetString().ShouldBe(Researcher);
        callable[0].GetProperty("version").GetInt32().ShouldBe(1);

        using var history = await host.Client.GetAsync($"/tracon/api/agents/{Router}/versions");
        var saved = (await TraconTestHost.ReadJsonAsync(history)).EnumerateArray().Single();

        saved.GetProperty("callableAgents")[0].GetProperty("version").GetInt32().ShouldBe(1);
    }

    [Theory]
    [InlineData(0, "pins callable agent 'researcher' to version 0")]
    [InlineData(-2, "pins callable agent 'researcher' to version -2")]
    [InlineData(9, "pins callable agent 'researcher' to version 9, but the agent has no version 9")]
    public async Task A_version_that_cannot_hold_is_refused_and_nothing_is_written(int version, string expectedDetail)
    {
        await using var host = await StartAsync(Provider(SubAgentCallMode.Tool));

        await CreateResearcherAsync(host, "ALPHA text", "ALPHA description");

        using var response = await host.Client.PostAsJsonAsync(
            "/tracon/api/agents",
            RouterRequest(SubAgentCallMode.Tool, new CallableAgentReference { Name = Researcher, Version = version }));

        await AssertInvalidAsync(response, expectedDetail);
        await AssertRouterNotStoredAsync(host);
    }

    [Fact]
    public async Task An_agent_defined_in_code_cannot_be_pinned_because_it_keeps_no_history()
    {
        await using var host = await TraconTestHost.StartAsync(builder =>
        {
            builder.AddModelProvider(Provider(SubAgentCallMode.Tool));
            builder.AddAgent(new AgentDefinition
            {
                Name = "coded",
                Instructions = "From code.",
                Model = Binding(ResearcherModel),
                Origin = AgentDefinitionOrigin.Code,
            });
        });

        using var response = await host.Client.PostAsJsonAsync(
            "/tracon/api/agents",
            RouterRequest(SubAgentCallMode.Tool, new CallableAgentReference { Name = "coded", Version = 1 }));

        await AssertInvalidAsync(response, "'coded' is defined in code and keeps no version history");
        await AssertRouterNotStoredAsync(host);
    }

    [Fact]
    public async Task A_name_listed_twice_is_refused_even_when_one_entry_is_pinned()
    {
        await using var host = await StartAsync(Provider(SubAgentCallMode.Tool));

        await CreateResearcherAsync(host, "ALPHA text", "ALPHA description");

        using var response = await host.Client.PostAsJsonAsync(
            "/tracon/api/agents",
            RouterRequest(SubAgentCallMode.Tool, new CallableAgentReference { Name = Researcher }, new CallableAgentReference { Name = Researcher, Version = 1 }));

        await AssertInvalidAsync(response, "lists callable agent 'researcher' more than once");
        await AssertRouterNotStoredAsync(host);
    }

    [Fact]
    public async Task The_validate_endpoint_reports_a_pin_with_its_code_and_the_path_of_the_entry()
    {
        await using var host = await StartAsync(Provider(SubAgentCallMode.Tool));

        await CreateResearcherAsync(host, "ALPHA text", "ALPHA description");

        using var response = await host.Client.PostAsJsonAsync(
            "/tracon/api/agents/validate",
            RouterRequest(SubAgentCallMode.Tool, new CallableAgentReference { Name = Researcher, Version = 9 }));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var report = await TraconTestHost.ReadJsonAsync(response);

        report.GetProperty("valid").GetBoolean().ShouldBeFalse();

        var message = report.GetProperty("messages").EnumerateArray().Single();

        message.GetProperty("code").GetString().ShouldBe("callable_agent_version_not_found");
        message.GetProperty("path").GetString().ShouldBe("callableAgents[0].version");
    }

    [Fact]
    public async Task A_pin_is_judged_against_the_callers_own_tenant()
    {
        // Tenant A's callee has versions 1 and 2. Tenant B's callee of the same name has only version 1, and tenant B
        // cannot see tenant A's other agent at all. A version number of another tenant's agent proves nothing.
        var provider = Provider(SubAgentCallMode.Tool);
        await using var host = await TraconTestHost.StartAsync(builder =>
        {
            builder.AddModelProvider(provider);
            builder.UseTenancy(static options =>
            {
                options.Enabled = true;
                options.AllowHeaderResolution = true;
            });
        });

        async Task<HttpResponseMessage> SendAsync(string tenant, HttpMethod method, string path, AgentDefinitionRequest body)
        {
            using var request = new HttpRequestMessage(method, path) { Content = JsonContent.Create(body) };

            request.Headers.Add("X-Tracon-Tenant", tenant);

            return await host.Client.SendAsync(request);
        }

        (await SendAsync("tenant-a", HttpMethod.Post, "/tracon/api/agents", ResearcherRequest("ALPHA text", "ALPHA description"))).StatusCode.ShouldBe(HttpStatusCode.Created);
        (await SendAsync("tenant-a", HttpMethod.Put, $"/tracon/api/agents/{Researcher}", ResearcherRequest("BETA text", "BETA description"))).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await SendAsync("tenant-a", HttpMethod.Post, "/tracon/api/agents", ResearcherRequest("Only in A.", "Only in A.") with { Name = "only-in-a" })).StatusCode.ShouldBe(HttpStatusCode.Created);
        (await SendAsync("tenant-b", HttpMethod.Post, "/tracon/api/agents", ResearcherRequest("B text", "B description"))).StatusCode.ShouldBe(HttpStatusCode.Created);

        using var missingVersion = await SendAsync(
            "tenant-b",
            HttpMethod.Post,
            "/tracon/api/agents",
            RouterRequest(SubAgentCallMode.Tool, new CallableAgentReference { Name = Researcher, Version = 2 }));

        await AssertInvalidAsync(missingVersion, "pins callable agent 'researcher' to version 2, but the agent has no version 2");

        using var otherTenantsAgent = await SendAsync(
            "tenant-b",
            HttpMethod.Post,
            "/tracon/api/agents",
            RouterRequest(SubAgentCallMode.Tool, new CallableAgentReference { Name = "only-in-a", Version = 1 }));

        await AssertInvalidAsync(otherTenantsAgent, "no such agent exists in the catalog");

        using var own = await SendAsync(
            "tenant-b",
            HttpMethod.Post,
            "/tracon/api/agents",
            RouterRequest(SubAgentCallMode.Tool, new CallableAgentReference { Name = Researcher, Version = 1 }));

        own.StatusCode.ShouldBe(HttpStatusCode.Created, await own.Content.ReadAsStringAsync());
    }

    // --- The removed field is refused, not dropped in silence ---

    [Theory]
    [InlineData("POST", "/tracon/api/agents")]
    [InlineData("PUT", "/tracon/api/agents/router")]
    [InlineData("POST", "/tracon/api/agents/validate")]
    public async Task The_removed_callableAgentNames_field_is_refused_and_names_what_replaces_it(string method, string path)
    {
        await using var host = await StartAsync(Provider(SubAgentCallMode.Tool));

        await CreateResearcherAsync(host, "ALPHA text", "ALPHA description");

        using var request = new HttpRequestMessage(new HttpMethod(method), path)
        {
            Content = JsonContent.Create(new
            {
                name = Router,
                instructions = "Delegate.",
                callableAgentNames = new[] { Researcher },
                model = new { provider = ProviderName, model = RouterModel },
            }),
        };

        using var response = await host.Client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var problem = await TraconTestHost.ReadJsonAsync(response);

        problem.GetProperty("title").GetString().ShouldBe("Removed field");
        problem.GetProperty("detail").GetString().ShouldNotBeNull().ShouldContain("'callableAgentNames' was removed", Case.Sensitive);
        problem.GetProperty("detail").GetString().ShouldNotBeNull().ShouldContain("callableAgents", Case.Sensitive);
        await AssertRouterNotStoredAsync(host);
    }

    // --- Helpers ---------------------------------------------------------

    /// <summary>A provider whose router calls the callee once, then answers with what the callee returned.</summary>
    private static FakeModelProvider Provider(SubAgentCallMode mode)
    {
        var provider = new FakeModelProvider(ProviderName);

        provider.ForModel(RouterModel, router =>
        {
            if (mode == SubAgentCallMode.Tool)
            {
                router.CallsTool(Researcher, new { query = "sub task" });
            }
            else
            {
                router
                    .CallsTool(StartTask, new { agentName = Researcher, input = "sub task", description = "sub task" })
                    .CallsTool(WaitForCompletion, new { taskIds = new[] { 1 } })
                    .CallsTool(GetResults, new { taskId = 1 });
            }

            router.EchoesLastToolResult("Delegated: ");
        });

        provider.ForModel(ResearcherModel, static researcher => researcher.RespondsWith("Sub task done"));

        return provider;
    }

    private static Task<TraconTestHost> StartAsync(FakeModelProvider provider)
        => TraconTestHost.StartAsync(builder => builder.AddModelProvider(provider));

    private static ModelBinding Binding(string model) => new() { Provider = ProviderName, Model = model };

    private static AgentDefinitionRequest ResearcherRequest(string instructions, string description)
        => new()
        {
            Name = Researcher,
            Description = description,
            Instructions = instructions,
            Model = Binding(ResearcherModel),
        };

    private static AgentDefinitionRequest RouterRequest(SubAgentCallMode mode, params CallableAgentReference[] callable)
        => new()
        {
            Name = Router,
            Description = "Delegates the work.",
            Instructions = "Delegate.",
            Model = Binding(RouterModel),
            CallableAgents = callable,
            SubAgents = new SubAgentSettings { CallMode = mode },
        };

    private static async Task CreateResearcherAsync(TraconTestHost host, string instructions, string description)
    {
        using var created = await host.Client.PostAsJsonAsync("/tracon/api/agents", ResearcherRequest(instructions, description));

        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
    }

    private static async Task UpdateResearcherAsync(TraconTestHost host, string instructions, string description)
    {
        using var updated = await host.Client.PutAsJsonAsync($"/tracon/api/agents/{Researcher}", ResearcherRequest(instructions, description));

        updated.StatusCode.ShouldBe(HttpStatusCode.OK, await updated.Content.ReadAsStringAsync());
    }

    private static async Task CreateRouterAsync(TraconTestHost host, SubAgentCallMode mode, params CallableAgentReference[] callable)
    {
        using var created = await host.Client.PostAsJsonAsync("/tracon/api/agents", RouterRequest(mode, callable));

        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
    }

    private static async Task UpdateRouterAsync(TraconTestHost host, SubAgentCallMode mode, params CallableAgentReference[] callable)
    {
        using var updated = await host.Client.PutAsJsonAsync($"/tracon/api/agents/{Router}", RouterRequest(mode, callable));

        updated.StatusCode.ShouldBe(HttpStatusCode.OK, await updated.Content.ReadAsStringAsync());
    }

    private static async Task RunRouterAsync(TraconTestHost host, bool streaming)
    {
        var catalog = host.Services.GetRequiredService<IAgentCatalog>();
        var agent = (await catalog.ResolveAsync(Router, culture: null, CancellationToken.None)).ShouldNotBeNull();

        if (!streaming)
        {
            _ = await agent.RunAsync("start");

            return;
        }

        await foreach (var update in agent.RunStreamingAsync("start"))
        {
            _ = update;
        }
    }

    private static async Task<RunRecord> ChildRowAsync(TraconTestHost host)
        => (await host.Services.GetRequiredService<IRunStore>().QueryRunsAsync(new RunQuery { OnlyRootRuns = false }))
            .Single(static run => run.Depth == 1);

    /// <summary>The instructions of every request one model of the provider received.</summary>
    private static List<string> InstructionsOf(FakeModelProvider provider, string model)
        => [.. provider.Requests
            .Where(request => string.Equals(request.Options?.ModelId, model, StringComparison.Ordinal))
            .Select(static request => request.Options!.Instructions ?? string.Empty)];

    /// <summary>
    /// Everything the router's model was shown: its instructions, the name and description of each tool, and the
    /// messages. A callee's description reaches the model in one of these, by the mode.
    /// </summary>
    private static string ShownToRouter(FakeModelProvider provider)
    {
        var shown = new List<string>();

        foreach (var request in provider.Requests.Where(static request => string.Equals(request.Options?.ModelId, RouterModel, StringComparison.Ordinal)))
        {
            shown.Add(request.Options!.Instructions ?? string.Empty);
            shown.AddRange((request.Options.Tools ?? []).Select(static tool => $"{tool.Name} {tool.Description}"));
            shown.AddRange(request.Messages.Select(static message => message.Text));
        }

        return string.Join('\n', shown);
    }

    private static async Task AssertInvalidAsync(HttpResponseMessage response, string expectedDetail)
    {
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var problem = await TraconTestHost.ReadJsonAsync(response);

        problem.GetProperty("detail").GetString().ShouldNotBeNull().ShouldContain(expectedDetail, Case.Sensitive);
    }

    private static async Task AssertRouterNotStoredAsync(TraconTestHost host)
    {
        using var read = await host.Client.GetAsync($"/tracon/api/agents/{Router}");

        read.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }
}
