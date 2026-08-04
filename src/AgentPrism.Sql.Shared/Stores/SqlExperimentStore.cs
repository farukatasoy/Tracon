using System.Data.Common;
using System.Text.Json;

namespace AgentPrism;

/// <summary>
/// A/B deneylerini PostgreSQL'de saklayan depo.
/// </summary>
/// <remarks>
/// Davranis sozlesmesi <see cref="InMemoryExperimentStore"/> ile birebir aynidir ve
/// ortak sozlesme testleriyle korunur. Tum islemler <see cref="ITenantContext.TenantId"/>
/// ile sinirlidir. "Ayni agent icin tek Running deney" kurali veritabaninda kismi
/// benzersiz indeksle (<c>experiments_running_agent_uq</c>) de zorlanir; bu depo
/// ihlali saglayicidan bagimsiz olarak <c>SqlDialect.IsUniqueViolation</c> ile yakalanir.
/// </remarks>
internal sealed class SqlExperimentStore : IExperimentStore
{
    private readonly SqlStoreContext _context;
    private readonly SqlQueriesBase _sql;
    private readonly ITenantContext _tenantContext;

    /// <summary>Yeni bir deney deposu olusturur.</summary>
    /// <param name="context">Depo baglami.</param>
    /// <param name="tenantContext">Kiraci baglami.</param>
    /// <exception cref="ArgumentNullException">Bagimliliklardan biri <see langword="null"/> ise.</exception>
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

    /// <summary>Saglayiciya ozgu davranislarin kapisi.</summary>
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
            // INSERT ... ON CONFLICT DO UPDATE ... WHERE status = 0 hicbir satir
            // etkilemedi: kayit var ama Draft degil.
            var existing = await GetAsync(experiment.TenantId, experiment.Name, cancellationToken).ConfigureAwait(false);

            throw existing is null
                ? new AgentPrismException($"'{experiment.Name}' deneyi kaydedilemedi.")
                : new AgentPrismException(
                    $"'{experiment.Name}' deneyi '{existing.Status}' durumunda; yalnizca Draft durumundaki deneyler duzenlenebilir.");
        }

        return await GetAsync(experiment.TenantId, experiment.Name, cancellationToken).ConfigureAwait(false)
            ?? throw new AgentPrismException($"'{experiment.Name}' deneyi kaydedildi ama okunamadi.");
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
            throw new AgentPrismException($"'{name}' deneyi calisirken silinemez; once durdurulmalidir.");
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
                ?? throw new AgentPrismException($"'{name}' adinda bir deney bulunamadi.", ex);

            throw new AgentPrismException(
                $"'{experiment.AgentName}' agent'i icin baska bir deney zaten calisiyor. " +
                "Ayni agent icin ayni anda tek deney calisabilir.",
                ex);
        }

        return await GetAsync(tenantId, name, cancellationToken).ConfigureAwait(false)
            ?? throw new AgentPrismException($"'{name}' deneyi baslatildi ama okunamadi.");
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
                ? new AgentPrismException($"'{name}' adinda bir deney bulunamadi.")
                : new AgentPrismException($"'{name}' deneyi calismiyor.");
        }

        return await GetAsync(tenantId, name, cancellationToken).ConfigureAwait(false)
            ?? throw new AgentPrismException($"'{name}' deneyi durduruldu ama okunamadi.");
    }

    private async ValueTask<AgentPrismException> BuildStartFailureAsync(string tenantId, string name, CancellationToken cancellationToken)
    {
        var existing = await GetAsync(tenantId, name, cancellationToken).ConfigureAwait(false);

        return existing is null
            ? new AgentPrismException($"'{name}' adinda bir deney bulunamadi.")
            : new AgentPrismException(
                $"'{name}' deneyi '{existing.Status}' durumunda; yalnizca Draft durumundan baslatilabilir.");
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
        };

    private void AddNullableText(DbCommand command, string name, string? value)
        => Dialect.AddText(command, name, value);
}
