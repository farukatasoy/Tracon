using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;

namespace AgentPrism.Core.UnitTests.Fakes;

/// <summary>
/// A fake chat client that makes no network call. Tests set the response
/// and streaming chunks to return in advance.
/// </summary>
internal sealed class FakeChatClient : IChatClient
{
    private readonly Func<IEnumerable<ChatMessage>, ChatResponse> _responder;
    private readonly IReadOnlyList<ChatResponseUpdate>? _streamingUpdates;

    public FakeChatClient(
        Func<IEnumerable<ChatMessage>, ChatResponse>? responder = null,
        IReadOnlyList<ChatResponseUpdate>? streamingUpdates = null)
    {
        // NOTE: "tamam" ("ok") stays untranslated on purpose — it is the default
        // reply text and RunRecordingAgentTests.Store_failure_does_not_interrupt_the_run
        // (out of this file's scope) asserts against this exact literal.
        _responder = responder ?? (_ => new ChatResponse(new ChatMessage(ChatRole.Assistant, "tamam")));
        _streamingUpdates = streamingUpdates;
    }

    /// <summary>The last request list that reached the client. Tests verify this.</summary>
    public List<ChatMessage> LastRequest { get; } = [];

    /// <summary>The last options that reached the client.</summary>
    public ChatOptions? LastOptions { get; private set; }

    /// <summary>The number of times the client was called.</summary>
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

        var updates = _streamingUpdates ?? [new ChatResponseUpdate(ChatRole.Assistant, "tamam")]; // see NOTE above

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
        // The fake client has no resource to release.
    }
}
