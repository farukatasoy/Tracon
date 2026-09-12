using System.Data.Common;
using System.Text.Json;

namespace Tracon;

/// <summary>
/// The data-plane implementation of <see cref="IDataSubjectStore"/>: counting,
/// exporting, and erasing one data subject's content.
/// </summary>
/// <remarks>
/// SQL text per target is NOT hand-copied: <see cref="DataSubjectTargetRegistry"/>
/// defines the table/predicate/columns, <see cref="SqlDialect"/> applies the
/// provider-specific templates — the same pattern <c>SqlRetentionStore</c> uses.
/// </remarks>
internal sealed class SqlDataSubjectStore : IDataSubjectStore
{
    private readonly SqlStoreContext _context;
    private readonly SqlDialect _dialect;

    /// <summary>Creates a new data subject data-plane store.</summary>
    /// <param name="context">The store context.</param>
    /// <exception cref="ArgumentNullException"><paramref name="context"/> is <see langword="null"/>.</exception>
    public SqlDataSubjectStore(SqlStoreContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        _context = context;
        _dialect = context.Dialect;
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
    /// if <paramref name="beforeCommitAsync"/> does not throw.
    /// </summary>
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

                foreach (var target in DataSubjectTargetRegistry.EraseTargets)
                {
                    var (table, predicate, _) = DataSubjectTargetRegistry.Resolve(_dialect, target);
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
                            SqlJsonRowWriter.WriteRow(writer, reader);
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
        DbHelpers.Add(command, "tenant_id", tenantId);
        _dialect.AddTextArray(command, "session_ids", scope.SessionIds);
        _dialect.AddUuidArray(command, "run_ids", scope.RunIds);
        _dialect.AddUuidArray(command, "conversation_ids", scope.ConversationIds);
    }
}
