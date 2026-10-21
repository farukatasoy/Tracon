using System.Text.Json;

namespace Tracon;

/// <summary>
/// The default (in-memory) <see cref="IDataSubjectStore"/>: export and erasure are
/// meaningful only once a SQL provider is enabled — the same precedent as
/// <c>NullRetentionStore</c>.
/// </summary>
/// <remarks>
/// The one thing it can still do is the copy of a session's content that conversation memory keeps in
/// the host's vector store: that copy does not depend on a SQL provider, so an erasure of a data subject
/// whose sessions are in memory still deletes it.
/// </remarks>
internal sealed class NullDataSubjectStore : IDataSubjectStore
{
    private static readonly IReadOnlyDictionary<string, int> Empty =
        new Dictionary<string, int>(StringComparer.Ordinal);

    private readonly IReadOnlyList<ISubjectContentEraser> _contentErasers;

    /// <summary>Creates the store.</summary>
    /// <param name="contentErasers">The stores that keep a copy of a session's content outside SQL.</param>
    public NullDataSubjectStore(IEnumerable<ISubjectContentEraser>? contentErasers = null)
        => _contentErasers = contentErasers is null ? [] : [.. contentErasers];

    /// <inheritdoc />
    public async ValueTask<IReadOnlyDictionary<string, int>> PreviewAsync(
        string tenantId,
        DataSubjectScope scope,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (_contentErasers.Count == 0)
        {
            return Empty;
        }

        ArgumentNullException.ThrowIfNull(scope);

        var counts = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var eraser in _contentErasers)
        {
            counts[eraser.Target] = await eraser.CountAsync(tenantId, scope.SessionIds, cancellationToken).ConfigureAwait(false);
        }

        return counts;
    }

    /// <inheritdoc />
    public async ValueTask<DataSubjectExport> ExportAsync(
        string tenantId,
        DataSubjectScope scope,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (_contentErasers.Count == 0)
        {
            return new DataSubjectExport { Json = "{}" };
        }

        ArgumentNullException.ThrowIfNull(scope);

        using var buffer = new MemoryStream();

        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("tenantId", tenantId);
            writer.WriteString("exportedAt", DateTimeOffset.UtcNow);

            foreach (var eraser in _contentErasers)
            {
                await eraser.WriteExportAsync(tenantId, scope.SessionIds, writer, cancellationToken).ConfigureAwait(false);
            }

            writer.WriteEndObject();
        }

        return new DataSubjectExport { Json = System.Text.Encoding.UTF8.GetString(buffer.ToArray()) };
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyDictionary<string, int>> EraseAsync(
        string tenantId,
        DataSubjectScope scope,
        Func<IReadOnlyDictionary<string, int>, CancellationToken, ValueTask> beforeCommitAsync,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(beforeCommitAsync);
        cancellationToken.ThrowIfCancellationRequested();

        // There is nothing in SQL to delete, but the caller's audit write still needs to
        // run — it accurately records "an erasure was requested; there was no SQL
        // data plane to act on it". The copy in the host's vector store goes first, the
        // same order the SQL store keeps: the audit trail never records an erasure that failed.
        if (_contentErasers.Count == 0)
        {
            await beforeCommitAsync(Empty, cancellationToken).ConfigureAwait(false);

            return Empty;
        }

        ArgumentNullException.ThrowIfNull(scope);

        var counts = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var eraser in _contentErasers)
        {
            counts[eraser.Target] = await eraser.EraseAsync(tenantId, scope.SessionIds, cancellationToken).ConfigureAwait(false);
        }

        await beforeCommitAsync(counts, cancellationToken).ConfigureAwait(false);

        return counts;
    }
}
