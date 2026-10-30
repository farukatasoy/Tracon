using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;
// Same rationale as ToolGovernanceEndpointTests.cs: the package's
// Tracon.Testing.TraconTestHost and this project's own TraconTestHost
// share the same name; a blanket `using Tracon.Testing;` would produce CS0104.
using FakeModelProvider = Tracon.Testing.FakeModelProvider;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// Code that runs a catalog agent with model options for one run
/// (<see cref="ChatClientAgentRunOptions"/>) cannot change what the definition owns.
/// </summary>
/// <remarks>
/// Measured 2026-10-20: a model id on the run reached the provider while the run record kept
/// the definition's model, and a chat client factory ran over the agent's model client. The
/// run-recording wrapper is the boundary every catalog agent passes, so the check sits there.
/// </remarks>
public sealed class RunChatOptionsTests
{
    private const string ProviderName = "run-options-model";
    private const string ModelName = "run-options-1";
    private const string AgentName = "run-options-agent";

    [Fact]
    public async Task A_run_cannot_change_the_model_the_definition_names()
    {
        var provider = new FakeModelProvider(ProviderName).ForModel(ModelName, static cfg => cfg.EchoesUserMessage());
        await using var host = await StartAsync(provider);
        var agent = (await host.Services.GetRequiredService<IAgentCatalog>().ResolveAsync(AgentName, culture: null, CancellationToken.None)).ShouldNotBeNull();

        var exception = await Should.ThrowAsync<ArgumentException>(
            () => agent.RunAsync("hi", session: null, new ChatClientAgentRunOptions(new ChatOptions { ModelId = "other-model" })));

        exception.Message.ShouldContain("ChatOptions.ModelId", Case.Sensitive);
        provider.Requests.ShouldBeEmpty();
        (await host.Services.GetRequiredService<IRunStore>().QueryRunsAsync(new RunQuery())).ShouldBeEmpty();
    }

    [Fact]
    public async Task A_run_may_set_an_option_the_definition_leaves_open()
    {
        var provider = new FakeModelProvider(ProviderName).ForModel(ModelName, static cfg => cfg.EchoesUserMessage());
        await using var host = await StartAsync(provider);
        var agent = (await host.Services.GetRequiredService<IAgentCatalog>().ResolveAsync(AgentName, culture: null, CancellationToken.None)).ShouldNotBeNull();

        await agent.RunAsync(
            "hi",
            session: null,
            new ChatClientAgentRunOptions(new ChatOptions
            {
                AdditionalProperties = new AdditionalPropertiesDictionary { ["run-note"] = "kept" },
                Instructions = "RUN-NOTE",
            }));

        var request = provider.Requests.ShouldHaveSingleItem().Options.ShouldNotBeNull();
        request.AdditionalProperties.ShouldNotBeNull()["run-note"].ShouldBe("kept");
        request.ModelId.ShouldBe(ModelName);
        request.Instructions.ShouldNotBeNull().ShouldContain("Answer briefly.", Case.Sensitive);
        request.Instructions.ShouldContain("RUN-NOTE", Case.Sensitive);
    }

    /// <summary>
    /// A request field the definition can set is the definition's: a seed on the run would reach the provider
    /// unchecked and unrecorded. A handoff step may turn parallel tool calls off, which only narrows them.
    /// </summary>
    [Fact]
    public async Task A_run_cannot_set_a_request_field_but_may_turn_parallel_tool_calls_off()
    {
        var provider = new FakeModelProvider(ProviderName).ForModel(ModelName, static cfg => cfg.EchoesUserMessage());
        await using var host = await StartAsync(provider);
        var agent = (await host.Services.GetRequiredService<IAgentCatalog>().ResolveAsync(AgentName, culture: null, CancellationToken.None)).ShouldNotBeNull();

        var exception = await Should.ThrowAsync<ArgumentException>(
            () => agent.RunAsync("hi", session: null, new ChatClientAgentRunOptions(new ChatOptions { Seed = 42 })));

        exception.Message.ShouldContain("ChatOptions.Seed", Case.Sensitive);
        provider.Requests.ShouldBeEmpty();

        await agent.RunAsync("hi", session: null, new ChatClientAgentRunOptions(new ChatOptions { AllowMultipleToolCalls = false }));

        provider.Requests.ShouldHaveSingleItem().Options.ShouldNotBeNull().AllowMultipleToolCalls.ShouldBe(false);
    }

    private static Task<TraconTestHost> StartAsync(FakeModelProvider provider)
        => TraconTestHost.StartAsync(builder => builder
            .AddModelProvider(provider)
            .AddAgent(new AgentDefinition
            {
                Name = AgentName,
                Instructions = "Answer briefly.",
                Model = new ModelBinding { Provider = ProviderName, Model = ModelName },
                Origin = AgentDefinitionOrigin.Code,
            }));
}
