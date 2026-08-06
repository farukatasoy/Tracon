using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;

namespace AgentPrism.Testing.Internal;

/// <summary>
/// Bir <see cref="FakeModelScript"/> tarafindan yonlendirilen, aga cikmayan
/// sohbet istemcisi.
/// </summary>
/// <remarks>
/// Metin yanitlari TEK bir guncelleme halinde doner (kelime kelime bolunmez).
/// Coğu tuketici yalniz nihai metni denetler; parca parca gelen bir akis
/// yalnizca kirilgan tam-metin eslesmesi (tek cerceve varsayan SSE testleri)
/// uretirdi. Akisli/akissiz cagri ayrimi yine de <see cref="FakeModelRequest.IsStreaming"/>
/// ile gozlemlenebilir.
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
        // Sahte istemcinin serbest birakilacak kaynagi yok.
    }

    private List<ChatResponseUpdate> BuildUpdates(IReadOnlyList<ChatMessage> messages)
    {
        var step = script.Dequeue();

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
        Justification = "Test yardimcisidir; uretimde calismaz. Anonim tip ozelliklerini kopyalamak icin yansima kullanilir.")]
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
