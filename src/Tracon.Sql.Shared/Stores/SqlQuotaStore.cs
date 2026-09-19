using System.Data.Common;
using System.Globalization;

namespace Tracon;

/// <summary>Stores quota rules and consumption counters in the SQL database.</summary>
/// <remarks>
/// <para>
/// The behavior contract is identical to <c>InMemoryQuotaStore</c> and
/// is guarded by the shared contract tests.
/// </para>
/// <para>
/// In a multi-instance deployment <strong>this store is required</strong>:
/// the in-memory store keeps a separate counter per process, and the quota
/// ends up divided by the instance count.
/// </para>
/// </remarks>
internal sealed class SqlQuotaStore : IQuotaStore
{
    private readonly SqlStoreContext _context;
    private readonly SqlQueriesBase _sql;

    /// <summary>Creates a new quota store.</summary>
    /// <param name="context">The store context.</param>
    /// <exception cref="ArgumentNullException">One of the dependencies is <see langword="null"/>.</exception>
    public SqlQuotaStore(SqlStoreContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        _context = context;
        _sql = context.Sql;
    }

    /// <summary>The gateway for provider-specific behavior.</summary>
    private SqlDialect Dialect => _context.Dialect;

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<QuotaDefinition>> ListAsync(
        string tenantId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);

        var command = CreateCommand(_sql.SelectQuotas);
        DbHelpers.AddTenant(command, tenantId);

        return await DbHelpers.ReadListAsync(command, ReadQuota, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask<QuotaDefinition?> GetAsync(
        string tenantId,
        Guid id,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);

        var command = CreateCommand(_sql.SelectQuota);
        DbHelpers.Add(command, "id", id);
        DbHelpers.AddTenant(command, tenantId);

        return await DbHelpers.ReadSingleAsync(command, ReadQuota, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask<QuotaDefinition> SaveAsync(
        QuotaDefinition definition,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(definition);

        var command = CreateCommand(_sql.UpsertQuota);
        DbHelpers.Add(command, "id", definition.Id);
        DbHelpers.AddTenant(command, definition.TenantId);
        Dialect.AddText(command, "agent_name", definition.AgentName);
        DbHelpers.Add(command, "period", (short)definition.Period);
        Dialect.AddInt64(command, "max_runs", definition.MaxRuns);
        Dialect.AddInt64(command, "max_tokens", definition.MaxTokens);
        Dialect.AddDecimal(command, "max_cost", definition.MaxCost);
        DbHelpers.Add(command, "enabled", definition.Enabled);
        Dialect.AddTimestamp(command, "created_at", definition.CreatedAt);
        Dialect.AddTimestamp(command, "updated_at", definition.UpdatedAt);

        var saved = await DbHelpers.ReadSingleAsync(command, ReadQuota, cancellationToken).ConfigureAwait(false);

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
        DbHelpers.Add(command, "id", id);
        DbHelpers.AddTenant(command, tenantId);

        return await DbHelpers.ExecuteAsync(command, cancellationToken).ConfigureAwait(false) > 0;
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<QuotaUsageRecord>> GetUsageAsync(
        QuotaUsageQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var command = CreateCommand(_sql.SelectQuotaUsage);
        DbHelpers.AddTenant(command, query.TenantId);

        // 🚨 In the `(@p IS NULL OR col = @p)` pattern, when the parameter is
        // NULL the driver cannot infer its type and PostgreSQL returns
        // `42P08: could not determine data type`. Every optional filter
        // parameter must be typed EXPLICITLY.
        Dialect.AddText(command, "agent_name", query.AgentName);
        Dialect.AddInt16(command, "period", query.Period is { } period ? (short?)period : null);

        return await DbHelpers.ReadListAsync(command, ReadUsage, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask AddUsageAsync(
        QuotaConsumption consumption,
        IReadOnlyDictionary<QuotaPeriod, DateOnly> periodStarts,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(consumption);
        ArgumentNullException.ThrowIfNull(periodStarts);

        var connection = await _context.DataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);

        await using (connection.ConfigureAwait(false))
        {
            var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

            await using (transaction.ConfigureAwait(false))
            {
                foreach (var (period, periodStart) in periodStarts)
                {
                    // A run increments BOTH the agent counter AND the
                    // tenant-wide counter, so a tenant-wide rule can be
                    // queried without knowing the agent name.
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
        DbConnection connection,
        DbTransaction transaction,
        QuotaConsumption consumption,
        QuotaPeriod period,
        DateOnly periodStart,
        string agentName,
        CancellationToken cancellationToken)
    {
        var command = _context.CreateCommand(_sql.AddQuotaUsage, connection, transaction);

        DbHelpers.AddTenant(command, consumption.TenantId);
        DbHelpers.Add(command, "agent_name", agentName);
        DbHelpers.Add(command, "period", (short)period);
        DbHelpers.Add(command, "period_start", periodStart);
        DbHelpers.Add(command, "runs", consumption.Runs);
        DbHelpers.Add(command, "tokens", consumption.Tokens);

        // Fiyat tanimsizsa toplama SIFIR eklenir (para toplami degismez); NULL
        // eklemek toplami tumuyle NULL yapardi.
        DbHelpers.Add(command, "cost", consumption.Cost ?? 0m);
        Dialect.AddTimestamp(command, "updated_at", consumption.OccurredAt);

        await DbHelpers.ExecuteAsync(command, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask<bool> TryClaimThresholdNotificationAsync(
        string tenantId,
        string agentName,
        QuotaPeriod period,
        DateOnly periodStart,
        QuotaMetric metric,
        int thresholdPercent,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentNullException.ThrowIfNull(agentName);

        var command = CreateCommand(_sql.TryClaimQuotaThresholdNotification);

        DbHelpers.AddTenant(command, tenantId);
        DbHelpers.Add(command, "agent_name", agentName);
        DbHelpers.Add(command, "period", (short)period);
        DbHelpers.Add(command, "period_start", periodStart);
        DbHelpers.Add(command, "key", ThresholdKeyText(metric, thresholdPercent));

        return await DbHelpers.ExecuteAsync(command, cancellationToken).ConfigureAwait(false) > 0;
    }

    /// <summary>
    /// The <c>notified_thresholds</c> token for one metric/threshold pair.
    /// <c>InMemoryQuotaStore</c> keeps its own copy of this shape (this type
    /// is linked source, compiled separately into each SQL provider assembly,
    /// and cannot be shared with <c>Tracon.Core</c> across an assembly
    /// boundary); the two must stay behaviorally identical, the same
    /// contract every other method here already keeps with it.
    /// </summary>
    private static string ThresholdKeyText(QuotaMetric metric, int thresholdPercent)
        => string.Create(CultureInfo.InvariantCulture, $"{(int)metric}:{thresholdPercent}");

    private DbCommand CreateCommand(string sql) => _context.CreateCommand(sql);

    private static QuotaDefinition ReadQuota(DbDataReader reader)
        => new()
        {
            Id = reader.GetGuid(0),
            TenantId = reader.GetString(1),
            AgentName = DbHelpers.GetNullableString(reader, 2),
            Period = (QuotaPeriod)reader.GetInt16(3),
            MaxRuns = reader.IsDBNull(4) ? null : reader.GetInt64(4),
            MaxTokens = reader.IsDBNull(5) ? null : reader.GetInt64(5),
            MaxCost = DbHelpers.GetNullableDecimal(reader, 6),
            Enabled = reader.GetBoolean(7),
            CreatedAt = DbHelpers.GetTimestamp(reader, 8),
            UpdatedAt = DbHelpers.GetTimestamp(reader, 9),
        };

    private static QuotaUsageRecord ReadUsage(DbDataReader reader)
        => new()
        {
            TenantId = reader.GetString(0),
            AgentName = reader.GetString(1),
            Period = (QuotaPeriod)reader.GetInt16(2),
            PeriodStart = reader.GetFieldValue<DateOnly>(3),
            Runs = reader.GetInt64(4),
            Tokens = reader.GetInt64(5),
            Cost = reader.GetDecimal(6),
            UpdatedAt = DbHelpers.GetTimestamp(reader, 7),
        };
}
