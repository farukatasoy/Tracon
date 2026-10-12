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
    /// <remarks>
    /// The rows come in the target's time order. <see cref="DeleteBatchAsync"/>
    /// deletes in no order, so the two calls can select different rows: archive
    /// and delete a batch with <see cref="ArchiveBatchAsync"/> instead.
    /// </remarks>
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
    /// <returns>
    /// The number of rows deleted. Zero means no matching row could be deleted:
    /// usually none remain, but see the remarks.
    /// </returns>
    /// <remarks>
    /// <para>
    /// For <see cref="RetentionTargets.Attachments"/> with an
    /// <see cref="IAttachmentStorage"/> registered, each row's external content is
    /// deleted BEFORE the row. A row whose content delete fails is kept and still
    /// matches, so a batch can return fewer rows than
    /// <paramref name="batchSize"/> while matching rows remain. The next run
    /// tries the kept row again.
    /// </para>
    /// <para>
    /// The batch has no order, so this method is for a run that does not
    /// archive. A run that archives uses <see cref="ArchiveBatchAsync"/>.
    /// </para>
    /// </remarks>
    ValueTask<int> DeleteBatchAsync(
        string target,
        string? tenantId,
        DateTimeOffset cutoff,
        int batchSize,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads one batch of rows older than the cutoff date, hands it to
    /// <paramref name="archiveAsync"/>, and then deletes exactly the rows of that batch.
    /// </summary>
    /// <param name="target">The target name.</param>
    /// <param name="tenantId">
    /// Only this tenant's rows; if <see langword="null"/>, runs installation-wide
    /// (the <c>'*'</c> policy).
    /// </param>
    /// <param name="cutoff">The cutoff date (UTC).</param>
    /// <param name="batchSize">The maximum number of rows to archive and delete.</param>
    /// <param name="archiveAsync">
    /// Writes the batch to the archive. It is not called when no row matches.
    /// When it throws, no row is deleted and the exception propagates.
    /// </param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>
    /// The number of rows deleted. It is never more than the number of rows
    /// handed to <paramref name="archiveAsync"/>; zero when no row matched.
    /// </returns>
    /// <remarks>
    /// <para>
    /// This is the archive mode of a retention run: a row that was not handed to
    /// <paramref name="archiveAsync"/> is never deleted. A row that stops matching
    /// between the read and the delete is kept. It is archived again when it
    /// matches on a later run, so an <see cref="IArchiveSink"/> can receive the
    /// same row more than once.
    /// </para>
    /// <para>
    /// For <see cref="RetentionTargets.Attachments"/> with an
    /// <see cref="IAttachmentStorage"/> registered, the external content of each
    /// archived row is deleted BEFORE the row, the same as in
    /// <see cref="DeleteBatchAsync"/>; a row whose content delete fails is kept.
    /// </para>
    /// <para>
    /// The default implementation calls <see cref="ReadForArchiveAsync"/> and then
    /// <see cref="DeleteBatchAsync"/> with the number of rows read. It is NOT EXACT:
    /// the two calls can select different rows, and a row that was never archived
    /// can be deleted. It exists only so a store written before this member keeps
    /// compiling. The SQL stores override it; the shipped
    /// <c>RetentionStoreContract</c> checks the rule above.
    /// </para>
    /// </remarks>
    async ValueTask<int> ArchiveBatchAsync(
        string target,
        string? tenantId,
        DateTimeOffset cutoff,
        int batchSize,
        Func<IReadOnlyList<ArchiveRow>, CancellationToken, ValueTask> archiveAsync,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(archiveAsync);

        var rows = await ReadForArchiveAsync(target, tenantId, cutoff, batchSize, cancellationToken).ConfigureAwait(false);

        if (rows.Count == 0)
        {
            return 0;
        }

        await archiveAsync(rows, cancellationToken).ConfigureAwait(false);

        return await DeleteBatchAsync(target, tenantId, cutoff, rows.Count, cancellationToken).ConfigureAwait(false);
    }

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
    /// <remarks>
    /// Only the rows written here are deleted, and only after this call returns;
    /// when it throws, nothing is deleted. A row can arrive more than once: a row
    /// that is kept after its write (it stopped matching, or its delete failed)
    /// is written again by a later run.
    /// </remarks>
    ValueTask WriteAsync(
        string target,
        DateTimeOffset partitionDate,
        IReadOnlyList<ArchiveRow> rows,
        CancellationToken cancellationToken = default);
}
