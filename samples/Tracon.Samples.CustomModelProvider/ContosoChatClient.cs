using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;

namespace Tracon.Samples.CustomModelProvider;

/// <summary>
/// The raw chat client <see cref="ContosoModelProvider"/> hands to
/// Tracon.
/// </summary>
/// <remarks>
/// <para>
/// It makes no network call: it answers from the last user message so the
/// sample's tests can complete a real agent run offline. A production client
/// would call its vendor SDK here.
/// </para>
/// <para>
/// Note what this class does <strong>not</strong> do. It does not resolve
/// tool calls, emit telemetry, inspect content, or count failures — those
/// rings are added by <c>ModelProviderRegistry</c> around whatever the
/// provider returns. Adding them here would nest a second tool-call loop,
/// and the turn carrying a tool result back into the model would run beneath
/// Tracon's content guard.
/// </para>
/// <para>
/// It is safe to use from several threads at once and holds nothing that
/// needs releasing: Tracon never disposes it.
/// </para>
/// </remarks>
internal sealed class ContosoChatClient(
    ContosoModelProvider.ContosoBackend backend,
    string modelId,
    bool shout) : IChatClient
{
    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messages);
        cancellationToken.ThrowIfCancellationRequested();

        var response = new ChatResponse(new ChatMessage(ChatRole.Assistant, Answer(messages)))
        {
            ModelId = modelId,
            FinishReason = ChatFinishReason.Stop,
        };

        return Task.FromResult(response);
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messages);

        foreach (var word in Answer(messages).Split(' '))
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return new ChatResponseUpdate(ChatRole.Assistant, word + " ") { ModelId = modelId };
            await Task.Yield();
        }
    }

    /// <remarks>
    /// Returning <see langword="null"/> for everything is the correct default.
    /// In particular, a provider must never surface a
    /// <c>FunctionInvokingChatClient</c> here: that is how Tracon's own
    /// rings discover each other, and claiming to be one would misreport who
    /// owns the tool-call loop.
    /// </remarks>
    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose()
    {
        // Nothing to release. The backend is shared across every client built
        // for the same credential and outlives any one of them.
    }

    internal bool UsesApiKey(string apiKey)
        => string.Equals(backend.ApiKey, apiKey, StringComparison.Ordinal);

    private string Answer(IEnumerable<ChatMessage> messages)
    {
        var lastUserText = messages
            .Where(static message => message.Role == ChatRole.User)
            .Select(static message => message.Text)
            .LastOrDefault(static text => !string.IsNullOrWhiteSpace(text))
            ?? string.Empty;

        // The key is used, never logged or returned: a credential must not
        // reach a response, a log line, or a stored record.
        var reply = $"contoso[{modelId}] heard: {lastUserText}";

        _ = backend.ApiKey;

        return shout ? reply.ToUpperInvariant() : reply;
    }
}
