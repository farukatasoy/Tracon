using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;

namespace AgentPrism.Ui.E2ETests.Infrastructure;

/// <summary>
/// Aga cikmayan, betiklenmis bir model saglayicisi.
/// </summary>
/// <remarks>
/// <para>
/// Arayuz testleri gercek bir tool dongusu gorebilmelidir: tool karti ancak
/// <c>FunctionCallContent</c> ve <c>FunctionResultContent</c> uretildiginde
/// dolar. Bu yuzden istemci, tool tanimliysa ilk turda bir tool cagrisi uretir,
/// sonucu geldiginde metin yanitini akitir.
/// </para>
/// <para>
/// Boru hattina <c>UseFunctionInvocation</c> eklenir; tool'u gercekten calistiran
/// odur. Saglayici bunu kendisi kurar - AgentPrism derleyicisi boru hattini
/// saglayiciya birakir.
/// </para>
/// </remarks>
internal sealed class ScriptedModelProvider : IModelProvider, IDisposable
{
    /// <summary>Saglayicinin katalogda gorunen adi.</summary>
    public const string ProviderName = "scripted";

    /// <summary>Tek modelinin adi.</summary>
    public const string ModelName = "scripted-1";

    private readonly IChatClient _client;

    public ScriptedModelProvider()
        => _client = new ScriptedChatClient().AsBuilder().UseFunctionInvocation().Build();

    /// <inheritdoc />
    public string Name => ProviderName;

    /// <inheritdoc />
    public IReadOnlyList<ModelDescriptor> Models { get; } =
    [
        new ModelDescriptor
        {
            Name = ModelName,
            DisplayName = "Scripted test model",
            ContextWindowTokens = 8_192,
            MaxOutputTokens = 1_024,
        },
    ];

    /// <inheritdoc />
    public IChatClient CreateChatClient(ModelBinding binding) => _client;

    /// <inheritdoc />
    public void Dispose() => _client.Dispose();

    private sealed class ScriptedChatClient : IChatClient
    {
        public async Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            var updates = new List<ChatResponseUpdate>();

            await foreach (var update in GetStreamingResponseAsync(messages, options, cancellationToken))
            {
                updates.Add(update);
            }

            return updates.ToChatResponse();
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var history = messages.ToList();
            var toolAlreadyRan = history.Any(static message =>
                message.Contents.Any(static content => content is FunctionResultContent));

            var tool = options?.Tools?.OfType<AIFunction>().FirstOrDefault();

            if (tool is not null && !toolAlreadyRan)
            {
                yield return new ChatResponseUpdate(
                    ChatRole.Assistant,
                    [new FunctionCallContent("call-1", tool.Name, new Dictionary<string, object?>(StringComparer.Ordinal) { ["orderId"] = "ORD-7" })]);

                yield break;
            }

            var prompt = history.LastOrDefault(static message => message.Role == ChatRole.User)?.Text ?? string.Empty;

            // Akisin gercekten parca parca geldigini gorebilmek icin kelime kelime yazilir.
            foreach (var word in $"Echo: {prompt}".Split(' '))
            {
                await Task.Yield();

                yield return new ChatResponseUpdate(ChatRole.Assistant, word + " ");
            }

            yield return new ChatResponseUpdate(
                ChatRole.Assistant,
                [new UsageContent(new UsageDetails { InputTokenCount = 12, OutputTokenCount = 5, TotalTokenCount = 17 })]);
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
            // Sahte istemcinin serbest birakilacak kaynagi yok.
        }
    }
}
