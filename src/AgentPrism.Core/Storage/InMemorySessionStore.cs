using System.Collections.Concurrent;

namespace AgentPrism;

/// <summary>
/// A store that keeps sessions in process memory.
/// </summary>
/// <remarks>
/// <para>
/// The limits are the same as <see cref="InMemoryAgentDefinitionStore"/>:
/// process lifetime and a single node. Use <c>AgentPrism.PostgreSql</c> in production.
/// </para>
/// <para>
/// Sessions are separated <strong>per tenant</strong>; the tenant is read
/// from <see cref="ITenantContext"/>.
/// </para>
/// </remarks>
internal sealed class InMemorySessionStore : ISessionStore
{
    private readonly ConcurrentDictionary<(string TenantId, string Id), SessionRecord> _sessions = new();
    private readonly ITenantContext _tenantContext;

    /// <summary>Creates a new in-memory session store.</summary>
    /// <param name="tenantContext">
    /// The current tenant's context. If not given, the store behaves as single-tenant.
    /// </param>
    public InMemorySessionStore(ITenantContext? tenantContext = null)
        => _tenantContext = tenantContext ?? FixedTenantContext.Default;

    /// <inheritdoc />
    /// <remarks>
    /// If a record with the same identity exists, <see cref="SessionRecord.CreatedAt"/>
    /// is preserved. The "creation time" belongs to the first write; the
    /// persistent store shows the same behavior.
    /// </remarks>
    public ValueTask SaveAsync(SessionRecord record, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);

        // SAME rule as the SQL implementation: if a record carries its own
        // tenant, that one wins (scheduled jobs may write on behalf of
        // another tenant).
        var tenantId = record.TenantId ?? _tenantContext.TenantId;

        _sessions.AddOrUpdate(
            (tenantId, record.Id),
            static (_, incoming) => incoming,
            // 🚨 The version ADVANCES on an unconditional overwrite instead of
            // being taken from `incoming`. SaveAsync does not check the
            // caller's version, so letting the caller's (possibly stale)
            // value land would let a later TryUpdateAsync match a generation
            // that no longer describes the stored state.
            static (_, existing, incoming) => incoming with
            {
                CreatedAt = existing.CreatedAt,
                Version = existing.Version + 1,
            },
            record with { TenantId = tenantId, Version = 1 });

        return default;
    }

    /// <inheritdoc />
    /// <remarks>
    /// <see cref="ConcurrentDictionary{TKey, TValue}.TryAdd(TKey, TValue)"/> is atomic:
    /// of two concurrent calls with the same identity, only one returns
    /// <see langword="true"/>.
    /// </remarks>
    public ValueTask<bool> TryCreateAsync(SessionRecord record, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);

        var tenantId = record.TenantId ?? _tenantContext.TenantId;

        return new ValueTask<bool>(
            _sessions.TryAdd((tenantId, record.Id), record with { TenantId = tenantId, Version = 1 }));
    }

    /// <inheritdoc />
    /// <remarks>
    /// <see cref="ConcurrentDictionary{TKey, TValue}.TryUpdate"/> compares the
    /// CURRENT value by reference before swapping, so the check and the write
    /// are one atomic step. A read-then-write pair here would carry exactly
    /// the defect this member exists to close.
    /// </remarks>
    public ValueTask<bool> TryUpdateAsync(
        SessionRecord record,
        long expectedVersion,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);

        var tenantId = record.TenantId ?? _tenantContext.TenantId;
        var key = (tenantId, record.Id);

        if (!_sessions.TryGetValue(key, out var existing) || existing.Version != expectedVersion)
        {
            return new ValueTask<bool>(false);
        }

        // CreatedAt belongs to the first write, the same rule SaveAsync applies.
        var updated = record with
        {
            TenantId = tenantId,
            CreatedAt = existing.CreatedAt,
            Version = expectedVersion + 1,
        };

        return new ValueTask<bool>(_sessions.TryUpdate(key, updated, existing));
    }

    /// <inheritdoc />
    public ValueTask<SessionRecord?> GetAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sessionId);

        _sessions.TryGetValue((_tenantContext.TenantId, sessionId), out var record);
        return new ValueTask<SessionRecord?>(record);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Because the key is a <c>(TenantId, Id)</c> composite, this cannot be
    /// filtered by the ambient tenant; it scans across all tenants for the
    /// record carrying this identity.
    /// </remarks>
    public ValueTask<string?> GetOwnerTenantIdAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sessionId);

        foreach (var key in _sessions.Keys)
        {
            if (string.Equals(key.Id, sessionId, StringComparison.Ordinal))
            {
                return new ValueTask<string?>(key.TenantId);
            }
        }

        return new ValueTask<string?>((string?)null);
    }

    /// <inheritdoc />
    public ValueTask<bool> DeleteAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sessionId);
        return new ValueTask<bool>(_sessions.TryRemove((_tenantContext.TenantId, sessionId), out _));
    }

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<SessionRecord>> QueryAsync(SessionQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var matches = new List<SessionRecord>();
        var tenantId = query.TenantId ?? _tenantContext.TenantId;

        foreach (var record in _sessions.Values)
        {
            if (query.AgentName is { } agentName && !string.Equals(record.AgentName, agentName, StringComparison.Ordinal))
            {
                continue;
            }

            if (!string.Equals(record.TenantId, tenantId, StringComparison.Ordinal))
            {
                continue;
            }

            matches.Add(record);
        }

        matches.Sort(static (left, right) => right.UpdatedAt.CompareTo(left.UpdatedAt));

        return new ValueTask<IReadOnlyList<SessionRecord>>(Paginate(matches, query.Skip, query.Take));
    }

    private static List<T> Paginate<T>(List<T> source, int skip, int take)
    {
        if (skip <= 0 && take >= source.Count)
        {
            return source;
        }

        var start = Math.Clamp(skip, 0, source.Count);
        var count = Math.Clamp(take, 0, source.Count - start);

        return source.GetRange(start, count);
    }
}
