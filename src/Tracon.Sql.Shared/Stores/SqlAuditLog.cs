using System.Collections.Concurrent;
using System.Data.Common;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Tracon;

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
/// <para>
/// <strong>Signing.</strong> When the host holds an active key (<c>Tracon:Audit:ChainKeyId</c>), the hash is an
/// HMAC-SHA256 under that key and the key id is written to <c>key_id</c>; without one the entry is written with the
/// plain SHA-256 it always had. The key VALUE never reaches the database. Turning the key off does not stop the
/// write (an audit entry must not be lost); the next verification reports the unsigned row as a downgrade, and
/// the first such write per tenant logs a warning.
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
    private readonly ITenantContext _tenantContext;
    private readonly AuditChainKeyRing _keys;
    private readonly ILogger _logger;

    // One entry per tenant that has already been warned about, for the life of the process. The set is as
    // large as the number of tenants that write while the key is off, which is the number of tenants.
    private readonly ConcurrentDictionary<string, byte> _warnedTenants = new(StringComparer.Ordinal);

    /// <summary>Creates a new audit trail ledger.</summary>
    /// <param name="context">The store context.</param>
    /// <param name="tenantContext">
    /// Resolves the AMBIENT tenant for <see cref="QueryAsync"/> and
    /// <see cref="VerifyChainAsync"/> when their query's <c>TenantId</c> is
    /// <see langword="null"/> or empty — the same pattern <c>SqlRunStore</c> uses.
    /// </param>
    /// <param name="keys">
    /// The keys that sign and verify the chain. Defaults to none, which writes and checks every entry without a key.
    /// </param>
    /// <param name="logger">The logger for the key-off warning. Defaults to a logger that writes nowhere.</param>
    /// <exception cref="ArgumentNullException">One of the dependencies is <see langword="null"/>.</exception>
    public SqlAuditLog(
        SqlStoreContext context,
        ITenantContext tenantContext,
        AuditChainKeyRing? keys = null,
        ILogger<SqlAuditLog>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(tenantContext);

        _context = context;
        _sql = context.Sql;
        _tenantContext = tenantContext;
        _keys = keys ?? AuditChainKeyRing.None;
        _logger = logger ?? NullLogger<SqlAuditLog>.Instance;
    }

    /// <summary>The gateway for provider-specific behavior.</summary>
    private SqlDialect Dialect => _context.Dialect;

    /// <inheritdoc />
    public async ValueTask WriteAsync(AuditEntry entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);

        var id = entry.Id == Guid.Empty ? TraconId.NewId() : entry.Id;

        // 🚨 Truncated ONCE, up front, and this same value is both hashed AND
        // written to `created_at` — see AuditChainHasher.TruncateToMicroseconds.
        var createdAt = AuditChainHasher.TruncateToMicroseconds(entry.CreatedAt);

        for (var attempt = 1; ; attempt++)
        {
            var tail = await ReadTailAsync(entry.TenantId, cancellationToken).ConfigureAwait(false);
            var previousHash = tail?.Hash;

            WarnWhenTheKeyIsOffAfterASignedEntry(entry.TenantId, tail);

            var hash = AuditChainHasher.ComputeHash(
                previousHash,
                entry.TenantId,
                entry.Actor,
                entry.Action,
                entry.Entity,
                entry.Before,
                entry.After,
                createdAt,
                _keys.Active);

            var command = CreateCommand(_sql.InsertAuditEntry);
            DbHelpers.Add(command, "id", id);
            DbHelpers.AddTenant(command, entry.TenantId);
            AddNullableText(command, "actor", entry.Actor);
            DbHelpers.Add(command, "action", entry.Action);
            DbHelpers.Add(command, "entity", entry.Entity);
            AddNullableJson(command, "before", entry.Before);
            AddNullableJson(command, "after", entry.After);
            Dialect.AddTimestamp(command, "created_at", createdAt);
            AddNullableText(command, "prev_hash", previousHash);
            AddNullableText(command, "hash", hash);
            AddNullableText(command, "key_id", _keys.Active?.Id);

            try
            {
                await DbHelpers.ExecuteAsync(command, cancellationToken).ConfigureAwait(false);

                return;
            }
            catch (DbException ex) when (
                (Dialect.IsUniqueViolation(ex) || Dialect.IsDeadlock(ex))
                && attempt < MaxChainWriteAttempts)
            {
                // Another writer won the race for this tenant's last hash. A
                // small random delay before retrying breaks the lockstep that
                // would otherwise form between every writer racing on the
                // same tenant (see MaxRetryDelayMilliseconds).
                //
                // 🚨 A DEADLOCK is the same race under another error code and it
                // used to escape this loop as a raw DbException — the audit entry
                // was then LOST, which is the one outcome an audit trail may not
                // have. Retrying is safe because every attempt re-reads the last
                // hash and re-derives `hash` from it; nothing from the failed
                // attempt is carried over. Found by the class scan of the
                // MigrationRunner deadlock (2026-08-21).
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
        DbHelpers.AddTenant(command, ResolveTenantId(query.TenantId));
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
        DbHelpers.AddTenant(command, ResolveTenantId(query.TenantId));
        Dialect.AddTimestamp(command, "started_after", query.After);
        Dialect.AddTimestamp(command, "started_before", query.Before);

        var entries = await DbHelpers.ReadListAsync(command, ReadEntry, cancellationToken).ConfigureAwait(false);

        return AuditChainWalker.Verify(entries, hasLowerBound: query.After is not null, keys: _keys);
    }

    /// <summary>
    /// Resolves the AMBIENT fallback (see <see cref="IAuditLog"/>'s remarks): an empty
    /// or missing override tenant resolves to the caller's OWN tenant, never to "every
    /// tenant".
    /// </summary>
    private string ResolveTenantId(string? tenantId) => tenantId is { Length: > 0 } ? tenantId : _tenantContext.TenantId;

    private async ValueTask<ChainTail?> ReadTailAsync(string tenantId, CancellationToken cancellationToken)
    {
        var command = CreateCommand(_sql.SelectLastAuditHash);
        DbHelpers.AddTenant(command, tenantId);

        return await DbHelpers.ReadSingleAsync(
            command,
            static reader => new ChainTail(
                DbHelpers.GetNullableString(reader, 0),
                DbHelpers.GetNullableString(reader, 1)),
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Logs, once per tenant per process, that an entry is about to follow a signed one without a signature.
    /// </summary>
    /// <remarks>
    /// The write goes ahead: refusing it would lose an audit entry, and losing one is worse than writing it
    /// unsigned. The cost is visible later, as a downgrade at the next verification, so the operator is told now.
    /// The retry loop may run this more than once for one write; the set makes that harmless.
    /// </remarks>
    private void WarnWhenTheKeyIsOffAfterASignedEntry(string tenantId, ChainTail? tail)
    {
        if (_keys.Active is not null || tail?.KeyId is not { } signedWith)
        {
            return;
        }

        if (_warnedTenants.TryAdd(tenantId, 0) && _logger.IsEnabled(LogLevel.Warning))
        {
            _logger.LogWarning(
                "The audit chain of tenant {TenantId} was signed with key {KeyId}, but {Setting} is not set. " +
                "The entry is written without a signature and the chain will verify as Broken from this entry on.",
                tenantId,
                signedWith,
                AuditChainKeyRing.ChainKeyIdSetting);
        }
    }

    /// <summary>The newest row of a tenant's chain: the hash the next entry links to and the key that signed it.</summary>
    /// <param name="Hash">The newest row's hash, or <see langword="null"/> for a row written before the chain existed.</param>
    /// <param name="KeyId">The key id the newest row names, or <see langword="null"/> when it was written without one.</param>
    private sealed record ChainTail(string? Hash, string? KeyId);

    /// <summary>
    /// Creates a command on the transaction a store lent to the current flow, or on a
    /// connection of its own when none is lent.
    /// </summary>
    /// <remarks>
    /// On SQLite the data subject erasure calls the audit write while it holds the
    /// database's single write lock. A write from a second connection waited for that lock
    /// until the command timed out, and the deadlock retry below repeated the wait up to
    /// <see cref="MaxChainWriteAttempts"/> times. On the lent transaction the entry commits
    /// or rolls back with the erasure, and no other writer can take the chain's last hash
    /// while the lock is held, so the insert does not race.
    /// </remarks>
    private DbCommand CreateCommand(string sql)
        => _context.LentTransaction is { } lent
            ? _context.CreateCommand(sql, lent.Connection, lent.Transaction)
            : _context.CreateCommand(sql);

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
            KeyId = DbHelpers.GetNullableString(reader, 10),
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
