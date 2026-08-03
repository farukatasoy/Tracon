using System.Text.Json;
using Microsoft.Extensions.Options;
using Npgsql;
using NpgsqlTypes;

namespace AgentPrism;

/// <summary>Webhook aboneliklerini ve teslim gecmisini PostgreSQL'de saklayan depo.</summary>
/// <remarks>
/// 🚨 Bu depo hicbir zaman bir <strong>sir</strong> yazmaz veya okumaz; yalnizca
/// sirrin okunacagi yapilandirma anahtarinin <em>adini</em> tutar (K-059).
/// Veritabani yedegi, denetim izi ve arayuz yaniti bu yuzden sir tasimaz.
/// </remarks>
public sealed class PostgresWebhookStore : IWebhookStore
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly SqlQueries _sql;
    private readonly int _commandTimeout;

    /// <summary>Yeni bir webhook deposu olusturur.</summary>
    /// <param name="dataSource">Veri kaynagi.</param>
    /// <param name="options">PostgreSQL ayarlari.</param>
    /// <exception cref="ArgumentNullException">Bagimliliklardan biri <see langword="null"/> ise.</exception>
    public PostgresWebhookStore(NpgsqlDataSource dataSource, IOptions<AgentPrismPostgreSqlOptions> options)
    {
        ArgumentNullException.ThrowIfNull(dataSource);
        ArgumentNullException.ThrowIfNull(options);

        _dataSource = dataSource;
        _sql = new SqlQueries(options.Value.SchemaName);
        _commandTimeout = options.Value.CommandTimeoutSeconds;
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<WebhookSubscription>> ListSubscriptionsAsync(
        string tenantId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);

        var command = CreateCommand(_sql.SelectWebhookSubscriptions);
        command.Parameters.AddWithValue("tenant_id", tenantId);

        return await NpgsqlHelpers.ReadListAsync(command, ReadSubscription, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask<WebhookSubscription?> GetSubscriptionAsync(
        string tenantId,
        string name,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var command = CreateCommand(_sql.SelectWebhookSubscription);
        command.Parameters.AddWithValue("tenant_id", tenantId);
        command.Parameters.AddWithValue("name", name);

        return await NpgsqlHelpers.ReadSingleAsync(command, ReadSubscription, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<WebhookSubscription>> FindForEventAsync(
        string tenantId,
        string eventType,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(eventType);

        var command = CreateCommand(_sql.SelectWebhookSubscriptionsForEvent);
        command.Parameters.AddWithValue("tenant_id", tenantId);
        command.Parameters.AddWithValue("event_type", eventType);

        return await NpgsqlHelpers.ReadListAsync(command, ReadSubscription, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask<WebhookSubscription> SaveSubscriptionAsync(
        WebhookSubscription subscription,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(subscription);

        var command = CreateCommand(_sql.UpsertWebhookSubscription);
        command.Parameters.AddWithValue("id", subscription.Id);
        command.Parameters.AddWithValue("tenant_id", subscription.TenantId);
        command.Parameters.AddWithValue("name", subscription.Name);
        command.Parameters.AddWithValue("url", subscription.Url);
        command.Parameters.Add(new NpgsqlParameter("events", NpgsqlDbType.Array | NpgsqlDbType.Text)
        {
            Value = subscription.Events.ToArray(),
        });
        command.Parameters.AddWithValue(
            "secret_configuration_key",
            (object?)subscription.SecretConfigurationKey ?? DBNull.Value);
        command.Parameters.Add(new NpgsqlParameter("headers", NpgsqlDbType.Jsonb)
        {
            Value = SerializeHeaders(subscription.Headers),
        });
        command.Parameters.AddWithValue("enabled", subscription.Enabled);
        command.Parameters.AddWithValue("consecutive_failures", subscription.ConsecutiveFailures);
        command.Parameters.AddWithValue("created_at", subscription.CreatedAt.UtcDateTime);
        command.Parameters.AddWithValue("updated_at", subscription.UpdatedAt.UtcDateTime);

        var saved = await NpgsqlHelpers.ReadSingleAsync(command, ReadSubscription, cancellationToken).ConfigureAwait(false);

        return saved ?? subscription;
    }

    /// <inheritdoc />
    public async ValueTask<bool> DeleteSubscriptionAsync(
        string tenantId,
        string name,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        // Teslim gecmisi ON DELETE CASCADE ile birlikte silinir (migration 0012).
        var command = CreateCommand(_sql.DeleteWebhookSubscription);
        command.Parameters.AddWithValue("tenant_id", tenantId);
        command.Parameters.AddWithValue("name", name);

        return await NpgsqlHelpers.ExecuteAsync(command, cancellationToken).ConfigureAwait(false) > 0;
    }

    /// <inheritdoc />
    public async ValueTask<bool> RecordSubscriptionOutcomeAsync(
        Guid subscriptionId,
        bool succeeded,
        int disableThreshold,
        DateTimeOffset updatedAt,
        CancellationToken cancellationToken = default)
    {
        var command = CreateCommand(_sql.UpdateWebhookSubscriptionOutcome);
        command.Parameters.AddWithValue("id", subscriptionId);
        command.Parameters.AddWithValue("succeeded", succeeded);
        command.Parameters.AddWithValue("threshold", disableThreshold);
        command.Parameters.AddWithValue("updated_at", updatedAt.UtcDateTime);

        var result = await NpgsqlHelpers.ExecuteScalarAsync(command, cancellationToken).ConfigureAwait(false);

        return result is bool disabled && disabled;
    }

    /// <inheritdoc />
    public async ValueTask<WebhookDelivery> CreateDeliveryAsync(
        WebhookDelivery delivery,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(delivery);

        var command = CreateCommand(_sql.InsertWebhookDelivery);
        command.Parameters.AddWithValue("id", delivery.Id);
        command.Parameters.AddWithValue("subscription_id", delivery.SubscriptionId);
        command.Parameters.AddWithValue("tenant_id", delivery.TenantId);
        command.Parameters.AddWithValue("event_type", delivery.EventType);
        command.Parameters.AddWithValue("payload", delivery.Payload);
        command.Parameters.AddWithValue("status", (short)delivery.Status);
        command.Parameters.AddWithValue("attempt", (short)delivery.Attempt);
        command.Parameters.AddWithValue("response_code", (object?)delivery.ResponseCode ?? DBNull.Value);
        command.Parameters.AddWithValue("error", (object?)delivery.Error ?? DBNull.Value);
        command.Parameters.AddWithValue("created_at", delivery.CreatedAt.UtcDateTime);
        command.Parameters.AddWithValue(
            "delivered_at",
            delivery.DeliveredAt is { } deliveredAt ? deliveredAt.UtcDateTime : DBNull.Value);

        await NpgsqlHelpers.ExecuteAsync(command, cancellationToken).ConfigureAwait(false);

        return delivery;
    }

    /// <inheritdoc />
    public async ValueTask<WebhookDelivery?> GetDeliveryAsync(
        Guid deliveryId,
        CancellationToken cancellationToken = default)
    {
        var command = CreateCommand(_sql.SelectWebhookDelivery);
        command.Parameters.AddWithValue("id", deliveryId);

        return await NpgsqlHelpers.ReadSingleAsync(command, ReadDelivery, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask RecordDeliveryResultAsync(
        WebhookDeliveryResult result,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(result);

        var command = CreateCommand(_sql.UpdateWebhookDeliveryResult);
        command.Parameters.AddWithValue("id", result.DeliveryId);
        command.Parameters.AddWithValue("status", (short)result.Status);
        command.Parameters.AddWithValue("attempt", (short)result.Attempt);
        command.Parameters.AddWithValue("response_code", (object?)result.ResponseCode ?? DBNull.Value);
        command.Parameters.AddWithValue("error", (object?)result.Error ?? DBNull.Value);
        command.Parameters.AddWithValue("recorded_at", result.RecordedAt.UtcDateTime);

        await NpgsqlHelpers.ExecuteAsync(command, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<WebhookDelivery>> QueryDeliveriesAsync(
        WebhookDeliveryQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var command = CreateCommand(_sql.SelectWebhookDeliveries);
        command.Parameters.AddWithValue("tenant_id", query.TenantId);

        // 🚨 `(@p IS NULL OR col = @p)` deseninde parametre NULL olunca Npgsql
        // tipi cikaramaz (`42P08`). Isteğe bagli suzgec parametreleri ACIKCA
        // tiplenir.
        command.Parameters.Add(new NpgsqlParameter("subscription_id", NpgsqlDbType.Uuid)
        {
            Value = (object?)query.SubscriptionId ?? DBNull.Value,
        });
        command.Parameters.Add(new NpgsqlParameter("status", NpgsqlDbType.Smallint)
        {
            Value = query.Status is { } status ? (short)status : DBNull.Value,
        });
        command.Parameters.AddWithValue("skip", Math.Max(0, query.Skip));
        command.Parameters.AddWithValue("take", Math.Max(1, query.Take));

        return await NpgsqlHelpers.ReadListAsync(command, ReadDelivery, cancellationToken).ConfigureAwait(false);
    }

    private NpgsqlCommand CreateCommand(string sql)
    {
        var command = _dataSource.CreateCommand(sql);
        command.CommandTimeout = _commandTimeout;

        return command;
    }

    private static string SerializeHeaders(IReadOnlyDictionary<string, string> headers)
    {
        if (headers.Count == 0)
        {
            return "{}";
        }

        // Kaynak uretilmis baglam: AOT uyumlulugu icin yansimaya dayanan
        // serilestirme kullanilmaz.
        return JsonSerializer.Serialize(
            headers.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase),
            AgentPrismJsonContext.Default.DictionaryStringString);
    }

    private static Dictionary<string, string> DeserializeHeaders(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        var parsed = JsonSerializer.Deserialize(json, AgentPrismJsonContext.Default.DictionaryStringString);

        return parsed is null
            ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(parsed, StringComparer.OrdinalIgnoreCase);
    }

    private static WebhookSubscription ReadSubscription(NpgsqlDataReader reader)
        => new()
        {
            Id = reader.GetGuid(0),
            TenantId = reader.GetString(1),
            Name = reader.GetString(2),
            Url = reader.GetString(3),
            Events = reader.GetFieldValue<string[]>(4),
            SecretConfigurationKey = NpgsqlHelpers.GetNullableString(reader, 5),
            Headers = DeserializeHeaders(NpgsqlHelpers.GetNullableString(reader, 6)),
            Enabled = reader.GetBoolean(7),
            ConsecutiveFailures = reader.GetInt32(8),
            CreatedAt = NpgsqlHelpers.GetTimestamp(reader, 9),
            UpdatedAt = NpgsqlHelpers.GetTimestamp(reader, 10),
        };

    private static WebhookDelivery ReadDelivery(NpgsqlDataReader reader)
        => new()
        {
            Id = reader.GetGuid(0),
            SubscriptionId = reader.GetGuid(1),
            TenantId = reader.GetString(2),
            EventType = reader.GetString(3),
            Payload = reader.GetString(4),
            Status = (WebhookDeliveryStatus)reader.GetInt16(5),
            Attempt = reader.GetInt16(6),
            ResponseCode = reader.IsDBNull(7) ? null : reader.GetInt32(7),
            Error = NpgsqlHelpers.GetNullableString(reader, 8),
            CreatedAt = NpgsqlHelpers.GetTimestamp(reader, 9),
            DeliveredAt = NpgsqlHelpers.GetNullableTimestamp(reader, 10),
        };
}
