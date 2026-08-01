using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;

namespace AgentPrism.Core.UnitTests.Fakes;

/// <summary>
/// Ag cagrisi yapmayan sahte sohbet istemcisi. Testler dondurulecek yaniti
/// ve akis parcalarini onceden belirler.
/// </summary>
internal sealed class FakeChatClient : IChatClient
{
    private readonly Func<IEnumerable<ChatMessage>, ChatResponse> _responder;
    private readonly IReadOnlyList<ChatResponseUpdate>? _streamingUpdates;

    public FakeChatClient(
        Func<IEnumerable<ChatMessage>, ChatResponse>? responder = null,
        IReadOnlyList<ChatResponseUpdate>? streamingUpdates = null)
    {
        _responder = responder ?? (_ => new ChatResponse(new ChatMessage(ChatRole.Assistant, "tamam")));
        _streamingUpdates = streamingUpdates;
    }

    /// <summary>Istemciye ulasan son istek listesi. Testler bunu dogrular.</summary>
    public List<ChatMessage> LastRequest { get; } = [];

    /// <summary>Istemciye ulasan son secenekler.</summary>
    public ChatOptions? LastOptions { get; private set; }

    /// <summary>Kac kez cagrildi.</summary>
    public int CallCount { get; private set; }

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        CallCount++;
        LastOptions = options;
        LastRequest.Clear();
        LastRequest.AddRange(messages);

        return Task.FromResult(_responder(messages));
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        CallCount++;
        LastOptions = options;
        LastRequest.Clear();
        LastRequest.AddRange(messages);

        var updates = _streamingUpdates ?? [new ChatResponseUpdate(ChatRole.Assistant, "tamam")];

        foreach (var update in updates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return update;
            await Task.Yield();
        }
    }

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose()
    {
        // Sahte istemcinin serbest birakilacak kaynagi yok.
    }
}
