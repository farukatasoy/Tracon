using Microsoft.Extensions.AI;

namespace Tracon.Core.UnitTests.Fakes;

/// <summary>
/// A chat client that reports its OWN request timeout, the way
/// <see cref="HttpClient"/> does: an <see cref="OperationCanceledException"/>
/// raised while NOTHING was cancelled (K-737).
/// </summary>
/// <remarks>
/// Every official provider SDK talks over <see cref="HttpClient"/>, so this
/// is the shape any of them produces when the provider stops answering. Code
/// that reads such an exception as "someone cancelled" attributes a provider
/// outage to a limit that never fired.
/// </remarks>
internal sealed class TimingOutChatClient : IChatClient
{
    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
        => throw Timeout();

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
        => throw Timeout();

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose()
    {
        // Nothing to release.
    }

    private static TaskCanceledException Timeout()
    {
        const string Message = "The request to https://api.example/v1 timed out after 30s.";

        return new TaskCanceledException(Message, new TimeoutException(Message));
    }
}
