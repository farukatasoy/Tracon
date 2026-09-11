using System.Text.Json.Serialization;

namespace AgentPrism;

/// <summary>Creates and supervises a provider-hosted live voice session.</summary>
/// <remarks>
/// <para>
/// A live session is <strong>not</strong> the conversation layer behind
/// <c>UseVoiceConversation()</c>. There, AgentPrism receives the audio, transcribes
/// it, runs the agent and synthesizes the answer. Here the provider hosts the whole
/// conversation and carries the media directly to the media peer; AgentPrism stands
/// in two places only: it <em>creates</em> the session, so tenancy, role and
/// concurrency gates apply and the raw API key never reaches the browser, and it
/// <em>attaches</em> a server-side control connection, so the work the model
/// delegates becomes an ordinary AgentPrism run.
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
/// tenant before a session is created; the provider neither reads nor enforces one,
/// and must not key any cache or credential by tenant.
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
    /// The ceiling is in <strong>characters</strong>, not tokens: AgentPrism ships no
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
}

/// <summary>The server-side control connection of a live voice session.</summary>
/// <remarks>
/// <para>
/// <strong>Lifetime:</strong> never resolved from dependency injection, so neither
/// <c>singleton</c>, <c>scoped</c> nor <c>transient</c> describes it. One instance
/// belongs to one session, is produced by
/// <see cref="ILiveVoiceProvider.AttachAsync"/>, and is disposed when that session
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

    /// <summary>Gets the backing model used in <see cref="LiveVoiceDelegationMode.Responses"/> mode.</summary>
    public string? BackendModel { get; init; }
}

/// <summary>How a live model hands heavier work back.</summary>
/// <remarks>
/// Written <strong>as a name</strong> in JSON. The numeric values are
/// <strong>stable</strong> and must not change.
/// </remarks>
[JsonConverter(typeof(JsonStringEnumConverter<LiveVoiceDelegationMode>))]
public enum LiveVoiceDelegationMode
{
    /// <summary>The delegated work is executed by the attached client — AgentPrism.</summary>
    Client = 0,

    /// <summary>The provider executes the delegated work against a backing model of its own.</summary>
    Responses = 1,
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
    /// The provider's number, not a wall clock. AgentPrism does not see the media
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
/// actually observe are modelled here — AgentPrism does not carry a contract for an
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
