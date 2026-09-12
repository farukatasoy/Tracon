using Microsoft.Extensions.AI;
using Tracon.Core.UnitTests.Fakes;

namespace Tracon.Core.UnitTests.Guards;

/// <summary>
/// Verifies that <see cref="ContentGuardContext.Source"/> and
/// <see cref="ContentGuardContext.ToolName"/> classify text correctly — the
/// distinction Phase 140 adds so a guard can tell a user message from a tool
/// result from the model's own output.
/// </summary>
public sealed class ContentGuardSourceTests
{
    [Fact]
    public async Task User_message_is_classified_as_UserMessage()
    {
        var guard = StubContentGuard.Blocking("never-matches");
        using var chatClient = Guarded(new FakeChatClient(), guard);

        await chatClient.GetResponseAsync(
            [new ChatMessage(ChatRole.User, "hello")],
            cancellationToken: TestContext.Current.CancellationToken);

        guard.SeenSources.ShouldContain(ContentGuardSource.UserMessage);
        guard.SeenToolNames[guard.SeenSources.IndexOf(ContentGuardSource.UserMessage)].ShouldBeNull();
    }

    [Fact]
    public async Task Model_response_text_is_classified_as_ModelOutput()
    {
        var guard = StubContentGuard.Blocking("never-matches");
        using var chatClient = Guarded(
            new FakeChatClient(_ => new ChatResponse(new ChatMessage(ChatRole.Assistant, "model response"))),
            guard);

        await chatClient.GetResponseAsync(
            [new ChatMessage(ChatRole.User, "hello")],
            cancellationToken: TestContext.Current.CancellationToken);

        guard.SeenSources.ShouldBe([ContentGuardSource.UserMessage, ContentGuardSource.ModelOutput]);
    }

    [Fact]
    public async Task Streaming_model_output_is_classified_as_ModelOutput()
    {
        // The buffered streaming path (ContentGuardingChatClient.InspectBufferAsync)
        // does not go through RewriteContentAsync's role-based classification —
        // it must still report ModelOutput, not Unknown.
        var guard = StubContentGuard.Blocking("never-matches");
        using var chatClient = Guarded(
            new FakeChatClient(streamingUpdates:
            [
                new ChatResponseUpdate(ChatRole.Assistant, "streamed "),
                new ChatResponseUpdate(ChatRole.Assistant, "output"),
            ]),
            guard);

        await foreach (var _ in chatClient.GetStreamingResponseAsync(
            [new ChatMessage(ChatRole.User, "hello")],
            cancellationToken: TestContext.Current.CancellationToken))
        {
            // Draining the stream is enough; the buffered path inspects once at the end.
        }

        guard.SeenSources.ShouldContain(ContentGuardSource.ModelOutput);
    }

    [Fact]
    public async Task Tool_result_is_classified_as_ToolResult_with_its_resolved_tool_name()
    {
        // 🚨 The real second-turn loop, not a hand-built message list: the tool
        // name lives on the FIRST call's FunctionCallContent, and only the
        // real FunctionInvokingChatClient loop produces that shape naturally.
        var inner = new FakeChatClient(messages => messages
            .SelectMany(static message => message.Contents)
            .OfType<FunctionResultContent>()
            .Any()
                ? new ChatResponse(new ChatMessage(ChatRole.Assistant, "done"))
                : new ChatResponse(new ChatMessage(
                    ChatRole.Assistant,
                    [new FunctionCallContent("call-1", "get_order_status", null)])));

        var guard = StubContentGuard.Blocking("never-matches");
        using var chatClient = Guarded(inner, guard);

        await chatClient.GetResponseAsync(
            [new ChatMessage(ChatRole.User, "what is the status of my order")],
            new ChatOptions
            {
                Tools = [AIFunctionFactory.Create(static () => "shipped", "get_order_status")],
            },
            TestContext.Current.CancellationToken);

        var toolResultIndex = guard.SeenSources.IndexOf(ContentGuardSource.ToolResult);
        toolResultIndex.ShouldBeGreaterThanOrEqualTo(0);
        guard.SeenToolNames[toolResultIndex].ShouldBe("get_order_status");
    }

    [Fact]
    public async Task Tool_result_keeps_ToolResult_source_even_when_the_call_id_cannot_be_resolved()
    {
        // 🚨 The phase's central safety claim: a multi-turn conversation can
        // drop the earlier FunctionCallContent from context. Source must stay
        // ToolResult regardless — a security decision keys off Source, never
        // off whether the name happened to resolve.
        var guard = StubContentGuard.Blocking("never-matches");
        using var chatClient = Guarded(new FakeChatClient(), guard);

        await chatClient.GetResponseAsync(
            [
                new ChatMessage(ChatRole.User, "summarize the result"),
                new ChatMessage(ChatRole.Tool, [new FunctionResultContent("call-1", "some tool output")]),
            ],
            cancellationToken: TestContext.Current.CancellationToken);

        var toolResultIndex = guard.SeenSources.IndexOf(ContentGuardSource.ToolResult);
        toolResultIndex.ShouldBeGreaterThanOrEqualTo(0);
        guard.SeenToolNames[toolResultIndex].ShouldBeNull();
    }

    [Fact]
    public async Task No_tool_result_in_the_conversation_never_reports_ToolResult()
    {
        var guard = StubContentGuard.Blocking("never-matches");
        using var chatClient = Guarded(new FakeChatClient(), guard);

        await chatClient.GetResponseAsync(
            [
                new ChatMessage(ChatRole.User, "first turn"),
                new ChatMessage(ChatRole.Assistant, "first reply"),
                new ChatMessage(ChatRole.User, "second turn"),
            ],
            cancellationToken: TestContext.Current.CancellationToken);

        guard.SeenSources.ShouldNotContain(ContentGuardSource.ToolResult);
    }

    [Fact]
    public async Task Empty_message_list_does_not_throw_and_reports_no_UserMessage_source()
    {
        // No input message exists to classify; the only guard call this can
        // produce is on the OUTPUT direction (the model's own response).
        var guard = StubContentGuard.Blocking("never-matches");
        using var chatClient = Guarded(new FakeChatClient(), guard);

        await chatClient.GetResponseAsync([], cancellationToken: TestContext.Current.CancellationToken);

        guard.SeenSources.ShouldNotContain(ContentGuardSource.UserMessage);
        guard.SeenSources.ShouldNotContain(ContentGuardSource.ToolResult);
    }

    private static IChatClient Guarded(FakeChatClient inner, params IContentGuard[] guards)
        => TestData
            .Providers(TestData.ContentGuards(guards: guards), new FakeModelProvider(inner))
            .CreateChatClient(TestData.Binding());
}
