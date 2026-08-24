namespace AgentPrism;

/// <summary>
/// The no-op default data-plane implementation. It is registered with in-memory
/// stores and always returns "nothing matched".
/// </summary>
/// <remarks>
/// Retention counts and deletes rows in a persistent SQL provider. In-memory stores
/// are already lost when the process ends, so a real retention implementation comes
/// only with <c>UsePostgreSql()</c>, <c>UseSqlServer()</c>, or <c>UseSqlite()</c>.
/// Without this class, <c>RetentionExecutor</c> would throw when <c>IRetentionStore</c>
/// is unregistered. It instead silently uses the "nothing to delete" behavior.
/// </remarks>
internal sealed class NullRetentionStore : IRetentionStore
{
    /// <inheritdoc />
    public ValueTask<long> CountOlderThanAsync(
        string target,
        string? tenantId,
        DateTimeOffset cutoff,
        CancellationToken cancellationToken = default)
        => new(0L);

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<ArchiveRow>> ReadForArchiveAsync(
        string target,
        string? tenantId,
        DateTimeOffset cutoff,
        int batchSize,
        CancellationToken cancellationToken = default)
        => new((IReadOnlyList<ArchiveRow>)[]);

    /// <inheritdoc />
    public ValueTask<int> DeleteBatchAsync(
        string target,
        string? tenantId,
        DateTimeOffset cutoff,
        int batchSize,
        CancellationToken cancellationToken = default)
        => new(0);

    /// <inheritdoc />
    public ValueTask<DateTimeOffset?> FindRowLimitCutoffAsync(
        string target,
        string? tenantId,
        long maxRows,
        CancellationToken cancellationToken = default)
        => new((DateTimeOffset?)null);
}
