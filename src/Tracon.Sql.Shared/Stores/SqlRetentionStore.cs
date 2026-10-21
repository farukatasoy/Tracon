using System.Data.Common;
using System.Globalization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Tracon;

/// <summary>
/// The data-plane implementation of <see cref="IRetentionStore"/>: counting, batch
/// deletion, and reading for archival.
/// </summary>
/// <remarks>
/// <para>
/// SQL text per target is NOT hand-copied: <see cref="RetentionTargetRegistry"/>
/// defines the table/predicate, <see cref="SqlDialect"/> applies the provider-specific
/// 3-part template (count/read/delete).
/// </para>
/// <para>
/// Archive rows are converted to JSON by <see cref="SqlJsonRowWriter"/>, without
/// knowing the column schema up front and without reflection — this store compiles into
/// <c>Tracon.PostgreSql</c> and that package must stay AOT-compatible.
/// </para>
/// <para>
/// The <see cref="RetentionTargets.Attachments"/> target deletes the external
/// content BEFORE the row when <see cref="IAttachmentStorage"/> is registered:
/// the row is the only record of where the content is. A row whose content
/// delete fails stays and is retried on the next run.
/// </para>
/// <para>
/// The archive mode (<see cref="ArchiveBatchAsync"/>) deletes by the keys of the
/// rows it archived (<see cref="RetentionTargetDefinition.KeyColumns"/>), never by
/// a second batch: an unordered batch delete chooses other rows than the ordered
/// archive read, and rows that were never archived are deleted.
/// </para>
/// </remarks>
internal sealed class SqlRetentionStore : IRetentionStore
{
    /// <summary>The columns an attachment batch reads before its content is deleted.</summary>
    private const string AttachmentKeyColumns = "id, tenant_id, external_uri";

    /// <summary>
    /// The most key parameters one archive delete binds. SQL Server accepts at
    /// most 2,100 parameters per command; the rest of the batch goes in the next
    /// command.
    /// </summary>
    private const int MaxKeyParametersPerDelete = 1000;

    private readonly SqlStoreContext _context;
    private readonly SqlDialect _dialect;
    private readonly IAttachmentStorage? _storage;
    private readonly ILogger<SqlRetentionStore> _logger;
    private readonly IReadOnlyList<ISubjectContentEraser> _contentErasers;

    /// <summary>Creates a new retention data-plane store.</summary>
    /// <param name="context">The store context.</param>
    /// <param name="storage">
    /// The external attachment content store, when registered. Without it the
    /// attachment content lives in the row and goes with it.
    /// </param>
    /// <param name="logger">The logger for a content delete that fails; optional.</param>
    /// <param name="contentErasers">
    /// The stores that keep a copy of a session's content outside SQL (conversation memory). A
    /// session's copy is erased before the session goes. Empty when none is set up.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="context"/> is <see langword="null"/>.</exception>
    public SqlRetentionStore(
        SqlStoreContext context,
        IAttachmentStorage? storage = null,
        ILogger<SqlRetentionStore>? logger = null,
        IEnumerable<ISubjectContentEraser>? contentErasers = null)
    {
        ArgumentNullException.ThrowIfNull(context);

        _context = context;
        _dialect = context.Dialect;
        _storage = storage;
        _logger = logger ?? NullLogger<SqlRetentionStore>.Instance;
        _contentErasers = contentErasers is null ? [] : [.. contentErasers];
    }

    /// <inheritdoc />
    public async ValueTask<long> CountOlderThanAsync(
        string target,
        string? tenantId,
        DateTimeOffset cutoff,
        CancellationToken cancellationToken = default)
    {
        var definition = RetentionTargetRegistry.Resolve(_dialect, target);
        var sql = _dialect.BuildRetentionCountSql(definition.Table, Where(definition, tenantId));

        var command = _context.CreateCommand(sql);
        _dialect.AddTimestamp(command, "cutoff", cutoff);
        AddTenant(command, tenantId);

        var result = await DbHelpers.ExecuteScalarAsync(command, cancellationToken).ConfigureAwait(false);

        return result switch
        {
            null => 0L,
            long count => count,
            int count => count,
            _ => Convert.ToInt64(result, CultureInfo.InvariantCulture),
        };
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<ArchiveRow>> ReadForArchiveAsync(
        string target,
        string? tenantId,
        DateTimeOffset cutoff,
        int batchSize,
        CancellationToken cancellationToken = default)
    {
        var definition = RetentionTargetRegistry.Resolve(_dialect, target);
        var sql = _dialect.BuildRetentionArchiveSelectSql(
            definition.Table,
            Where(definition, tenantId),
            definition.OrderColumn);

        var command = _context.CreateCommand(sql);
        _dialect.AddTimestamp(command, "cutoff", cutoff);
        AddTenant(command, tenantId);
        DbHelpers.Add(command, "batchSize", Math.Max(1, batchSize));

        // F-447: the sink is the consumer's code; a protected column reaches it as plaintext.
        var readValue = ProtectedValue.RowReader(_context, RetentionTargetRegistry.ProtectedArchiveColumns(target));

        return await DbHelpers
            .ReadListAsync(command, reader => new ArchiveRow { Json = SqlJsonRowWriter.RowToJson(reader, readValue) }, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask<int> DeleteBatchAsync(
        string target,
        string? tenantId,
        DateTimeOffset cutoff,
        int batchSize,
        CancellationToken cancellationToken = default)
    {
        var definition = RetentionTargetRegistry.Resolve(_dialect, target);

        if (_storage is not null && string.Equals(target, RetentionTargets.Attachments, StringComparison.Ordinal))
        {
            return await DeleteAttachmentBatchAsync(_storage, definition, tenantId, cutoff, batchSize, cancellationToken)
                .ConfigureAwait(false);
        }

        if (_contentErasers.Count > 0 && string.Equals(target, RetentionTargets.Sessions, StringComparison.Ordinal))
        {
            return await DeleteSessionBatchAsync(definition, tenantId, cutoff, batchSize, cancellationToken).ConfigureAwait(false);
        }

        var sql = _dialect.BuildRetentionDeleteBatchSql(definition.Table, Where(definition, tenantId));

        var command = _context.CreateCommand(sql);
        _dialect.AddTimestamp(command, "cutoff", cutoff);
        AddTenant(command, tenantId);
        DbHelpers.Add(command, "batchSize", Math.Max(1, batchSize));

        return await DbHelpers.ExecuteAsync(command, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    /// <remarks>
    /// <para>
    /// The read is the ordered archive read. Each row's key is read with it, and
    /// after <paramref name="archiveAsync"/> returns, only those keys are deleted —
    /// with the target's condition checked again, so a row that stopped matching
    /// in between stays. The keys are bound with the values the read returned,
    /// so each provider compares its own representation (SQLite keeps an id as
    /// upper-case text).
    /// </para>
    /// <para>
    /// No transaction spans <paramref name="archiveAsync"/>: an archive write can
    /// be slow, and it must not hold row locks (or a SQL Server table lock after
    /// escalation) while it runs. A batch that fails half way through its delete
    /// keeps the rest of its rows, and a later run archives them again.
    /// </para>
    /// <para>
    /// A key that holds the tenant (<see cref="RetentionTargetDefinition.TenantInKey"/>)
    /// is unique only within one tenant. Without a tenant filter, the batch is
    /// deleted tenant by tenant, each with the filter, so a row of another tenant
    /// with the same id is never matched.
    /// </para>
    /// </remarks>
    public async ValueTask<int> ArchiveBatchAsync(
        string target,
        string? tenantId,
        DateTimeOffset cutoff,
        int batchSize,
        Func<IReadOnlyList<ArchiveRow>, CancellationToken, ValueTask> archiveAsync,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(archiveAsync);

        var definition = RetentionTargetRegistry.Resolve(_dialect, target);
        var where = Where(definition, tenantId);
        var attachments = _storage is not null
            && string.Equals(target, RetentionTargets.Attachments, StringComparison.Ordinal);
        var byTenant = tenantId is null && definition.TenantInKey;

        // F-447: the sink is the consumer's code; a protected column reaches it as plaintext.
        var readValue = ProtectedValue.RowReader(_context, RetentionTargetRegistry.ProtectedArchiveColumns(target));

        var select = _context.CreateCommand(
            _dialect.BuildRetentionArchiveSelectSql(definition.Table, where, definition.OrderColumn));
        _dialect.AddTimestamp(select, "cutoff", cutoff);
        AddTenant(select, tenantId);
        DbHelpers.Add(select, "batchSize", Math.Max(1, batchSize));

        var batch = await DbHelpers
            .ReadListAsync(
                select,
                reader => ReadArchivedRow(reader, definition.KeyColumns, attachments, byTenant, readValue),
                cancellationToken)
            .ConfigureAwait(false);

        if (batch.Count == 0)
        {
            return 0;
        }

        await archiveAsync(batch.ConvertAll(static row => row.Row), cancellationToken).ConfigureAwait(false);

        if (attachments)
        {
            return await DeleteAttachmentRowsAsync(_storage!, batch.ConvertAll(static row => row.Attachment!), cancellationToken)
                .ConfigureAwait(false);
        }

        if (_contentErasers.Count > 0 && string.Equals(target, RetentionTargets.Sessions, StringComparison.Ordinal))
        {
            // The archive has the rows; the copy of each session outside SQL goes before the row does.
            var sessions = batch.ConvertAll(row => (
                Id: Convert.ToString(row.Key[0], CultureInfo.InvariantCulture)!,
                Tenant: row.Tenant ?? AmbientTenantScope.Normalize(tenantId!)));

            return await EraseSessionContentThenDeleteAsync(definition, cutoff, sessions, cancellationToken).ConfigureAwait(false);
        }

        if (!byTenant)
        {
            return await DeleteArchivedKeysAsync(definition, where, tenantId, cutoff, batch.ConvertAll(static row => row.Key), cancellationToken)
                .ConfigureAwait(false);
        }

        var deleted = 0;

        foreach (var group in batch.GroupBy(static row => row.Tenant!, StringComparer.Ordinal))
        {
            deleted += await DeleteArchivedKeysAsync(
                    definition,
                    Where(definition, group.Key),
                    group.Key,
                    cutoff,
                    [.. group.Select(static row => row.Key)],
                    cancellationToken)
                .ConfigureAwait(false);
        }

        return deleted;
    }

    /// <summary>Reads one archive row and the values of its key.</summary>
    /// <param name="reader">The reader, positioned on a row of the ordered archive read.</param>
    /// <param name="keyColumns">The target's key columns.</param>
    /// <param name="attachment">Whether the row is an attachment whose external content goes first.</param>
    /// <param name="withTenant">Whether the row's tenant is read for a delete tenant by tenant.</param>
    /// <param name="readValue">Reads a protected column back as plaintext; <see langword="null"/> when the target has none.</param>
    /// <returns>The row.</returns>
    private static ArchivedRow ReadArchivedRow(
        DbDataReader reader,
        IReadOnlyList<string> keyColumns,
        bool attachment,
        bool withTenant,
        Func<string, object, object>? readValue)
    {
        var key = new object[keyColumns.Count];

        for (var column = 0; column < key.Length; column++)
        {
            key[column] = reader.GetValue(reader.GetOrdinal(keyColumns[column]));
        }

        var tenant = attachment || withTenant ? ReadTenant(reader) : null;

        var attachmentKey = attachment
            ? new AttachmentKey(
                reader.GetGuid(reader.GetOrdinal("id")),
                tenant!,
                DbHelpers.GetNullableString(reader, reader.GetOrdinal("external_uri")))
            : null;

        return new ArchivedRow(new ArchiveRow { Json = SqlJsonRowWriter.RowToJson(reader, readValue) }, key, tenant, attachmentKey);
    }

    /// <summary>Reads the row's tenant in its canonical form, the form every tenant parameter is bound in.</summary>
    /// <param name="reader">The reader, positioned on a row of a table with its own tenant column.</param>
    /// <returns>The tenant.</returns>
    private static string ReadTenant(DbDataReader reader)
        => AmbientTenantScope.Normalize(reader.GetString(reader.GetOrdinal("tenant_id")));

    /// <summary>Deletes the archived rows by their keys, a bounded number of keys per command.</summary>
    /// <param name="definition">The target definition.</param>
    /// <param name="where">The target's condition, with the tenant filter.</param>
    /// <param name="tenantId">The tenant; nothing is bound when <see langword="null"/>.</param>
    /// <param name="cutoff">The cutoff date.</param>
    /// <param name="keys">The key of each row to delete, one value per key column.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The number of rows deleted.</returns>
    private async ValueTask<int> DeleteArchivedKeysAsync(
        RetentionTargetDefinition definition,
        string where,
        string? tenantId,
        DateTimeOffset cutoff,
        List<IReadOnlyList<object>> keys,
        CancellationToken cancellationToken)
    {
        var keyColumns = definition.KeyColumns;
        var rowsPerCommand = Math.Max(1, MaxKeyParametersPerDelete / keyColumns.Count);
        var deleted = 0;

        for (var offset = 0; offset < keys.Count; offset += rowsPerCommand)
        {
            var rowCount = Math.Min(rowsPerCommand, keys.Count - offset);
            var command = _context.CreateCommand(
                _dialect.BuildRetentionDeleteKeysSql(definition.Table, where, keyColumns, rowCount));
            _dialect.AddTimestamp(command, "cutoff", cutoff);
            AddTenant(command, tenantId);

            for (var row = 0; row < rowCount; row++)
            {
                var key = keys[offset + row];

                for (var column = 0; column < key.Count; column++)
                {
                    DbHelpers.Add(command, SqlDialect.RetentionKeyParameterName(row, column), key[column]);
                }
            }

            deleted += await DbHelpers.ExecuteAsync(command, cancellationToken).ConfigureAwait(false);
        }

        return deleted;
    }

    /// <summary>
    /// Deletes one batch of expired sessions: the copy of each session's content that lives outside
    /// SQL first, the row second.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A plain batch <c>DELETE</c> never reads the ids, so the copy a host's vector store keeps would
    /// stay behind for good: nothing would name its session any more. So the batch is READ, the copies of
    /// its sessions are erased, and only then are its rows deleted, by their keys.
    /// </para>
    /// <para>
    /// A copy that cannot be erased fails the run with the rows still in place, so the next run reads
    /// the same sessions again. The keys are deleted with the target's condition checked again: a session
    /// that was used in between (and so no longer expired) stays. Its earlier turns are no longer
    /// remembered, but the mistake runs in the safe direction: a copy never outlives its session's row.
    /// </para>
    /// </remarks>
    private async ValueTask<int> DeleteSessionBatchAsync(
        RetentionTargetDefinition definition,
        string? tenantId,
        DateTimeOffset cutoff,
        int batchSize,
        CancellationToken cancellationToken)
    {
        var where = Where(definition, tenantId);
        var select = _context.CreateCommand(_dialect.BuildRetentionSelectBatchSql(definition.Table, "id, tenant_id", where));
        _dialect.AddTimestamp(select, "cutoff", cutoff);
        AddTenant(select, tenantId);
        DbHelpers.Add(select, "batchSize", Math.Max(1, batchSize));

        var rows = await DbHelpers
            .ReadListAsync(select, static reader => (Id: reader.GetString(0), Tenant: ReadTenant(reader)), cancellationToken)
            .ConfigureAwait(false);

        return await EraseSessionContentThenDeleteAsync(definition, cutoff, rows, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Erases the copies outside SQL of the given sessions, then deletes the sessions' rows by key.</summary>
    /// <param name="definition">The sessions target.</param>
    /// <param name="cutoff">The cutoff date.</param>
    /// <param name="sessions">The sessions, each with its tenant in the form every tenant parameter is bound in.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The number of rows deleted.</returns>
    private async ValueTask<int> EraseSessionContentThenDeleteAsync(
        RetentionTargetDefinition definition,
        DateTimeOffset cutoff,
        IReadOnlyList<(string Id, string Tenant)> sessions,
        CancellationToken cancellationToken)
    {
        var deleted = 0;

        // 🚨 A session id is unique inside one tenant only: copies are erased, and rows deleted,
        // tenant by tenant, each with the tenant in the filter.
        foreach (var group in sessions.GroupBy(static session => session.Tenant, StringComparer.Ordinal))
        {
            var ids = group.Select(static session => session.Id).ToList();

            foreach (var eraser in _contentErasers)
            {
                await eraser.EraseAsync(group.Key, ids, cancellationToken).ConfigureAwait(false);
            }

            deleted += await DeleteArchivedKeysAsync(
                    definition,
                    Where(definition, group.Key),
                    group.Key,
                    cutoff,
                    [.. ids.Select(static id => (IReadOnlyList<object>)[id])],
                    cancellationToken)
                .ConfigureAwait(false);
        }

        return deleted;
    }

    /// <summary>
    /// Deletes one batch of orphaned attachments: the external content of each
    /// row first, the row second.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A plain batch <c>DELETE</c> never reads <c>external_uri</c>, so the
    /// content stayed in the external store forever. Deleting the row first and
    /// the content second has the opposite defect: a storage failure loses the
    /// only record of where the content is. So the batch is READ, each content
    /// is deleted, and only then is its row deleted by id.
    /// </para>
    /// <para>
    /// A content delete that fails keeps its row and is logged; the batch goes
    /// on with the next row. The batch then returns fewer rows than its size,
    /// the run stops, and the next run retries the kept row. The caller's
    /// cancellation is not a failure and propagates.
    /// </para>
    /// <para>
    /// The row is deleted by id without the predicate again: its content is
    /// already gone, so a row that gained an owner in between still has to go.
    /// </para>
    /// </remarks>
    private async ValueTask<int> DeleteAttachmentBatchAsync(
        IAttachmentStorage storage,
        RetentionTargetDefinition definition,
        string? tenantId,
        DateTimeOffset cutoff,
        int batchSize,
        CancellationToken cancellationToken)
    {
        var select = _context.CreateCommand(
            _dialect.BuildRetentionSelectBatchSql(definition.Table, AttachmentKeyColumns, Where(definition, tenantId)));
        _dialect.AddTimestamp(select, "cutoff", cutoff);
        AddTenant(select, tenantId);
        DbHelpers.Add(select, "batchSize", Math.Max(1, batchSize));

        var rows = await DbHelpers
            .ReadListAsync(
                select,
                static reader => new AttachmentKey(reader.GetGuid(0), reader.GetString(1), DbHelpers.GetNullableString(reader, 2)),
                cancellationToken)
            .ConfigureAwait(false);

        return await DeleteAttachmentRowsAsync(storage, rows, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Deletes attachment rows one by one: the external content first, the row second.</summary>
    /// <param name="storage">The external content store.</param>
    /// <param name="rows">The rows to delete.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The number of rows deleted; a row whose content delete failed is kept and not counted.</returns>
    private async ValueTask<int> DeleteAttachmentRowsAsync(
        IAttachmentStorage storage,
        IReadOnlyList<AttachmentKey> rows,
        CancellationToken cancellationToken)
    {
        var deleted = 0;

        foreach (var row in rows)
        {
            if (row.ExternalUri is { } externalUri)
            {
                try
                {
                    await storage
                        .DeleteAsync(new Uri(externalUri, UriKind.RelativeOrAbsolute), cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (Exception exception) when (OperationCancellation.IsFailure(exception, cancellationToken))
                {
                    LogContentDeleteFailed(exception, row);

                    continue;
                }
            }

            var delete = _context.CreateCommand(_context.Sql.DeleteAttachment);
            DbHelpers.AddTenant(delete, row.TenantId);
            DbHelpers.Add(delete, "id", row.Id);

            var removed = await DbHelpers
                .ReadListAsync(delete, static reader => DbHelpers.GetNullableString(reader, 0), cancellationToken)
                .ConfigureAwait(false);

            deleted += removed.Count;
        }

        return deleted;
    }

    /// <summary>Logs a content delete that failed.</summary>
    /// <param name="exception">The storage failure.</param>
    /// <param name="row">The kept row.</param>
    /// <remarks>
    /// The location is NOT logged: it can be a signed URL. The attachment id and
    /// tenant are enough to find the row.
    /// </remarks>
    private void LogContentDeleteFailed(Exception exception, AttachmentKey row)
    {
        if (_logger.IsEnabled(LogLevel.Warning))
        {
            _logger.LogWarning(
                exception,
                "Retention could not delete the external content of attachment {AttachmentId} (tenant {TenantId}). " +
                "The row is kept so the content can still be found; the next retention run tries again.",
                row.Id,
                row.TenantId);
        }
    }

    /// <inheritdoc />
    public async ValueTask<DateTimeOffset?> FindRowLimitCutoffAsync(
        string target,
        string? tenantId,
        long maxRows,
        CancellationToken cancellationToken = default)
    {
        var definition = RetentionTargetRegistry.Resolve(_dialect, target);
        var sql = _dialect.BuildRetentionFindNthRowCutoffSql(
            definition.Table,
            definition.RowLimitOrderExpression,
            tenantId is null ? null : definition.TenantPredicate);

        var command = _context.CreateCommand(sql);
        _dialect.AddInt64(command, "n", maxRows);
        AddTenant(command, tenantId);

        var rows = await DbHelpers
            .ReadListAsync(command, static reader => DbHelpers.GetNullableTimestamp(reader, 0), cancellationToken)
            .ConfigureAwait(false);

        return rows.Count > 0 ? rows[0] : null;
    }

    /// <summary>Adds the tenant filter to the target's predicate.</summary>
    /// <param name="definition">The target definition.</param>
    /// <param name="tenantId">The tenant; no filter is added when <see langword="null"/>.</param>
    /// <returns>The <c>WHERE</c> predicate to run.</returns>
    private static string Where(RetentionTargetDefinition definition, string? tenantId)
        => tenantId is null
            ? definition.WherePredicate
            : SqlDialect.Combine(definition.WherePredicate, definition.TenantPredicate);

    /// <summary>Binds the tenant parameter only when needed.</summary>
    /// <param name="command">The command.</param>
    /// <param name="tenantId">The tenant; nothing is bound when <see langword="null"/>.</param>
    private static void AddTenant(DbCommand command, string? tenantId)
    {
        if (tenantId is not null)
        {
            DbHelpers.AddTenant(command, tenantId);
        }
    }

    /// <summary>The key of an attachment row and where its external content is.</summary>
    /// <param name="Id">The attachment id.</param>
    /// <param name="TenantId">The owning tenant.</param>
    /// <param name="ExternalUri">The external location; <see langword="null"/> when the content is in the row.</param>
    private sealed record AttachmentKey(Guid Id, string TenantId, string? ExternalUri);

    /// <summary>A row of an archive batch.</summary>
    /// <param name="Row">The row as it goes to the archive.</param>
    /// <param name="Key">The values of the target's key columns, as the provider returned them.</param>
    /// <param name="Tenant">The row's tenant, when it was read.</param>
    /// <param name="Attachment">The attachment key, when the external content goes before the row.</param>
    private sealed record ArchivedRow(ArchiveRow Row, IReadOnlyList<object> Key, string? Tenant, AttachmentKey? Attachment);
}
