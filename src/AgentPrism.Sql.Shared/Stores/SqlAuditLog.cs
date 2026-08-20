using System.Data.Common;

namespace AgentPrism;

/// <summary>Ledger that stores audit trail records in the SQL database.</summary>
/// <remarks>
/// <para>
/// A write is matched by a read that goes through the <c>tenant_id, created_at</c>
/// index (<c>audit_log_tenant_created_idx</c>, set up in 0001). No migration was
/// needed for the base columns; the original schema is sufficient.
/// </para>
/// <para>
/// <strong>Hash chain.</strong> <see cref="WriteAsync"/> reads the
/// tenant's current last hash, computes the new entry's hash
/// (<see cref="AuditChainHasher"/>), and inserts. A concurrent writer racing on
/// the SAME tenant can read the SAME last hash; the migration's
/// <c>audit_log_tenant_prev_hash_idx</c> unique index then rejects the loser's
/// insert (a duplicate <c>(tenant_id, prev_hash)</c> pair) and this method
/// retries with the now-current last hash — the same
/// read-then-insert-with-retry pattern <c>SqlIdempotencyStore</c> uses for its
/// own uniqueness race, just retried instead of reported. No cross-connection
/// lock (advisory/applock) is needed: the constraint IS the serialization point.
/// </para>
/// </remarks>
internal sealed class SqlAuditLog : IAuditLog
{
    /// <summary>
    /// Bound for the retry loop. Only a genuine write storm on ONE tenant
    /// exhausts this; each retry is a single indexed read plus a rejected
    /// insert, both cheap.
    /// </summary>
    private const int MaxChainWriteAttempts = 50;

    /// <summary>
    /// Upper bound of the random retry delay. Measured against real
    /// PostgreSQL: WITHOUT this jitter, 20 fully concurrent writers on one
    /// tenant regularly exhausted 100 retries — every loser retries
    /// immediately, all losers hit the SAME "current last hash" at nearly the
    /// SAME instant again, and only one of them can win each round, so the
    /// group stays roughly lockstep instead of converging. A small random
    /// delay desynchronizes them; with it, 20 writers settle within single
    /// digit attempts.
    /// </summary>
    private const int MaxRetryDelayMilliseconds = 15;

    private readonly SqlStoreContext _context;
    private readonly SqlQueriesBase _sql;

    /// <summary>Creates a new audit trail ledger.</summary>
    /// <param name="context">The store context.</param>
    /// <exception cref="ArgumentNullException">One of the dependencies is <see langword="null"/>.</exception>
    public SqlAuditLog(SqlStoreContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        _context = context;
        _sql = context.Sql;
    }

    /// <summary>The gateway for provider-specific behavior.</summary>
    private SqlDialect Dialect => _context.Dialect;

    /// <inheritdoc />
    public async ValueTask WriteAsync(AuditEntry entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);

        var id = entry.Id == Guid.Empty ? AgentPrismId.NewId() : entry.Id;

        // 🚨 Truncated ONCE, up front, and this same value is both hashed AND
        // written to `created_at` — see AuditChainHasher.TruncateToMicroseconds.
        var createdAt = AuditChainHasher.TruncateToMicroseconds(entry.CreatedAt);

        for (var attempt = 1; ; attempt++)
        {
            var previousHash = await ReadLastHashAsync(entry.TenantId, cancellationToken).ConfigureAwait(false);
            var hash = AuditChainHasher.ComputeHash(
                previousHash,
                entry.TenantId,
                entry.Actor,
                entry.Action,
                entry.Entity,
                entry.Before,
                entry.After,
                createdAt);

            var command = CreateCommand(_sql.InsertAuditEntry);
            DbHelpers.Add(command, "id", id);
            DbHelpers.Add(command, "tenant_id", entry.TenantId);
            AddNullableText(command, "actor", entry.Actor);
            DbHelpers.Add(command, "action", entry.Action);
            DbHelpers.Add(command, "entity", entry.Entity);
            AddNullableJson(command, "before", entry.Before);
            AddNullableJson(command, "after", entry.After);
            Dialect.AddTimestamp(command, "created_at", createdAt);
            AddNullableText(command, "prev_hash", previousHash);
            AddNullableText(command, "hash", hash);

            try
            {
                await DbHelpers.ExecuteAsync(command, cancellationToken).ConfigureAwait(false);

                return;
            }
            catch (DbException ex) when (Dialect.IsUniqueViolation(ex) && attempt < MaxChainWriteAttempts)
            {
                // Another writer won the race for this tenant's last hash. A
                // small random delay before retrying breaks the lockstep that
                // would otherwise form between every writer racing on the
                // same tenant (see MaxRetryDelayMilliseconds).
                await Task.Delay(
                    TimeSpan.FromMilliseconds(Random.Shared.Next(1, MaxRetryDelayMilliseconds)),
                    cancellationToken).ConfigureAwait(false);
            }
        }
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<AuditEntry>> QueryAsync(
        AuditQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var command = CreateCommand(_sql.SelectAuditLog);
        DbHelpers.Add(command, "tenant_id", query.TenantId ?? string.Empty);
        AddNullableText(command, "actor", query.Actor);
        AddNullableText(command, "action", query.Action);
        AddNullableText(command, "entity", query.Entity);
        Dialect.AddTimestamp(command, "started_after", query.After);
        Dialect.AddTimestamp(command, "started_before", query.Before);
        DbHelpers.Add(command, "take", Math.Max(query.Limit, 0));

        return await DbHelpers.ReadListAsync(command, ReadEntry, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask<AuditChainVerification> VerifyChainAsync(
        AuditChainQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var command = CreateCommand(_sql.SelectAuditChain);
        DbHelpers.Add(command, "tenant_id", query.TenantId ?? string.Empty);
        Dialect.AddTimestamp(command, "started_after", query.After);
        Dialect.AddTimestamp(command, "started_before", query.Before);

        var entries = await DbHelpers.ReadListAsync(command, ReadEntry, cancellationToken).ConfigureAwait(false);

        return AuditChainWalker.Verify(entries, hasLowerBound: query.After is not null);
    }

    private async ValueTask<string?> ReadLastHashAsync(string tenantId, CancellationToken cancellationToken)
    {
        var command = CreateCommand(_sql.SelectLastAuditHash);
        DbHelpers.Add(command, "tenant_id", tenantId);

        var result = await DbHelpers.ExecuteScalarAsync(command, cancellationToken).ConfigureAwait(false);

        return result as string;
    }

    private DbCommand CreateCommand(string sql) => _context.CreateCommand(sql);

    private static AuditEntry ReadEntry(DbDataReader reader)
        => new()
        {
            Id = reader.GetGuid(0),
            TenantId = reader.GetString(1),
            Actor = DbHelpers.GetNullableString(reader, 2),
            Action = reader.GetString(3),
            Entity = reader.GetString(4),
            Before = DbHelpers.GetNullableString(reader, 5),
            After = DbHelpers.GetNullableString(reader, 6),
            CreatedAt = DbHelpers.GetTimestamp(reader, 7),
            PreviousHash = DbHelpers.GetNullableString(reader, 8),
            Hash = DbHelpers.GetNullableString(reader, 9),
        };

    private void AddNullableText(DbCommand command, string name, string? value)
        => Dialect.AddText(command, name, value);

    /// <summary>
    /// Binds <c>before</c>/<c>after</c> with <see cref="SqlDialect.AddJson"/>, NOT
    /// <see cref="SqlDialect.AddJsonb"/> — measured against real PostgreSQL:
    /// <c>jsonb</c> does not round-trip the ORIGINAL TEXT (it reorders keys and
    /// changes whitespace, the same reason <c>sessions.state</c> is
    /// plain <c>json</c>), so a hash computed from the text as written no longer
    /// matched the text read back and every entry misreported as
    /// <see cref="AuditChainStatus.Broken"/>. Migration 0031 changes the column
    /// type from <c>jsonb</c> to <c>json</c> to match.
    /// </summary>
    private void AddNullableJson(DbCommand command, string name, string? value)
        => Dialect.AddJson(command, name, value);
}
