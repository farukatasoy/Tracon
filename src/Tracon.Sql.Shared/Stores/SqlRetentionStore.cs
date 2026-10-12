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
/// </remarks>
internal sealed class SqlRetentionStore : IRetentionStore
{
    /// <summary>The columns an attachment batch reads before its content is deleted.</summary>
    private const string AttachmentKeyColumns = "id, tenant_id, external_uri";

    private readonly SqlStoreContext _context;
    private readonly SqlDialect _dialect;
    private readonly IAttachmentStorage? _storage;
    private readonly ILogger<SqlRetentionStore> _logger;

    /// <summary>Creates a new retention data-plane store.</summary>
    /// <param name="context">The store context.</param>
    /// <param name="storage">
    /// The external attachment content store, when registered. Without it the
    /// attachment content lives in the row and goes with it.
    /// </param>
    /// <param name="logger">The logger for a content delete that fails; optional.</param>
    /// <exception cref="ArgumentNullException"><paramref name="context"/> is <see langword="null"/>.</exception>
    public SqlRetentionStore(
        SqlStoreContext context,
        IAttachmentStorage? storage = null,
        ILogger<SqlRetentionStore>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(context);

        _context = context;
        _dialect = context.Dialect;
        _storage = storage;
        _logger = logger ?? NullLogger<SqlRetentionStore>.Instance;
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

        return await DbHelpers
            .ReadListAsync(command, static reader => new ArchiveRow { Json = SqlJsonRowWriter.RowToJson(reader) }, cancellationToken)
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

        var sql = _dialect.BuildRetentionDeleteBatchSql(definition.Table, Where(definition, tenantId));

        var command = _context.CreateCommand(sql);
        _dialect.AddTimestamp(command, "cutoff", cutoff);
        AddTenant(command, tenantId);
        DbHelpers.Add(command, "batchSize", Math.Max(1, batchSize));

        return await DbHelpers.ExecuteAsync(command, cancellationToken).ConfigureAwait(false);
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
}
