using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;

namespace AgentPrism;

/// <summary>
/// Model cagrisindan hemen once ek referanslarini (<see cref="UriContent"/>)
/// gercek icerige (<see cref="DataContent"/>) cozen sarmalayici.
/// </summary>
/// <remarks>
/// <para>
/// Sohbet gecmisinde bir ek yalnizca kucuk bir <see cref="UriContent"/>
/// referansi olarak yasar (bkz. <c>docs/14-COK-MODLULUK.md</c>, bolum 14.1).
/// Saglayicilarin cogu kendi erisemedikleri bir URL'yi okuyamaz; bu yuzden
/// referans, saglayiciya gonderilmeden HEMEN once ve yalnizca bu cagri icin
/// bellekte gercek baytlara cozulur. Sonuc kalici hicbir yere yazilmaz.
/// </para>
/// <para>
/// Referans olmayan icerikler (metin, harici bir URL'ye isaret eden
/// <see cref="UriContent"/>) degistirilmeden gecer.
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
                $"'{attachmentId}' kimlikli ek bulunamadi veya bu kiraciya ait degil.");
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
