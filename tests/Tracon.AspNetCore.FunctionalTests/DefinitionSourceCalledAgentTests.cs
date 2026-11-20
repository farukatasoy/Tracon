using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;
using FakeModelProvider = Tracon.Testing.FakeModelProvider;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// An agent that another agent calls, whose definition a custom <see cref="IAgentDefinitionSource"/> returns.
/// </summary>
/// <remarks>
/// A called agent gets no value from the caller (the run endpoint's values belong to the agent it runs, and a
/// workflow hands each step the values its own schema declares). It still has to be found in the source that
/// holds its name and compiled from that source's definition, with its own defaults bound. This runs the real
/// background-task tools of Microsoft Agent Framework through the catalog, the same way a code agent is called.
/// </remarks>
public sealed class DefinitionSourceCalledAgentTests
{
    private const string RouterModel = "caller-router-model";
    private const string ChildModel = "caller-child-model";
    private const string ProviderName = "caller-provider";
    private const string Child = "source-researcher";

    private const string StartTask = "background_agents_start_task";
    private const string WaitForCompletion = "background_agents_wait_for_first_completion";
    private const string GetResults = "background_agents_get_task_results";

    [Fact]
    public async Task A_called_agent_is_compiled_from_the_sources_definition_with_its_own_defaults_bound()
    {
        var provider = new FakeModelProvider(ProviderName)
            .ForModel(RouterModel, cfg => cfg
                .CallsTool(StartTask, new { agentName = Child, input = "sub task", description = "sub task" })
                .CallsTool(WaitForCompletion, new { taskIds = new[] { 1 } })
                .CallsTool(GetResults, new { taskId = 1 })
                .EchoesLastToolResult("Delegated: ", inputTokens: 4, outputTokens: 6))
            .ForModel(ChildModel, cfg => cfg.RespondsWith("Sub task done", inputTokens: 4, outputTokens: 6));

        var source = new InMemoryDefinitionSource().Add(new AgentDefinition
        {
            Name = Child,
            Instructions = "Research {{topic}}.",
            Parameters = [new AgentParameter { Name = "topic", Kind = AgentParameterKind.Text, DefaultValue = "tides" }],
            Model = new ModelBinding { Provider = ProviderName, Model = ChildModel },
        });

        await using var host = await TraconTestHost.StartAsync(
            configureTracon: builder =>
            {
                builder.AddModelProvider(provider);
                builder.AddAgentDefinitionSource(source);
                builder.AddAgent(new AgentDefinition
                {
                    Name = "caller-router",
                    Instructions = "Delegate.",
                    Model = new ModelBinding { Provider = ProviderName, Model = RouterModel },
                    CallableAgents = [Child],
                });
            });

        var agent = (await host.Services.GetRequiredService<IAgentCatalog>()
            .ResolveAsync("caller-router", cancellationToken: TestContext.Current.CancellationToken)).ShouldNotBeNull();

        var response = await agent.RunAsync("start", cancellationToken: TestContext.Current.CancellationToken);

        response.Text.ShouldContain("Delegated", Case.Sensitive);

        var runs = await host.Services.GetRequiredService<IRunStore>().QueryRunsAsync(new RunQuery { OnlyRootRuns = false });
        var child = runs.Single(static run => run.Depth == 1);

        child.AgentName.ShouldBe(Child);
        child.Status.ShouldBe(RunStatus.Completed);
        provider.Requests.Select(static request => request.Options?.Instructions).ShouldContain("Research tides.", StringComparer.Ordinal);
    }
}
