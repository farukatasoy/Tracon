using System.Data.Common;
using System.Text.Json;

namespace Tracon;

/// <summary>
/// Stores serialized agent sessions in the SQL database.
/// </summary>
/// <remarks>
/// <para>
/// Session state is the output of Microsoft Agent Framework's <c>SerializeSessionAsync</c>
/// and is treated as <strong>opaque</strong>; its content is not interpreted.
/// </para>
/// <para>
/// <see cref="SessionRecord.StateSchemaVersion"/> and
/// <see cref="SessionRecord.StateMafVersion"/> are carried through
/// verbatim, both on write and on read — this store does not compute or
/// validate them. <see cref="AgentSessionManager"/> stamps them when saving
/// and is the layer that turns a version mismatch into a clear error,
/// because building that error requires comparing the RECORDED version
/// against the version running RIGHT NOW, something only the caller closest
/// to Microsoft Agent Framework knows.
/// </para>
/// <para>
/// <see cref="SessionRecord.OwnerId"/> is the ONE column this store does
/// not carry through verbatim: every write COALESCEs it, so a save that brings
/// no owner leaves an owner already stored in place. Ownership is claimed once
/// and "set → unset" is not a legitimate transition for it. Without that rule a
/// background write with no request behind it -
/// the queued run's worker, a continuation - would clear the column and drop
/// the session out of its owner's listing for good.
/// </para>
/// </remarks>
internal sealed class SqlSessionStore : ISessionStore
{
    private readonly SqlStoreContext _context;
    private readonly SqlQueriesBase _sql;
    private readonly ITenantContext _tenantContext;

    /// <summary>Creates a new session store.</summary>
    /// <param name="context">The store context.</param>
    /// <param name="tenantContext">The tenant context.</param>
    /// <exception cref="ArgumentNullException">One of the dependencies is <see langword="null"/>.</exception>
    public SqlSessionStore(
        SqlStoreContext context,
        ITenantContext tenantContext)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(tenantContext);

        _context = context;
        _sql = context.Sql;
        _tenantContext = tenantContext;
    }

    /// <summary>The gateway for provider-specific behavior.</summary>
    private SqlDialect Dialect => _context.Dialect;

    /// <inheritdoc />
    /// <remarks>
    /// If a record with the same id already exists, <see cref="SessionRecord.CreatedAt"/>
    /// is preserved; "creation time" belongs to the first write. <see cref="InMemorySessionStore"/>
    /// exhibits the same behavior.
    /// </remarks>
    public async ValueTask SaveAsync(SessionRecord record, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);

        await _context.ExecuteUpsertAsync(
            _sql.UpsertSession,
            command =>
            {
                DbHelpers.Add(command, "id", record.Id);
                DbHelpers.AddTenant(command, record.TenantId ?? _tenantContext.TenantId);
                DbHelpers.Add(command, "agent_name", record.AgentName);
                // The `json` column stores the text AS-IS. `jsonb` would reorder keys
                // and invalidate System.Text.Json's `$type` discriminator.
                Dialect.AddJson(command, "state", ProtectedValue.Write(_context, ProtectedColumn.SessionState, record.State.GetRawText()));
                DbHelpers.Add(command, "state_schema_version", record.StateSchemaVersion);
                Dialect.AddText(command, "state_maf_version", record.StateMafVersion);
                Dialect.AddTimestamp(command, "created_at", record.CreatedAt);
                Dialect.AddTimestamp(command, "updated_at", record.UpdatedAt);
                Dialect.AddText(command, "owner_id", record.OwnerId);
            },
            (command, token) => DbHelpers.ExecuteAsync(command, token),
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    /// <remarks>
    /// This is a plain <c>INSERT</c>; a second concurrent call with the same
    /// (tenant_id, id) trips a uniqueness violation, caught by
    /// <see cref="SqlDialect.IsUniqueViolation"/> and converted to
    /// <see langword="false"/> — just like <c>SqlIdempotencyStore.ReserveAsync</c>
    /// does. Unlike <see cref="SaveAsync"/>'s unconditional overwrite, this
    /// ensures only one of two concurrent first requests to the same NEW
    /// session "wins" the session.
    /// </remarks>
    public async ValueTask<bool> TryCreateAsync(SessionRecord record, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);

        try
        {
            await _context.ExecuteStatementAsync(
            _sql.InsertSession,
            command =>
            {
                DbHelpers.Add(command, "id", record.Id);
                DbHelpers.AddTenant(command, record.TenantId ?? _tenantContext.TenantId);
                DbHelpers.Add(command, "agent_name", record.AgentName);
                Dialect.AddJson(command, "state", ProtectedValue.Write(_context, ProtectedColumn.SessionState, record.State.GetRawText()));
                DbHelpers.Add(command, "state_schema_version", record.StateSchemaVersion);
                Dialect.AddText(command, "state_maf_version", record.StateMafVersion);
                Dialect.AddTimestamp(command, "created_at", record.CreatedAt);
                Dialect.AddTimestamp(command, "updated_at", record.UpdatedAt);
                Dialect.AddText(command, "owner_id", record.OwnerId);
            },
            DbHelpers.ExecuteAsync,
            cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (DbException ex) when (Dialect.IsUniqueViolation(ex))
        {
            return false;
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// One conditional <c>UPDATE</c>: the <c>version</c> predicate and the
    /// <c>version + 1</c> assignment live in the SAME statement, so the check
    /// and the write cannot be interleaved. Zero affected rows means the row
    /// is gone, belongs to another tenant, or another writer advanced it
    /// first — all three are the same answer to the caller.
    /// </remarks>
    public async ValueTask<bool> TryUpdateAsync(
        SessionRecord record,
        long expectedVersion,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);

        return await _context.ExecuteStatementAsync(
            _sql.UpdateSessionIfVersionMatches,
            command =>
            {
                DbHelpers.Add(command, "id", record.Id);
                DbHelpers.AddTenant(command, record.TenantId ?? _tenantContext.TenantId);
                DbHelpers.Add(command, "agent_name", record.AgentName);
                Dialect.AddJson(command, "state", ProtectedValue.Write(_context, ProtectedColumn.SessionState, record.State.GetRawText()));
                DbHelpers.Add(command, "state_schema_version", record.StateSchemaVersion);
                Dialect.AddText(command, "state_maf_version", record.StateMafVersion);
                Dialect.AddTimestamp(command, "updated_at", record.UpdatedAt);
                Dialect.AddText(command, "owner_id", record.OwnerId);
                DbHelpers.Add(command, "expected_version", expectedVersion);
            },
            DbHelpers.ExecuteAsync,
            cancellationToken).ConfigureAwait(false) > 0;
    }

    /// <inheritdoc />
    public async ValueTask<SessionRecord?> GetAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sessionId);

        return await _context.ExecuteStatementAsync(
            _sql.SelectSession,
            command =>
            {
                DbHelpers.Add(command, "id", sessionId);
                DbHelpers.AddTenant(command, _tenantContext.TenantId);
            },
            (command, token) => DbHelpers.ReadSingleAsync(command, reader => new SessionRecord
            {
                Id = sessionId,
                AgentName = reader.GetString(0),
                State = ParseState(ProtectedValue.Read(_context, reader.GetString(1))!),
                StateSchemaVersion = reader.GetInt32(2),
                CreatedAt = DbHelpers.GetTimestamp(reader, 3),
                UpdatedAt = DbHelpers.GetTimestamp(reader, 4),
                TenantId = reader.GetString(5),
                Version = reader.GetInt64(6),
                StateMafVersion = DbHelpers.GetNullableString(reader, 7),
                OwnerId = DbHelpers.GetNullableString(reader, 8),
            }, token),
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask<bool> DeleteAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sessionId);

        return await _context.ExecuteStatementAsync(
            _sql.DeleteSession,
            command =>
            {
                DbHelpers.Add(command, "id", sessionId);
                DbHelpers.AddTenant(command, _tenantContext.TenantId);
            },
            DbHelpers.ExecuteAsync,
            cancellationToken).ConfigureAwait(false) > 0;
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<SessionRecord>> QueryAsync(
        SessionQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        return await _context.ExecuteStatementAsync(
            _sql.SelectSessions,
            command =>
            {
                DbHelpers.AddTenant(command, query.TenantId ?? _tenantContext.TenantId);
                Dialect.AddText(command, "agent_name", query.AgentName);
                // 🚨 Typed explicitly like every other optional filter parameter: an
                // untyped NULL is 42P08 on PostgreSQL and a silently wrong plan on SQL
                // Server. The predicate itself lives in the WHERE clause of each
                // dialect's SelectSessions, ahead of paging.
                Dialect.AddText(command, "owner_id", query.OwnerId);
                DbHelpers.Add(command, "skip", Math.Max(query.Skip, 0));
                DbHelpers.Add(command, "take", Math.Max(query.Take, 0));
            },
            (command, token) => DbHelpers.ReadListAsync(command, reader =>
            {
                var id = reader.GetString(0);

                return new SessionRecord
                {
                    Id = id,
                    AgentName = reader.GetString(1),
                    State = ParseState(ProtectedValue.Read(_context, reader.GetString(2))!),
                    StateSchemaVersion = reader.GetInt32(3),
                    CreatedAt = DbHelpers.GetTimestamp(reader, 4),
                    UpdatedAt = DbHelpers.GetTimestamp(reader, 5),
                    TenantId = reader.GetString(6),
                    Version = reader.GetInt64(7),
                    StateMafVersion = DbHelpers.GetNullableString(reader, 8),
                    OwnerId = DbHelpers.GetNullableString(reader, 9),
                };
            }, token),
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Parses the stored state text. Validating <see cref="SessionRecord.StateSchemaVersion"/>
    /// against what this build understands is <see cref="AgentSessionManager"/>'s job, not this
    /// store's: only the caller closest to Microsoft Agent Framework can compare the recorded
    /// generation against the one running right now.
    /// </summary>
    private static JsonElement ParseState(string json)
    {
        // JsonDocument ownership ends here; Clone returns an independent copy.
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}
