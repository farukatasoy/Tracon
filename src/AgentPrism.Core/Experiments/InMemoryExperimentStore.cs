using System.Collections.Concurrent;

namespace AgentPrism;

/// <summary>
/// A/B deneylerini surec bellegi icinde tutan depo.
/// </summary>
/// <remarks>
/// <see cref="InMemoryAgentDefinitionStore"/> ile ayni sinirlar gecerlidir: veriler
/// surec omruyle sinirlidir. Uretimde <c>AgentPrism.PostgreSql</c> kullanin.
/// </remarks>
public sealed class InMemoryExperimentStore : IExperimentStore
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
            throw new AgentPrismException(
                $"'{experiment.Name}' deneyi '{existing.Status}' durumunda; yalnizca Draft durumundaki deneyler duzenlenebilir.");
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
            throw new AgentPrismException($"'{name}' deneyi calisirken silinemez; once durdurulmalidir.");
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
            throw new AgentPrismException($"'{name}' adinda bir deney bulunamadi.");
        }

        if (experiment.Status != ExperimentStatus.Draft)
        {
            throw new AgentPrismException($"'{name}' deneyi '{experiment.Status}' durumunda; yalnizca Draft durumundan baslatilabilir.");
        }

        var conflict = _experiments.Values.FirstOrDefault(other =>
            other.Status == ExperimentStatus.Running
            && string.Equals(other.TenantId, tenantId, StringComparison.Ordinal)
            && string.Equals(other.AgentName, experiment.AgentName, StringComparison.Ordinal));

        if (conflict is not null)
        {
            throw new AgentPrismException(
                $"'{experiment.AgentName}' agent'i icin '{conflict.Name}' deneyi zaten calisiyor. Ayni agent icin ayni anda tek deney calisabilir.");
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
            throw new AgentPrismException($"'{name}' adinda bir deney bulunamadi.");
        }

        if (experiment.Status != ExperimentStatus.Running)
        {
            throw new AgentPrismException($"'{name}' deneyi calismiyor.");
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
            throw new AgentPrismException($"'{name}' adinda bir deney bulunamadi.");
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
            throw new AgentPrismException($"'{name}' deneyi calismiyor.");
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
