using System.Net;
using System.Net.Http.Json;
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
        var agent = (await host.Services.GetRequiredService<IAgentCatalog>().ResolveAsync(AgentName, cancellationToken: CancellationToken.None)).ShouldNotBeNull();

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
        var agent = (await host.Services.GetRequiredService<IAgentCatalog>().ResolveAsync(AgentName, cancellationToken: CancellationToken.None)).ShouldNotBeNull();

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
        var agent = (await host.Services.GetRequiredService<IAgentCatalog>().ResolveAsync(AgentName, cancellationToken: CancellationToken.None)).ShouldNotBeNull();

        var exception = await Should.ThrowAsync<ArgumentException>(
            () => agent.RunAsync("hi", session: null, new ChatClientAgentRunOptions(new ChatOptions { Seed = 42 })));

        exception.Message.ShouldContain("ChatOptions.Seed", Case.Sensitive);
        provider.Requests.ShouldBeEmpty();

        await agent.RunAsync("hi", session: null, new ChatClientAgentRunOptions(new ChatOptions { AllowMultipleToolCalls = false }));

        provider.Requests.ShouldHaveSingleItem().Options.ShouldNotBeNull().AllowMultipleToolCalls.ShouldBe(false);
    }

    /// <summary>
    /// Added instructions and the identifier of the run travel in one options object, on the buffered and the
    /// streamed path: the model reads the definition's instructions and then the run's, and the record is kept
    /// under the identifier the caller chose.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Added_instructions_follow_the_definitions_and_the_run_is_recorded_under_the_chosen_identifier(bool streaming)
    {
        var provider = new FakeModelProvider(ProviderName).ForModel(ModelName, static cfg => cfg.EchoesUserMessage());
        await using var host = await StartAsync(provider);
        var agent = (await host.Services.GetRequiredService<IAgentCatalog>().ResolveAsync(AgentName, cancellationToken: CancellationToken.None)).ShouldNotBeNull();
        var runId = TraconId.NewId();
        var options = new TraconRunOptions { RunId = runId, AdditionalInstructions = "RUN-NOTE" };

        if (streaming)
        {
            await foreach (var update in agent.RunStreamingAsync("hi", session: null, options, TestContext.Current.CancellationToken))
            {
                _ = update;
            }
        }
        else
        {
            await agent.RunAsync("hi", session: null, options, TestContext.Current.CancellationToken);
        }

        var request = provider.Requests.ShouldHaveSingleItem().Options.ShouldNotBeNull();

        request.Instructions.ShouldBe("Answer briefly.\nRUN-NOTE");
        request.ModelId.ShouldBe(ModelName);
        request.Temperature.ShouldBe(0.25f, "what the definition owns survives the merge");

        var run = (await host.Services.GetRequiredService<IRunStore>().QueryRunsAsync(new RunQuery(), TestContext.Current.CancellationToken)).ShouldHaveSingleItem();

        run.Id.ShouldBe(runId);
        run.Status.ShouldBe(RunStatus.Completed);
    }

    /// <summary>The identifier the caller chose names the record of a run that fails, too.</summary>
    [Fact]
    public async Task A_run_that_fails_is_recorded_under_the_chosen_identifier()
    {
        await using var host = await TraconTestHost.StartAsync(builder => builder
            .AddModelProvider(new ThrowingModelProvider())
            .AddAgent(new AgentDefinition
            {
                Name = "failing-run-agent",
                Instructions = "Answer briefly.",
                Model = new ModelBinding { Provider = "kirik", Model = "kirik-1" },
                Origin = AgentDefinitionOrigin.Code,
            }));
        var agent = (await host.Services.GetRequiredService<IAgentCatalog>().ResolveAsync("failing-run-agent", cancellationToken: CancellationToken.None)).ShouldNotBeNull();
        var runId = TraconId.NewId();

        await Should.ThrowAsync<Exception>(
            () => agent.RunAsync("hi", session: null, new TraconRunOptions { RunId = runId, AdditionalInstructions = "RUN-NOTE" }, TestContext.Current.CancellationToken));

        var run = (await host.Services.GetRequiredService<IRunStore>().QueryRunsAsync(new RunQuery(), TestContext.Current.CancellationToken)).ShouldHaveSingleItem();

        run.Id.ShouldBe(runId);
        run.Status.ShouldBe(RunStatus.Failed);
    }

    /// <summary>
    /// The text is for one run: the next run of the agent does not carry it, and the record of the run does not hold
    /// it (a replay therefore runs without it, which the options' remarks say).
    /// </summary>
    [Fact]
    public async Task Added_instructions_are_for_one_run_and_the_record_does_not_hold_them()
    {
        var provider = new FakeModelProvider(ProviderName).ForModel(ModelName, static cfg => cfg.EchoesUserMessage());
        await using var host = await StartAsync(provider);
        var agent = (await host.Services.GetRequiredService<IAgentCatalog>().ResolveAsync(AgentName, cancellationToken: CancellationToken.None)).ShouldNotBeNull();
        var runId = TraconId.NewId();

        await agent.RunAsync("hi", session: null, new TraconRunOptions { RunId = runId, AdditionalInstructions = "RUN-NOTE" }, TestContext.Current.CancellationToken);
        await agent.RunAsync("hi again", session: null, new TraconRunOptions(), TestContext.Current.CancellationToken);

        provider.Requests.Select(static request => request.Options!.Instructions).ShouldBe(["Answer briefly.\nRUN-NOTE", "Answer briefly."]);

        var store = host.Services.GetRequiredService<IRunStore>();
        var input = await host.Services.GetRequiredService<IRunInputStore>().GetAsync(
            host.Services.GetRequiredService<ITenantContext>().TenantId,
            runId,
            TestContext.Current.CancellationToken);

        input.ShouldNotBeNull().Messages.ShouldAllBe(static message => !message.Text.Contains("RUN-NOTE", StringComparison.Ordinal));

        await foreach (var runEvent in store.ReadEventsAsync(runId, cancellationToken: TestContext.Current.CancellationToken))
        {
            (runEvent.Text ?? string.Empty).ShouldNotContain("RUN-NOTE", Case.Sensitive);
            (runEvent.Payload ?? string.Empty).ShouldNotContain("RUN-NOTE", Case.Sensitive);
        }
    }

    /// <summary>
    /// A replay repeats the run from its record, and the record holds no added instructions: the replayed request
    /// carries the definition's instructions only. The limit is written in the options' remarks, and this pins it.
    /// </summary>
    [Fact]
    public async Task A_replay_runs_without_the_added_instructions()
    {
        var provider = new FakeModelProvider(ProviderName).ForModel(ModelName, static cfg => cfg.EchoesUserMessage());
        await using var host = await StartAsync(provider);
        var agent = (await host.Services.GetRequiredService<IAgentCatalog>().ResolveAsync(AgentName, cancellationToken: CancellationToken.None)).ShouldNotBeNull();
        var runId = TraconId.NewId();

        await agent.RunAsync("hi", session: null, new TraconRunOptions { RunId = runId, AdditionalInstructions = "RUN-NOTE" }, TestContext.Current.CancellationToken);

        using var replay = await host.Client.PostAsJsonAsync($"/tracon/api/runs/{runId}/replay", new { toolMode = "LiveTools" }, TestContext.Current.CancellationToken);

        replay.StatusCode.ShouldBe(HttpStatusCode.OK, await replay.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        provider.Requests.Count.ShouldBe(2);
        provider.Requests[^1].Options.ShouldNotBeNull().Instructions.ShouldBe("Answer briefly.");
    }

    private static Task<TraconTestHost> StartAsync(FakeModelProvider provider)
        => TraconTestHost.StartAsync(builder => builder
            .AddModelProvider(provider)
            .AddAgent(new AgentDefinition
            {
                Name = AgentName,
                Instructions = "Answer briefly.",
                Model = new ModelBinding { Provider = ProviderName, Model = ModelName, Temperature = 0.25f },
                Origin = AgentDefinitionOrigin.Code,
            }));
}
