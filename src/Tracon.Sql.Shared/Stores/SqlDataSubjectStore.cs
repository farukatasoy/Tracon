using System.Data.Common;
using System.Text.Json;

namespace Tracon;

/// <summary>
/// The data-plane implementation of <see cref="IDataSubjectStore"/>: counting,
/// exporting, and erasing one data subject's content.
/// </summary>
/// <remarks>
/// <para>
/// SQL text per target is NOT hand-copied: <see cref="DataSubjectTargetRegistry"/>
/// defines the table/predicate/columns, <see cref="SqlDialect"/> applies the
/// provider-specific templates — the same pattern <c>SqlRetentionStore</c> uses.
/// </para>
/// <para>
/// When <see cref="IAttachmentStorage"/> is registered, an erasure also deletes
/// the external content of every erased attachment, inside the transaction and
/// before the commit. A content delete that fails rolls the erasure back.
/// </para>
/// <para>
/// The same goes for a copy of the subject's sessions that lives outside SQL
/// (<see cref="ISubjectContentEraser"/>, conversation memory in the host's vector store): the preview
/// counts it, the erasure deletes it before the commit, and the export lists it.
/// </para>
/// </remarks>
internal sealed class SqlDataSubjectStore : IDataSubjectStore
{
    private readonly SqlStoreContext _context;
    private readonly SqlDialect _dialect;
    private readonly IAttachmentStorage? _storage;
    private readonly IReadOnlyList<ISubjectContentEraser> _contentErasers;

    /// <summary>Creates a new data subject data-plane store.</summary>
    /// <param name="context">The store context.</param>
    /// <param name="storage">
    /// The external attachment content store, when registered. Without it the
    /// attachment content lives in the row and goes with it.
    /// </param>
    /// <param name="contentErasers">
    /// The stores that keep a copy of a session's content outside SQL. Empty when none is set up.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="context"/> is <see langword="null"/>.</exception>
    public SqlDataSubjectStore(
        SqlStoreContext context,
        IAttachmentStorage? storage = null,
        IEnumerable<ISubjectContentEraser>? contentErasers = null)
    {
        ArgumentNullException.ThrowIfNull(context);

        _context = context;
        _dialect = context.Dialect;
        _storage = storage;
        _contentErasers = contentErasers is null ? [] : [.. contentErasers];
    }

    /// <inheritdoc />
    public ValueTask<IReadOnlyDictionary<string, int>> PreviewAsync(
        string tenantId,
        DataSubjectScope scope,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentNullException.ThrowIfNull(scope);

        return RunEraseTransactionAsync(tenantId, scope, beforeCommitAsync: null, cancellationToken);
    }

    /// <inheritdoc />
    public ValueTask<IReadOnlyDictionary<string, int>> EraseAsync(
        string tenantId,
        DataSubjectScope scope,
        Func<IReadOnlyDictionary<string, int>, CancellationToken, ValueTask> beforeCommitAsync,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(beforeCommitAsync);

        return RunEraseTransactionAsync(tenantId, scope, beforeCommitAsync, cancellationToken);
    }

    /// <summary>
    /// Runs every erasure target's <c>DELETE</c> inside one transaction and either
    /// commits or rolls it back — a preview (<paramref name="beforeCommitAsync"/>
    /// is <see langword="null"/>) always rolls back; a real erasure commits only
    /// if the external content delete and <paramref name="beforeCommitAsync"/> do
    /// not throw.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The order is: the copies of the sessions' content outside the rows (<see cref="ISubjectContentEraser"/>:
    /// conversation memory, the files of a session workspace), then every row <c>DELETE</c> (not committed), then the
    /// external content of the erased attachments, then the audit callback, then the commit. The content of an
    /// attachment goes after its row because the row is the only record of where it is: a content delete that fails
    /// rolls the rows back, so a repeat call can still find it. The audit callback comes last so that the audit
    /// trail never records an erasure that then rolled back.
    /// </para>
    /// <para>
    /// A copy is found by the id of its session, which the scope already holds, so it can go first; and it must
    /// go first. The session workspace keeps its files in the same database (<c>agent_files</c>), and SQLite
    /// lets one connection write at a time: a delete issued on another connection while this transaction holds
    /// its writes waits for the busy timeout and fails. The copies are erased before the transaction takes its first
    /// write. A copy that cannot be erased fails the call with every row in place, and a repeat call finds the
    /// sessions; a copy that is gone when a later step fails is gone for good, and the repeat call erases the rest.
    /// </para>
    /// <para>
    /// If the audit callback or the commit fails after the content is gone, the
    /// rows stay and point at nothing; the external store treats a repeat delete
    /// as success, so a repeat call finishes the erasure.
    /// </para>
    /// <para>
    /// On SQLite (<see cref="SqlDialect.SerializesWriters"/>) this transaction holds
    /// the database's single write lock while the callback runs, so an audit write from a
    /// second connection waited for its own process until it timed out, and the HTTP erasure
    /// never returned. The transaction is lent to the callback
    /// (<see cref="SqlStoreContext.LentTransaction"/>): <see cref="SqlAuditLog"/> writes the
    /// entry on it, and the entry commits or rolls back with the erasure. An
    /// <see cref="IAuditLog"/> the consumer registered in its place writes on its own
    /// connection. On PostgreSQL and SQL Server the audit write keeps its own connection: a
    /// second connection waits only for the rows the erasure touched.
    /// </para>
    /// </remarks>
    private async ValueTask<IReadOnlyDictionary<string, int>> RunEraseTransactionAsync(
        string tenantId,
        DataSubjectScope scope,
        Func<IReadOnlyDictionary<string, int>, CancellationToken, ValueTask>? beforeCommitAsync,
        CancellationToken cancellationToken)
    {
        // The copies outside the rows go before the transaction takes a write (see the remarks). A preview
        // deletes nothing; it counts them after the rows are counted and rolled back.
        var contentCounts = new Dictionary<string, int>(StringComparer.Ordinal);

        if (beforeCommitAsync is not null)
        {
            foreach (var eraser in _contentErasers)
            {
                contentCounts[eraser.Target] = await eraser.EraseAsync(tenantId, scope.SessionIds, cancellationToken).ConfigureAwait(false);
            }
        }

        var connection = await _context.DataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);

        await using (connection.ConfigureAwait(false))
        {
            var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

            await using (transaction.ConfigureAwait(false))
            {
                var counts = new Dictionary<string, int>(StringComparer.Ordinal);
                var externalUris = new List<string>();

                foreach (var target in DataSubjectTargetRegistry.EraseTargets)
                {
                    var (table, predicate, _) = DataSubjectTargetRegistry.Resolve(_dialect, target);

                    // The attachment DELETE returns each row's external location in
                    // the SAME statement, so no row can be erased without its
                    // location being seen.
                    if (_storage is not null
                        && string.Equals(target, DataSubjectTargetRegistry.Attachments, StringComparison.Ordinal))
                    {
                        var returning = _context.CreateCommand(
                            _dialect.BuildDataSubjectDeleteReturningSql(table, predicate, "external_uri"),
                            connection,
                            transaction);
                        BindScope(returning, tenantId, scope);

                        var locations = await DbHelpers
                            .ReadListAsync(returning, static reader => DbHelpers.GetNullableString(reader, 0), cancellationToken)
                            .ConfigureAwait(false);

                        counts[target] = locations.Count;
                        externalUris.AddRange(locations.OfType<string>());

                        continue;
                    }

                    var sql = _dialect.BuildDataSubjectDeleteSql(table, predicate);

                    var command = _context.CreateCommand(sql, connection, transaction);
                    BindScope(command, tenantId, scope);

                    counts[target] = await DbHelpers.ExecuteAsync(command, cancellationToken).ConfigureAwait(false);
                }

                if (beforeCommitAsync is null)
                {
                    await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);

                    // A preview counts the copies outside SQL after the rollback: it deletes nothing there.
                    foreach (var eraser in _contentErasers)
                    {
                        counts[eraser.Target] = await eraser.CountAsync(tenantId, scope.SessionIds, cancellationToken).ConfigureAwait(false);
                    }

                    return counts;
                }

                foreach (var (target, count) in contentCounts)
                {
                    counts[target] = count;
                }

                try
                {
                    await DeleteExternalContentAsync(externalUris, cancellationToken).ConfigureAwait(false);

                    // On a database that lets one connection write at a time, the audit
                    // write joins this transaction (see the remarks). The loan is opened in
                    // this body, so it flows down into the callback.
                    using (_dialect.SerializesWriters ? _context.LendTransaction(connection, transaction) : null)
                    {
                        await beforeCommitAsync(counts, cancellationToken).ConfigureAwait(false);
                    }
                }
                catch
                {
                    await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);

                    throw;
                }

                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

                return counts;
            }
        }
    }

    /// <summary>Deletes the external content of the erased attachments, one location at a time.</summary>
    /// <param name="externalUris">The locations the attachment <c>DELETE</c> returned.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The completion task. The first failure propagates and fails the erasure.</returns>
    private async ValueTask DeleteExternalContentAsync(IReadOnlyList<string> externalUris, CancellationToken cancellationToken)
    {
        if (_storage is null)
        {
            return;
        }

        foreach (var externalUri in externalUris)
        {
            await _storage
                .DeleteAsync(new Uri(externalUri, UriKind.RelativeOrAbsolute), cancellationToken)
                .ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async ValueTask<DataSubjectExport> ExportAsync(
        string tenantId,
        DataSubjectScope scope,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentNullException.ThrowIfNull(scope);

        using var buffer = new MemoryStream();

        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("tenantId", tenantId);
            writer.WriteString("exportedAt", DateTimeOffset.UtcNow);

            foreach (var target in DataSubjectTargetRegistry.ExportTargets)
            {
                var (table, predicate, columns) = DataSubjectTargetRegistry.Resolve(_dialect, target);
                var sql = _dialect.BuildDataSubjectSelectSql(table, columns, predicate);

                // A protected column is read back as plaintext, as every store
                // read does; the stored envelope is never handed to the subject.
                var readValue = ProtectedValue.RowReader(_context, DataSubjectTargetRegistry.ProtectedExportColumns(target));

                var command = _context.CreateCommand(sql);
                BindScope(command, tenantId, scope);

                writer.WriteStartArray(target);

                await using (command.ConfigureAwait(false))
                {
                    var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

                    await using (reader.ConfigureAwait(false))
                    {
                        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                        {
                            SqlJsonRowWriter.WriteRow(writer, reader, readValue);
                        }
                    }
                }

                writer.WriteEndArray();
            }

            foreach (var eraser in _contentErasers)
            {
                await eraser.WriteExportAsync(tenantId, scope.SessionIds, writer, cancellationToken).ConfigureAwait(false);
            }

            writer.WriteEndObject();
        }

        return new DataSubjectExport { Json = System.Text.Encoding.UTF8.GetString(buffer.ToArray()) };
    }

    /// <summary>
    /// Binds the four parameters every target predicate may reference. All four
    /// are always bound, even when a specific target's predicate does not
    /// reference one of them (see <see cref="DataSubjectTargetRegistry"/>).
    /// </summary>
    private void BindScope(DbCommand command, string tenantId, DataSubjectScope scope)
    {
        DbHelpers.AddTenant(command, tenantId);
        _dialect.AddTextArray(command, "session_ids", scope.SessionIds);
        _dialect.AddUuidArray(command, "run_ids", scope.RunIds);
        _dialect.AddUuidArray(command, "conversation_ids", scope.ConversationIds);
    }
}
