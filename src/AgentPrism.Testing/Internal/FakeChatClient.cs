using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;

namespace AgentPrism.Testing.Internal;

/// <summary>
/// A non-networked chat client driven by a <see cref="FakeModelScript"/>.
/// </summary>
/// <remarks>
/// Text responses come back as a SINGLE update (not split word by word).
/// Most consumers only check the final text; a piecemeal stream would only
/// produce a brittle full-text match (SSE tests that assume one frame). The
/// streaming/non-streaming call distinction is still observable through
/// <see cref="FakeModelRequest.IsStreaming"/>.
/// </remarks>
internal sealed class FakeChatClient(FakeModelScript script, Action<FakeModelRequest> onRequest) : IChatClient
{
    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var messageList = ToList(messages);
        onRequest(new FakeModelRequest { Messages = messageList, Options = options, IsStreaming = false });

        return Task.FromResult(BuildUpdates(messageList).ToChatResponse());
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var messageList = ToList(messages);
        onRequest(new FakeModelRequest { Messages = messageList, Options = options, IsStreaming = true });

        foreach (var update in BuildUpdates(messageList))
        {
            await Task.Yield();
            yield return update;
        }
    }

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose()
    {
        // The fake client has no resource to release.
    }

    private List<ChatResponseUpdate> BuildUpdates(IReadOnlyList<ChatMessage> messages)
    {
        var step = script.Dequeue();

        if (step is { ToolCalls.Count: > 0 } multiCall)
        {
            return
            [
                new ChatResponseUpdate(
                    ChatRole.Assistant,
                    [.. multiCall.ToolCalls!.Select(static call =>
                        (AIContent)new FunctionCallContent(Guid.NewGuid().ToString("N"), call.ToolName, ToArguments(call.Arguments)))]),
            ];
        }

        if (step is { ToolName: { Length: > 0 } toolName })
        {
            return
            [
                new ChatResponseUpdate(
                    ChatRole.Assistant,
                    [new FunctionCallContent(Guid.NewGuid().ToString("N"), toolName, ToArguments(step.ToolArguments))]),
            ];
        }

        var (text, usage) = step is null
            ? ResolveFallback(messages)
            : (step.Text ?? string.Empty, step.Usage);

        var updates = new List<ChatResponseUpdate> { new(ChatRole.Assistant, text) };

        if (usage is { } reported)
        {
            updates.Add(new ChatResponseUpdate(
                ChatRole.Assistant,
                [
                    new UsageContent(new UsageDetails
                    {
                        InputTokenCount = reported.InputTokens,
                        OutputTokenCount = reported.OutputTokens,
                        TotalTokenCount = reported.InputTokens + reported.OutputTokens,
                    }),
                ]));
        }

        return updates;
    }

    private (string Text, FakeUsage? Usage) ResolveFallback(IReadOnlyList<ChatMessage> messages)
    {
        switch (script.Fallback)
        {
            case FakeFallbackKind.EchoUserMessage:
                var lastUser = messages.LastOrDefault(static message => message.Role == ChatRole.User);
                return ($"Echo: {lastUser?.Text}", script.FallbackUsage);

            case FakeFallbackKind.EchoLastToolResult:
                var lastResult = messages
                    .SelectMany(static message => message.Contents)
                    .OfType<FunctionResultContent>()
                    .LastOrDefault();
                return ($"{script.FallbackPrefix}{lastResult?.Result}", script.FallbackUsage);

            default:
                return (script.FallbackText, script.FallbackUsage);
        }
    }

    private static List<ChatMessage> ToList(IEnumerable<ChatMessage> messages) => [.. messages];

    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2075",
        Justification = "Test helper; does not run in production. Reflection copies anonymous-type properties.")]
    private static IDictionary<string, object?>? ToArguments(object? arguments)
    {
        if (arguments is null)
        {
            return null;
        }

        if (arguments is IDictionary<string, object?> dictionary)
        {
            return dictionary;
        }

        var result = new Dictionary<string, object?>(StringComparer.Ordinal);

        foreach (var property in arguments.GetType().GetProperties())
        {
            result[property.Name] = property.GetValue(arguments);
        }

        return result;
    }
}
