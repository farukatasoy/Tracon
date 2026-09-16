using System.Collections.Concurrent;

namespace Tracon;

/// <summary>
/// The default implementation that keeps retention policies and run history in
/// process memory.
/// </summary>
/// <remarks>
/// For single-process deployments and tests. <c>UsePostgreSql()</c>,
/// <c>UseSqlServer()</c>, or <c>UseSqlite()</c> replaces it with a SQL implementation.
/// </remarks>
internal sealed class InMemoryRetentionPolicyStore : IRetentionPolicyStore
{
    private readonly ConcurrentDictionary<Guid, RetentionPolicy> _policies = new();
    private readonly ConcurrentDictionary<Guid, RetentionRun> _runs = new();

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<RetentionPolicy>> ListPoliciesAsync(
        string tenantId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        cancellationToken.ThrowIfCancellationRequested();

        IReadOnlyList<RetentionPolicy> result = _policies.Values
            .Where(policy => Matches(policy.TenantId, tenantId))
            .OrderBy(policy => policy.Target, StringComparer.Ordinal)
            .ToList();

        return new ValueTask<IReadOnlyList<RetentionPolicy>>(result);
    }

    /// <inheritdoc />
    public ValueTask<RetentionPolicy?> GetPolicyAsync(
        string tenantId,
        string target,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(target);
        cancellationToken.ThrowIfCancellationRequested();

        var tenantSpecific = Find(tenantId, target);

        return new ValueTask<RetentionPolicy?>(tenantSpecific ?? Find("*", target));
    }

    /// <inheritdoc />
    public ValueTask<RetentionPolicy> SavePolicyAsync(
        RetentionPolicy policy,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(policy);
        cancellationToken.ThrowIfCancellationRequested();

        var existing = Find(policy.TenantId, policy.Target);

        if (existing is not null && existing.Id != policy.Id)
        {
            _policies.TryRemove(existing.Id, out _);
        }

        var saved = policy with { Id = existing?.Id ?? policy.Id };
        _policies[saved.Id] = saved;

        return new ValueTask<RetentionPolicy>(saved);
    }

    /// <inheritdoc />
    public ValueTask<bool> DeletePolicyAsync(
        string tenantId,
        string target,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(target);
        cancellationToken.ThrowIfCancellationRequested();

        var existing = Find(tenantId, target);

        return new ValueTask<bool>(existing is not null && _policies.TryRemove(existing.Id, out _));
    }

    /// <inheritdoc />
    public ValueTask<RetentionRun> CreateRunAsync(RetentionRun run, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(run);
        cancellationToken.ThrowIfCancellationRequested();

        _runs[run.Id] = run;

        return new ValueTask<RetentionRun>(run);
    }

    /// <inheritdoc />
    public ValueTask AppendRunProgressAsync(
        Guid runId,
        long deletedDelta,
        long archivedDelta,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (_runs.TryGetValue(runId, out var run))
        {
            _runs[runId] = run with
            {
                DeletedRows = run.DeletedRows + deletedDelta,
                ArchivedRows = run.ArchivedRows + archivedDelta,
            };
        }

        return default;
    }

    /// <inheritdoc />
    public ValueTask CompleteRunAsync(
        Guid runId,
        DateTimeOffset completedAt,
        string? errorMessage,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (_runs.TryGetValue(runId, out var run))
        {
            _runs[runId] = run with { CompletedAt = completedAt, Error = errorMessage };
        }

        return default;
    }

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<RetentionRun>> ListRunsAsync(
        string tenantId,
        string? target,
        int skip,
        int take,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        cancellationToken.ThrowIfCancellationRequested();

        var matches = _runs.Values.Where(run => Matches(run.TenantId, tenantId));

        if (target is { Length: > 0 })
        {
            matches = matches.Where(run => string.Equals(run.Target, target, StringComparison.Ordinal));
        }

        IReadOnlyList<RetentionRun> result = matches
            .OrderByDescending(run => run.StartedAt)
            .Skip(Math.Max(0, skip))
            .Take(Math.Max(1, take))
            .ToList();

        return new ValueTask<IReadOnlyList<RetentionRun>>(result);
    }

    private RetentionPolicy? Find(string tenantId, string target)
        => _policies.Values.FirstOrDefault(candidate =>
            Matches(candidate.TenantId, tenantId)
            && string.Equals(candidate.Target, target, StringComparison.Ordinal));

    private static bool Matches(string candidateTenantId, string tenantId)
        => string.Equals(candidateTenantId, tenantId, StringComparison.Ordinal);
}
