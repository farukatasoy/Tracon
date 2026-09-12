using System.Collections.Concurrent;

namespace Tracon;

/// <summary>
/// A store that keeps A/B experiments in process memory.
/// </summary>
/// <remarks>
/// The same limits as <see cref="InMemoryAgentDefinitionStore"/> apply. Data is
/// limited to the process lifetime. Use <c>Tracon.PostgreSql</c> in production.
/// </remarks>
internal sealed class InMemoryExperimentStore : IExperimentStore
{
    private readonly ConcurrentDictionary<(string TenantId, string Name), Experiment> _experiments = new();

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<Experiment>> ListAsync(string tenantId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tenantId);

        var result = _experiments.Values
            .Where(experiment => string.Equals(experiment.TenantId, tenantId, StringComparison.Ordinal))
            .OrderBy(static experiment => experiment.Name, StringComparer.Ordinal)
            .ToList();

        return new ValueTask<IReadOnlyList<Experiment>>(result);
    }

    /// <inheritdoc />
    public ValueTask<Experiment?> GetAsync(string tenantId, string name, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tenantId);
        ArgumentNullException.ThrowIfNull(name);

        _experiments.TryGetValue((tenantId, name), out var experiment);
        return new ValueTask<Experiment?>(experiment);
    }

    /// <inheritdoc />
    public ValueTask<Experiment?> GetRunningAsync(string tenantId, string agentName, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tenantId);
        ArgumentNullException.ThrowIfNull(agentName);

        var running = _experiments.Values.FirstOrDefault(experiment =>
            experiment.Status == ExperimentStatus.Running
            && string.Equals(experiment.TenantId, tenantId, StringComparison.Ordinal)
            && string.Equals(experiment.AgentName, agentName, StringComparison.Ordinal));

        return new ValueTask<Experiment?>(running);
    }

    /// <inheritdoc />
    public ValueTask<Experiment> SaveAsync(Experiment experiment, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(experiment);

        var key = (experiment.TenantId, experiment.Name);

        if (_experiments.TryGetValue(key, out var existing) && existing.Status != ExperimentStatus.Draft)
        {
            throw new TraconException(
                $"Experiment '{experiment.Name}' has status '{existing.Status}'; only Draft experiments can be edited.");
        }

        var saved = experiment with
        {
            Status = existing?.Status ?? ExperimentStatus.Draft,
            StartedAt = existing?.StartedAt,
            EndedAt = existing?.EndedAt,
            UpdatedAt = DateTimeOffset.UtcNow,
        };

        _experiments[key] = saved;
        return new ValueTask<Experiment>(saved);
    }

    /// <inheritdoc />
    public ValueTask<bool> DeleteAsync(string tenantId, string name, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tenantId);
        ArgumentNullException.ThrowIfNull(name);

        var key = (tenantId, name);

        if (_experiments.TryGetValue(key, out var existing) && existing.Status == ExperimentStatus.Running)
        {
            throw new TraconException($"Experiment '{name}' cannot be deleted while it is running; stop it first.");
        }

        return new ValueTask<bool>(_experiments.TryRemove(key, out _));
    }

    /// <inheritdoc />
    public ValueTask<Experiment> StartAsync(string tenantId, string name, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tenantId);
        ArgumentNullException.ThrowIfNull(name);

        var key = (tenantId, name);

        if (!_experiments.TryGetValue(key, out var experiment))
        {
            throw new TraconException($"Experiment named '{name}' was not found.");
        }

        if (experiment.Status != ExperimentStatus.Draft)
        {
            throw new TraconException($"Experiment '{name}' has status '{experiment.Status}'; it can only start from Draft.");
        }

        var conflict = _experiments.Values.FirstOrDefault(other =>
            other.Status == ExperimentStatus.Running
            && string.Equals(other.TenantId, tenantId, StringComparison.Ordinal)
            && string.Equals(other.AgentName, experiment.AgentName, StringComparison.Ordinal));

        if (conflict is not null)
        {
            throw new TraconException(
                $"Experiment '{conflict.Name}' is already running for agent '{experiment.AgentName}'. Only one experiment can run for an agent at a time.");
        }

        var now = DateTimeOffset.UtcNow;

        var started = experiment with
        {
            Status = ExperimentStatus.Running,
            StartedAt = now,
            UpdatedAt = now,
        };

        _experiments[key] = started;
        return new ValueTask<Experiment>(started);
    }

    /// <inheritdoc />
    public ValueTask<Experiment> StopAsync(string tenantId, string name, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tenantId);
        ArgumentNullException.ThrowIfNull(name);

        var key = (tenantId, name);

        if (!_experiments.TryGetValue(key, out var experiment))
        {
            throw new TraconException($"Experiment named '{name}' was not found.");
        }

        if (experiment.Status != ExperimentStatus.Running)
        {
            throw new TraconException($"Experiment '{name}' is not running.");
        }

        var now = DateTimeOffset.UtcNow;

        var stopped = experiment with
        {
            Status = ExperimentStatus.Stopped,
            EndedAt = now,
            UpdatedAt = now,
        };

        _experiments[key] = stopped;
        return new ValueTask<Experiment>(stopped);
    }

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<Experiment>> ListRunningWithCanaryAsync(CancellationToken cancellationToken = default)
    {
        var result = _experiments.Values
            .Where(static experiment => experiment.Status == ExperimentStatus.Running && experiment.Canary is not null)
            .ToList();

        return new ValueTask<IReadOnlyList<Experiment>>(result);
    }

    /// <inheritdoc />
    public ValueTask<Experiment> SetCanaryPolicyAsync(
        string tenantId,
        string name,
        CanaryPolicy? policy,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tenantId);
        ArgumentNullException.ThrowIfNull(name);

        var key = (tenantId, name);

        if (!_experiments.TryGetValue(key, out var experiment))
        {
            throw new TraconException($"Experiment named '{name}' was not found.");
        }

        var updated = experiment with
        {
            Canary = policy,
            UpdatedAt = DateTimeOffset.UtcNow,
        };

        _experiments[key] = updated;
        return new ValueTask<Experiment>(updated);
    }

    /// <inheritdoc />
    public ValueTask<Experiment> AdvanceCanaryRampAsync(
        string tenantId,
        string name,
        IReadOnlyList<ExperimentVariant> variants,
        CancellationToken cancellationToken = default)
        => ApplyCanaryVariantsAsync(tenantId, name, variants, stop: false, reason: null);

    /// <inheritdoc />
    public ValueTask<Experiment> RollbackCanaryAsync(
        string tenantId,
        string name,
        IReadOnlyList<ExperimentVariant> variants,
        string reason,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reason);

        return ApplyCanaryVariantsAsync(tenantId, name, variants, stop: true, reason: reason);
    }

    private ValueTask<Experiment> ApplyCanaryVariantsAsync(
        string tenantId,
        string name,
        IReadOnlyList<ExperimentVariant> variants,
        bool stop,
        string? reason)
    {
        ArgumentNullException.ThrowIfNull(tenantId);
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(variants);

        var key = (tenantId, name);

        if (!_experiments.TryGetValue(key, out var experiment) || experiment.Status != ExperimentStatus.Running)
        {
            throw new TraconException($"Experiment '{name}' is not running.");
        }

        var now = DateTimeOffset.UtcNow;

        var updated = experiment with
        {
            Variants = variants,
            Status = stop ? ExperimentStatus.Stopped : experiment.Status,
            EndedAt = stop ? now : experiment.EndedAt,
            RollbackReason = stop ? reason : experiment.RollbackReason,
            UpdatedAt = now,
        };

        _experiments[key] = updated;
        return new ValueTask<Experiment>(updated);
    }
}
