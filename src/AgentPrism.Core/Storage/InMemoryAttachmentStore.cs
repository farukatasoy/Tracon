using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace AgentPrism;

/// <summary>A store that keeps attachments in process memory.</summary>
/// <remarks>
/// Use the persistent store from <c>AgentPrism.PostgreSql</c> in production.
/// This implementation is for development and tests. All attachments are lost
/// when the process restarts.
/// </remarks>
public sealed class InMemoryAttachmentStore : IAttachmentStore
{
    private readonly ConcurrentDictionary<Guid, Entry> _entries = new();
    private readonly IAttachmentStorage? _storage;

    /// <summary>Initializes a new in-memory attachment store.</summary>
    /// <param name="storage">
    /// When supplied, content is stored here and this store keeps only metadata.
    /// When <see langword="null"/>, content is kept directly in memory.
    /// </param>
    public InMemoryAttachmentStore(IAttachmentStorage? storage = null) => _storage = storage;

    /// <inheritdoc />
    public async ValueTask<AttachmentDescriptor> SaveAsync(
        AttachmentContent content,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);

        var id = AgentPrismId.NewId();
        var sha256 = Convert.ToHexString(SHA256.HashData(content.Data.Span));

        var descriptor = new AttachmentDescriptor
        {
            Id = id,
            TenantId = content.TenantId,
            SessionId = content.SessionId,
            RunId = content.RunId,
            FileName = content.FileName,
            MediaType = content.MediaType,
            ByteSize = content.Data.Length,
            Sha256 = sha256,
            CreatedBy = content.CreatedBy,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        byte[]? bytes = null;
        Uri? externalUri = null;

        if (_storage is not null)
        {
            using var stream = new MemoryStream(content.Data.ToArray(), writable: false);
            externalUri = await _storage
                .WriteAsync(content.TenantId, id, stream, content.MediaType, cancellationToken)
                .ConfigureAwait(false);
        }
        else
        {
            bytes = content.Data.ToArray();
        }

        _entries[id] = new Entry(descriptor, bytes, externalUri);
        return descriptor;
    }

    /// <inheritdoc />
    public ValueTask<AttachmentDescriptor?> GetAsync(
        string tenantId,
        Guid id,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tenantId);

        return new ValueTask<AttachmentDescriptor?>(
            _entries.TryGetValue(id, out var entry) && IsOwnedBy(entry, tenantId)
                ? entry.Descriptor
                : null);
    }

    /// <inheritdoc />
    public async ValueTask<Stream?> OpenReadAsync(
        string tenantId,
        Guid id,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tenantId);

        if (!_entries.TryGetValue(id, out var entry) || !IsOwnedBy(entry, tenantId))
        {
            return null;
        }

        if (entry.ExternalUri is { } uri && _storage is not null)
        {
            return await _storage.ReadAsync(uri, cancellationToken).ConfigureAwait(false);
        }

        return entry.Bytes is { } bytes ? new MemoryStream(bytes, writable: false) : null;
    }

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<AttachmentDescriptor>> ListAsync(
        AttachmentQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var results = _entries.Values
            .Select(static entry => entry.Descriptor)
            .Where(descriptor => string.Equals(descriptor.TenantId, query.TenantId, StringComparison.Ordinal))
            .Where(descriptor => query.SessionId is null ||
                string.Equals(descriptor.SessionId, query.SessionId, StringComparison.Ordinal))
            .OrderByDescending(static descriptor => descriptor.CreatedAt)
            .Skip(query.Skip)
            .Take(query.Take)
            .ToArray();

        return new ValueTask<IReadOnlyList<AttachmentDescriptor>>(results);
    }

    /// <inheritdoc />
    public async ValueTask<bool> DeleteAsync(
        string tenantId,
        Guid id,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tenantId);

        if (!_entries.TryGetValue(id, out var entry) || !IsOwnedBy(entry, tenantId))
        {
            return false;
        }

        if (!_entries.TryRemove(id, out _))
        {
            return false;
        }

        if (entry.ExternalUri is { } uri && _storage is not null)
        {
            await _storage.DeleteAsync(uri, cancellationToken).ConfigureAwait(false);
        }

        return true;
    }

    /// <inheritdoc />
    public async ValueTask<int> DeleteBySessionAsync(
        string tenantId,
        string sessionId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tenantId);
        ArgumentNullException.ThrowIfNull(sessionId);

        var matches = _entries.Values
            .Select(static entry => entry.Descriptor)
            .Where(descriptor => string.Equals(descriptor.TenantId, tenantId, StringComparison.Ordinal) &&
                string.Equals(descriptor.SessionId, sessionId, StringComparison.Ordinal))
            .ToArray();

        var count = 0;

        foreach (var descriptor in matches)
        {
            if (await DeleteAsync(tenantId, descriptor.Id, cancellationToken).ConfigureAwait(false))
            {
                count++;
            }
        }

        return count;
    }

    private static bool IsOwnedBy(Entry entry, string tenantId)
        => string.Equals(entry.Descriptor.TenantId, tenantId, StringComparison.Ordinal);

    private sealed record Entry(AttachmentDescriptor Descriptor, byte[]? Bytes, Uri? ExternalUri);
}
