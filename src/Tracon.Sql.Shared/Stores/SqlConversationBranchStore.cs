using System.Data.Common;

namespace Tracon;

/// <summary>SQL store that branches conversations by copying them.</summary>
/// <remarks>
/// <para>
/// The copy happens in a single transaction: the branch conversation and its
/// items are created together or not at all. A half-formed branch would mean
/// a session with incomplete history, and would silently produce wrong answers.
/// </para>
/// <para>
/// Items are not copied with a <strong>single</strong> <c>INSERT … SELECT</c>
/// statement; they are read and written row by row. The rationale was measured:
/// each row's new item id must be a fresh uuid v7, and none of the
/// three dialects has a common uuid v7 generator (PostgreSQL's
/// <c>gen_random_uuid()</c> produces v4, SQL Server's <c>NEWID()</c> cannot be
/// sorted, SQLite has no built-in at all). Instead of writing three separate
/// provider-specific statements, the id is generated in the application; since
/// the write stays inside a single transaction, durability is unaffected.
/// </para>
/// </remarks>
internal sealed class SqlConversationBranchStore : IConversationBranchStore
{
    private readonly SqlStoreContext _context;
    private readonly SqlQueriesBase _sql;

    /// <summary>Creates a new SQL branching store.</summary>
    /// <param name="context">The store context.</param>
    /// <exception cref="ArgumentNullException"><paramref name="context"/> is <see langword="null"/>.</exception>
    public SqlConversationBranchStore(SqlStoreContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        _context = context;
        _sql = context.Sql;
    }

    private SqlDialect Dialect => _context.Dialect;

    /// <inheritdoc />
    public async ValueTask<ConversationBranch?> BranchAsync(
        string tenantId,
        Guid parentConversationId,
        long? upToSequence,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);

        var newConversationId = TraconId.NewId();
        var now = DateTimeOffset.UtcNow;

        var connection = await _context.DataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);

        await using (connection.ConfigureAwait(false))
        {
            var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

            await using (transaction.ConfigureAwait(false))
            {
                var branchPoint = await ReadBranchPointAsync(
                    connection,
                    transaction,
                    parentConversationId,
                    upToSequence,
                    cancellationToken).ConfigureAwait(false);

                // The conversation row is copied from the source. If the
                // source does not exist or belongs to another tenant, the
                // SELECT returns empty and no row is written; the affected
                // row count reports this in a single query.
                var insert = _context.CreateCommand(_sql.InsertBranchConversation, connection, transaction);
                Dialect.AddUuid(insert, "id", newConversationId);
                Dialect.AddUuid(insert, "parent_conversation_id", parentConversationId);
                DbHelpers.AddTenant(insert, tenantId);
                Dialect.AddTimestamp(insert, "now", now);
                Dialect.AddInt64(insert, "branch_from_seq", branchPoint.LastSequence);

                if (await DbHelpers.ExecuteAsync(insert, cancellationToken).ConfigureAwait(false) == 0)
                {
                    return null;
                }

                var copied = await CopyItemsAsync(
                    connection,
                    transaction,
                    parentConversationId,
                    newConversationId,
                    upToSequence,
                    cancellationToken).ConfigureAwait(false);

                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

                return new ConversationBranch(newConversationId, branchPoint.LastSequence, copied);
            }
        }
    }

    private async ValueTask<BranchPoint> ReadBranchPointAsync(
        DbConnection connection,
        DbTransaction transaction,
        Guid parentConversationId,
        long? upToSequence,
        CancellationToken cancellationToken)
    {
        var command = _context.CreateCommand(_sql.SelectConversationBranchPoint, connection, transaction);
        Dialect.AddUuid(command, "conversation_id", parentConversationId);
        Dialect.AddInt64(command, "up_to_sequence", upToSequence);

        var point = await DbHelpers
            .ReadSingleAsync(
                command,
                static reader => new BranchPoint(reader.GetInt64(0), reader.GetInt64(1)),
                cancellationToken)
            .ConfigureAwait(false);

        // The aggregate query always returns one row; we still leave a
        // defensive default (empty conversation: -1 and 0).
        return point ?? new BranchPoint(-1, 0);
    }

    private async ValueTask<int> CopyItemsAsync(
        DbConnection connection,
        DbTransaction transaction,
        Guid parentConversationId,
        Guid newConversationId,
        long? upToSequence,
        CancellationToken cancellationToken)
    {
        var select = _context.CreateCommand(_sql.SelectConversationItemsForBranch, connection, transaction);
        Dialect.AddUuid(select, "conversation_id", parentConversationId);
        Dialect.AddInt64(select, "up_to_sequence", upToSequence);

        var items = await DbHelpers
            .ReadListAsync(
                select,
                static reader => new CopiedItem(
                    reader.GetInt64(0),
                    reader.GetString(1),
                    DbHelpers.GetTimestamp(reader, 2)),
                cancellationToken)
            .ConfigureAwait(false);

        foreach (var item in items)
        {
            var insert = _context.CreateCommand(_sql.InsertConversationItem, connection, transaction);
            Dialect.AddUuid(insert, "id", TraconId.NewId());
            Dialect.AddUuid(insert, "conversation_id", newConversationId);
            Dialect.AddInt64(insert, "seq", item.Sequence);

            // 🚨 The text is carried over AS-IS; it is not re-serialized. A
            // round of deserialize/serialize could move the `$type`
            // discriminator's position and make the branch's history
            // unreadable (K-027).
            Dialect.AddJson(insert, "item", item.Item);
            Dialect.AddTimestamp(insert, "created_at", item.CreatedAt);

            await DbHelpers.ExecuteAsync(insert, cancellationToken).ConfigureAwait(false);
        }

        return items.Count;
    }

    private sealed record BranchPoint(long LastSequence, long ItemCount);

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Auto)]
    private readonly record struct CopiedItem(long Sequence, string Item, DateTimeOffset CreatedAt);
}
