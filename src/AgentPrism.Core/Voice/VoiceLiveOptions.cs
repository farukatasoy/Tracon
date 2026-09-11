namespace AgentPrism;

/// <summary>Options of the provider-hosted live voice layer.</summary>
/// <remarks>
/// <para>
/// This is <strong>not</strong> <see cref="VoiceConversationOptions"/>. That
/// type configures the conversation AgentPrism runs itself: utterance buffering,
/// audio format, per-turn speech budget. None of that applies here, because the
/// media never reaches AgentPrism. The two layers are independent and can be
/// enabled together or separately.
/// </para>
/// <para>
/// The type is <strong>not</strong> a <c>record</c>: option classes must not
/// produce a <c>ToString</c> that can be written to a log.
/// </para>
/// </remarks>
public sealed class VoiceLiveOptions
{
    /// <summary>The default name of the configuration section.</summary>
    public const string SectionName = "AgentPrism:Voice:Live";

    /// <summary>
    /// Gets or sets how many delegations may run at once in one session. The default is 2.
    /// </summary>
    /// <remarks>
    /// The ceiling exists because the <em>provider</em> decides when to delegate.
    /// Without it, a model that delegates in a loop decides how many agent runs
    /// AgentPrism starts — a denial-of-service axis that points into the consumer's
    /// own model spend.
    /// </remarks>
    public int MaxConcurrentDelegations { get; set; } = 2;

    /// <summary>
    /// Gets or sets how many appends one delegation may send. The default is 12.
    /// </summary>
    public int MaxAppendsPerDelegation { get; set; } = 12;

    /// <summary>
    /// Gets or sets how many characters of transcript the ledger keeps. The default
    /// is 20000.
    /// </summary>
    /// <remarks>
    /// The ledger is what a delegation is cut from. It is bounded so a long
    /// conversation cannot grow without limit; the oldest entries are dropped first.
    /// </remarks>
    public int MaxTranscriptLedgerCharacters { get; set; } = 20_000;

    /// <summary>
    /// Gets or sets how many transcript entries one delegation's prompt may carry.
    /// The default is 40.
    /// </summary>
    public int MaxLedgerEntriesPerDelegation { get; set; } = 40;

    /// <summary>Gets or sets how long one delegation may run. The default is 2 minutes.</summary>
    public TimeSpan DelegationTimeout { get; set; } = TimeSpan.FromMinutes(2);

    /// <summary>Gets or sets how the live model hands work back. The default is <c>Client</c>.</summary>
    public LiveVoiceDelegationMode DelegationMode { get; set; } = LiveVoiceDelegationMode.Client;

    /// <summary>Gets or sets the system instructions given to the live model.</summary>
    public string? Instructions { get; set; }

    /// <summary>
    /// Gets or sets whether the conversation's transcript is written to the agent
    /// session's history. The default is <see langword="true"/>.
    /// </summary>
    /// <remarks>
    /// A privacy switch. The user's <em>audio</em> is never stored, but the
    /// <em>text</em> of what they said is durable while this is on, and the delegated
    /// runs then see the full conversation. Turning it off keeps the ledger in memory
    /// only; delegation still works, with the same context, for the session's lifetime.
    /// Either way the value is reported to the client when the session is created, so
    /// no recording happens silently.
    /// </remarks>
    public bool PersistTranscript { get; set; } = true;

    /// <summary>Gets or sets how long a live session may stay open. The default is 30 minutes.</summary>
    public TimeSpan MaxSessionDuration { get; set; } = TimeSpan.FromMinutes(30);

    /// <summary>
    /// Gets or sets how long a created session may wait for its media peer. The
    /// default is 2 minutes.
    /// </summary>
    /// <remarks>
    /// A session that is created and never connected still occupies a tenant's
    /// concurrency slot. When this elapses the record is closed with
    /// <see cref="VoiceSessionEndReason.Abandoned"/>.
    /// </remarks>
    public TimeSpan PendingSessionTimeout { get; set; } = TimeSpan.FromMinutes(2);

    /// <summary>
    /// Gets or sets how many live sessions one tenant may hold open at once. The
    /// default is 3.
    /// </summary>
    /// <remarks>
    /// The limit is checked <strong>before</strong> the provider is called. A
    /// session that is created and then refused is still a billed session.
    /// </remarks>
    public int MaxConcurrentSessionsPerTenant { get; set; } = 3;
}
