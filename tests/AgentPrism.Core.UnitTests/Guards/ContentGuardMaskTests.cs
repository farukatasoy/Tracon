using AgentPrism.Core.UnitTests.Fakes;
using Microsoft.Extensions.AI;

namespace AgentPrism.Core.UnitTests.Guards;

/// <summary>Verifies that masking decisions are reflected to the model and to the caller.</summary>
public sealed class ContentGuardMaskTests
{
    [Fact]
    public async Task Masked_text_reaches_the_model_masked()
    {
        var inner = new FakeChatClient();
        using var chatClient = Guarded(inner, StubContentGuard.Masking("4539578763621486", "[redacted]"));

        await chatClient.GetResponseAsync(
            [new ChatMessage(ChatRole.User, "my card number is 4539578763621486")],
            cancellationToken: TestContext.Current.CancellationToken);

        var sent = inner.LastRequest.Single().Text;
        sent.ShouldBe("my card number is [redacted]");
        sent.ShouldNotContain("4539578763621486", Case.Sensitive);
    }

    [Fact]
    public async Task Callers_message_list_is_not_mutated()
    {
        // 🚨 Masking changes the prompt sent to the model, not the conversation
        // history. Writing to history would make the mask permanent and the
        // user's own text would be irrecoverably lost.
        var original = new ChatMessage(ChatRole.User, "my card number is 4539578763621486");
        var messages = new List<ChatMessage> { original };

        using var chatClient = Guarded(new FakeChatClient(), StubContentGuard.Masking("4539578763621486", "[redacted]"));

        await chatClient.GetResponseAsync(messages, cancellationToken: TestContext.Current.CancellationToken);

        messages.Single().ShouldBeSameAs(original);
        original.Text.ShouldContain("4539578763621486", Case.Sensitive);
    }

    [Fact]
    public async Task Masked_messages_raw_representation_is_dropped()
    {
        // 🚨 Some provider adapters build the request from the raw representation
        // (measured in Phase 26: the Anthropic adapter does NOT overwrite the raw
        // object it was given). If it were carried over, unmasked text would reach
        // the network.
        var inner = new FakeChatClient();

        var message = new ChatMessage(ChatRole.User, "my card number is 4539578763621486")
        {
            RawRepresentation = "raw body: 4539578763621486",
        };

        using var chatClient = Guarded(inner, StubContentGuard.Masking("4539578763621486", "[redacted]"));

        await chatClient.GetResponseAsync([message], cancellationToken: TestContext.Current.CancellationToken);

        inner.LastRequest.Single().RawRepresentation.ShouldBeNull();

        // The caller's object was not mutated.
        message.RawRepresentation.ShouldNotBeNull();
    }

    [Fact]
    public async Task Message_object_is_not_rebuilt_when_there_is_no_match()
    {
        // Allocation discipline: no new ChatMessage is produced on the no-match path.
        var inner = new FakeChatClient();
        var original = new ChatMessage(ChatRole.User, "harmless prompt");

        using var chatClient = Guarded(inner, StubContentGuard.Masking("never-matches", "***"));

        await chatClient.GetResponseAsync([original], cancellationToken: TestContext.Current.CancellationToken);

        inner.LastRequest.Single().ShouldBeSameAs(original);
    }

    [Fact]
    public async Task Output_masking_is_reflected_in_the_response()
    {
        using var chatClient = Guarded(
            new FakeChatClient(_ => new ChatResponse(new ChatMessage(ChatRole.Assistant, "iban TR330006100519786457841326"))
            {
                RawRepresentation = "raw response: TR330006100519786457841326",
            }),
            StubContentGuard.Masking("TR330006100519786457841326", "[redacted]"));

        var response = await chatClient.GetResponseAsync(
            [new ChatMessage(ChatRole.User, "what is the iban")],
            cancellationToken: TestContext.Current.CancellationToken);

        response.Text.ShouldBe("iban [redacted]");
        response.RawRepresentation.ShouldBeNull();
    }

    [Fact]
    public async Task Tool_result_is_masked_and_its_type_is_preserved()
    {
        // A tool result is a FunctionResultContent; it must not be converted to
        // TextContent while masking, or the MAF call id would be lost.
        var inner = new FakeChatClient();

        using var chatClient = Guarded(inner, StubContentGuard.Masking("sk-live-key", "[redacted]"));

        await chatClient.GetResponseAsync(
            [
                new ChatMessage(ChatRole.User, "summarize the result"),
                new ChatMessage(ChatRole.Tool, [new FunctionResultContent("call-1", "key sk-live-key")]),
            ],
            cancellationToken: TestContext.Current.CancellationToken);

        var result = inner.LastRequest[1].Contents.Single().ShouldBeOfType<FunctionResultContent>();
        result.CallId.ShouldBe("call-1");
        result.Result.ShouldBe("key [redacted]");
    }

    [Fact]
    public async Task Tool_result_that_cannot_be_normalized_is_masked_even_when_no_guard_pattern_matches()
    {
        // 🚨 Fail-closed (Open Question 1, option A, Phase 102): a tool result
        // ToolResultText cannot normalize into text must NEVER be routed through
        // a guard's pattern match — there is no real text to match, and a guard
        // whose pattern does not happen to match the fixed placeholder would
        // otherwise let the real, unexamined content through completely unmasked.
        var inner = new FakeChatClient();

        using var chatClient = Guarded(inner, StubContentGuard.Masking("never-matches-anything", "***"));

        var secret = new Dictionary<string, string>(StringComparer.Ordinal) { ["apiKey"] = "sk-live-should-never-leak" };

        await chatClient.GetResponseAsync(
            [
                new ChatMessage(ChatRole.User, "summarize the result"),
                new ChatMessage(ChatRole.Tool, [new FunctionResultContent("call-1", secret)]),
            ],
            cancellationToken: TestContext.Current.CancellationToken);

        var result = inner.LastRequest[1].Contents.Single().ShouldBeOfType<FunctionResultContent>();
        result.CallId.ShouldBe("call-1");
        result.Result.ShouldBe(ContentGuardMessageMasker.UninspectableToolResultText);
        result.Result.ShouldNotBe(secret);
    }

    private static IChatClient Guarded(FakeChatClient inner, params IContentGuard[] guards)
        => TestData
            .Providers(TestData.ContentGuards(guards: guards), new FakeModelProvider(inner))
            .CreateChatClient(TestData.Binding());
}
