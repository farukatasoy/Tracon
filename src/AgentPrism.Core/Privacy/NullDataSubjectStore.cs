namespace AgentPrism;

/// <summary>
/// The default (in-memory) <see cref="IDataSubjectStore"/>: export and erasure are
/// meaningful only once a SQL provider is enabled — the same precedent as
/// <c>NullRetentionStore</c> (phase 25).
/// </summary>
public sealed class NullDataSubjectStore : IDataSubjectStore
{
    private static readonly IReadOnlyDictionary<string, int> Empty =
        new Dictionary<string, int>(StringComparer.Ordinal);

    /// <inheritdoc />
    public ValueTask<IReadOnlyDictionary<string, int>> PreviewAsync(
        string tenantId,
        DataSubjectScope scope,
        CancellationToken cancellationToken = default)
        => new(Empty);

    /// <inheritdoc />
    public ValueTask<DataSubjectExport> ExportAsync(
        string tenantId,
        DataSubjectScope scope,
        CancellationToken cancellationToken = default)
        => new(new DataSubjectExport { Json = "{}" });

    /// <inheritdoc />
    public async ValueTask<IReadOnlyDictionary<string, int>> EraseAsync(
        string tenantId,
        DataSubjectScope scope,
        Func<IReadOnlyDictionary<string, int>, CancellationToken, ValueTask> beforeCommitAsync,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(beforeCommitAsync);

        // There is nothing to delete, but the caller's audit write still needs to
        // run — it accurately records "an erasure was requested; there was no SQL
        // data plane to act on it".
        await beforeCommitAsync(Empty, cancellationToken).ConfigureAwait(false);

        return Empty;
    }
}
