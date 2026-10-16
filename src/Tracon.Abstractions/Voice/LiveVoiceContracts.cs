using System.Text.Json.Serialization;

namespace Tracon;

/// <summary>Creates and supervises a provider-hosted live voice session.</summary>
/// <remarks>
/// <para>
/// A live session is <strong>not</strong> the conversation layer behind
/// <c>UseVoiceConversation()</c>. There, Tracon receives the audio, transcribes
/// it, runs the agent and synthesizes the answer. Here the provider hosts the whole
/// conversation and carries the media directly to the media peer; Tracon stands
/// in two places only: it <em>creates</em> the session, so tenancy, role and
/// concurrency gates apply and the raw API key never reaches the browser, and it
/// <em>attaches</em> a server-side control connection, so the work the model
/// delegates becomes an ordinary Tracon run.
/// </para>
/// <para>
/// <strong>Lifetime:</strong> registered <c>singleton</c>. One instance serves every
/// session in the process, so an implementation must be thread-safe and must hold no
/// per-session state of its own — <see cref="ILiveVoiceSideband"/> is where a
/// session's state lives.
/// </para>
/// <para>
/// <strong>Tenant:</strong> <c>tenant-independent</c>, like
/// <see cref="ISpeechSynthesizer"/>. The caller has already resolved and gated the
/// tenant before a session is created: Tracon applies the tenant's egress policy to
/// <see cref="ProviderName"/> and resolves the tenant's own credential from its
/// provider binding, exactly as for a model call. The provider neither reads nor
/// enforces a tenant, and must not key any cache or credential by tenant; a
/// tenant credential reaches it only through the request
/// (<see cref="LiveVoiceCreateRequest.Credential"/>,
/// <see cref="LiveVoiceAttachRequest.Credential"/>), and only when
/// <see cref="SupportsTenantCredentials"/> is <see langword="true"/>.
/// </para>
/// <para>
/// <strong>Delivery:</strong> <c>best-effort</c>. Creating a session and attaching to
/// it are ordinary network calls with no retry and no idempotency key: a failure is
/// surfaced to the caller, never retried silently, because a retried create is a
/// second billed session.
/// </para>
/// </remarks>
public interface ILiveVoiceProvider
{
    /// <summary>Gets the provider name written to the session record, e.g. <c>openai</c>.</summary>
    string ProviderName { get; }

    /// <summary>Gets the model identifier written to the session record.</summary>
    string ModelId { get; }

    /// <summary>
    /// Gets the largest number of characters a single
    /// <see cref="ILiveVoiceSideband.AppendAsync"/> may carry.
    /// </summary>
    /// <remarks>
    /// The ceiling is in <strong>characters</strong>, not tokens: Tracon ships no
    /// tokenizer and must not take one on (AOT, and no new package). An implementation
    /// whose provider states the limit in tokens converts it once, conservatively, and
    /// documents the ratio in a single place.
    /// </remarks>
    int MaxAppendCharacters { get; }

    /// <summary>Relays the media peer's SDP offer and creates the session.</summary>
    /// <param name="request">The offer and the session settings.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The provider's session identifier and its SDP answer.</returns>
    ValueTask<LiveVoiceSessionHandle> CreateSessionAsync(
        LiveVoiceCreateRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Opens a server-side control connection to an existing session.</summary>
    /// <param name="providerSessionId">The identifier returned by <see cref="CreateSessionAsync"/>.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The sideband connection.</returns>
    /// <remarks>
    /// The connection carries <strong>no</strong> primary audio: WebRTC or SIP carries
    /// that. Both connections share one session.
    /// </remarks>
    ValueTask<ILiveVoiceSideband> AttachAsync(
        string providerSessionId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets whether the provider honours a tenant credential passed in
    /// <see cref="LiveVoiceCreateRequest.Credential"/> and
    /// <see cref="LiveVoiceAttachRequest.Credential"/>.
    /// </summary>
    /// <remarks>
    /// The default is <see langword="false"/>. Tracon then refuses a session for a
    /// tenant that has its own binding for <see cref="ProviderName"/> rather than
    /// open it on the provider's configured credential, which would bill the wrong
    /// account. An implementation that returns <see langword="true"/> uses the
    /// credential for both calls of the session and never falls back to its
    /// configured one.
    /// </remarks>
    bool SupportsTenantCredentials => false;

    /// <summary>Opens a server-side control connection to an existing session.</summary>
    /// <param name="request">The provider's session identifier and the credential to connect with.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The sideband connection.</returns>
    /// <remarks>
    /// The default implementation calls <see cref="AttachAsync(string, CancellationToken)"/>
    /// when no credential is given and throws <see cref="NotSupportedException"/> when
    /// one is: an implementation that cannot use the tenant's credential must not
    /// connect with its own instead. Tracon calls this method, the pair of
    /// <see cref="CreateSessionAsync"/>.
    /// </remarks>
    /// <exception cref="NotSupportedException">
    /// <see cref="LiveVoiceAttachRequest.Credential"/> is set and the implementation does
    /// not override this method.
    /// </exception>
    ValueTask<ILiveVoiceSideband> AttachSessionAsync(
        LiveVoiceAttachRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        return request.Credential is null
            ? AttachAsync(request.ProviderSessionId, cancellationToken)
            : throw new NotSupportedException(
                $"The live voice provider '{ProviderName}' does not accept a tenant credential; " +
                "it would connect with its own configured credential instead.");
    }
}

/// <summary>The server-side control connection of a live voice session.</summary>
/// <remarks>
/// <para>
/// <strong>Lifetime:</strong> never resolved from dependency injection, so neither
/// <c>singleton</c>, <c>scoped</c> nor <c>transient</c> describes it. One instance
/// belongs to one session, is produced by
/// <see cref="ILiveVoiceProvider.AttachSessionAsync"/>, and is disposed when that session
/// ends. <see cref="AppendAsync"/> may be called concurrently by several delegations,
/// so an implementation serializes its writes; <see cref="ReceiveAsync"/> is consumed
/// by a single pump.
/// </para>
/// <para>
/// <strong>Tenant:</strong> <c>tenant-independent</c>. The connection carries no
/// tenant of its own — the caller establishes the session's tenant before the first
/// event is handled, and every run the delegations produce inherits it from there.
/// </para>
/// <para>
/// <strong>Delivery:</strong> <c>best-effort</c> in both directions. An append that
/// the provider refuses is reported, not retried, because a retried append can make
/// the model say the same sentence twice; and the event stream ends when the socket
/// does, with no replay of what was missed.
/// </para>
/// <para>
/// There is deliberately no <c>InterruptAsync</c>: the provider surface offers no way
/// for the sideband to stop the model mid-sentence. Should one appear, it is added as
/// a new <see cref="LiveVoiceAppendChannel"/> value rather than as a new interface
/// member, so the record and the enum grow while the interface does not.
/// </para>
/// <para>
/// <strong>Disposal races the pump.</strong> The host may call <see cref="IAsyncDisposable.DisposeAsync"/>
/// from a caller-initiated close while the single pump described above still has an
/// outstanding <see cref="ReceiveAsync"/> call in flight — closing a session does not
/// wait for the pump to notice first. An implementation over a transport with its own
/// two-way close (a <c>ClientWebSocket</c>, for one) must not call that two-way close
/// from its own dispose: it typically reads as part of completing the close
/// handshake, and a second, concurrent reader racing the pump's own read can consume a
/// message the pump never sees, even though the two-way call itself reports success.
/// Send a one-way close signal instead (<c>CloseOutputAsync</c> on a
/// <c>ClientWebSocket</c>) and let the pump's own loop end on its own — only tearing
/// the transport down once that loop has actually finished, bounded by a timeout for a
/// peer that never acknowledges.
/// </para>
/// </remarks>
public interface ILiveVoiceSideband : IAsyncDisposable
{
    /// <summary>Sends text into the live conversation's context.</summary>
    /// <param name="append">The channel, the delegation and the text.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The completion task.</returns>
    ValueTask AppendAsync(LiveVoiceAppend append, CancellationToken cancellationToken = default);

    /// <summary>Reads the session's events until the session closes.</summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The event stream.</returns>
    IAsyncEnumerable<LiveVoiceEvent> ReceiveAsync(CancellationToken cancellationToken = default);
}

/// <summary>The request that creates a live voice session.</summary>
public sealed record LiveVoiceCreateRequest
{
    /// <summary>Gets the media peer's SDP offer.</summary>
    public required string SdpOffer { get; init; }

    /// <summary>Gets the session settings.</summary>
    public LiveVoiceSessionOptions Options { get; init; } = new();

    /// <summary>
    /// Gets the tenant's own credential for the provider, or <see langword="null"/>
    /// to use the credential the provider was configured with.
    /// </summary>
    /// <remarks>
    /// Tracon resolves it from the tenant's provider binding for
    /// <see cref="ILiveVoiceProvider.ProviderName"/> and passes it only to a provider
    /// whose <see cref="ILiveVoiceProvider.SupportsTenantCredentials"/> is
    /// <see langword="true"/>. It lives in memory for the call and is never
    /// persisted; its text form never shows the key.
    /// </remarks>
    public ModelProviderCredential? Credential { get; init; }
}

/// <summary>The request that attaches the server-side control connection to a live voice session.</summary>
public sealed record LiveVoiceAttachRequest
{
    /// <summary>Gets the identifier <see cref="ILiveVoiceProvider.CreateSessionAsync"/> returned.</summary>
    public required string ProviderSessionId { get; init; }

    /// <summary>
    /// Gets the credential the session was created with: the tenant's own, or
    /// <see langword="null"/> for the provider's configured credential.
    /// </summary>
    /// <remarks>The same credential as <see cref="LiveVoiceCreateRequest.Credential"/> for the same session.</remarks>
    public ModelProviderCredential? Credential { get; init; }
}

/// <summary>The result of creating a live voice session.</summary>
public sealed record LiveVoiceSessionHandle
{
    /// <summary>Gets the provider's identifier for the session.</summary>
    public required string ProviderSessionId { get; init; }

    /// <summary>Gets the SDP answer to hand back to the media peer.</summary>
    public required string SdpAnswer { get; init; }

    /// <summary>Gets the model the provider actually bound; <see langword="null"/> if it reported none.</summary>
    public string? Model { get; init; }
}

/// <summary>The settings of one live voice session.</summary>
public sealed record LiveVoiceSessionOptions
{
    /// <summary>Gets the model; <see langword="null"/> uses the provider's configured default.</summary>
    public string? Model { get; init; }

    /// <summary>Gets the output voice; <see langword="null"/> uses the provider's default.</summary>
    public string? Voice { get; init; }

    /// <summary>Gets the system instructions given to the live model.</summary>
    public string? Instructions { get; init; }

    /// <summary>Gets how the live model hands work back. The default is <see cref="LiveVoiceDelegationMode.Client"/>.</summary>
    public LiveVoiceDelegationMode DelegationMode { get; init; } = LiveVoiceDelegationMode.Client;
}

/// <summary>How a live model hands heavier work back.</summary>
/// <remarks>
/// Written <strong>as a name</strong> in JSON. The numeric values are
/// <strong>stable</strong> and must not change.
/// </remarks>
[JsonConverter(typeof(JsonStringEnumConverter<LiveVoiceDelegationMode>))]
public enum LiveVoiceDelegationMode
{
    /// <summary>The delegated work is executed by the attached client — Tracon.</summary>
    Client = 0,
}

/// <summary>Text sent into a live conversation's context.</summary>
public sealed record LiveVoiceAppend
{
    /// <summary>Gets the channel the text is written to.</summary>
    public required LiveVoiceAppendChannel Channel { get; init; }

    /// <summary>Gets the delegation the text belongs to.</summary>
    /// <remarks>
    /// Required on <strong>every</strong> channel, instructions included. The
    /// measured provider surface rejects an append with no delegation, so there is no
    /// session-wide append: the live model's own instructions are set when the session
    /// is created.
    /// </remarks>
    public required string DelegationId { get; init; }

    /// <summary>Gets the text.</summary>
    public required string Text { get; init; }
}

/// <summary>The channel a live append is written to.</summary>
/// <remarks>
/// Written <strong>as a name</strong> in JSON. The numeric values are
/// <strong>stable</strong> and must not change.
/// </remarks>
[JsonConverter(typeof(JsonStringEnumConverter<LiveVoiceAppendChannel>))]
public enum LiveVoiceAppendChannel
{
    /// <summary>Progress the model may narrate but must not read out verbatim.</summary>
    Thinking = 0,

    /// <summary>The result the model speaks.</summary>
    Commentary = 1,

    /// <summary>Guidance that steers how the model speaks the result.</summary>
    /// <remarks>
    /// Filled from configuration only, never from a run's output: text a model
    /// produced must not become the instruction another model obeys.
    /// </remarks>
    Instructions = 2,
}

/// <summary>One event observed on a live voice session's sideband.</summary>
/// <remarks>
/// A single record with optional fields, distinguished by <see cref="Kind"/>. The
/// growth surface is the record and the enum, not the interface: a provider that
/// reports a new fact adds a field here without breaking a consumer.
/// </remarks>
public sealed record LiveVoiceEvent
{
    /// <summary>Gets what the event says.</summary>
    public required LiveVoiceEventKind Kind { get; init; }

    /// <summary>Gets the provider's session identifier, when the event carries one.</summary>
    public string? SessionId { get; init; }

    /// <summary>Gets the text of a transcript delta or of an error.</summary>
    public string? Text { get; init; }

    /// <summary>Gets the provider's identifier for the conversation item, when it carries one.</summary>
    public string? ItemId { get; init; }

    /// <summary>Gets the delegation this event belongs to.</summary>
    public string? DelegationId { get; init; }

    /// <summary>
    /// Gets the point in the conversation, in milliseconds from the session's start,
    /// that a delegation was cut at.
    /// </summary>
    /// <remarks>
    /// This answers "which part of the conversation is the delegated task". It is the
    /// start of the last utterance segment before the delegation, so a cut that keeps
    /// every entry starting at or before it includes that segment.
    /// </remarks>
    public int? OffsetMilliseconds { get; init; }

    /// <summary>Gets when a transcript segment begins, in milliseconds from the session's start.</summary>
    public int? StartMilliseconds { get; init; }

    /// <summary>Gets when a transcript segment ends, in milliseconds from the session's start.</summary>
    public int? EndMilliseconds { get; init; }

    /// <summary>
    /// Gets the session duration the provider reports as billable, in seconds.
    /// </summary>
    /// <remarks>
    /// The provider's number, not a wall clock. Tracon does not see the media
    /// and therefore cannot measure the duration itself; when the provider reports
    /// none, the duration stays <see langword="null"/> rather than being invented.
    /// </remarks>
    public decimal? Seconds { get; init; }

    /// <summary>Gets the provider's stated reason for a close.</summary>
    public string? Reason { get; init; }
}

/// <summary>What a live voice sideband event says.</summary>
/// <remarks>
/// Written <strong>as a name</strong> in JSON. The numeric values are
/// <strong>stable</strong> and must not change. Only facts an implementation can
/// actually observe are modelled here — Tracon does not carry a contract for an
/// event no provider sends.
/// </remarks>
[JsonConverter(typeof(JsonStringEnumConverter<LiveVoiceEventKind>))]
public enum LiveVoiceEventKind
{
    /// <summary>The session is live and the sideband is attached.</summary>
    SessionStarted = 0,

    /// <summary>A segment of what the user said.</summary>
    InputTranscript = 1,

    /// <summary>A segment of what the model said.</summary>
    OutputTranscript = 2,

    /// <summary>The model handed work back; <see cref="LiveVoiceEvent.DelegationId"/> identifies it.</summary>
    DelegationCreated = 3,

    /// <summary>The provider accepted an append.</summary>
    AppendAccepted = 4,

    /// <summary>The provider reported the session's billable duration.</summary>
    UsageUpdated = 5,

    /// <summary>The provider reported an error.</summary>
    Error = 6,

    /// <summary>The session closed.</summary>
    Closed = 7,
}
