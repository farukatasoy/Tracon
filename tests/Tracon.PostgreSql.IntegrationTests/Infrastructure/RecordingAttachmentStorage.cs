using System.Collections.Concurrent;

namespace Tracon.PostgreSql.IntegrationTests.Infrastructure;

/// <summary>
/// An in-memory <see cref="IAttachmentStorage"/> that records every delete and
/// can be told to fail them.
/// </summary>
/// <remarks>
/// Modelled on the embedded sample's buffer storage. A delete of a location
/// that no longer exists succeeds, as the interface requires.
/// </remarks>
internal sealed class RecordingAttachmentStorage : IAttachmentStorage
{
    private readonly ConcurrentDictionary<Uri, byte[]> _blobs = new();
    private readonly ConcurrentQueue<Uri> _deleted = new();

    /// <summary>Gets or sets a value indicating whether every delete throws.</summary>
    public bool FailDeletes { get; set; }

    /// <summary>Gets the locations deleted so far, in order.</summary>
    public IReadOnlyCollection<Uri> Deleted => _deleted;

    /// <summary>Gets the locations that still hold bytes.</summary>
    public ICollection<Uri> Stored => _blobs.Keys;

    /// <inheritdoc />
    public async ValueTask<Uri> WriteAsync(
        string tenantId,
        Guid id,
        Stream content,
        string mediaType,
        CancellationToken cancellationToken = default)
    {
        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, cancellationToken);

        var uri = new Uri($"recording://{tenantId}/{id}");
        _blobs[uri] = buffer.ToArray();

        return uri;
    }

    /// <inheritdoc />
    public ValueTask<Stream?> ReadAsync(Uri uri, CancellationToken cancellationToken = default)
        => new(_blobs.TryGetValue(uri, out var bytes) ? new MemoryStream(bytes, writable: false) : null);

    /// <inheritdoc />
    public ValueTask DeleteAsync(Uri uri, CancellationToken cancellationToken = default)
    {
        if (FailDeletes)
        {
            throw new IOException("The attachment storage is unavailable.");
        }

        _blobs.TryRemove(uri, out _);
        _deleted.Enqueue(uri);

        return ValueTask.CompletedTask;
    }
}
