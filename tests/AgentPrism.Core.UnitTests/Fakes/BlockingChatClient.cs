using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;

namespace AgentPrism.Core.UnitTests.Fakes;

/// <summary>
/// Cagiranin belirteci iptal edilene kadar hicbir zaman tamamlanmayan sahte
/// sohbet istemcisi. Disaridan tetiklenen iptalin gercek bir model cagrisini
/// nasil kestigini benzetmek icin kullanilir.
/// </summary>
internal sealed class BlockingChatClient : IChatClient
{
    public async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);

        throw new InvalidOperationException("BlockingChatClient hicbir zaman tamamlanmamalidir.");
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        yield return new ChatResponseUpdate(ChatRole.Assistant, "baslangic");

        await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
    }

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose()
    {
        // Sahte istemcinin serbest birakilacak kaynagi yok.
    }
}
