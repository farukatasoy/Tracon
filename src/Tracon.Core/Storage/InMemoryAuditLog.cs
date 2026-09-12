using System.Collections.Concurrent;

namespace Tracon;

/// <summary>An audit log that keeps entries in process memory.</summary>
/// <remarks>
/// <para>Use <c>Tracon.PostgreSql</c> in production.</para>
/// <para>
/// The hash chain is computed here too: entries are kept per tenant
/// (<c>ConcurrentDictionary&lt;string, List&lt;AuditEntry&gt;&gt;</c>) and each
/// tenant's list is locked for its own read-last-hash/compute/append sequence —
/// a single process has no cross-connection writer race the way a SQL provider
/// does, so a lock (not a retry loop) is enough. Locking ON THE LIST ITSELF
/// (rather than a dedicated lock field) is deliberate: net8.0 is also targeted
/// and <c>System.Threading.Lock</c> arrived only with .NET 9 — the same pattern
/// <c>RunTraceCollector.RunSpanBuffer</c> uses.
/// </para>
/// </remarks>
internal sealed class InMemoryAuditLog : IAuditLog
{
    private readonly ConcurrentDictionary<string, List<AuditEntry>> _byTenant = new(StringComparer.Ordinal);
    private readonly ITenantContext _tenantContext;

    /// <summary>Creates a new in-memory audit log.</summary>
    /// <param name="tenantContext">
    /// Resolves the AMBIENT tenant for <see cref="QueryAsync"/> and
    /// <see cref="VerifyChainAsync"/> when their query's <c>TenantId</c> is
    /// <see langword="null"/> or empty. Defaults to
    /// <see cref="FixedTenantContext.Default"/> when constructed outside DI.
    /// </param>
    public InMemoryAuditLog(ITenantContext? tenantContext = null)
    {
        _tenantContext = tenantContext ?? FixedTenantContext.Default;
    }

    /// <inheritdoc />
    public ValueTask WriteAsync(AuditEntry entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);

        var chain = _byTenant.GetOrAdd(entry.TenantId, static _ => []);

        lock (chain)
        {
            var previousHash = chain.Count > 0 ? chain[^1].Hash : null;

            var hash = AuditChainHasher.ComputeHash(
                previousHash,
                entry.TenantId,
                entry.Actor,
                entry.Action,
                entry.Entity,
                entry.Before,
                entry.After,
                entry.CreatedAt);

            chain.Add(entry with { PreviousHash = previousHash, Hash = hash });
        }

        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public ValueTask<AuditChainVerification> VerifyChainAsync(
        AuditChainQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        IEnumerable<AuditEntry> matches = SnapshotOne(ResolveTenantId(query.TenantId));

        if (query.After is { } after)
        {
            matches = matches.Where(entry => entry.CreatedAt >= after);
        }

        if (query.Before is { } before)
        {
            matches = matches.Where(entry => entry.CreatedAt <= before);
        }

        var ordered = matches
            .OrderBy(static entry => entry.CreatedAt)
            .ThenBy(static entry => entry.Id)
            .ToList();

        return new ValueTask<AuditChainVerification>(AuditChainWalker.Verify(ordered, hasLowerBound: query.After is not null));
    }

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<AuditEntry>> QueryAsync(
        AuditQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        IEnumerable<AuditEntry> matches = SnapshotOne(ResolveTenantId(query.TenantId));

        if (query.Actor is { Length: > 0 } actor)
        {
            matches = matches.Where(entry => string.Equals(entry.Actor, actor, StringComparison.Ordinal));
        }

        if (query.Action is { Length: > 0 } action)
        {
            matches = matches.Where(entry => string.Equals(entry.Action, action, StringComparison.Ordinal));
        }

        if (query.Entity is { Length: > 0 } entity)
        {
            matches = matches.Where(entry => string.Equals(entry.Entity, entity, StringComparison.Ordinal));
        }

        if (query.After is { } after)
        {
            matches = matches.Where(entry => entry.CreatedAt > after);
        }

        if (query.Before is { } before)
        {
            matches = matches.Where(entry => entry.CreatedAt < before);
        }

        var result = matches
            .OrderByDescending(static entry => entry.CreatedAt)
            .Take(Math.Max(query.Limit, 0))
            .ToList();

        return new ValueTask<IReadOnlyList<AuditEntry>>(result);
    }

    /// <summary>
    /// Resolves the AMBIENT fallback (see <see cref="IAuditLog"/>'s remarks): an empty
    /// or missing override tenant resolves to the caller's OWN tenant, never to "every
    /// tenant" — there is no code path in this store that scans across tenants.
    /// </summary>
    private string ResolveTenantId(string? tenantId) => tenantId is { Length: > 0 } ? tenantId : _tenantContext.TenantId;

    private List<AuditEntry> SnapshotOne(string tenantId)
    {
        if (!_byTenant.TryGetValue(tenantId, out var chain))
        {
            return [];
        }

        lock (chain)
        {
            return [.. chain];
        }
    }
}
