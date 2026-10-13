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
/// </remarks>
internal sealed class SqlDataSubjectStore : IDataSubjectStore
{
    private readonly SqlStoreContext _context;
    private readonly SqlDialect _dialect;
    private readonly IAttachmentStorage? _storage;

    /// <summary>Creates a new data subject data-plane store.</summary>
    /// <param name="context">The store context.</param>
    /// <param name="storage">
    /// The external attachment content store, when registered. Without it the
    /// attachment content lives in the row and goes with it.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="context"/> is <see langword="null"/>.</exception>
    public SqlDataSubjectStore(SqlStoreContext context, IAttachmentStorage? storage = null)
    {
        ArgumentNullException.ThrowIfNull(context);

        _context = context;
        _dialect = context.Dialect;
        _storage = storage;
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
    /// The order is: every row <c>DELETE</c> (not committed), then the external
    /// content of the erased attachments, then the audit callback, then the
    /// commit. The content goes before the commit because the rows are the only
    /// record of where it is: a content delete that fails rolls the rows back,
    /// so a repeat call can still find it. The content goes before the audit
    /// callback so that the audit trail never records an erasure that then
    /// rolled back.
    /// </para>
    /// <para>
    /// If the audit callback or the commit fails after the content is gone, the
    /// rows stay and point at nothing; the external store treats a repeat delete
    /// as success, so a repeat call finishes the erasure.
    /// </para>
    /// </remarks>
    private async ValueTask<IReadOnlyDictionary<string, int>> RunEraseTransactionAsync(
        string tenantId,
        DataSubjectScope scope,
        Func<IReadOnlyDictionary<string, int>, CancellationToken, ValueTask>? beforeCommitAsync,
        CancellationToken cancellationToken)
    {
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

                    return counts;
                }

                try
                {
                    await DeleteExternalContentAsync(externalUris, cancellationToken).ConfigureAwait(false);
                    await beforeCommitAsync(counts, cancellationToken).ConfigureAwait(false);
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

                // F-401: a protected column is read back as plaintext, as every store
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
