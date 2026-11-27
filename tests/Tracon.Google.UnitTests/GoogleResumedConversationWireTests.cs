using System.Text.Json;
using Microsoft.Extensions.AI;
using Tracon.Google.UnitTests.Infrastructure;

namespace Tracon.Google.UnitTests;

/// <summary>
/// What the Gemini adapter puts on the wire for the history a voice conversation leaves behind when it ends on a goodbye
/// said before the call: the model's words and its call, the tool's result, and then the caller's next words in a session
/// that was resumed.
/// </summary>
/// <remarks>
/// A local server records the request body. This pins what the adapter SENDS; whether the service accepts two consecutive
/// user contents is not something an offline test can say.
/// </remarks>
public sealed class GoogleResumedConversationWireTests
{
    [Fact]
    public async Task A_history_that_ends_on_a_tool_result_and_then_the_callers_words_is_sent_as_these_contents()
    {
        await using var server = new GoogleLoopbackServer();

        var options = TestData.Options(o => o.Endpoint = server.BaseAddress);
        var provider = new GoogleModelProvider(
            GoogleProviderNames.Google,
            new GoogleChatClientFactory(options),
            [new ModelDescriptor { Name = TestData.Model }],
            healthCheckOptions: options);

        using var client = provider.CreateChatClient(TestData.Binding());

        var history = new List<ChatMessage>
        {
            new(ChatRole.User, "I think we are done."),
            new(ChatRole.Assistant, [new TextContent("Goodbye, take care."), new FunctionCallContent("end-1", "end_conversation", new Dictionary<string, object?>(StringComparer.Ordinal))]),
            new(ChatRole.Tool, [new FunctionResultContent("end-1", "The conversation has ended.")]),
            new(ChatRole.User, "Hello again."),
        };

        await client.GetResponseAsync(history, cancellationToken: TestContext.Current.CancellationToken);

        using var body = JsonDocument.Parse(server.Requests.Single().Body);

        var contents = body.RootElement.GetProperty("contents").EnumerateArray()
            .Select(static content => (
                Role: content.GetProperty("role").GetString(),
                Parts: content.GetProperty("parts").EnumerateArray()
                    .Select(static part => part.EnumerateObject().Select(static property => property.Name).First())
                    .ToArray()))
            .ToList();

        contents.Select(static content => content.Role).ShouldBe(["user", "model", "user", "user"]);
        contents[1].Parts.ShouldBe(["text", "functionCall"]);
        contents[2].Parts.ShouldBe(["functionResponse"]);
        contents[3].Parts.ShouldBe(["text"]);
    }
}
