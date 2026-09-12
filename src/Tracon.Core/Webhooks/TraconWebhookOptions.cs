namespace Tracon;

/// <summary>Event publishing (webhook) settings.</summary>
/// <remarks>
/// Read from the <c>Tracon:Webhooks</c> configuration section.
/// </remarks>
public sealed class TraconWebhookOptions
{
    /// <summary>The configuration section name.</summary>
    public const string SectionName = "Tracon:Webhooks";

    /// <summary>
    /// Whether event publishing is enabled. While disabled, a subscription
    /// can be registered, but no event is written to the queue.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Whether delivery to private network addresses is allowed.
    /// <strong>Disabled by default.</strong>
    /// </summary>
    /// <remarks>
    /// Enabling this opens up an SSRF surface: the server becomes able to
    /// send requests to any service on the internal network, and to the
    /// cloud metadata endpoint (<c>169.254.169.254</c>). It should only be
    /// enabled deliberately, on a closed network.
    /// </remarks>
    public bool AllowPrivateNetworkTargets { get; set; }

    /// <summary>
    /// Whether unencrypted <c>http</c> targets are allowed. Even when
    /// allowed, only <strong>loopback</strong> addresses are accepted.
    /// </summary>
    /// <remarks>
    /// Needed to test a listener in local development. The loopback
    /// restriction is not lifted: sending an unencrypted event to a
    /// publicly reachable address would expose the event's content to the network.
    /// </remarks>
    public bool AllowInsecureHttp { get; set; }

    /// <summary>
    /// Gets or sets the only prefix under which a configuration key may be
    /// referenced as a subscription's signing secret. Default is
    /// <c>"Tracon:WebhookSecrets:"</c>.
    /// </summary>
    /// <remarks>
    /// A security boundary, not a convenience default — the same rationale as
    /// <see cref="TraconTenantProviderOptions.AllowedConfigurationPrefix"/>.
    /// Without it, a subscription could name an unrelated configuration key as
    /// its "signing secret" and Tracon would sign deliveries with a value
    /// that was never meant to leave the process.
    /// </remarks>
    public string AllowedConfigurationPrefix { get; set; } = "Tracon:WebhookSecrets:";

    /// <summary>
    /// Gets or sets how many extra headers a subscription may add to a
    /// delivery. Default 20.
    /// </summary>
    /// <remarks>
    /// Extra headers are administrator input and travel on every delivery.
    /// Headers whose name Tracon sets itself are always dropped, whatever
    /// this limit is.
    /// </remarks>
    public int MaxExtraHeaders { get; set; } = 20;

    /// <summary>The timeout for a single delivery attempt.</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>The maximum bytes read from the recipient's response body.</summary>
    /// <remarks>
    /// The response body is kept only for diagnostics. Reading it without a
    /// limit would let a malicious recipient exhaust memory.
    /// </remarks>
    public int MaxResponseBytes { get; set; } = 8 * 1024;

    /// <summary>
    /// If a subscription fails this many times in a row, it is automatically
    /// disabled and the event is written to the audit trail.
    /// </summary>
    public int DisableAfterConsecutiveFailures { get; set; } = 20;

    /// <summary>
    /// The retry ladder. The list's length is also the maximum number of attempts.
    /// </summary>
    /// <remarks>
    /// This ladder is handed to the queue through
    /// <c>ReleaseForRetryAsync(retryAfter)</c>; a second queue or scheduler
    /// is not written.
    /// </remarks>
    public IList<TimeSpan> RetryDelays { get; } =
    [
        TimeSpan.FromMinutes(1),
        TimeSpan.FromMinutes(5),
        TimeSpan.FromMinutes(30),
        TimeSpan.FromHours(2),
        TimeSpan.FromHours(6),
    ];

    /// <summary>
    /// The signature timestamp tolerance the recipient should accept. This is
    /// the value recommended to the recipient in the README; Tracon only documents it.
    /// </summary>
    public TimeSpan SignatureTolerance { get; set; } = TimeSpan.FromMinutes(5);
}
