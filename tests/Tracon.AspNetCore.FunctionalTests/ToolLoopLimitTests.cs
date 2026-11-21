using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;
using FakeModelProvider = Tracon.Testing.FakeModelProvider;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// The tool-call loop of an agent that sets no limit of its own stops at the framework's default of 40 rounds: the 41st
/// model request offers no function tools, and the run ends as a completed run.
/// </summary>
/// <remarks>
/// <para>
/// The site documents this limit and what the model sees at it, so the number and the behavior are pinned here. A harness
/// agent can lower the limit with its own setting, which has its own test; an agent that is not a harness agent has no
/// setting for it.
/// </para>
/// <para>
/// The model is a script, not a real one: it answers each request with the next step of the script whether or not the
/// request offers the tool. A real model that is offered no tool answers in text.
/// </para>
/// </remarks>
public sealed class ToolLoopLimitTests
{
    private const string Agent = "looping-agent";
    private const string Provider = "looping-model";
    private const string Tool = "ping";
    private const int DefaultLimit = 40;

    [Fact]
    public async Task The_forty_first_model_request_offers_no_function_tools_and_the_model_answers_in_text()
    {
        var model = new FakeModelProvider(Provider);

        for (var round = 0; round < DefaultLimit; round++)
        {
            model.CallsTool(Tool);
        }

        model.RespondsWith("done");

        await using var host = await StartAsync(model);

        var result = await RunAsync(host);

        model.Requests.Count.ShouldBe(DefaultLimit + 1);
        OffersFunctions(model.Requests[DefaultLimit - 1]).ShouldBeTrue("the 40th request still offers the tool");
        OffersFunctions(model.Requests[DefaultLimit]).ShouldBeFalse("the 41st request offers no function tool");
        result.Text.ShouldBe("done");
        result.Status.ShouldBe(RunStatus.Completed);
    }

    [Fact]
    public async Task A_model_that_asks_for_the_tool_again_at_the_limit_ends_the_run_completed_with_no_further_request()
    {
        var model = new FakeModelProvider(Provider);

        for (var round = 0; round < DefaultLimit + 5; round++)
        {
            model.CallsTool(Tool);
        }

        await using var host = await StartAsync(model);

        var result = await RunAsync(host);

        model.Requests.Count.ShouldBe(DefaultLimit + 1, "the loop sends no request after the one that offered no tool");
        OffersFunctions(model.Requests[DefaultLimit]).ShouldBeFalse();
        result.Status.ShouldBe(RunStatus.Completed);
        result.Text.ShouldBeEmpty("the run has no text to show: the last answer the model gave was a call");
    }

    private static Task<TraconTestHost> StartAsync(FakeModelProvider model)
        => TraconTestHost.StartAsync(builder =>
        {
            builder.AddModelProvider(model);
            builder.AddTool((Func<string>)(() => "pong"), name: Tool, description: "Answers pong.");
            builder.AddAgent(new AgentDefinition
            {
                Name = Agent,
                Instructions = "Answer the user.",
                Model = new ModelBinding { Provider = Provider, Model = "looping-1" },
                ToolNames = [Tool],
                Origin = AgentDefinitionOrigin.Code,
            });
        });

    private static async Task<(string Text, RunStatus Status)> RunAsync(TraconTestHost host)
    {
        var agent = (await host.Services.GetRequiredService<IAgentCatalog>().ResolveAsync(Agent, cancellationToken: TestContext.Current.CancellationToken)).ShouldNotBeNull();
        var session = await agent.CreateSessionAsync(TestContext.Current.CancellationToken);

        var response = await agent.RunAsync([new ChatMessage(ChatRole.User, "ping until done")], session, options: null, TestContext.Current.CancellationToken);

        var store = host.Services.GetRequiredService<IRunStore>();
        var run = (await store.QueryRunsAsync(new RunQuery { AgentName = Agent })).ShouldHaveSingleItem();

        return (response.Text, run.Status);
    }

    private static bool OffersFunctions(Tracon.Testing.FakeModelRequest request)
        => (request.Options?.Tools ?? []).OfType<AIFunctionDeclaration>().Any();
}
