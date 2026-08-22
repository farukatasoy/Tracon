using System.Buffers;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace AgentPrism;

/// <summary>Persists generated image content as tenant- and session-owned attachments.</summary>
/// <remarks>
/// A URI response is fetched immediately through <see cref="EgressSocketGuard"/>.
/// Image-provider URLs are often temporary, and a normal <see cref="HttpClient"/>
/// would make a provider response an SSRF bypass.
/// </remarks>
internal sealed class ImageAttachmentWriter
{
    private readonly IAttachmentStore _store;
    private readonly AttachmentTypeGuard _typeGuard;
    private readonly EgressSocketGuard _egressGuard;
    private readonly ILogger<ImageAttachmentWriter> _logger;

    public ImageAttachmentWriter(
        IAttachmentStore store,
        AttachmentTypeGuard typeGuard,
        EgressSocketGuard egressGuard,
        ILogger<ImageAttachmentWriter> logger)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(typeGuard);
        ArgumentNullException.ThrowIfNull(egressGuard);
        ArgumentNullException.ThrowIfNull(logger);

        _store = store;
        _typeGuard = typeGuard;
        _egressGuard = egressGuard;
        _logger = logger;
    }

    /// <summary>Saves every generated image.</summary>
    public async ValueTask<IReadOnlyList<AttachmentDescriptor>> WriteAsync(
        IList<AIContent> contents,
        string tenantId,
        string? sessionId,
        Guid? runId,
        string? createdBy,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(contents);
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);

        if (contents.Count == 0)
        {
            throw new AgentPrismException("The image provider returned no image content.");
        }

        var saved = new List<AttachmentDescriptor>(contents.Count);

        try
        {
            foreach (var content in contents)
            {
                var bytes = await ReadBytesAsync(content, cancellationToken).ConfigureAwait(false);
                var validation = _typeGuard.Validate(bytes.Span);

                if (!validation.IsValid)
                {
                    throw new AgentPrismException(
                        $"The produced image could not be saved: {validation.Error} " +
                        "The attachment type is derived from its magic bytes.");
                }

                var descriptor = await _store.SaveAsync(
                    new AttachmentContent
                    {
                        TenantId = tenantId,
                        SessionId = sessionId,
                        RunId = runId,
                        FileName = BuildFileName(validation.MediaType!),
                        MediaType = validation.MediaType!,
                        Data = bytes,
                        CreatedBy = createdBy,
                    },
                    cancellationToken).ConfigureAwait(false);

                saved.Add(descriptor);
            }
        }
        catch
        {
            await DeleteSavedAsync(saved, tenantId).ConfigureAwait(false);
            throw;
        }

        return saved;
    }

    private async ValueTask<ReadOnlyMemory<byte>> ReadBytesAsync(AIContent content, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(content);

        if (content is DataContent data)
        {
            return data.Data;
        }

        if (content is UriContent uriContent)
        {
            var uri = uriContent.Uri;

            if (!uri.IsAbsoluteUri)
            {
                throw new AgentPrismException("The image provider returned a relative image URI.");
            }

            // The tool wrapper owns the execution timeout and passes its
            // cancellation token here. An infinite HttpClient timeout prevents a
            // second, racing timeout from masking that tool-level classification.
            using var client = _egressGuard.CreateHttpClient(Timeout.InfiniteTimeSpan);
            using var response = await client
                .GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);

            response.EnsureSuccessStatusCode();

            if (response.Content.Headers.ContentLength is { } length && length > _typeGuard.MaxBytes)
            {
                throw new AgentPrismException(
                    $"The generated image exceeds the {_typeGuard.MaxBytes} byte attachment limit.");
            }

            var stream = await response.Content
                .ReadAsStreamAsync(cancellationToken)
                .ConfigureAwait(false);

            await using (stream.ConfigureAwait(false))
            {
                return await ReadLimitedAsync(stream, _typeGuard.MaxBytes, cancellationToken).ConfigureAwait(false);
            }
        }

        if (content is HostedFileContent)
        {
            // IAttachmentStore persists image bytes. It has no durable hosted-file
            // reference field, and writing the provider id as image data would make
            // the transcript point at a broken image. Current supported providers
            // produce DataContent or UriContent; retain this explicit failure for a
            // future provider instead of silently losing the generated image.
            throw new AgentPrismException(
                "The image provider returned a hosted file reference, which this attachment store cannot persist.");
        }

        throw new AgentPrismException(
            $"The image provider returned unsupported content type '{content.GetType().Name}'.");
    }

    private static async ValueTask<ReadOnlyMemory<byte>> ReadLimitedAsync(
        Stream stream,
        long maximumBytes,
        CancellationToken cancellationToken)
    {
        var initialCapacity = (int)Math.Min(maximumBytes, 81_920L);
        using var output = new MemoryStream(initialCapacity);
        var buffer = ArrayPool<byte>.Shared.Rent(81_920);

        try
        {
            while (true)
            {
                var read = await stream
                    .ReadAsync(buffer.AsMemory(), cancellationToken)
                    .ConfigureAwait(false);

                if (read == 0)
                {
                    return output.ToArray();
                }

                if (output.Length > maximumBytes - read)
                {
                    throw new AgentPrismException(
                        $"The generated image exceeds the {maximumBytes} byte attachment limit.");
                }

                await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private async ValueTask DeleteSavedAsync(IReadOnlyList<AttachmentDescriptor> saved, string tenantId)
    {
        foreach (var attachment in saved)
        {
            try
            {
                _ = await _store.DeleteAsync(tenantId, attachment.Id, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                _logger.LogError(
                    exception,
                    "Could not roll back generated image attachment {AttachmentId} for tenant {TenantId}.",
                    attachment.Id,
                    tenantId);
            }
        }
    }

    private static string BuildFileName(string mediaType)
    {
        var extension = mediaType switch
        {
            "image/png" => "png",
            "image/jpeg" => "jpg",
            "image/gif" => "gif",
            "image/webp" => "webp",
            _ => "bin",
        };

        return $"image-{DateTime.UtcNow:yyyyMMdd-HHmmss-fffffff}.{extension}";
    }
}
