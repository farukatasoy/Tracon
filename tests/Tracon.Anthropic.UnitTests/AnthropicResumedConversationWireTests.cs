using Anthropic;
using Microsoft.Extensions.AI;
using Tracon.Anthropic.UnitTests.Infrastructure;

namespace Tracon.Anthropic.UnitTests;

/// <summary>
/// What the Anthropic adapter puts on the wire for the history a voice conversation leaves behind when it ends on a goodbye
/// said before the call: the model's words and its call, the tool's result, and then the caller's next words in a session
/// that was resumed.
/// </summary>
/// <remarks>
/// The body is read from what the SDK actually sends. This pins what the adapter SENDS; whether the service accepts that
/// sequence, with thinking on or off, is not something an offline test can say.
/// </remarks>
public sealed class AnthropicResumedConversationWireTests
{
    [Fact]
    public async Task A_history_that_ends_on_a_tool_result_and_then_the_callers_words_is_sent_as_these_messages()
    {
        var handler = new CapturingAnthropicHandler();

        var sdkClient = new AnthropicClient(new global::Anthropic.Core.ClientOptions
        {
            ApiKey = TestData.ApiKey,
            HttpClient = new HttpClient(handler),
            MaxRetries = 0,
        });

        using var client = AnthropicChatClientFactory.FromClient(sdkClient, TestData.Model).CreateChatClient(TestData.Binding());

        var history = new List<ChatMessage>
        {
            new(ChatRole.User, "I think we are done."),
            new(ChatRole.Assistant, [new TextContent("Goodbye, take care."), new FunctionCallContent("end-1", "end_conversation", new Dictionary<string, object?>(StringComparer.Ordinal))]),
            new(ChatRole.Tool, [new FunctionResultContent("end-1", "The conversation has ended.")]),
            new(ChatRole.User, "Hello again."),
        };

        await client.GetResponseAsync(history, cancellationToken: TestContext.Current.CancellationToken);

        var messages = handler.Last["messages"]!.AsArray()
            .Select(static message => (
                Role: message!["role"]!.GetValue<string>(),
                Blocks: message["content"]!.AsArray().Select(static block => block!["type"]!.GetValue<string>()).ToArray()))
            .ToList();

        // Measured: the adapter does not merge the tool result with the caller's words: two user messages follow each other.
        messages.Select(static message => message.Role).ShouldBe(["user", "assistant", "user", "user"]);
        messages[1].Blocks.ShouldBe(["text", "tool_use"]);
        messages[2].Blocks.ShouldBe(["tool_result"]);
        messages[3].Blocks.ShouldBe(["text"]);
    }
}
