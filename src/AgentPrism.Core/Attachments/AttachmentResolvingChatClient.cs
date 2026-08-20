using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;

namespace AgentPrism;

/// <summary>
/// A wrapper that resolves attachment references, <see cref="UriContent"/>, to
/// actual content, <see cref="DataContent"/>, immediately before the model call.
/// </summary>
/// <remarks>
/// <para>
/// In chat history, an attachment exists only as a small <see cref="UriContent"/>
/// reference. Most providers cannot
/// read a URL they cannot access. The reference therefore resolves to the actual bytes
/// in memory immediately before it is sent to the provider, and only for that call.
/// The result is not persisted.
/// </para>
/// <para>
/// Content that is not a reference, such as text or <see cref="UriContent"/> that
/// points to an external URL, passes through unchanged.
/// </para>
/// </remarks>
internal sealed class AttachmentResolvingChatClient(IChatClient inner, IAttachmentStore store, ITenantContext tenantContext)
    : DelegatingChatClient(inner)
{
    /// <inheritdoc />
    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
        => await base.GetResponseAsync(
                await ResolveAsync(messages, cancellationToken).ConfigureAwait(false),
                options,
                cancellationToken)
            .ConfigureAwait(false);

    /// <inheritdoc />
    public override IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
        => StreamAsync(messages, options, cancellationToken);

    private async IAsyncEnumerable<ChatResponseUpdate> StreamAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var resolved = await ResolveAsync(messages, cancellationToken).ConfigureAwait(false);

        await foreach (var update in base
            .GetStreamingResponseAsync(resolved, options, cancellationToken)
            .ConfigureAwait(false))
        {
            yield return update;
        }
    }

    private async ValueTask<List<ChatMessage>> ResolveAsync(
        IEnumerable<ChatMessage> messages,
        CancellationToken cancellationToken)
    {
        var result = new List<ChatMessage>();

        foreach (var message in messages)
        {
            result.Add(await ResolveMessageAsync(message, cancellationToken).ConfigureAwait(false));
        }

        return result;
    }

    private async ValueTask<ChatMessage> ResolveMessageAsync(ChatMessage message, CancellationToken cancellationToken)
    {
        List<AIContent>? resolvedContents = null;

        for (var i = 0; i < message.Contents.Count; i++)
        {
            if (message.Contents[i] is not UriContent uriContent ||
                !AttachmentUriReference.TryParse(uriContent.Uri, out var attachmentId))
            {
                continue;
            }

            var bytes = await ReadAllBytesAsync(attachmentId, cancellationToken).ConfigureAwait(false);

            resolvedContents ??= [.. message.Contents];
            resolvedContents[i] = new DataContent(bytes, uriContent.MediaType);
        }

        if (resolvedContents is null)
        {
            return message;
        }

        var clone = message.Clone();
        clone.Contents = resolvedContents;
        return clone;
    }

    private async ValueTask<byte[]> ReadAllBytesAsync(Guid attachmentId, CancellationToken cancellationToken)
    {
        if (await store.OpenReadAsync(tenantContext.TenantId, attachmentId, cancellationToken).ConfigureAwait(false)
            is not { } stream)
        {
            throw new AgentPrismException(
                $"Attachment '{attachmentId}' was not found or does not belong to this tenant.");
        }

        await using (stream.ConfigureAwait(false))
        {
            var buffer = new MemoryStream();

            await using (buffer.ConfigureAwait(false))
            {
                await stream.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
                return buffer.ToArray();
            }
        }
    }
}
