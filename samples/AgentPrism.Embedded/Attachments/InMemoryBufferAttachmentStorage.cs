using System.Collections.Concurrent;

namespace AgentPrism.Embedded;

/// <summary>
/// 🚨 <strong>DEMONSTRATION ONLY.</strong> Stands in for a real external object
/// store (S3, Azure Blob, GCS).
/// </summary>
/// <remarks>
/// AgentPrism takes no dependency on any cloud SDK, so there is no default
/// <see cref="IAttachmentStorage"/> — content lives in the database until a
/// host registers one. This buffer wrapper is <strong>not</strong> an
/// AgentPrism type and is not shipped in any package; a real deployment
/// derives its own implementation from whichever object-store client it
/// already uses.
/// </remarks>
internal sealed class InMemoryBufferAttachmentStorage : IAttachmentStorage
{
    private readonly ConcurrentDictionary<Uri, (byte[] Content, string MediaType)> _blobs = new();

    public ValueTask<Uri> WriteAsync(
        string tenantId,
        Guid id,
        Stream content,
        string mediaType,
        CancellationToken cancellationToken = default)
        => WriteCoreAsync(tenantId, id, content, mediaType, cancellationToken);

    private async ValueTask<Uri> WriteCoreAsync(
        string tenantId,
        Guid id,
        Stream content,
        string mediaType,
        CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);

        var uri = new Uri($"embedded-buffer://{tenantId}/{id}");
        _blobs[uri] = (buffer.ToArray(), mediaType);

        return uri;
    }

    public ValueTask<Stream?> ReadAsync(Uri uri, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(uri);

        return new ValueTask<Stream?>(
            _blobs.TryGetValue(uri, out var blob) ? new MemoryStream(blob.Content, writable: false) : null);
    }

    public ValueTask DeleteAsync(Uri uri, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(uri);

        _blobs.TryRemove(uri, out _);

        return default;
    }
}
