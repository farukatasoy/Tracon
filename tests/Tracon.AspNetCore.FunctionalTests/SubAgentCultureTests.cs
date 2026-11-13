using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;
using Tracon.Testing;
using TraconTestHost = Tracon.AspNetCore.FunctionalTests.Infrastructure.TraconTestHost;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// A sub-agent runs with the culture its parent run was requested with: the culture of the root run reaches the
/// instructions of every agent below it, in both call modes, on a queued run and through a sub-agent of a sub-agent.
/// </summary>
/// <remarks>
/// What the sub-agent's model <em>receives</em> is read from <see cref="FakeModelProvider.Requests"/>: the claim
/// is about the text on the wire, not about the compiled definition.
/// </remarks>
public sealed class SubAgentCultureTests
{
    private const string ProviderName = "polyglot";
    private const string RouterModel = "router-model";
    private const string MiddleModel = "middle-model";
    private const string ResearcherModel = "researcher-model";

    private const string RouterName = "yonlendirici";
    private const string MiddleName = "ara-katman";
    private const string ChildName = "arastirma-ci";
    private const string ChildTool = "arastirma_ci";
    private const string MiddleTool = "ara_katman";

    private const string ChildDefault = "Research briefly.";
    private const string ChildTurkish = "Kisaca arastir ve Turkce cevap ver.";
    private const string MiddleDefault = "Pass the work on.";
    private const string MiddleTurkish = "Isi aktar ve Turkce cevap ver.";

    // Microsoft Agent Framework's background task tools.
    private const string StartTask = "background_agents_start_task";
    private const string WaitForCompletion = "background_agents_wait_for_first_completion";
    private const string GetResults = "background_agents_get_task_results";

    [Theory]
    [InlineData(SubAgentCallMode.Tool)]
    [InlineData(SubAgentCallMode.Background)]
    public async Task A_sub_agent_runs_with_the_culture_the_parent_run_was_requested_with(SubAgentCallMode mode)
    {
        var provider = Provider(router => CallsTheChild(router, mode));
        await using var host = await StartAsync(provider, mode);

        await RunAsync(host, RouterName, culture: "tr");

        InstructionsOf(provider, ResearcherModel).ShouldBe(ChildTurkish);
    }

    [Theory]
    [InlineData(SubAgentCallMode.Tool)]
    [InlineData(SubAgentCallMode.Background)]
    public async Task A_region_subtag_of_the_parents_culture_falls_back_to_the_sub_agents_parent_culture_entry(SubAgentCallMode mode)
    {
        var provider = Provider(router => CallsTheChild(router, mode));
        await using var host = await StartAsync(provider, mode);

        await RunAsync(host, RouterName, culture: "tr-TR");

        InstructionsOf(provider, ResearcherModel).ShouldBe(ChildTurkish);
    }

    [Theory]
    [InlineData(SubAgentCallMode.Tool)]
    [InlineData(SubAgentCallMode.Background)]
    public async Task A_sub_agent_with_no_entry_for_the_parents_culture_runs_with_its_default_instructions(SubAgentCallMode mode)
    {
        var provider = Provider(router => CallsTheChild(router, mode));
        await using var host = await StartAsync(provider, mode);

        await RunAsync(host, RouterName, culture: "de");

        InstructionsOf(provider, ResearcherModel).ShouldBe(ChildDefault);
    }

    [Theory]
    [InlineData(SubAgentCallMode.Tool)]
    [InlineData(SubAgentCallMode.Background)]
    public async Task A_parent_run_with_no_culture_gives_its_sub_agent_the_default_instructions(SubAgentCallMode mode)
    {
        var provider = Provider(router => CallsTheChild(router, mode));
        await using var host = await StartAsync(provider, mode);

        await RunAsync(host, RouterName, culture: null);

        InstructionsOf(provider, ResearcherModel).ShouldBe(ChildDefault);
    }

    [Fact]
    public async Task A_queued_parent_run_gives_its_sub_agent_the_requested_culture()
    {
        var provider = Provider(router => CallsTheChild(router, SubAgentCallMode.Tool));
        await using var host = await StartAsync(
            provider,
            SubAgentCallMode.Tool,
            static tracon => tracon.UseScheduling(static o => o.PollInterval = TimeSpan.FromMilliseconds(20)));

        using var request = new HttpRequestMessage(HttpMethod.Post, RunUri(RouterName))
        {
            Content = JsonContent.Create(new AgentRunRequest { Message = "start", Culture = "tr" }),
        };
        request.Headers.Add("Prefer", "respond-async");

        using var accepted = await host.Client.SendAsync(request, TestContext.Current.CancellationToken);

        accepted.StatusCode.ShouldBe(HttpStatusCode.Accepted);

        await WaitUntil.TrueAsync(() => RequestsOf(provider, ResearcherModel).Count > 0, "the queued run's sub-agent to reach the model");

        InstructionsOf(provider, ResearcherModel).ShouldBe(ChildTurkish);
    }

    [Fact]
    public async Task The_culture_reaches_a_sub_agent_of_a_sub_agent()
    {
        var provider = new FakeModelProvider(ProviderName);
        provider.ForModel(RouterModel, static router => router
            .CallsTool(MiddleTool, new { query = "pass it on" })
            .EchoesLastToolResult("Delegated: "));
        provider.ForModel(MiddleModel, static middle => middle
            .CallsTool(ChildTool, new { query = "sub task" })
            .EchoesLastToolResult("Passed on: "));
        provider.ForModel(ResearcherModel, static model => model.RespondsWith("Sub task done"));

        await using var host = await StartAsync(provider, SubAgentCallMode.Tool, middleLayer: true);

        await RunAsync(host, RouterName, culture: "tr");

        InstructionsOf(provider, MiddleModel).ShouldBe(MiddleTurkish);
        InstructionsOf(provider, ResearcherModel).ShouldBe(ChildTurkish);
    }

    private static void CallsTheChild(FakeModelProvider router, SubAgentCallMode mode)
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
    }

    private static FakeModelProvider Provider(Action<FakeModelProvider> router)
    {
        var provider = new FakeModelProvider(ProviderName);

        provider.ForModel(RouterModel, router);
        provider.ForModel(ResearcherModel, static model => model.RespondsWith("Sub task done"));

        return provider;
    }

    private static List<FakeModelRequest> RequestsOf(FakeModelProvider provider, string model)
        => [.. provider.Requests.Where(request => string.Equals(request.Options?.ModelId, model, StringComparison.Ordinal))];

    private static string? InstructionsOf(FakeModelProvider provider, string model)
    {
        var requests = RequestsOf(provider, model);

        requests.ShouldNotBeEmpty($"the model '{model}' was never called");

        return requests[0].Options!.Instructions;
    }

    private static Uri RunUri(string agent) => new($"/tracon/api/agents/{agent}/run", UriKind.Relative);

    private static async Task RunAsync(TraconTestHost host, string agent, string? culture)
    {
        using var response = await host.Client.PostAsJsonAsync(
            RunUri(agent),
            new AgentRunRequest { Message = "start", Culture = culture },
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        // The stream is read to its end: the run is over only then.
        _ = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
    }

    private static Task<TraconTestHost> StartAsync(
        FakeModelProvider provider,
        SubAgentCallMode mode,
        Action<ITraconBuilder>? configureTracon = null,
        bool middleLayer = false)
        => TraconTestHost.StartAsync(
            configureTracon: builder =>
            {
                builder.AddModelProvider(provider);

                builder.AddAgent(new AgentDefinition
                {
                    Name = ChildName,
                    Description = "Performs research.",
                    Instructions = ChildDefault,
                    InstructionsByCulture = new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["tr"] = ChildTurkish,
                    },
                    Model = Binding(ResearcherModel),
                    Origin = AgentDefinitionOrigin.Code,
                });

                if (middleLayer)
                {
                    builder.AddAgent(new AgentDefinition
                    {
                        Name = MiddleName,
                        Description = "Passes the work on.",
                        Instructions = MiddleDefault,
                        InstructionsByCulture = new Dictionary<string, string>(StringComparer.Ordinal)
                        {
                            ["tr"] = MiddleTurkish,
                        },
                        Model = Binding(MiddleModel),
                        CallableAgents = [ChildName],
                        SubAgents = new SubAgentSettings { CallMode = mode },
                        Origin = AgentDefinitionOrigin.Code,
                    });
                }

                builder.AddAgent(new AgentDefinition
                {
                    Name = RouterName,
                    Description = "Delegates the work.",
                    Instructions = "Delegate.",
                    Model = Binding(RouterModel),
                    CallableAgents = [middleLayer ? MiddleName : ChildName],
                    SubAgents = new SubAgentSettings { CallMode = mode },
                    Origin = AgentDefinitionOrigin.Code,
                });

                configureTracon?.Invoke(builder);
            });

    private static ModelBinding Binding(string model) => new() { Provider = ProviderName, Model = model };
}
