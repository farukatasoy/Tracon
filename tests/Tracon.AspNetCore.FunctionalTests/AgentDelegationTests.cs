using Tracon.AspNetCore.FunctionalTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using FakeModelProvider = Tracon.Testing.FakeModelProvider;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// End-to-end scenario where one agent genuinely calls another agent.
/// </summary>
/// <remarks>
/// The scenario is set up with two agents: <c>yonlendirici</c> hands the work
/// off to <c>arastirmaci</c>. The model provider does not go over the network,
/// but it does call Microsoft Agent Framework's actual background task tools;
/// no fake shortcut is used. The two agents use DIFFERENT models of the SAME
/// provider; each model has its own independent response queue
/// (<see cref="FakeModelProvider.ForModel"/>), so they do not disturb each
/// other's order.
/// </remarks>
public sealed class AgentDelegationTests
{
    private const string RouterModel = "router-model";
    private const string ResearcherModel = "researcher-model";

    // Microsoft Agent Framework's background task tools.
    private const string StartTask = "background_agents_start_task";
    private const string WaitForCompletion = "background_agents_wait_for_first_completion";
    private const string GetResults = "background_agents_get_task_results";

    [Fact]
    public async Task Non_streaming_call_produces_two_separate_run_rows()
    {
        await using var host = await StartAsync();

        var agent = await ResolveRouterAsync(host);
        var response = await agent.RunAsync("start");

        response.Text.ShouldContain("Delegated", Case.Sensitive);

        await AssertTreeAsync(host);
    }

    [Fact]
    public async Task Streaming_call_also_produces_two_separate_run_rows()
    {
        // 🚨 The streaming path is tested separately. The scope lives in an
        // AsyncLocal, and an assignment made inside an async iterator body does
        // not cross the `yield return` boundary; testing only the non-streaming
        // path would miss this regression.
        await using var host = await StartAsync();

        var agent = await ResolveRouterAsync(host);

        await foreach (var update in agent.RunStreamingAsync("start"))
        {
            _ = update;
        }

        await AssertTreeAsync(host);
    }

    [Fact]
    public async Task Child_run_summary_events_are_written_to_the_root_run()
    {
        await using var host = await StartAsync();

        var agent = await ResolveRouterAsync(host);
        await agent.RunAsync("start");

        var runs = host.Services.GetRequiredService<IRunStore>();
        var root = (await runs.QueryRunsAsync(new RunQuery())).ShouldHaveSingleItem();

        var types = new List<RunEventType>();

        await foreach (var runEvent in runs.ReadEventsAsync(root.Id))
        {
            types.Add(runEvent.Type);
        }

        // The FULL stream of the child run is not mirrored; only its start and
        // end are reported. Mirroring would multiply event volume across the tree.
        types.ShouldContain(RunEventType.ChildRunStarted);
        types.ShouldContain(RunEventType.ChildRunCompleted);
        types.Count(static type => type == RunEventType.RunStarted).ShouldBe(1);
    }

    [Fact]
    public async Task No_child_call_can_be_made_when_the_depth_limit_is_zero()
    {
        await using var host = await StartAsync(maxDepth: 0);

        var agent = await ResolveRouterAsync(host);
        var response = await agent.RunAsync("start");

        response.Text.ShouldContain("call depth limit was exceeded", Case.Sensitive);

        var runs = host.Services.GetRequiredService<IRunStore>();

        // The child run NEVER starts; a rejected call produces no row.
        (await runs.QueryRunsAsync(new RunQuery { OnlyRootRuns = false })).ShouldHaveSingleItem();
    }

    private static async Task<Microsoft.Agents.AI.AIAgent> ResolveRouterAsync(TraconTestHost host)
    {
        var catalog = host.Services.GetRequiredService<IAgentCatalog>();

        return (await catalog.ResolveAsync("yonlendirici", culture: null, CancellationToken.None)).ShouldNotBeNull();
    }

    private static async Task AssertTreeAsync(TraconTestHost host)
    {
        var runs = host.Services.GetRequiredService<IRunStore>();
        var all = await runs.QueryRunsAsync(new RunQuery { OnlyRootRuns = false });

        all.Count.ShouldBe(2);

        var root = all.Single(static run => run.Depth == 0);
        var child = all.Single(static run => run.Depth == 1);

        root.AgentName.ShouldBe("yonlendirici");
        root.ParentRunId.ShouldBeNull();
        root.ChildRunCount.ShouldBe(1);

        child.AgentName.ShouldBe("arastirmaci");
        child.ParentRunId.ShouldBe(root.Id);
        child.RootRunId.ShouldBe(root.Id);
        child.Status.ShouldBe(RunStatus.Completed);

        // The tree total combines the tokens of both runs.
        root.TreeUsage!.TotalTokens.ShouldBe(20);
        root.Usage!.TotalTokens.ShouldBe(10);
    }

    private static Task<TraconTestHost> StartAsync(int maxDepth = 3)
        => TraconTestHost.StartAsync(
            configureTracon: builder =>
            {
                var provider = new FakeModelProvider("routing")
                    .ForModel(RouterModel, cfg => cfg
                        .CallsTool(StartTask, new { agentName = "arastirmaci", input = "sub task", description = "sub task" })
                        .CallsTool(WaitForCompletion, new { taskIds = new[] { 1 } })
                        .CallsTool(GetResults, new { taskId = 1 })
                        // The final response GENUINELY reflects the last tool result - the
                        // error text the test expects when the depth limit is exceeded also
                        // comes from here.
                        .EchoesLastToolResult("Delegated: ", inputTokens: 4, outputTokens: 6))
                    .ForModel(ResearcherModel, cfg => cfg
                        .RespondsWith("Sub task done", inputTokens: 4, outputTokens: 6));

                builder.AddModelProvider(provider);

                builder.AddAgent(new AgentDefinition
                {
                    Name = "arastirmaci",
                    Description = "Performs research.",
                    Instructions = "Research.",
                    Model = Binding(ResearcherModel),
                    Origin = AgentDefinitionOrigin.Code,
                });

                builder.AddAgent(new AgentDefinition
                {
                    Name = "yonlendirici",
                    Description = "Delegates the work.",
                    Instructions = "Delegate.",
                    Model = Binding(RouterModel),
                    CallableAgentNames = ["arastirmaci"],
                    Origin = AgentDefinitionOrigin.Code,
                });
            },
            configureServices: services => services.Configure<TraconOptions>(
                options => options.AgentGraph.MaxDepth = maxDepth));

    private static ModelBinding Binding(string model) => new() { Provider = "routing", Model = model };
}
