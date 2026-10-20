using System.Net;
using System.Net.Http.Json;
using System.Text;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;
using FakeModelProvider = Tracon.Testing.FakeModelProvider;
using FakeModelRequest = Tracon.Testing.FakeModelRequest;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// An agent whose <see cref="SubAgentSettings.CallMode"/> is <see cref="SubAgentCallMode.Tool"/> calls its
/// callable agent with ONE tool call (Phase 228), on the plain and the harness path.
/// </summary>
/// <remarks>
/// <para>
/// The rig is the one of <see cref="AgentDelegationTests"/>, with the router's model calling the agent's own
/// tool instead of the framework's task tools. The child is named <c>arastirma-ci</c> on purpose: the tool the
/// model sees is <c>arastirma_ci</c>, so every case also proves that the name rule is the name on the wire.
/// </para>
/// <para>
/// What the router's model <em>receives</em> is read from <see cref="FakeModelProvider.Requests"/>, not
/// inferred from a transcript: the claim of the phase is that the model sees one tool and not the task tools.
/// </para>
/// </remarks>
public sealed class SubAgentToolModeTests
{
    private const string ProviderName = "routing";
    private const string RouterModel = "router-model";
    private const string ResearcherModel = "researcher-model";

    private const string ChildName = "arastirma-ci";
    private const string ChildTool = "arastirma_ci";

    // Microsoft Agent Framework's background task tools.
    private const string StartTask = "background_agents_start_task";
    private const string WaitForCompletion = "background_agents_wait_for_first_completion";
    private const string GetResults = "background_agents_get_task_results";

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task The_parent_gets_the_callable_agent_as_one_tool_and_the_child_run_is_linked(bool harness, bool streaming)
    {
        var provider = Provider(router => router
            .CallsTool(ChildTool, new { query = "sub task" })
            .EchoesLastToolResult("Delegated: ", inputTokens: 4, outputTokens: 6));

        await using var host = await StartAsync(provider, harness: harness);

        var text = await RunRouterAsync(host, streaming);

        text.ShouldContain("Delegated: Sub task done", Case.Sensitive);

        var runs = host.Services.GetRequiredService<IRunStore>();
        var all = await runs.QueryRunsAsync(new RunQuery { OnlyRootRuns = false });

        all.Count.ShouldBe(2);

        var root = all.Single(static run => run.Depth == 0);
        var child = all.Single(static run => run.Depth == 1);

        root.AgentName.ShouldBe("yonlendirici");
        root.ChildRunCount.ShouldBe(1);
        child.AgentName.ShouldBe(ChildName);
        child.ParentRunId.ShouldBe(root.Id);
        child.RootRunId.ShouldBe(root.Id);
        child.Status.ShouldBe(RunStatus.Completed);

        // The tree total combines both runs, as it does for a background task.
        root.TreeUsage!.TotalTokens.ShouldBe(20);

        var types = new List<RunEventType>();

        await foreach (var runEvent in runs.ReadEventsAsync(root.Id))
        {
            types.Add(runEvent.Type);
        }

        types.ShouldContain(RunEventType.ChildRunStarted);
        types.ShouldContain(RunEventType.ChildRunCompleted);

        // The model of the router saw the agent as a tool and never saw the task tools.
        var tools = ToolNames(provider, RouterModel);

        tools.ShouldNotBeEmpty();
        tools.ShouldAllBe(names => names.Contains(ChildTool, StringComparer.Ordinal));
        tools.ShouldAllBe(names => !names.Any(static name => name.StartsWith("background_agents_", StringComparison.Ordinal)));

        // One delegation is two model calls of the router: the call and the answer.
        RequestsOf(provider, RouterModel).Count.ShouldBe(2);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Without_the_mode_the_parent_keeps_the_task_tools_and_gets_no_agent_tool(bool harness)
    {
        var provider = Provider(static router => router.RespondsWith("done"));

        await using var host = await StartAsync(provider, harness: harness, mode: null);

        _ = await RunRouterAsync(host, streaming: false);

        var tools = ToolNames(provider, RouterModel);

        tools.ShouldNotBeEmpty();
        tools.ShouldAllBe(names => names.Contains(StartTask, StringComparer.Ordinal));
        tools.ShouldAllBe(names => !names.Contains(ChildTool, StringComparer.Ordinal));
    }

    [Fact]
    public async Task The_depth_limit_comes_back_as_the_tool_result_and_no_child_run_starts()
    {
        var provider = Provider(static router => router
            .CallsTool(ChildTool, new { query = "sub task" })
            .EchoesLastToolResult("Delegated: "));

        await using var host = await StartAsync(provider, maxDepth: 0);

        var text = await RunRouterAsync(host, streaming: false);

        text.ShouldContain("call depth limit was exceeded", Case.Sensitive);

        var runs = host.Services.GetRequiredService<IRunStore>();

        (await runs.QueryRunsAsync(new RunQuery { OnlyRootRuns = false })).ShouldHaveSingleItem();
    }

    [Fact]
    public async Task The_tree_run_budget_comes_back_as_the_tool_result_for_the_call_that_exceeds_it()
    {
        var provider = Provider(static router => router
            .CallsTool(ChildTool, new { query = "first" })
            .CallsTool(ChildTool, new { query = "second" })
            .EchoesLastToolResult("Delegated: "));

        await using var host = await StartAsync(provider, maxRuns: 1);

        var text = await RunRouterAsync(host, streaming: false);

        text.ShouldContain("child-run limit is reached", Case.Sensitive);

        var runs = host.Services.GetRequiredService<IRunStore>();
        var all = await runs.QueryRunsAsync(new RunQuery { OnlyRootRuns = false });

        // The first call ran; the second was refused before a run row was opened.
        all.Count(static run => run.Depth == 1).ShouldBe(1);
    }

    [Fact]
    public async Task A_child_that_needs_tool_approval_comes_back_as_a_refusal_and_the_parent_is_not_left_waiting()
    {
        var provider = Provider(
            router => router
                .CallsTool(ChildTool, new { query = "do the dangerous task" })
                .EchoesLastToolResult("Delegated: "),
            researcher => researcher
                .CallsTool("dangerous_task", new { order = "ORD-7" })
                .RespondsWith("never reached"));

        await using var host = await StartAsync(provider, withApprovalTool: true);

        var text = await RunRouterAsync(host, streaming: false);

        text.ShouldContain("requires user approval", Case.Sensitive);
        text.ShouldContain("auto-approval rule", Case.Sensitive);

        var runs = host.Services.GetRequiredService<IRunStore>();
        var root = (await runs.QueryRunsAsync(new RunQuery { OnlyRootRuns = true })).ShouldHaveSingleItem();

        root.Status.ShouldBe(RunStatus.Completed);
    }

    [Fact]
    public async Task An_empty_query_fails_the_tool_call_before_the_child_starts_and_the_parent_completes()
    {
        // Measured, not assumed (the plan's "empty or oversize input" question): the framework's
        // AsAIFunction hands the query to AIAgent.RunAsync(string), which refuses an empty or
        // whitespace message with ArgumentException ("Argument is whitespace"). The tool call fails
        // ("Error: Function failed.", the function-invoking client hides the detail), the wrapper is
        // never reached, so no child run row is opened and the child's model is never called.
        var provider = Provider(static router => router
            .CallsTool(ChildTool, new { query = string.Empty })
            .EchoesLastToolResult("Delegated: "));

        await using var host = await StartAsync(provider);

        var text = await RunRouterAsync(host, streaming: false);

        // The tool was there to be called; the call is what failed.
        ToolNames(provider, RouterModel).ShouldAllBe(names => names.Contains(ChildTool, StringComparer.Ordinal));
        text.ShouldContain("Delegated: Error", Case.Sensitive);

        RequestsOf(provider, ResearcherModel).ShouldBeEmpty();

        var runs = host.Services.GetRequiredService<IRunStore>();
        var root = (await runs.QueryRunsAsync(new RunQuery { OnlyRootRuns = false })).ShouldHaveSingleItem();

        root.Status.ShouldBe(RunStatus.Completed);
    }

    [Fact]
    public async Task A_definition_saved_over_http_with_a_name_conflict_is_refused_with_both_names_in_tool_mode_only()
    {
        // The save path and POST /api/agents/validate share one validator; the 400 is what an operator
        // sees. The agent "lookup-agent" becomes the tool "lookup_agent", which the registered tool
        // "lookup_agent" already is.
        var provider = Provider(static router => router.RespondsWith("done"));

        await using var host = await TraconTestHost.StartAsync(builder => builder
            .AddModelProvider(provider)
            .AddTool(() => "found", name: "lookup_agent", description: "Looks something up.")
            .AddAgent(new AgentDefinition
            {
                Name = "lookup-agent",
                Instructions = "Look.",
                Model = Binding(ResearcherModel),
                Origin = AgentDefinitionOrigin.Code,
            }));

        static object Router(string name, object? subAgents) => new
        {
            name,
            instructions = "Delegate.",
            toolNames = new[] { "lookup_agent" },
            callableAgentNames = new[] { "lookup-agent" },
            subAgents,
            model = new { provider = ProviderName, model = RouterModel },
        };

        using var refused = await host.Client.PostAsJsonAsync(
            new Uri("/tracon/api/agents", UriKind.Relative), Router("router-tool", new { callMode = "Tool" }));

        refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var problem = await refused.Content.ReadAsStringAsync();

        problem.ShouldContain("'lookup-agent'", Case.Sensitive);
        problem.ShouldContain("'lookup_agent'", Case.Sensitive);

        using var validated = await host.Client.PostAsJsonAsync(
            new Uri("/tracon/api/agents/validate", UriKind.Relative), Router("router-tool", new { callMode = "Tool" }));

        (await validated.Content.ReadAsStringAsync()).ShouldContain("sub_agent_tool_name_conflict", Case.Sensitive);

        // The same names are not a conflict while the agent calls its callable agents as background tasks.
        using var accepted = await host.Client.PostAsJsonAsync(
            new Uri("/tracon/api/agents", UriKind.Relative), Router("router-background", subAgents: null));

        accepted.StatusCode.ShouldBe(HttpStatusCode.Created, await accepted.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_definition_saved_over_http_with_tool_mode_runs_in_tool_mode_and_keeps_the_mode_when_read_back()
    {
        var provider = Provider(static router => router
            .CallsTool(ChildTool, new { query = "sub task" })
            .EchoesLastToolResult("Delegated: "));

        await using var host = await TraconTestHost.StartAsync(builder => builder
            .AddModelProvider(provider)
            .AddAgent(new AgentDefinition
            {
                Name = ChildName,
                Description = "Performs research.",
                Instructions = "Research.",
                Model = Binding(ResearcherModel),
                Origin = AgentDefinitionOrigin.Code,
            }));

        using var created = await host.Client.PostAsJsonAsync(
            new Uri("/tracon/api/agents", UriKind.Relative),
            new
            {
                name = "stored-router",
                instructions = "Delegate.",
                callableAgentNames = new[] { ChildName },
                subAgents = new { callMode = "Tool" },
                model = new { provider = ProviderName, model = RouterModel },
            });

        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());

        var read = await host.Client.GetStringAsync(new Uri("/tracon/api/agents/stored-router", UriKind.Relative));

        read.ShouldContain("\"callMode\":\"Tool\"", Case.Sensitive);

        var catalog = host.Services.GetRequiredService<IAgentCatalog>();
        var agent = (await catalog.ResolveAsync("stored-router", culture: null, CancellationToken.None)).ShouldNotBeNull();

        (await agent.RunAsync("start")).Text.ShouldContain("Delegated: Sub task done", Case.Sensitive);
        ToolNames(provider, RouterModel).ShouldAllBe(names => names.Contains(ChildTool, StringComparer.Ordinal));
    }

    [Theory]
    [InlineData(SubAgentCallMode.Tool)]
    [InlineData(SubAgentCallMode.Background)]
    public async Task A_child_whose_model_provider_fails_is_reported_to_the_parent_in_both_modes(SubAgentCallMode mode)
    {
        // The auditor's question (Phase 228): what does the parent see when the sub-agent itself throws?
        // Measured per mode, not assumed - the two surfaces report a failed sub-agent differently.
        var provider = Provider(router =>
        {
            if (mode == SubAgentCallMode.Tool)
            {
                router.CallsTool(ChildTool, new { query = "sub task" });
            }
            else
            {
                router
                    .CallsTool(StartTask, new { agentName = ChildName, input = "sub task", description = "sub task" })
                    .CallsTool(WaitForCompletion, new { taskIds = new[] { 1 } })
                    .CallsTool(GetResults, new { taskId = 1 });
            }

            router.EchoesLastToolResult("Delegated: ");
        });

        await using var host = await StartAsync(provider, mode: mode, childProvider: new ThrowingModelProvider("broken"));

        var text = await RunRouterAsync(host, streaming: false);

        var runs = host.Services.GetRequiredService<IRunStore>();
        var all = await runs.QueryRunsAsync(new RunQuery { OnlyRootRuns = false });
        var root = all.Single(static run => run.Depth == 0);

        root.Status.ShouldBe(RunStatus.Completed, text);
        text.ShouldStartWith("Delegated: ", Case.Sensitive);
        all.Single(static run => run.Depth == 1).Status.ShouldBe(RunStatus.Failed);

        // Both surfaces give the parent the same reason (the provider and the fault type, never the
        // provider's own message). The framework's own wording differs; the reason does not.
        text.ShouldContain("The model provider request failed. Provider: 'broken', fault: 'FakeUpstreamOutageException'", Case.Sensitive);
        text.ShouldContain(mode == SubAgentCallMode.Tool ? $"Agent '{ChildName}' could not complete: " : "Task failed: ", Case.Sensitive);

        // The provider's own message can hold a connection string or a key; it reaches neither.
        text.ShouldNotContain("DNS", Case.Sensitive);
    }

    [Fact]
    public async Task A_parent_that_retries_a_failed_sub_agent_is_not_ended_by_the_tool_loops_error_count()
    {
        // The loop ends a run after three failed tool turns in a row. A failed sub-agent is a result the
        // model reads and may retry, so it must not count: before the wrapper, the fourth call below
        // ended the parent with the child's exception.
        var provider = Provider(static router => router
            .CallsTool(ChildTool, new { query = "try 1" })
            .CallsTool(ChildTool, new { query = "try 2" })
            .CallsTool(ChildTool, new { query = "try 3" })
            .CallsTool(ChildTool, new { query = "try 4" })
            .EchoesLastToolResult("Delegated: "));

        await using var host = await StartAsync(provider, childProvider: new ThrowingModelProvider("broken"));

        var text = await RunRouterAsync(host, streaming: false);

        text.ShouldContain("could not complete", Case.Sensitive);

        var runs = host.Services.GetRequiredService<IRunStore>();
        var all = await runs.QueryRunsAsync(new RunQuery { OnlyRootRuns = false });

        all.Single(static run => run.Depth == 0).Status.ShouldBe(RunStatus.Completed, text);
        all.Count(static run => run.Depth == 1).ShouldBe(4);
    }

    [Fact]
    public async Task The_wait_limit_comes_back_as_the_tool_result_and_the_child_run_closes_in_tool_mode()
    {
        // ChildDeadline is claimed for both modes (site, CHANGELOG); SubAgentTimeoutTests runs only Background.
        var hanging = new HangingModelProvider("hanging", respectsCancellation: true);
        var provider = Provider(static router => router
            .CallsTool(ChildTool, new { query = "sub task" })
            .EchoesLastToolResult("Delegated: "));

        await using var host = await StartAsync(
            provider, childProvider: hanging, childDeadline: TimeSpan.FromMilliseconds(50));

        var text = await RunRouterAsync(host, streaming: false).WaitAsync(TimeSpan.FromSeconds(10));

        text.ShouldContain("did not respond in time", Case.Sensitive);

        var runs = host.Services.GetRequiredService<IRunStore>();
        var root = (await runs.QueryRunsAsync(new RunQuery())).ShouldHaveSingleItem();
        var types = new List<RunEventType>();

        await foreach (var runEvent in runs.ReadEventsAsync(root.Id))
        {
            types.Add(runEvent.Type);
        }

        types.ShouldContain(RunEventType.ChildRunTimedOut);
        types.ShouldContain(RunEventType.ChildRunCompleted);
        root.Status.ShouldBe(RunStatus.Completed);
    }

    private static FakeModelProvider Provider(
        Action<FakeModelProvider> router,
        Action<FakeModelProvider>? researcher = null)
    {
        var provider = new FakeModelProvider(ProviderName);

        provider.ForModel(RouterModel, router);
        provider.ForModel(ResearcherModel, researcher ?? (static model => model.RespondsWith("Sub task done", inputTokens: 4, outputTokens: 6)));

        return provider;
    }

    /// <summary>The tool names of every request the router's model received, one list for each request.</summary>
    private static List<List<string>> ToolNames(FakeModelProvider provider, string model)
        => [.. RequestsOf(provider, model)
            .Select(static request => (request.Options!.Tools ?? []).Select(static tool => tool.Name).ToList())];

    private static List<FakeModelRequest> RequestsOf(FakeModelProvider provider, string model)
        => [.. provider.Requests.Where(request => string.Equals(request.Options?.ModelId, model, StringComparison.Ordinal))];

    private static async Task<string> RunRouterAsync(TraconTestHost host, bool streaming)
    {
        var catalog = host.Services.GetRequiredService<IAgentCatalog>();
        var agent = (await catalog.ResolveAsync("yonlendirici", culture: null, CancellationToken.None)).ShouldNotBeNull();

        if (!streaming)
        {
            return (await agent.RunAsync("start")).Text;
        }

        var text = new StringBuilder();

        await foreach (var update in agent.RunStreamingAsync("start"))
        {
            text.Append(update.Text);
        }

        return text.ToString();
    }

    private static Task<TraconTestHost> StartAsync(
        FakeModelProvider provider,
        bool harness = false,
        SubAgentCallMode? mode = SubAgentCallMode.Tool,
        int maxDepth = 3,
        int maxRuns = 25,
        bool withApprovalTool = false,
        IModelProvider? childProvider = null,
        TimeSpan? childDeadline = null)
        => TraconTestHost.StartAsync(
            configureTracon: builder =>
            {
                builder.AddModelProvider(provider);

                if (childProvider is not null)
                {
                    builder.AddModelProvider(childProvider);
                }

                if (withApprovalTool)
                {
                    builder.AddTool(
                        (Func<string, string>)(order => $"{order} done."),
                        name: "dangerous_task",
                        description: "Does a task that needs approval.",
                        configure: static options => options.RequiresApproval = true);
                }

                builder.AddAgent(new AgentDefinition
                {
                    Name = ChildName,
                    Description = "Performs research.",
                    Instructions = "Research.",
                    Model = childProvider is null
                        ? Binding(ResearcherModel)
                        : new ModelBinding { Provider = childProvider.Name, Model = childProvider.Models[0].Name },
                    ToolNames = withApprovalTool ? ["dangerous_task"] : [],
                    Origin = AgentDefinitionOrigin.Code,
                });

                builder.AddAgent(new AgentDefinition
                {
                    Name = "yonlendirici",
                    Description = "Delegates the work.",
                    Instructions = "Delegate.",
                    Model = Binding(RouterModel),
                    CallableAgentNames = [ChildName],
                    SubAgents = mode is { } callMode
                        ? new SubAgentSettings
                        {
                            CallMode = callMode,
                            ChildDeadline = childDeadline,
                            WaitTimeout = childDeadline is { } deadline ? deadline * 20 : null,
                        }
                        : null,
                    Harness = harness ? new HarnessSettings() : null,
                    Origin = AgentDefinitionOrigin.Code,
                });
            },
            configureServices: services => services.Configure<TraconOptions>(options =>
            {
                options.AgentGraph.MaxDepth = maxDepth;
                options.AgentGraph.MaxTotalRuns = maxRuns;
            }));

    private static ModelBinding Binding(string model) => new() { Provider = ProviderName, Model = model };
}
