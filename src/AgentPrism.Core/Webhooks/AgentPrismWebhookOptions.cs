namespace AgentPrism;

/// <summary>Event publishing (webhook) settings — Phase 21.</summary>
/// <remarks>
/// Read from the <c>AgentPrism:Webhooks</c> configuration section.
/// </remarks>
public sealed class AgentPrismWebhookOptions
{
    /// <summary>The configuration section name.</summary>
    public const string SectionName = "AgentPrism:Webhooks";

    /// <summary>
    /// Whether event publishing is enabled. While disabled, a subscription
    /// can be registered, but no event is written to the queue.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// 🚨 Whether delivery to private network addresses is allowed.
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
    /// This ladder is handed to Phase 17's queue through
    /// <c>ReleaseForRetryAsync(retryAfter)</c>; a second queue or scheduler
    /// is not written (K-160).
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
    /// the value recommended to the recipient in the README; AgentPrism only documents it.
    /// </summary>
    public TimeSpan SignatureTolerance { get; set; } = TimeSpan.FromMinutes(5);
}
