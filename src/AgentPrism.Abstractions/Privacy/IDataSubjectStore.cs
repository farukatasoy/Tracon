namespace AgentPrism;

/// <summary>
/// The data plane that counts, exports, and erases one data subject's content
/// across the schema.
/// </summary>
/// <remarks>
/// The default (in-memory) setup registers <c>NullDataSubjectStore</c>, which
/// always returns an empty result: export and erasure are meaningful only when a
/// SQL provider is enabled — the same precedent as <see cref="IRetentionStore"/>
/// and its <c>NullRetentionStore</c>.
/// </remarks>
public interface IDataSubjectStore
{
    /// <summary>Counts the rows that would be deleted for a scope, by target, without deleting anything.</summary>
    /// <param name="tenantId">The tenant id.</param>
    /// <param name="scope">The subject's scope.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The row count per target; a target with nothing to delete is present with the value <c>0</c>.</returns>
    ValueTask<IReadOnlyDictionary<string, int>> PreviewAsync(
        string tenantId,
        DataSubjectScope scope,
        CancellationToken cancellationToken = default);

    /// <summary>Reads every column of a scope's content, target by target, as one export document.</summary>
    /// <param name="tenantId">The tenant id.</param>
    /// <param name="scope">The subject's scope.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The export document.</returns>
    ValueTask<DataSubjectExport> ExportAsync(
        string tenantId,
        DataSubjectScope scope,
        CancellationToken cancellationToken = default);

    /// <summary>Deletes a scope's content.</summary>
    /// <param name="tenantId">The tenant id.</param>
    /// <param name="scope">The subject's scope.</param>
    /// <param name="beforeCommitAsync">
    /// Runs after every delete has executed but BEFORE the change is committed. When
    /// it throws, every delete is rolled back and the exception propagates —
    /// That rule applied here: an erasure that cannot be written to the audit
    /// trail is not applied. The caller uses this to write the audit entry (which
    /// needs the row counts this method computes) while the erasure itself can
    /// still be undone if that write fails.
    /// </param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The row count actually deleted per target.</returns>
    ValueTask<IReadOnlyDictionary<string, int>> EraseAsync(
        string tenantId,
        DataSubjectScope scope,
        Func<IReadOnlyDictionary<string, int>, CancellationToken, ValueTask> beforeCommitAsync,
        CancellationToken cancellationToken = default);
}
