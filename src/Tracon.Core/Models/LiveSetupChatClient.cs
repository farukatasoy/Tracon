using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;

namespace Tracon;

/// <summary>
/// A chat client that picks the client of the current setup key and address on every call.
/// </summary>
/// <remarks>
/// <para>
/// The agent compiler, a fallback link, the loop judge and the context summarizer take their client when they are built
/// and keep it. Handing them this one means they follow a rotation too: nothing has to be built again, and the pipeline
/// around it (budget, cache, guard, telemetry) is the same one a static client gets.
/// </para>
/// <para>
/// A call reads the snapshot <strong>once</strong>, at its start. A streaming call that began before a rotation ends on
/// the client it began on; the next call uses the new one. The client that was replaced is not disposed.
/// </para>
/// </remarks>
internal sealed class LiveSetupChatClient(Func<IChatClient> current) : IChatClient
{
    /// <inheritdoc />
    public async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
        => await current().GetResponseAsync(messages, options, cancellationToken).ConfigureAwait(false);

    /// <inheritdoc />
    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var update in current().GetStreamingResponseAsync(messages, options, cancellationToken).ConfigureAwait(false))
        {
            yield return update;
        }
    }

    /// <inheritdoc />
    public object? GetService(Type serviceType, object? serviceKey = null)
    {
        ArgumentNullException.ThrowIfNull(serviceType);

        return serviceKey is null && serviceType.IsInstanceOfType(this)
            ? this
            : current().GetService(serviceType, serviceKey);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        // The clients belong to the cache of the setup; a call that is running may still hold one.
    }
}
