namespace Tracon;

/// <summary>
/// The data plane that counts, batch-deletes, and reads-for-archive over the
/// actual retention data.
/// </summary>
/// <remarks>
/// <para>
/// The target name parameter always comes from the <see cref="RetentionTargets"/>
/// allowlist; implementations must throw <see cref="ArgumentException"/> for
/// an unknown target.
/// </para>
/// <para>
/// Which column counts as "age" and which extra condition (for example, only
/// completed jobs) applies is fixed for EACH target and stays embedded inside
/// the implementation; the caller knows only the target name and the cutoff date.
/// </para>
/// <para>
/// <strong>Tenant scoping is mandatory.</strong> A retention
/// policy is defined per tenant; if <c>tenantId</c> is not given, the
/// operation touches <em>every</em> tenant's rows. Only the installation-wide
/// (<c>'*'</c>) policy should pass <see langword="null"/>.
/// </para>
/// <para>
/// The default (in-memory) setup registers <c>NullRetentionStore</c>, which
/// always returns empty/zero: retention is meaningful only when a SQL
/// provider is enabled.
/// </para>
/// <para>
/// <strong>DI lifetime — singleton.</strong> Registered as a singleton with
/// <c>TryAdd</c>; a consumer's own registration wins.
/// </para>
/// </remarks>
public interface IRetentionStore
{
    /// <summary>Returns the number of rows in the target currently older than the cutoff date.</summary>
    /// <param name="target">The target name.</param>
    /// <param name="tenantId">
    /// Only this tenant's rows; if <see langword="null"/>, runs installation-wide
    /// (the <c>'*'</c> policy).
    /// </param>
    /// <param name="cutoff">The cutoff date (UTC). Rows older than this match.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The number of matching rows.</returns>
    ValueTask<long> CountOlderThanAsync(
        string target,
        string? tenantId,
        DateTimeOffset cutoff,
        CancellationToken cancellationToken = default);

    /// <summary>Reads a batch of rows older than the cutoff date (does not delete).</summary>
    /// <param name="target">The target name.</param>
    /// <param name="tenantId">
    /// Only this tenant's rows; if <see langword="null"/>, runs installation-wide
    /// (the <c>'*'</c> policy).
    /// </param>
    /// <param name="cutoff">The cutoff date (UTC).</param>
    /// <param name="batchSize">The maximum number of rows to read.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The rows read; an empty list once no matching rows remain.</returns>
    ValueTask<IReadOnlyList<ArchiveRow>> ReadForArchiveAsync(
        string target,
        string? tenantId,
        DateTimeOffset cutoff,
        int batchSize,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a batch of rows older than the cutoff date. This is NOT a
    /// single bulk <c>DELETE</c>; it runs batch by batch, depending on the provider.
    /// </summary>
    /// <param name="target">The target name.</param>
    /// <param name="tenantId">
    /// Only this tenant's rows; if <see langword="null"/>, runs installation-wide
    /// (the <c>'*'</c> policy).
    /// </param>
    /// <param name="cutoff">The cutoff date (UTC).</param>
    /// <param name="batchSize">The maximum number of rows to delete.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The number of rows deleted. Zero means no matching rows remain.</returns>
    ValueTask<int> DeleteBatchAsync(
        string target,
        string? tenantId,
        DateTimeOffset cutoff,
        int batchSize,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Counting from newest, returns the ordering column's value at row
    /// <paramref name="maxRows"/>
    /// as the cutoff date.
    /// </summary>
    /// <param name="target">
    /// The target name.
    /// </param>
    /// <param name="tenantId">
    /// Only this tenant's rows; if <see langword="null"/>, runs installation-wide (the
    /// <c>'*'</c> policy).
    /// </param>
    /// <param name="maxRows">
    /// The maximum number of rows to keep (at least 1).
    /// </param>
    /// <param name="cancellationToken">
    /// The cancellation token.
    /// </param>
    /// <returns>
    /// The cutoff date; <see langword="null"/> if the target holds FEWER than
    /// <paramref name="maxRows"/>
    /// rows (the volume limit is not exceeded).
    /// </returns>
    /// <remarks>
    /// The returned value can be passed directly into this interface's other three
    /// methods (the <c>@cutoff</c> parameter): volume-based trimming uses the SAME
    /// batch mechanism as age-based deletion.
    /// </remarks>
    ValueTask<DateTimeOffset?> FindRowLimitCutoffAsync(
        string target,
        string? tenantId,
        long maxRows,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// The cold-storage extension point rows are written to before deletion.
/// </summary>
/// <remarks>
/// <para>
/// There is NO default implementation (no cloud SDK
/// dependency is taken). The consumer writes their own sink, based on the
/// file-system example under <c>samples/</c>, and registers it as <c>IArchiveSink</c>.
/// </para>
/// <para>
/// If none is registered, a policy with <c>archive = true</c> deletes NO
/// row — data that cannot be archived is never dropped. This prevents silent data loss.
/// </para>
/// <para>
/// <strong>DI lifetime — singleton, optional.</strong> No default implementation is
/// registered. A consumer that registers one must register it as a singleton — it is
/// resolved once, as a constructor parameter of Tracon's singleton retention
/// executor, via <c>IServiceProvider.GetService&lt;IArchiveSink&gt;()</c>.
/// A scoped registration would be a captive dependency.
/// </para>
/// </remarks>
public interface IArchiveSink
{
    /// <summary>Writes a batch of rows to the archive.</summary>
    /// <param name="target">The target name.</param>
    /// <param name="partitionDate">
    /// The date the batch belongs to (UTC, day precision). The sink may use
    /// this to partition the file/object path (for example,
    /// <c>run_events/2026-08-05.jsonl.gz</c>).
    /// </param>
    /// <param name="rows">The rows to write.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The completion task.</returns>
    ValueTask WriteAsync(
        string target,
        DateTimeOffset partitionDate,
        IReadOnlyList<ArchiveRow> rows,
        CancellationToken cancellationToken = default);
}
