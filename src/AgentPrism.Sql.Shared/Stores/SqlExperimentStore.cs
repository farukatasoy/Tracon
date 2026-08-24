using System.Data.Common;
using System.Text.Json;

namespace AgentPrism;

/// <summary>
/// Stores A/B experiments in the SQL database.
/// </summary>
/// <remarks>
/// The behavior contract is identical to <c>InMemoryExperimentStore</c> and is
/// protected by shared contract tests. All operations are bounded by
/// <see cref="ITenantContext.TenantId"/>. The "single Running experiment per agent"
/// rule is also enforced in the database with a partial unique index
/// (<c>experiments_running_agent_uq</c>); this store catches the violation with
/// <c>SqlDialect.IsUniqueViolation</c> independent of the provider.
/// </remarks>
internal sealed class SqlExperimentStore : IExperimentStore
{
    private readonly SqlStoreContext _context;
    private readonly SqlQueriesBase _sql;
    private readonly ITenantContext _tenantContext;

    /// <summary>Creates a new experiment store.</summary>
    /// <param name="context">The store context.</param>
    /// <param name="tenantContext">The tenant context.</param>
    /// <exception cref="ArgumentNullException">One of the dependencies is <see langword="null"/>.</exception>
    public SqlExperimentStore(
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
    public async ValueTask<IReadOnlyList<Experiment>> ListAsync(string tenantId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tenantId);

        var command = CreateCommand(_sql.SelectExperiments);
        DbHelpers.Add(command, "tenant_id", tenantId);

        return await DbHelpers.ReadListAsync(command, ReadExperiment, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask<Experiment?> GetAsync(string tenantId, string name, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tenantId);
        ArgumentNullException.ThrowIfNull(name);

        var command = CreateCommand(_sql.SelectExperiment);
        DbHelpers.Add(command, "tenant_id", tenantId);
        DbHelpers.Add(command, "name", name);

        return await DbHelpers.ReadSingleAsync(command, ReadExperiment, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask<Experiment?> GetRunningAsync(string tenantId, string agentName, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tenantId);
        ArgumentNullException.ThrowIfNull(agentName);

        var command = CreateCommand(_sql.SelectRunningExperiment);
        DbHelpers.Add(command, "tenant_id", tenantId);
        DbHelpers.Add(command, "agent_name", agentName);

        return await DbHelpers.ReadSingleAsync(command, ReadExperiment, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<Experiment>> ListRunningWithCanaryAsync(CancellationToken cancellationToken = default)
    {
        var command = CreateCommand(_sql.SelectRunningExperimentsWithCanary);

        return await DbHelpers.ReadListAsync(command, ReadExperiment, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask<Experiment> SaveAsync(Experiment experiment, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(experiment);

        var now = DateTimeOffset.UtcNow;
        var variantsJson = JsonSerializer.Serialize(experiment.Variants, AgentPrismJsonContext.Default.IReadOnlyListExperimentVariant);

        var command = CreateCommand(_sql.UpsertExperiment);
        DbHelpers.Add(command, "id", experiment.Id);
        DbHelpers.Add(command, "tenant_id", experiment.TenantId);
        DbHelpers.Add(command, "name", experiment.Name);
        DbHelpers.Add(command, "agent_name", experiment.AgentName);
        Dialect.AddJsonb(command, "variants", variantsJson);
        AddNullableText(command, "assignment_key", experiment.AssignmentKey);
        Dialect.AddTimestamp(command, "updated_at", now);

        var result = await DbHelpers.ExecuteScalarAsync(command, cancellationToken).ConfigureAwait(false);

        if (result is null)
        {
            // INSERT ... ON CONFLICT DO UPDATE ... WHERE status = 0 affected no
            // row: the record exists but is not Draft.
            var existing = await GetAsync(experiment.TenantId, experiment.Name, cancellationToken).ConfigureAwait(false);

            throw existing is null
                ? new AgentPrismException($"Failed to save experiment '{experiment.Name}'.")
                : new AgentPrismException(
                    $"Experiment '{experiment.Name}' is in status '{existing.Status}'; only experiments in Draft status can be edited.");
        }

        return await GetAsync(experiment.TenantId, experiment.Name, cancellationToken).ConfigureAwait(false)
            ?? throw new AgentPrismException($"Experiment '{experiment.Name}' was saved but could not be read back.");
    }

    /// <inheritdoc />
    public async ValueTask<bool> DeleteAsync(string tenantId, string name, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tenantId);
        ArgumentNullException.ThrowIfNull(name);

        var command = CreateCommand(_sql.DeleteExperiment);
        DbHelpers.Add(command, "tenant_id", tenantId);
        DbHelpers.Add(command, "name", name);

        var affected = await DbHelpers.ExecuteAsync(command, cancellationToken).ConfigureAwait(false);

        if (affected > 0)
        {
            return true;
        }

        var existing = await GetAsync(tenantId, name, cancellationToken).ConfigureAwait(false);

        if (existing is { Status: ExperimentStatus.Running })
        {
            throw new AgentPrismException($"Experiment '{name}' cannot be deleted while running; stop it first.");
        }

        return false;
    }

    /// <inheritdoc />
    public async ValueTask<Experiment> StartAsync(string tenantId, string name, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tenantId);
        ArgumentNullException.ThrowIfNull(name);

        var now = DateTimeOffset.UtcNow;

        var command = CreateCommand(_sql.StartExperiment);
        DbHelpers.Add(command, "tenant_id", tenantId);
        DbHelpers.Add(command, "name", name);
        Dialect.AddTimestamp(command, "now", now);

        try
        {
            _ = await DbHelpers.ExecuteScalarAsync(command, cancellationToken).ConfigureAwait(false)
                ?? throw await BuildStartFailureAsync(tenantId, name, cancellationToken).ConfigureAwait(false);
        }
        catch (DbException ex) when (Dialect.IsUniqueViolation(ex))
        {
            var experiment = await GetAsync(tenantId, name, cancellationToken).ConfigureAwait(false)
                ?? throw new AgentPrismException($"No experiment named '{name}' was found.", ex);

            throw new AgentPrismException(
                $"Another experiment is already running for agent '{experiment.AgentName}'. " +
                "Only one experiment can run at a time for the same agent.",
                ex);
        }

        return await GetAsync(tenantId, name, cancellationToken).ConfigureAwait(false)
            ?? throw new AgentPrismException($"Experiment '{name}' was started but could not be read back.");
    }

    /// <inheritdoc />
    public async ValueTask<Experiment> StopAsync(string tenantId, string name, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tenantId);
        ArgumentNullException.ThrowIfNull(name);

        var now = DateTimeOffset.UtcNow;

        var command = CreateCommand(_sql.StopExperiment);
        DbHelpers.Add(command, "tenant_id", tenantId);
        DbHelpers.Add(command, "name", name);
        Dialect.AddTimestamp(command, "now", now);

        var result = await DbHelpers.ExecuteScalarAsync(command, cancellationToken).ConfigureAwait(false);

        if (result is null)
        {
            var existing = await GetAsync(tenantId, name, cancellationToken).ConfigureAwait(false);

            throw existing is null
                ? new AgentPrismException($"No experiment named '{name}' was found.")
                : new AgentPrismException($"Experiment '{name}' is not running.");
        }

        return await GetAsync(tenantId, name, cancellationToken).ConfigureAwait(false)
            ?? throw new AgentPrismException($"Experiment '{name}' was stopped but could not be read back.");
    }

    /// <inheritdoc />
    public async ValueTask<Experiment> SetCanaryPolicyAsync(
        string tenantId,
        string name,
        CanaryPolicy? policy,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tenantId);
        ArgumentNullException.ThrowIfNull(name);

        var now = DateTimeOffset.UtcNow;
        var policyJson = policy is null ? null : JsonSerializer.Serialize(policy, AgentPrismJsonContext.Default.CanaryPolicy);

        var command = CreateCommand(_sql.SetExperimentCanaryPolicy);
        DbHelpers.Add(command, "tenant_id", tenantId);
        DbHelpers.Add(command, "name", name);
        Dialect.AddJsonb(command, "canary_policy", policyJson);
        Dialect.AddTimestamp(command, "now", now);

        _ = await DbHelpers.ExecuteScalarAsync(command, cancellationToken).ConfigureAwait(false)
            ?? throw new AgentPrismException($"No experiment named '{name}' was found.");

        return await GetAsync(tenantId, name, cancellationToken).ConfigureAwait(false)
            ?? throw new AgentPrismException($"Experiment '{name}''s canary policy was updated but could not be read back.");
    }

    /// <inheritdoc />
    public async ValueTask<Experiment> AdvanceCanaryRampAsync(
        string tenantId,
        string name,
        IReadOnlyList<ExperimentVariant> variants,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tenantId);
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(variants);

        var now = DateTimeOffset.UtcNow;
        var variantsJson = JsonSerializer.Serialize(variants, AgentPrismJsonContext.Default.IReadOnlyListExperimentVariant);

        var command = CreateCommand(_sql.AdvanceExperimentCanaryRamp);
        DbHelpers.Add(command, "tenant_id", tenantId);
        DbHelpers.Add(command, "name", name);
        Dialect.AddJsonb(command, "variants", variantsJson);
        Dialect.AddTimestamp(command, "now", now);

        _ = await DbHelpers.ExecuteScalarAsync(command, cancellationToken).ConfigureAwait(false)
            ?? throw new AgentPrismException($"Experiment '{name}' is not running.");

        return await GetAsync(tenantId, name, cancellationToken).ConfigureAwait(false)
            ?? throw new AgentPrismException($"Experiment '{name}''s canary weight was updated but could not be read back.");
    }

    /// <inheritdoc />
    public async ValueTask<Experiment> RollbackCanaryAsync(
        string tenantId,
        string name,
        IReadOnlyList<ExperimentVariant> variants,
        string reason,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tenantId);
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(variants);
        ArgumentNullException.ThrowIfNull(reason);

        var now = DateTimeOffset.UtcNow;
        var variantsJson = JsonSerializer.Serialize(variants, AgentPrismJsonContext.Default.IReadOnlyListExperimentVariant);

        var command = CreateCommand(_sql.RollbackExperimentCanary);
        DbHelpers.Add(command, "tenant_id", tenantId);
        DbHelpers.Add(command, "name", name);
        Dialect.AddJsonb(command, "variants", variantsJson);
        AddNullableText(command, "rollback_reason", reason);
        Dialect.AddTimestamp(command, "now", now);

        _ = await DbHelpers.ExecuteScalarAsync(command, cancellationToken).ConfigureAwait(false)
            ?? throw new AgentPrismException($"Experiment '{name}' is not running.");

        return await GetAsync(tenantId, name, cancellationToken).ConfigureAwait(false)
            ?? throw new AgentPrismException($"Experiment '{name}' was rolled back but could not be read back.");
    }

    private async ValueTask<AgentPrismException> BuildStartFailureAsync(string tenantId, string name, CancellationToken cancellationToken)
    {
        var existing = await GetAsync(tenantId, name, cancellationToken).ConfigureAwait(false);

        return existing is null
            ? new AgentPrismException($"No experiment named '{name}' was found.")
            : new AgentPrismException(
                $"Experiment '{name}' is in status '{existing.Status}'; it can only be started from Draft status.");
    }

    private DbCommand CreateCommand(string sql) => _context.CreateCommand(sql);

    private static Experiment ReadExperiment(DbDataReader reader)
        => new()
        {
            Id = reader.GetGuid(0),
            TenantId = reader.GetString(1),
            Name = reader.GetString(2),
            AgentName = reader.GetString(3),
            Variants = JsonSerializer.Deserialize(reader.GetString(4), AgentPrismJsonContext.Default.IReadOnlyListExperimentVariant)
                ?? [],
            Status = (ExperimentStatus)reader.GetInt16(5),
            AssignmentKey = DbHelpers.GetNullableString(reader, 6),
            StartedAt = DbHelpers.GetNullableTimestamp(reader, 7),
            EndedAt = DbHelpers.GetNullableTimestamp(reader, 8),
            UpdatedAt = DbHelpers.GetNullableTimestamp(reader, 9),
            Canary = DbHelpers.GetNullableString(reader, 10) is { } canaryJson
                ? JsonSerializer.Deserialize(canaryJson, AgentPrismJsonContext.Default.CanaryPolicy)
                : null,
            RollbackReason = DbHelpers.GetNullableString(reader, 11),
        };

    private void AddNullableText(DbCommand command, string name, string? value)
        => Dialect.AddText(command, name, value);
}
