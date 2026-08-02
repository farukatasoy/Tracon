using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;

namespace AgentPrism.AspNetCore.FunctionalTests.Infrastructure;

/// <summary>
/// Alt agent cagrisini gercekten tetikleyen, aga cikmayan model saglayicisi.
/// </summary>
/// <remarks>
/// <para>
/// Microsoft Agent Framework alt agent cagrisini iki adima boler: gorev
/// <c>background_agents_start_task</c> ile baslatilir ve
/// <c>background_agents_wait_for_first_completion</c> ile beklenir. Aradaki
/// calistirma <strong>bloke etmez</strong>; iki adimi tek turda taklit eden bir
/// sahte istemci gercek yolu atlar.
/// </para>
/// <para>
/// Boru hattina <c>UseFunctionInvocation</c> eklenir; tool'u gercekten calistiran
/// odur.
/// </para>
/// </remarks>
internal sealed class RoutingModelProvider : IModelProvider, IDisposable
{
    /// <summary>Saglayicinin katalogda gorunen adi.</summary>
    public const string ProviderName = "routing";

    /// <summary>Tek modelinin adi.</summary>
    public const string ModelName = "routing-1";

    private readonly IChatClient _client;

    public RoutingModelProvider(string childAgentName = "arastirmaci")
        => _client = new RoutingChatClient(childAgentName).AsBuilder().UseFunctionInvocation().Build();

    /// <inheritdoc />
    public string Name => ProviderName;

    /// <inheritdoc />
    public IReadOnlyList<ModelDescriptor> Models { get; } =
    [
        new ModelDescriptor { Name = ModelName, DisplayName = "Routing test model" },
    ];

    /// <inheritdoc />
    public IChatClient CreateChatClient(ModelBinding binding) => _client;

    /// <inheritdoc />
    public void Dispose() => _client.Dispose();

    private sealed class RoutingChatClient(string childAgentName) : IChatClient
    {
        private const string StartTask = "background_agents_start_task";
        private const string WaitForCompletion = "background_agents_wait_for_first_completion";
        private const string GetResults = "background_agents_get_task_results";

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
            var finished = CompletedToolNames(history);
            var tools = options?.Tools?.OfType<AIFunction>().ToList() ?? [];
            var canDelegate = tools.Exists(static tool => string.Equals(tool.Name, StartTask, StringComparison.Ordinal));

            if (canDelegate && !finished.Contains(StartTask))
            {
                yield return Call("call-start", StartTask, new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["agentName"] = childAgentName,
                    ["input"] = "alt gorev",
                    ["description"] = "alt gorev",
                });

                yield break;
            }

            if (canDelegate && !finished.Contains(WaitForCompletion))
            {
                yield return Call("call-wait", WaitForCompletion, new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["taskIds"] = new[] { 1 },
                });

                yield break;
            }

            if (canDelegate && !finished.Contains(GetResults))
            {
                yield return Call("call-results", GetResults, new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["taskId"] = 1,
                });

                yield break;
            }

            var lastResult = history
                .SelectMany(static message => message.Contents)
                .OfType<FunctionResultContent>()
                .LastOrDefault();

            await Task.Yield();

            yield return new ChatResponseUpdate(
                ChatRole.Assistant,
                canDelegate ? $"Devredildi: {lastResult?.Result}" : "Alt gorev tamam");

            yield return new ChatResponseUpdate(
                ChatRole.Assistant,
                [new UsageContent(new UsageDetails { InputTokenCount = 4, OutputTokenCount = 6, TotalTokenCount = 10 })]);
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
            // Sahte istemcinin serbest birakilacak kaynagi yok.
        }

        private static ChatResponseUpdate Call(string callId, string name, Dictionary<string, object?> arguments)
            => new(ChatRole.Assistant, [new FunctionCallContent(callId, name, arguments)]);

        /// <summary>Gecmiste sonucu donmus tool cagrilarinin adlarini cikarir.</summary>
        /// <remarks>
        /// <see cref="FunctionResultContent"/> tool adini tasimaz, yalnizca cagri
        /// kimligini; ad ayni gecmisteki <see cref="FunctionCallContent"/> ile
        /// eslestirilir. "Herhangi bir sonuc geldi mi" diye bakmak cok adimli bir
        /// senaryonun ikinci adimini hic calistirmazdi.
        /// </remarks>
        private static HashSet<string> CompletedToolNames(List<ChatMessage> history)
        {
            var callNames = new Dictionary<string, string>(StringComparer.Ordinal);
            var completed = new HashSet<string>(StringComparer.Ordinal);

            foreach (var content in history.SelectMany(static message => message.Contents))
            {
                switch (content)
                {
                    case FunctionCallContent call:
                        callNames[call.CallId] = call.Name;
                        break;

                    case FunctionResultContent result when callNames.TryGetValue(result.CallId, out var name):
                        completed.Add(name);
                        break;

                    default:
                        break;
                }
            }

            return completed;
        }
    }
}
