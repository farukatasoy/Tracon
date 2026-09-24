using System.Text.Json.Serialization;

namespace Tracon;

/// <summary>The status of a webhook delivery attempt.</summary>
/// <remarks>
/// Written as a name in JSON, stored as <c>smallint</c> in the database. The
/// value order <strong>must not change</strong> — only append.
/// </remarks>
[JsonConverter(typeof(JsonStringEnumConverter<WebhookDeliveryStatus>))]
public enum WebhookDeliveryStatus
{
    /// <summary>The delivery is queued, not yet attempted or to be retried.</summary>
    Pending = 0,

    /// <summary>The recipient returned <c>2xx</c>.</summary>
    Delivered = 1,

    /// <summary>All attempts were exhausted.</summary>
    Failed = 2,

    /// <summary>The delivery was never attempted: the subscription is disabled or the target address was rejected.</summary>
    Dropped = 3,
}

/// <summary>The event types Tracon can publish.</summary>
/// <remarks>
/// Event names are part of the contract and <strong>must not change</strong>:
/// subscribers save these strings.
/// </remarks>
public static class WebhookEvents
{
    /// <summary>A run completed successfully.</summary>
    public const string RunCompleted = "run.completed";

    /// <summary>A run ended in an error.</summary>
    public const string RunFailed = "run.failed";

    /// <summary>A tool call is awaiting approval.</summary>
    public const string ApprovalPending = "approval.pending";

    /// <summary>A workflow is awaiting human input.</summary>
    public const string WorkflowRequestPending = "workflow.request.pending";

    /// <summary>A queued job completed successfully.</summary>
    public const string JobCompleted = "job.completed";

    /// <summary>A queued job ended in an error.</summary>
    public const string JobFailed = "job.failed";

    /// <summary>An evaluation run completed.</summary>
    public const string EvalCompleted = "eval.completed";

    /// <summary>A quota threshold was exceeded (80% or 100%).</summary>
    public const string QuotaThreshold = "quota.threshold";

    /// <summary>
    /// The online evaluation window's average score dropped below the
    /// threshold. A single low score does NOT TRIGGER this event —
    /// the minimum sample count must be exceeded.
    /// </summary>
    public const string RunScoreLow = "run.score.low";

    /// <summary>The test event sent to verify a subscription endpoint.</summary>
    public const string Test = "test.ping";

    /// <summary>All recognized event names.</summary>
    public static IReadOnlyList<string> All { get; } =
    [
        RunCompleted,
        RunFailed,
        ApprovalPending,
        WorkflowRequestPending,
        JobCompleted,
        JobFailed,
        EvalCompleted,
        QuotaThreshold,
        RunScoreLow,
        Test,
    ];

    /// <summary>Reports whether an event name is recognized.</summary>
    /// <param name="eventType">The event name.</param>
    /// <returns><see langword="true"/> if the name is recognized.</returns>
    public static bool IsKnown(string? eventType)
        => eventType is not null && All.Contains(eventType, StringComparer.Ordinal);
}

/// <summary>An external system's event subscription.</summary>
/// <remarks>
/// This record <strong>has no secret field</strong>. The signing secret
/// does not sit in the database; only the name of the configuration key the
/// value is read from (<see cref="SecretConfigurationKey"/>) sits here, and
/// the value is resolved at run time through <c>IConfiguration</c>.
/// </remarks>
public sealed record WebhookSubscription
{
    /// <summary>The subscription identifier.</summary>
    public required Guid Id { get; init; }

    /// <summary>The tenant the subscription belongs to.</summary>
    public required string TenantId { get; init; }

    /// <summary>The name, unique within the tenant.</summary>
    public required string Name { get; init; }

    /// <summary>The address events are sent to.</summary>
    /// <remarks>
    /// The address goes through an SSRF check: only <c>https</c> (or
    /// <c>http</c> for loopback), private network ranges are rejected,
    /// redirects are not followed.
    /// </remarks>
    public required string Url { get; init; }

    /// <summary>The subscribed event names. See <see cref="WebhookEvents"/>.</summary>
    public required IReadOnlyList<string> Events { get; init; }

    /// <summary>
    /// The <strong>name</strong> of the configuration key the signing secret
    /// is read from. Not the secret itself.
    /// </summary>
    /// <remarks>
    /// Example: <c>"Tracon:WebhookSecrets:order-service"</c>. The value
    /// lives in <c>dotnet user-secrets</c> or an environment variable. If
    /// empty, requests are not signed.
    /// </remarks>
    public string? SecretConfigurationKey { get; init; }

    /// <summary>
    /// Extra headers added to every request. An HTTP response never returns a
    /// stored value: it carries every header name with the value <c>***</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// An authentication header is <strong>not written here</strong>: the
    /// value would be stored in the database. Declare it in
    /// <c>HeaderConfigurationKeys</c> instead, and use
    /// <see cref="SecretConfigurationKey"/> for signing. The HTTP save rejects a
    /// header whose name looks like a credential (<c>Authorization</c>,
    /// <c>X-Api-Key</c>, <c>Cookie</c>, any name ending in <c>-key</c>).
    /// </para>
    /// <para>
    /// The read, list and save responses mask every value. A save whose
    /// header value is <c>***</c> is rejected, so a client that reads, edits
    /// and saves a subscription must send every header value again.
    /// </para>
    /// </remarks>
    public IReadOnlyDictionary<string, string> Headers { get; init; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Credential headers, as a map from the header name to the NAME of the
    /// configuration key its value is read from. The value is resolved through
    /// <c>IConfiguration</c> on every delivery and is never stored.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every key name must sit under
    /// <c>TraconWebhookOptions.AllowedConfigurationPrefix</c>, inside the
    /// tenant's own key space (<c>{prefix}{tenantId}:...</c>; a flat name
    /// directly under the prefix belongs to the default tenant). The rule is
    /// checked on save and again before every delivery; a delivery whose
    /// subscription names a key outside the tenant is dropped.
    /// </para>
    /// <para>
    /// A header name may appear only once across <see cref="Headers"/> and
    /// this map, compared case-insensitively, and may not be one of the
    /// headers Tracon sets itself (<c>X-Tracon-*</c>). The map counts toward
    /// <c>TraconWebhookOptions.MaxExtraHeaders</c> together with
    /// <see cref="Headers"/>.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// var record = new WebhookSubscription
    /// {
    ///     Id = Guid.NewGuid(),
    ///     TenantId = "acme",
    ///     Name = "orders",
    ///     Url = "https://orders.example.com/hook",
    ///     Events = [WebhookEvents.RunCompleted],
    ///     CreatedAt = DateTimeOffset.UtcNow,
    ///     UpdatedAt = DateTimeOffset.UtcNow,
    ///     HeaderConfigurationKeys = new Dictionary&lt;string, string&gt;
    ///     {
    ///         ["X-Api-Key"] = "Tracon:WebhookSecrets:acme:OrdersKey",
    ///     },
    /// };
    /// </code>
    /// </example>
    public IReadOnlyDictionary<string, string> HeaderConfigurationKeys { get; init; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Whether the subscription is enabled.</summary>
    public bool Enabled { get; init; } = true;

    /// <summary>
    /// The consecutive-failure count. Once the threshold is exceeded, the
    /// subscription disables itself and is written to the audit trail.
    /// </summary>
    public int ConsecutiveFailures { get; init; }

    /// <summary>The creation time (UTC).</summary>
    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>The last-updated time (UTC).</summary>
    public required DateTimeOffset UpdatedAt { get; init; }
}

/// <summary>A single delivery record.</summary>
/// <remarks>
/// This record is <strong>not a queue row</strong>: scheduling, leasing, and
/// retry live in the <c>jobs</c> table. The <see cref="Attempt"/>
/// field here only reports history.
/// </remarks>
public sealed record WebhookDelivery
{
    /// <summary>The delivery identifier. Sent in the request as the <c>X-Tracon-Delivery</c> header.</summary>
    public required Guid Id { get; init; }

    /// <summary>The subscription identifier.</summary>
    public required Guid SubscriptionId { get; init; }

    /// <summary>The tenant identifier.</summary>
    public required string TenantId { get; init; }

    /// <summary>The event name.</summary>
    public required string EventType { get; init; }

    /// <summary>The JSON body sent.</summary>
    /// <remarks>
    /// Carries only a <strong>summary</strong>: identifier, status, agent,
    /// tokens, cost. Message content never sits here.
    /// </remarks>
    public required string Payload { get; init; }

    /// <summary>The delivery's status.</summary>
    public required WebhookDeliveryStatus Status { get; init; }

    /// <summary>The number of attempts made.</summary>
    public int Attempt { get; init; }

    /// <summary>
    /// The HTTP status code the recipient returned. <see langword="null"/> if a
    /// connection could not be established.
    /// </summary>
    public int? ResponseCode { get; init; }

    /// <summary>The most recent error message.</summary>
    public string? Error { get; init; }

    /// <summary>The creation time (UTC).</summary>
    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>The successful delivery time (UTC).</summary>
    public DateTimeOffset? DeliveredAt { get; init; }
}

/// <summary>The result of a delivery attempt.</summary>
public sealed record WebhookDeliveryResult
{
    /// <summary>The delivery identifier.</summary>
    public required Guid DeliveryId { get; init; }

    /// <summary>The final status.</summary>
    public required WebhookDeliveryStatus Status { get; init; }

    /// <summary>The attempt number.</summary>
    public required int Attempt { get; init; }

    /// <summary>The HTTP status code the recipient returned.</summary>
    public int? ResponseCode { get; init; }

    /// <summary>The error message.</summary>
    public string? Error { get; init; }

    /// <summary>The moment the result was recorded (UTC).</summary>
    public required DateTimeOffset RecordedAt { get; init; }
}

/// <summary>A filter for querying delivery history.</summary>
public sealed record WebhookDeliveryQuery
{
    /// <summary>The tenant identifier.</summary>
    public required string TenantId { get; init; }

    /// <summary>Fetches only this subscription's deliveries.</summary>
    public Guid? SubscriptionId { get; init; }

    /// <summary>Fetches only deliveries in this status.</summary>
    public WebhookDeliveryStatus? Status { get; init; }

    /// <summary>The number of records to skip.</summary>
    public int Skip { get; init; }

    /// <summary>The maximum number of records to fetch.</summary>
    public int Take { get; init; } = 50;
}
