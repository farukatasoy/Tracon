using System.Text.Json;
using Microsoft.Extensions.Options;
using Npgsql;
using NpgsqlTypes;

namespace AgentPrism;

/// <summary>
/// A/B deneylerini PostgreSQL'de saklayan depo.
/// </summary>
/// <remarks>
/// Davranis sozlesmesi <see cref="InMemoryExperimentStore"/> ile birebir aynidir ve
/// ortak sozlesme testleriyle korunur. Tum islemler <see cref="ITenantContext.TenantId"/>
/// ile sinirlidir. "Ayni agent icin tek Running deney" kurali veritabaninda kismi
/// benzersiz indeksle (<c>experiments_running_agent_uq</c>) de zorlanir; bu depo
/// ihlali <see cref="PostgresException"/> SQLSTATE <c>23505</c> olarak yakalar.
/// </remarks>
public sealed class PostgresExperimentStore : IExperimentStore
{
    /// <summary>Benzersizlik ihlali SQLSTATE kodu.</summary>
    private const string UniqueViolation = "23505";

    private readonly NpgsqlDataSource _dataSource;
    private readonly SqlQueries _sql;
    private readonly ITenantContext _tenantContext;
    private readonly int _commandTimeout;

    /// <summary>Yeni bir deney deposu olusturur.</summary>
    /// <param name="dataSource">Veri kaynagi.</param>
    /// <param name="options">PostgreSQL ayarlari.</param>
    /// <param name="tenantContext">Kiraci baglami.</param>
    /// <exception cref="ArgumentNullException">Bagimliliklardan biri <see langword="null"/> ise.</exception>
    public PostgresExperimentStore(
        NpgsqlDataSource dataSource,
        IOptions<AgentPrismPostgreSqlOptions> options,
        ITenantContext tenantContext)
    {
        ArgumentNullException.ThrowIfNull(dataSource);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(tenantContext);

        _dataSource = dataSource;
        _sql = new SqlQueries(options.Value.SchemaName);
        _tenantContext = tenantContext;
        _commandTimeout = options.Value.CommandTimeoutSeconds;
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<Experiment>> ListAsync(string tenantId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tenantId);

        var command = CreateCommand(_sql.SelectExperiments);
        command.Parameters.AddWithValue("tenant_id", tenantId);

        return await NpgsqlHelpers.ReadListAsync(command, ReadExperiment, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask<Experiment?> GetAsync(string tenantId, string name, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tenantId);
        ArgumentNullException.ThrowIfNull(name);

        var command = CreateCommand(_sql.SelectExperiment);
        command.Parameters.AddWithValue("tenant_id", tenantId);
        command.Parameters.AddWithValue("name", name);

        return await NpgsqlHelpers.ReadSingleAsync(command, ReadExperiment, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask<Experiment?> GetRunningAsync(string tenantId, string agentName, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tenantId);
        ArgumentNullException.ThrowIfNull(agentName);

        var command = CreateCommand(_sql.SelectRunningExperiment);
        command.Parameters.AddWithValue("tenant_id", tenantId);
        command.Parameters.AddWithValue("agent_name", agentName);

        return await NpgsqlHelpers.ReadSingleAsync(command, ReadExperiment, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask<Experiment> SaveAsync(Experiment experiment, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(experiment);

        var now = DateTimeOffset.UtcNow;
        var variantsJson = JsonSerializer.Serialize(experiment.Variants, AgentPrismJsonContext.Default.IReadOnlyListExperimentVariant);

        var command = CreateCommand(_sql.UpsertExperiment);
        command.Parameters.AddWithValue("id", experiment.Id);
        command.Parameters.AddWithValue("tenant_id", experiment.TenantId);
        command.Parameters.AddWithValue("name", experiment.Name);
        command.Parameters.AddWithValue("agent_name", experiment.AgentName);
        command.Parameters.Add(new NpgsqlParameter("variants", NpgsqlDbType.Jsonb) { Value = variantsJson });
        AddNullableText(command, "assignment_key", experiment.AssignmentKey);
        command.Parameters.AddWithValue("updated_at", now.UtcDateTime);

        var result = await NpgsqlHelpers.ExecuteScalarAsync(command, cancellationToken).ConfigureAwait(false);

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
        command.Parameters.AddWithValue("tenant_id", tenantId);
        command.Parameters.AddWithValue("name", name);

        var affected = await NpgsqlHelpers.ExecuteAsync(command, cancellationToken).ConfigureAwait(false);

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
        command.Parameters.AddWithValue("tenant_id", tenantId);
        command.Parameters.AddWithValue("name", name);
        command.Parameters.AddWithValue("now", now.UtcDateTime);

        try
        {
            _ = await NpgsqlHelpers.ExecuteScalarAsync(command, cancellationToken).ConfigureAwait(false)
                ?? throw await BuildStartFailureAsync(tenantId, name, cancellationToken).ConfigureAwait(false);
        }
        catch (PostgresException ex) when (string.Equals(ex.SqlState, UniqueViolation, StringComparison.Ordinal))
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
        command.Parameters.AddWithValue("tenant_id", tenantId);
        command.Parameters.AddWithValue("name", name);
        command.Parameters.AddWithValue("now", now.UtcDateTime);

        var result = await NpgsqlHelpers.ExecuteScalarAsync(command, cancellationToken).ConfigureAwait(false);

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

    private NpgsqlCommand CreateCommand(string sql)
    {
        var command = _dataSource.CreateCommand(sql);
        command.CommandTimeout = _commandTimeout;

        return command;
    }

    private static Experiment ReadExperiment(NpgsqlDataReader reader)
        => new()
        {
            Id = reader.GetGuid(0),
            TenantId = reader.GetString(1),
            Name = reader.GetString(2),
            AgentName = reader.GetString(3),
            Variants = JsonSerializer.Deserialize(reader.GetString(4), AgentPrismJsonContext.Default.IReadOnlyListExperimentVariant)
                ?? [],
            Status = (ExperimentStatus)reader.GetInt16(5),
            AssignmentKey = NpgsqlHelpers.GetNullableString(reader, 6),
            StartedAt = NpgsqlHelpers.GetNullableTimestamp(reader, 7),
            EndedAt = NpgsqlHelpers.GetNullableTimestamp(reader, 8),
            UpdatedAt = NpgsqlHelpers.GetNullableTimestamp(reader, 9),
        };

    private static void AddNullableText(NpgsqlCommand command, string name, string? value)
        => command.Parameters.Add(new NpgsqlParameter(name, NpgsqlDbType.Text)
        {
            Value = (object?)value ?? DBNull.Value,
        });
}
