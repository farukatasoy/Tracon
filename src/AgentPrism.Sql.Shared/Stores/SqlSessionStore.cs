using System.Data.Common;
using System.Text.Json;

namespace AgentPrism;

/// <summary>
/// Stores serialized agent sessions in the SQL database.
/// </summary>
/// <remarks>
/// <para>
/// Session state is the output of Microsoft Agent Framework's <c>SerializeSessionAsync</c>
/// and is treated as <strong>opaque</strong>; its content is not interpreted.
/// </para>
/// <para>
/// <see cref="CurrentSchemaVersion"/> is stored on every row. If the serialization
/// format changes in the future, a newer version reading an older format raises a
/// clear error instead of silently misbehaving.
/// </para>
/// </remarks>
internal sealed class SqlSessionStore : ISessionStore
{
    /// <summary>
    /// The format version of the session state being written.
    /// </summary>
    /// <remarks>
    /// This value only describes how AgentPrism interprets the session row; the
    /// internal structure of the state itself is determined by Microsoft Agent
    /// Framework. If the format changes, the value is incremented and a migration
    /// path is written.
    /// </remarks>
    public const int CurrentSchemaVersion = 1;

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

        var command = CreateCommand(_sql.UpsertSession);
        DbHelpers.Add(command, "id", record.Id);
        DbHelpers.Add(command, "tenant_id", record.TenantId ?? _tenantContext.TenantId);
        DbHelpers.Add(command, "agent_name", record.AgentName);
        // The `json` column stores the text AS-IS. `jsonb` would reorder keys
        // and invalidate System.Text.Json's `$type` discriminator (decision K-027).
        Dialect.AddJson(command, "state", ProtectedValue.Write(_context, ProtectedColumn.SessionState, record.State.GetRawText()));
        DbHelpers.Add(command, "schema_version", CurrentSchemaVersion);
        Dialect.AddTimestamp(command, "created_at", record.CreatedAt);
        Dialect.AddTimestamp(command, "updated_at", record.UpdatedAt);

        await DbHelpers.ExecuteAsync(command, cancellationToken).ConfigureAwait(false);
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

        var command = CreateCommand(_sql.InsertSession);
        DbHelpers.Add(command, "id", record.Id);
        DbHelpers.Add(command, "tenant_id", record.TenantId ?? _tenantContext.TenantId);
        DbHelpers.Add(command, "agent_name", record.AgentName);
        Dialect.AddJson(command, "state", ProtectedValue.Write(_context, ProtectedColumn.SessionState, record.State.GetRawText()));
        DbHelpers.Add(command, "schema_version", CurrentSchemaVersion);
        Dialect.AddTimestamp(command, "created_at", record.CreatedAt);
        Dialect.AddTimestamp(command, "updated_at", record.UpdatedAt);

        try
        {
            await DbHelpers.ExecuteAsync(command, cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (DbException ex) when (Dialect.IsUniqueViolation(ex))
        {
            return false;
        }
    }

    /// <inheritdoc />
    public async ValueTask<SessionRecord?> GetAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sessionId);

        var command = CreateCommand(_sql.SelectSession);
        DbHelpers.Add(command, "id", sessionId);
        DbHelpers.Add(command, "tenant_id", _tenantContext.TenantId);

        return await DbHelpers.ReadSingleAsync(
            command,
            reader => new SessionRecord
            {
                Id = sessionId,
                AgentName = reader.GetString(0),
                State = ReadState(sessionId, ProtectedValue.Read(_context, reader.GetString(1))!, reader.GetInt32(2)),
                CreatedAt = DbHelpers.GetTimestamp(reader, 3),
                UpdatedAt = DbHelpers.GetTimestamp(reader, 4),
                TenantId = reader.GetString(5),
            },
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    /// <remarks>
    /// <see cref="SqlQueriesBase.SelectSessionOwner"/> applies NO FILTER on the
    /// tenant column — unlike <see cref="GetAsync"/>, the record is found even
    /// when its owner does not match the ambient tenant.
    /// </remarks>
    public async ValueTask<string?> GetOwnerTenantIdAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sessionId);

        var command = CreateCommand(_sql.SelectSessionOwner);
        DbHelpers.Add(command, "id", sessionId);

        return await DbHelpers.ReadSingleAsync(
            command,
            static reader => reader.GetString(0),
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask<bool> DeleteAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sessionId);

        var command = CreateCommand(_sql.DeleteSession);
        DbHelpers.Add(command, "id", sessionId);
        DbHelpers.Add(command, "tenant_id", _tenantContext.TenantId);

        return await DbHelpers.ExecuteAsync(command, cancellationToken).ConfigureAwait(false) > 0;
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<SessionRecord>> QueryAsync(
        SessionQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var command = CreateCommand(_sql.SelectSessions);
        DbHelpers.Add(command, "tenant_id", query.TenantId ?? _tenantContext.TenantId);
        Dialect.AddText(command, "agent_name", query.AgentName);
        DbHelpers.Add(command, "skip", Math.Max(query.Skip, 0));
        DbHelpers.Add(command, "take", Math.Max(query.Take, 0));

        return await DbHelpers.ReadListAsync(
            command,
            reader =>
            {
                var id = reader.GetString(0);

                return new SessionRecord
                {
                    Id = id,
                    AgentName = reader.GetString(1),
                    State = ReadState(id, ProtectedValue.Read(_context, reader.GetString(2))!, reader.GetInt32(3)),
                    CreatedAt = DbHelpers.GetTimestamp(reader, 4),
                    UpdatedAt = DbHelpers.GetTimestamp(reader, 5),
                    TenantId = reader.GetString(6),
                };
            },
            cancellationToken).ConfigureAwait(false);
    }

    private DbCommand CreateCommand(string sql) => _context.CreateCommand(sql);

    private static JsonElement ReadState(string sessionId, string json, int schemaVersion)
    {
        if (schemaVersion > CurrentSchemaVersion)
        {
            throw new AgentPrismException(
                $"Session '{sessionId}' was written with format version {schemaVersion}; this AgentPrism version " +
                $"can read up to version {CurrentSchemaVersion}. Update the AgentPrism packages.");
        }

        // JsonDocument ownership ends here; Clone returns an independent copy.
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}
