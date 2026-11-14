using System.Runtime.CompilerServices;
using Google.GenAI.Types;
using Microsoft.Extensions.AI;

namespace Tracon.Google.UnitTests;

/// <summary>
/// A turn Gemini ended on a function call it could not use is a failure, and every other turn passes through untouched.
/// </summary>
/// <remarks>
/// The adapter maps <c>MALFORMED_FUNCTION_CALL</c> and <c>UNEXPECTED_TOOL_CALL</c> to a finish reason of tool calls whether or
/// not the answer holds a call, so a run completed with an empty answer. The raw response is what carries the reason, and these
/// cases build it the way the adapter hands it over.
/// </remarks>
public sealed class GoogleFunctionCallChatClientTests
{
    [Theory]
    [InlineData("MALFORMED_FUNCTION_CALL")]
    [InlineData("UNEXPECTED_TOOL_CALL")]
    public async Task A_response_that_ended_on_an_unusable_call_and_holds_none_is_a_failure(string reason)
    {
        using var client = new GoogleFunctionCallChatClient(new Scripted(Raw(reason), new TextContent(string.Empty)));

        var failure = await Should.ThrowAsync<GoogleFunctionCallException>(
            async () => await client.GetResponseAsync("go", cancellationToken: TestContext.Current.CancellationToken));

        failure.Message.ShouldContain(reason, Case.Sensitive);
    }

    [Fact]
    public async Task A_response_that_ended_the_same_way_but_holds_a_call_is_left_alone()
    {
        using var client = new GoogleFunctionCallChatClient(
            new Scripted(Raw("MALFORMED_FUNCTION_CALL"), new FunctionCallContent("call-1", "get_order_status")));

        var response = await client.GetResponseAsync("go", cancellationToken: TestContext.Current.CancellationToken);

        response.Messages.SelectMany(static message => message.Contents).OfType<FunctionCallContent>().ShouldHaveSingleItem();
    }

    [Theory]
    [InlineData("STOP")]
    [InlineData("MAX_TOKENS")]
    [InlineData("SAFETY")]
    public async Task A_response_that_ended_any_other_way_passes_through(string reason)
    {
        using var client = new GoogleFunctionCallChatClient(new Scripted(Raw(reason), new TextContent("done")));

        var response = await client.GetResponseAsync("go", cancellationToken: TestContext.Current.CancellationToken);

        response.Text.ShouldBe("done");
    }

    [Fact]
    public async Task A_response_with_no_raw_representation_passes_through()
    {
        using var client = new GoogleFunctionCallChatClient(new Scripted(raw: null, new TextContent("done")));

        (await client.GetResponseAsync("go", cancellationToken: TestContext.Current.CancellationToken)).Text.ShouldBe("done");
    }

    [Fact]
    public async Task A_stream_that_ends_on_an_unusable_call_with_none_before_it_is_a_failure_after_the_text_it_sent()
    {
        using var client = new GoogleFunctionCallChatClient(new Scripted(
            raw: null,
            new TextContent("one moment"),
            streamFinal: Raw("MALFORMED_FUNCTION_CALL")));

        var seen = new List<string>();

        await Should.ThrowAsync<GoogleFunctionCallException>(async () =>
        {
            await foreach (var update in client.GetStreamingResponseAsync("go", cancellationToken: TestContext.Current.CancellationToken))
            {
                seen.Add(update.Text);
            }
        });

        seen.ShouldBe(["one moment"], "what was sent before the failure was sent; the failure ends the stream");
    }

    [Fact]
    public async Task A_stream_that_sent_a_call_and_ends_on_that_reason_is_left_alone()
    {
        using var client = new GoogleFunctionCallChatClient(new Scripted(
            raw: null,
            new FunctionCallContent("call-1", "get_order_status"),
            streamFinal: Raw("MALFORMED_FUNCTION_CALL")));

        var updates = new List<ChatResponseUpdate>();

        await foreach (var update in client.GetStreamingResponseAsync("go", cancellationToken: TestContext.Current.CancellationToken))
        {
            updates.Add(update);
        }

        updates.SelectMany(static update => update.Contents).OfType<FunctionCallContent>().ShouldHaveSingleItem();
    }

    private static GenerateContentResponse Raw(string reason)
        => new()
        {
            Candidates =
            [
                new Candidate
                {
                    FinishReason = reason switch
                    {
                        "MALFORMED_FUNCTION_CALL" => FinishReason.MalformedFunctionCall,
                        "UNEXPECTED_TOOL_CALL" => FinishReason.UnexpectedToolCall,
                        "STOP" => FinishReason.Stop,
                        "MAX_TOKENS" => FinishReason.MaxTokens,
                        "SAFETY" => FinishReason.Safety,
                        _ => throw new ArgumentOutOfRangeException(nameof(reason), reason, "No such finish reason in this test."),
                    },
                },
            ],
        };

    /// <summary>An inner client that answers with the contents and raw response it is given.</summary>
    private sealed class Scripted(GenerateContentResponse? raw, AIContent content, GenerateContentResponse? streamFinal = null) : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
            => Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, [content])) { RawRepresentation = raw });

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.Yield();

            yield return new ChatResponseUpdate(ChatRole.Assistant, [content]);

            if (streamFinal is not null)
            {
                yield return new ChatResponseUpdate(ChatRole.Assistant, []) { RawRepresentation = streamFinal };
            }
        }

        public object? GetService(System.Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
            // The scripted client has nothing to release.
        }
    }
}
