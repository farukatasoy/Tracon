using System.Data.Common;
using System.Text.Json;

namespace Tracon;

/// <summary>Stores webhook subscriptions and delivery history in the SQL database.</summary>
/// <remarks>
/// This store never writes or reads a <strong>secret</strong>; it only holds
/// the <em>name</em> of the configuration key the secret will be read from.
/// The database backup, audit trail, and UI response therefore carry
/// no secret.
/// </remarks>
internal sealed class SqlWebhookStore : IWebhookStore
{
    private readonly SqlStoreContext _context;
    private readonly SqlQueriesBase _sql;

    /// <summary>Creates a new webhook store.</summary>
    /// <param name="context">The store context.</param>
    /// <exception cref="ArgumentNullException">One of the dependencies is <see langword="null"/>.</exception>
    public SqlWebhookStore(SqlStoreContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        _context = context;
        _sql = context.Sql;
    }

    /// <summary>The gateway for provider-specific behavior.</summary>
    private SqlDialect Dialect => _context.Dialect;

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<WebhookSubscription>> ListSubscriptionsAsync(
        string tenantId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);

        var command = CreateCommand(_sql.SelectWebhookSubscriptions);
        DbHelpers.AddTenant(command, tenantId);

        return await DbHelpers.ReadListAsync(command, ReadSubscription, cancellationToken).ConfigureAwait(false);
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
        DbHelpers.AddTenant(command, tenantId);
        DbHelpers.Add(command, "name", name);

        return await DbHelpers.ReadSingleAsync(command, ReadSubscription, cancellationToken).ConfigureAwait(false);
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
        DbHelpers.AddTenant(command, tenantId);
        DbHelpers.Add(command, "event_type", eventType);

        return await DbHelpers.ReadListAsync(command, ReadSubscription, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask<WebhookSubscription> SaveSubscriptionAsync(
        WebhookSubscription subscription,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(subscription);

        var command = CreateCommand(_sql.UpsertWebhookSubscription);
        DbHelpers.Add(command, "id", subscription.Id);
        DbHelpers.AddTenant(command, subscription.TenantId);
        DbHelpers.Add(command, "name", subscription.Name);
        DbHelpers.Add(command, "url", subscription.Url);
        Dialect.AddTextArray(command, "events", subscription.Events);
        Dialect.AddText(command, "secret_configuration_key", subscription.SecretConfigurationKey);
        Dialect.AddJsonb(command, "headers", SerializeHeaders(subscription.Headers));
        Dialect.AddJsonb(command, "header_configuration_keys", SerializeHeaders(subscription.HeaderConfigurationKeys));
        DbHelpers.Add(command, "enabled", subscription.Enabled);
        DbHelpers.Add(command, "consecutive_failures", subscription.ConsecutiveFailures);
        Dialect.AddTimestamp(command, "created_at", subscription.CreatedAt);
        Dialect.AddTimestamp(command, "updated_at", subscription.UpdatedAt);

        var saved = await DbHelpers.ReadSingleAsync(command, ReadSubscription, cancellationToken).ConfigureAwait(false);

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

        // The delivery history is deleted along with it via ON DELETE CASCADE (migration 0012).
        var command = CreateCommand(_sql.DeleteWebhookSubscription);
        DbHelpers.AddTenant(command, tenantId);
        DbHelpers.Add(command, "name", name);

        return await DbHelpers.ExecuteAsync(command, cancellationToken).ConfigureAwait(false) > 0;
    }

    /// <inheritdoc />
    [TenantAgnostic(
        "The delivery worker processes the outcome using the subscription id carried by the delivery; there is no separate tenant intent in the call.")]
    public async ValueTask<bool> RecordSubscriptionOutcomeAsync(
        Guid subscriptionId,
        bool succeeded,
        int disableThreshold,
        DateTimeOffset updatedAt,
        CancellationToken cancellationToken = default)
    {
        var command = CreateCommand(_sql.UpdateWebhookSubscriptionOutcome);
        DbHelpers.Add(command, "id", subscriptionId);
        DbHelpers.Add(command, "succeeded", succeeded);
        DbHelpers.Add(command, "threshold", disableThreshold);
        Dialect.AddTimestamp(command, "updated_at", updatedAt);

        var result = await DbHelpers.ExecuteScalarAsync(command, cancellationToken).ConfigureAwait(false);

        return result is not null && DbHelpers.ToBoolean(result);
    }

    /// <inheritdoc />
    public async ValueTask<WebhookDelivery> CreateDeliveryAsync(
        WebhookDelivery delivery,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(delivery);

        var command = CreateCommand(_sql.InsertWebhookDelivery);
        DbHelpers.Add(command, "id", delivery.Id);
        DbHelpers.Add(command, "subscription_id", delivery.SubscriptionId);
        DbHelpers.AddTenant(command, delivery.TenantId);
        DbHelpers.Add(command, "event_type", delivery.EventType);
        DbHelpers.Add(command, "payload", delivery.Payload);
        DbHelpers.Add(command, "status", (short)delivery.Status);
        DbHelpers.Add(command, "attempt", (short)delivery.Attempt);
        Dialect.AddInt32(command, "response_code", delivery.ResponseCode);
        Dialect.AddText(command, "error", delivery.Error);
        Dialect.AddTimestamp(command, "created_at", delivery.CreatedAt);
        Dialect.AddTimestamp(command, "delivered_at", delivery.DeliveredAt);

        await DbHelpers.ExecuteAsync(command, cancellationToken).ConfigureAwait(false);

        return delivery;
    }

    /// <inheritdoc />
    [TenantAgnostic(
        "The delivery id is generated only by the code that OPENS the delivery job and NEVER appears on the HTTP surface; the tenant is inherited from the subscription that produced the delivery.")]
    public async ValueTask<WebhookDelivery?> GetDeliveryAsync(
        Guid deliveryId,
        CancellationToken cancellationToken = default)
    {
        var command = CreateCommand(_sql.SelectWebhookDelivery);
        DbHelpers.Add(command, "id", deliveryId);

        return await DbHelpers.ReadSingleAsync(command, ReadDelivery, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    [TenantAgnostic(
        "Same rationale as GetDeliveryAsync: the delivery id comes from the job queue.")]
    public async ValueTask RecordDeliveryResultAsync(
        WebhookDeliveryResult result,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(result);

        var command = CreateCommand(_sql.UpdateWebhookDeliveryResult);
        DbHelpers.Add(command, "id", result.DeliveryId);
        DbHelpers.Add(command, "status", (short)result.Status);
        DbHelpers.Add(command, "attempt", (short)result.Attempt);
        Dialect.AddInt32(command, "response_code", result.ResponseCode);
        Dialect.AddText(command, "error", result.Error);
        Dialect.AddTimestamp(command, "recorded_at", result.RecordedAt);

        await DbHelpers.ExecuteAsync(command, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<WebhookDelivery>> QueryDeliveriesAsync(
        WebhookDeliveryQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var command = CreateCommand(_sql.SelectWebhookDeliveries);
        DbHelpers.AddTenant(command, query.TenantId);

        // 🚨 In the `(@p IS NULL OR col = @p)` pattern, when the parameter is
        // NULL the driver cannot infer its type (`42P08`). Optional filter
        // parameters are therefore typed EXPLICITLY.
        Dialect.AddUuid(command, "subscription_id", query.SubscriptionId);
        Dialect.AddInt16(command, "status", query.Status is { } status ? (short?)status : null);
        DbHelpers.Add(command, "skip", Math.Max(0, query.Skip));
        DbHelpers.Add(command, "take", Math.Max(1, query.Take));

        return await DbHelpers.ReadListAsync(command, ReadDelivery, cancellationToken).ConfigureAwait(false);
    }

    private DbCommand CreateCommand(string sql) => _context.CreateCommand(sql);

    private static string SerializeHeaders(IReadOnlyDictionary<string, string> headers)
    {
        if (headers.Count == 0)
        {
            return "{}";
        }

        // 🚨 Copied with the indexer, not ToDictionary: an Ordinal source that
        // carries 'X-A' and 'x-a' made ToDictionary throw ArgumentException and
        // the save answered 500. The last spelling wins, as it does when the
        // record is read back; the HTTP save rejects such a pair with 400
        // before it gets here.
        var copy = new Dictionary<string, string>(headers.Count, StringComparer.OrdinalIgnoreCase);

        foreach (var (name, value) in headers)
        {
            copy[name] = value;
        }

        // Source-generated context: reflection-based serialization is not
        // used, for AOT compatibility.
        return JsonSerializer.Serialize(copy, TraconJsonContext.Default.DictionaryStringString);
    }

    private static Dictionary<string, string> DeserializeHeaders(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        var parsed = JsonSerializer.Deserialize(json, TraconJsonContext.Default.DictionaryStringString);

        return parsed is null
            ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(parsed, StringComparer.OrdinalIgnoreCase);
    }

    private WebhookSubscription ReadSubscription(DbDataReader reader)
        => new()
        {
            Id = reader.GetGuid(0),
            TenantId = reader.GetString(1),
            Name = reader.GetString(2),
            Url = reader.GetString(3),
            Events = Dialect.ReadTextArray(reader, 4),
            SecretConfigurationKey = DbHelpers.GetNullableString(reader, 5),
            Headers = DeserializeHeaders(DbHelpers.GetNullableString(reader, 6)),
            Enabled = reader.GetBoolean(7),
            ConsecutiveFailures = reader.GetInt32(8),
            CreatedAt = DbHelpers.GetTimestamp(reader, 9),
            UpdatedAt = DbHelpers.GetTimestamp(reader, 10),

            // Last in the column list (WebhookSubscriptionColumns): the column
            // arrived with phase 190's migration, and the reader addresses
            // columns by position.
            HeaderConfigurationKeys = DeserializeHeaders(DbHelpers.GetNullableString(reader, 11)),
        };

    private static WebhookDelivery ReadDelivery(DbDataReader reader)
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
            Error = DbHelpers.GetNullableString(reader, 8),
            CreatedAt = DbHelpers.GetTimestamp(reader, 9),
            DeliveredAt = DbHelpers.GetNullableTimestamp(reader, 10),
        };
}
