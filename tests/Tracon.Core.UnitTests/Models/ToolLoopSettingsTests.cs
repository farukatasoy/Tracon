using Microsoft.Extensions.AI;
using Tracon.Core.UnitTests.Fakes;

namespace Tracon.Core.UnitTests.Models;

/// <summary>
/// <see cref="ModelBinding.ToolLoop"/>: each member reaches the tool-call loop the registry builds
/// around the provider client, and a link of the fallback chain gets the same settings as one object.
/// </summary>
/// <remarks>
/// <para>
/// The loop is inside the pipeline, so a member shows itself only in what the loop does. Each test
/// scripts a raw client that asks for a tool call and measures the one behavior its member changes.
/// A member the registry forgets to hand to the loop leaves the loop's own default, and the test
/// fails on the difference.
/// </para>
/// <para>
/// <c>AllowConcurrentToolCalls</c> is measured with a barrier in <c>ConcurrentToolInvocationTests</c>,
/// over HTTP.
/// </para>
/// </remarks>
public sealed class ToolLoopSettingsTests
{
    private const string FailingTool = "boom";
    private const string UnknownTool = "ghost";

    [Fact]
    public async Task By_default_a_failing_tool_does_not_end_the_turn_and_the_model_answers()
    {
        var raw = Raw(call => call == 1 ? Call(FailingTool) : Text("answer"));

        var response = await Registry(raw).CreateChatClient(Binding(new ToolLoopSettings()))
            .GetResponseAsync("go", Options(), TestContext.Current.CancellationToken);

        response.Text.ShouldBe("answer");
        raw.CallCount.ShouldBe(2);
    }

    [Fact]
    public async Task Zero_tolerated_errors_rethrows_the_first_tool_failure()
    {
        var raw = Raw(call => call == 1 ? Call(FailingTool) : Text("answer"));
        var client = Registry(raw).CreateChatClient(Binding(new ToolLoopSettings { MaximumConsecutiveErrors = 0 }));

        var exception = await Should.ThrowAsync<InvalidOperationException>(
            async () => await client.GetResponseAsync("go", Options(), TestContext.Current.CancellationToken));

        exception.Message.ShouldContain("the tool failed");
        raw.CallCount.ShouldBe(1, "the loop must not ask the model again after the rethrown failure");
    }

    [Fact]
    public async Task A_higher_tolerance_lets_a_run_of_failures_pass_that_the_default_would_stop()
    {
        // Four failing turns in a row, then an answer. The default (3) stops on the fourth failure.
        static ChatResponse Script(int call) => call <= 4 ? Call(FailingTool) : Text("answer");

        var stopped = Raw(Script);
        var tolerated = Raw(Script);

        await Should.ThrowAsync<InvalidOperationException>(
            async () => await Registry(stopped).CreateChatClient(Binding(new ToolLoopSettings()))
                .GetResponseAsync("go", Options(), TestContext.Current.CancellationToken));

        var response = await Registry(tolerated).CreateChatClient(Binding(new ToolLoopSettings { MaximumConsecutiveErrors = 5 }))
            .GetResponseAsync("go", Options(), TestContext.Current.CancellationToken);

        response.Text.ShouldBe("answer");
        tolerated.CallCount.ShouldBe(5);
    }

    [Fact]
    public async Task By_default_a_call_to_an_unknown_tool_gets_an_error_result_and_the_model_answers()
    {
        var raw = Raw(call => call == 1 ? Call(UnknownTool) : Text("answer"));

        var response = await Registry(raw).CreateChatClient(Binding(new ToolLoopSettings()))
            .GetResponseAsync("go", Options(), TestContext.Current.CancellationToken);

        response.Text.ShouldBe("answer");
        raw.CallCount.ShouldBe(2);
    }

    [Fact]
    public async Task An_unknown_call_ends_the_loop_and_comes_back_when_the_binding_asks_for_it()
    {
        var raw = Raw(call => call == 1 ? Call(UnknownTool) : Text("answer"));

        var response = await Registry(raw).CreateChatClient(Binding(new ToolLoopSettings { TerminateOnUnknownCalls = true }))
            .GetResponseAsync("go", Options(), TestContext.Current.CancellationToken);

        raw.CallCount.ShouldBe(1);
        response.Messages.SelectMany(static message => message.Contents).OfType<FunctionCallContent>()
            .ShouldHaveSingleItem().Name.ShouldBe(UnknownTool);
    }

    [Fact]
    public async Task The_iteration_limit_caps_how_many_times_the_loop_asks_the_model()
    {
        var one = Raw(_ => Call(FailingTool));
        var three = Raw(_ => Call(FailingTool));

        // IterationLimit is set by the compiler from a harness agent's MaximumIterationsPerRequest.
        await Registry(one).CreateChatClient(Binding(new ToolLoopSettings { IterationLimit = 1 }))
            .GetResponseAsync("go", Options(), TestContext.Current.CancellationToken);
        await Registry(three).CreateChatClient(Binding(new ToolLoopSettings { IterationLimit = 3 }))
            .GetResponseAsync("go", Options(), TestContext.Current.CancellationToken);

        three.CallCount.ShouldBe(one.CallCount + 2);
    }

    [Fact]
    public void A_negative_error_tolerance_is_refused_with_the_member_named()
    {
        var registry = Registry(Raw(_ => Text("answer")));

        var exception = Should.Throw<TraconException>(
            () => registry.CreateChatClient(Binding(new ToolLoopSettings { MaximumConsecutiveErrors = -1 })));

        exception.Message.ShouldContain("ToolLoop.MaximumConsecutiveErrors", Case.Sensitive);
        exception.Message.ShouldContain("-1", Case.Sensitive);
    }

    [Fact]
    public async Task Every_fallback_link_runs_its_loop_with_the_primarys_whole_settings()
    {
        var loop = new ToolLoopSettings
        {
            AllowConcurrentToolCalls = true,
            MaximumConsecutiveErrors = 1,
            TerminateOnUnknownCalls = true,
            IterationLimit = 2,
        };
        var primary = new FakeModelProvider(
            new FakeChatClient(_ => throw new TraconProviderUnavailableException("circuit open") { ProviderName = "primary" }),
            name: "primary");
        var fallback = new FakeModelProvider(Raw(_ => Text("fallback answer")), name: "fallback");

        using var client = TestData.Providers(primary, fallback).CreateChatClient(new ModelBinding
        {
            Provider = "primary",
            Model = "primary-model",
            ToolLoop = loop,
            Fallbacks = [new ModelFallback { Provider = "fallback", Model = "fallback-model" }],
        });

        await client.GetResponseAsync("go", cancellationToken: TestContext.Current.CancellationToken);

        // The whole object, not member by member: a member added later cannot be forgotten.
        fallback.LastBinding.ShouldNotBeNull().ToolLoop.ShouldBeSameAs(loop);
    }

    [Fact]
    public void A_null_loop_becomes_the_default_settings()
    {
        // A source-generated reader assigns null to a member that is absent from the JSON of a type with required members.
        var binding = TestData.Binding() with { ToolLoop = null! };

        binding.ToolLoop.ShouldNotBeNull().ShouldBe(new ToolLoopSettings());
    }

    private static FakeChatClient Raw(Func<int, ChatResponse> script)
    {
        var calls = 0;

        return new FakeChatClient(_ => script(Interlocked.Increment(ref calls)));
    }

    private static ChatResponse Call(string tool)
        => new(new ChatMessage(
            ChatRole.Assistant,
            [new FunctionCallContent($"call-{tool}", tool, new Dictionary<string, object?>(StringComparer.Ordinal))]));

    private static ChatResponse Text(string text) => new(new ChatMessage(ChatRole.Assistant, text));

    private static ChatOptions Options()
        => new()
        {
            Tools =
            [
                AIFunctionFactory.Create((Func<string>)(() => throw new InvalidOperationException("the tool failed")), FailingTool),
            ],
        };

    private static ModelBinding Binding(ToolLoopSettings loop) => TestData.Binding() with { ToolLoop = loop };

    private static ModelProviderRegistry Registry(IChatClient raw) => TestData.Providers(new FakeModelProvider(raw));
}
