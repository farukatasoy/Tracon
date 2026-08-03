using Microsoft.Extensions.Options;
using Npgsql;
using NpgsqlTypes;

namespace AgentPrism;

/// <summary>Kota kurallarini ve tuketim sayaclarini PostgreSQL'de saklayan depo.</summary>
/// <remarks>
/// <para>
/// Davranis sozlesmesi <see cref="InMemoryQuotaStore"/> ile birebir aynidir ve
/// ortak sozlesme testleriyle korunur.
/// </para>
/// <para>
/// 🚨 Cok ornekli bir dagitimda <strong>bu depo gereklidir</strong>: bellek ici
/// depo her surecte ayri bir sayac tutar ve kota, ornek sayisina bolunur.
/// </para>
/// </remarks>
public sealed class PostgresQuotaStore : IQuotaStore
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly SqlQueries _sql;
    private readonly int _commandTimeout;

    /// <summary>Yeni bir kota deposu olusturur.</summary>
    /// <param name="dataSource">Veri kaynagi.</param>
    /// <param name="options">PostgreSQL ayarlari.</param>
    /// <exception cref="ArgumentNullException">Bagimliliklardan biri <see langword="null"/> ise.</exception>
    public PostgresQuotaStore(NpgsqlDataSource dataSource, IOptions<AgentPrismPostgreSqlOptions> options)
    {
        ArgumentNullException.ThrowIfNull(dataSource);
        ArgumentNullException.ThrowIfNull(options);

        _dataSource = dataSource;
        _sql = new SqlQueries(options.Value.SchemaName);
        _commandTimeout = options.Value.CommandTimeoutSeconds;
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<QuotaDefinition>> ListAsync(
        string tenantId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);

        var command = CreateCommand(_sql.SelectQuotas);
        command.Parameters.AddWithValue("tenant_id", tenantId);

        return await NpgsqlHelpers.ReadListAsync(command, ReadQuota, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask<QuotaDefinition?> GetAsync(
        string tenantId,
        Guid id,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);

        var command = CreateCommand(_sql.SelectQuota);
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("tenant_id", tenantId);

        return await NpgsqlHelpers.ReadSingleAsync(command, ReadQuota, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask<QuotaDefinition> SaveAsync(
        QuotaDefinition definition,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(definition);

        var command = CreateCommand(_sql.UpsertQuota);
        command.Parameters.AddWithValue("id", definition.Id);
        command.Parameters.AddWithValue("tenant_id", definition.TenantId);
        command.Parameters.AddWithValue("agent_name", (object?)definition.AgentName ?? DBNull.Value);
        command.Parameters.AddWithValue("period", (short)definition.Period);
        command.Parameters.AddWithValue("max_runs", (object?)definition.MaxRuns ?? DBNull.Value);
        command.Parameters.AddWithValue("max_tokens", (object?)definition.MaxTokens ?? DBNull.Value);
        command.Parameters.AddWithValue("max_cost", (object?)definition.MaxCost ?? DBNull.Value);
        command.Parameters.AddWithValue("enabled", definition.Enabled);
        command.Parameters.AddWithValue("created_at", definition.CreatedAt.UtcDateTime);
        command.Parameters.AddWithValue("updated_at", definition.UpdatedAt.UtcDateTime);

        var saved = await NpgsqlHelpers.ReadSingleAsync(command, ReadQuota, cancellationToken).ConfigureAwait(false);

        return saved ?? definition;
    }

    /// <inheritdoc />
    public async ValueTask<bool> DeleteAsync(
        string tenantId,
        Guid id,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);

        var command = CreateCommand(_sql.DeleteQuota);
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("tenant_id", tenantId);

        return await NpgsqlHelpers.ExecuteAsync(command, cancellationToken).ConfigureAwait(false) > 0;
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<QuotaUsageRecord>> GetUsageAsync(
        QuotaUsageQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var command = CreateCommand(_sql.SelectQuotaUsage);
        command.Parameters.AddWithValue("tenant_id", query.TenantId);

        // 🚨 `(@p IS NULL OR col = @p)` deseninde parametre NULL olunca Npgsql
        // tipi cikaramaz ve PostgreSQL `42P08: could not determine data type`
        // verir. Isteğe bagli her suzgec parametresi ACIKCA tiplenmelidir.
        command.Parameters.Add(new NpgsqlParameter("agent_name", NpgsqlDbType.Text)
        {
            Value = (object?)query.AgentName ?? DBNull.Value,
        });
        command.Parameters.Add(new NpgsqlParameter("period", NpgsqlDbType.Smallint)
        {
            Value = query.Period is { } period ? (short)period : DBNull.Value,
        });

        return await NpgsqlHelpers.ReadListAsync(command, ReadUsage, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask AddUsageAsync(
        QuotaConsumption consumption,
        IReadOnlyDictionary<QuotaPeriod, DateOnly> periodStarts,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(consumption);
        ArgumentNullException.ThrowIfNull(periodStarts);

        var connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);

        await using (connection.ConfigureAwait(false))
        {
            var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

            await using (transaction.ConfigureAwait(false))
            {
                foreach (var (period, periodStart) in periodStarts)
                {
                    // Bir calistirma HEM agent sayacini HEM kiraci geneli
                    // sayacini artirir; kiraci geneli kural agent adini
                    // bilmeden sorgulanabilsin diye.
                    await IncrementAsync(connection, transaction, consumption, period, periodStart, consumption.AgentName, cancellationToken)
                        .ConfigureAwait(false);
                    await IncrementAsync(connection, transaction, consumption, period, periodStart, string.Empty, cancellationToken)
                        .ConfigureAwait(false);
                }

                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private async ValueTask IncrementAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        QuotaConsumption consumption,
        QuotaPeriod period,
        DateOnly periodStart,
        string agentName,
        CancellationToken cancellationToken)
    {
        var command = new NpgsqlCommand(_sql.AddQuotaUsage, connection, transaction)
        {
            CommandTimeout = _commandTimeout,
        };

        command.Parameters.AddWithValue("tenant_id", consumption.TenantId);
        command.Parameters.AddWithValue("agent_name", agentName);
        command.Parameters.AddWithValue("period", (short)period);
        command.Parameters.AddWithValue("period_start", periodStart);
        command.Parameters.AddWithValue("runs", consumption.Runs);
        command.Parameters.AddWithValue("tokens", consumption.Tokens);

        // Fiyat tanimsizsa toplama SIFIR eklenir (para toplami degismez); NULL
        // eklemek toplami tumuyle NULL yapardi.
        command.Parameters.AddWithValue("cost", consumption.Cost ?? 0m);
        command.Parameters.AddWithValue("updated_at", consumption.OccurredAt.UtcDateTime);

        await NpgsqlHelpers.ExecuteAsync(command, cancellationToken).ConfigureAwait(false);
    }

    private NpgsqlCommand CreateCommand(string sql)
    {
        var command = _dataSource.CreateCommand(sql);
        command.CommandTimeout = _commandTimeout;

        return command;
    }

    private static QuotaDefinition ReadQuota(NpgsqlDataReader reader)
        => new()
        {
            Id = reader.GetGuid(0),
            TenantId = reader.GetString(1),
            AgentName = NpgsqlHelpers.GetNullableString(reader, 2),
            Period = (QuotaPeriod)reader.GetInt16(3),
            MaxRuns = reader.IsDBNull(4) ? null : reader.GetInt64(4),
            MaxTokens = reader.IsDBNull(5) ? null : reader.GetInt64(5),
            MaxCost = NpgsqlHelpers.GetNullableDecimal(reader, 6),
            Enabled = reader.GetBoolean(7),
            CreatedAt = NpgsqlHelpers.GetTimestamp(reader, 8),
            UpdatedAt = NpgsqlHelpers.GetTimestamp(reader, 9),
        };

    private static QuotaUsageRecord ReadUsage(NpgsqlDataReader reader)
        => new()
        {
            TenantId = reader.GetString(0),
            AgentName = reader.GetString(1),
            Period = (QuotaPeriod)reader.GetInt16(2),
            PeriodStart = reader.GetFieldValue<DateOnly>(3),
            Runs = reader.GetInt64(4),
            Tokens = reader.GetInt64(5),
            Cost = reader.GetDecimal(6),
            UpdatedAt = NpgsqlHelpers.GetTimestamp(reader, 7),
        };
}
