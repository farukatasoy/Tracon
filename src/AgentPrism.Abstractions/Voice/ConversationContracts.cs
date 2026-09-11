using System.Text.Json.Serialization;

namespace AgentPrism;

/// <summary>The summary record of a real-time voice connection.</summary>
/// <remarks>
/// <para>
/// The audio <strong>content</strong> does not sit in this record. The
/// record is only for measurement and observability: who, when, how many
/// turns, how much audio. If audio is stored (default <em>no</em>), the bytes
/// are in the <c>attachments</c> table.
/// </para>
/// <para>
/// Each conversation turn also produces a normal <c>runs</c> row. Voice does
/// not change the run path; it only changes the input and output format.
/// </para>
/// </remarks>
public sealed record VoiceSessionRecord
{
    /// <summary>The connection's identifier.</summary>
    public required Guid Id { get; init; }

    /// <summary>
    /// The tenant. Resolved when the connection is established and
    /// <strong>fixed</strong> for the connection's lifetime.
    /// </summary>
    public required string TenantId { get; init; }

    /// <summary>The identifier of the agent session the conversation runs in.</summary>
    public required string SessionId { get; init; }

    /// <summary>The name of the agent being talked to.</summary>
    public required string AgentName { get; init; }

    /// <summary>The moment the connection opened.</summary>
    public required DateTimeOffset StartedAt { get; init; }

    /// <summary>The moment the connection closed; <see langword="null"/> while still open.</summary>
    public DateTimeOffset? EndedAt { get; init; }

    /// <summary>The number of completed conversation turns.</summary>
    public int Turns { get; init; }

    /// <summary>
    /// The total resolved audio duration (seconds). Stays
    /// <see langword="null"/> if the provider does not report duration —
    /// AgentPrism does not fabricate a duration.
    /// </summary>
    public decimal? InputSeconds { get; init; }

    /// <summary>The total characters synthesized to speech.</summary>
    public long? OutputChars { get; init; }

    /// <summary>Why the connection closed.</summary>
    public VoiceSessionEndReason? EndReason { get; init; }

    /// <summary>The actor who opened the connection.</summary>
    public string? CreatedBy { get; init; }

    /// <summary>
    /// The provider that hosted the conversation, e.g. <c>openai</c>;
    /// <see langword="null"/> when AgentPrism ran the conversation itself.
    /// </summary>
    public string? Provider { get; init; }

    /// <summary>The model the conversation ran on; <see langword="null"/> when unknown.</summary>
    public string? Model { get; init; }

    /// <summary>
    /// The billable wall-clock duration of a provider-hosted live session, in seconds.
    /// </summary>
    /// <remarks>
    /// This is <strong>not</strong> <see cref="InputSeconds"/>. That field is the
    /// resolved duration of audio AgentPrism transcribed; this one is how long the
    /// provider hosted the session. Different quantities, different fields. The number
    /// is the provider's — AgentPrism does not see the media of a live session and does
    /// not invent a duration, so it stays <see langword="null"/> when the provider
    /// reports none.
    /// </remarks>
    public decimal? LiveSeconds { get; init; }

    /// <summary>What the conversation cost; <see langword="null"/> when nothing was priced.</summary>
    public VoiceSessionCost? Cost { get; init; }
}

/// <summary>What one voice conversation cost.</summary>
/// <remarks>
/// <para>
/// The total has two addends because the two voice paths bill differently: a
/// provider-hosted live session bills by duration, while the conversation layer
/// AgentPrism runs itself bills the synthesized characters.
/// </para>
/// <para>
/// The sum lives in <see cref="Total"/> and nowhere else. A bare
/// <c>decimal?</c> forces every caller to decide which addend it meant, and a sum
/// spelled out by hand in several places silently loses a term the day a third one
/// arrives.
/// </para>
/// <para>
/// This is the cost of the <em>voice</em> connection only. Each delegated task
/// also produces an ordinary <c>runs</c> row with its own token cost, which is
/// deliberately not repeated here. A display that shows only this number
/// <strong>under-reports</strong>; the honest figure is this cost plus the session's
/// run costs.
/// </para>
/// </remarks>
public sealed record VoiceSessionCost
{
    /// <summary>What the session's duration cost.</summary>
    public decimal? DurationCost { get; init; }

    /// <summary>What the synthesized characters cost.</summary>
    public decimal? CharacterCost { get; init; }

    /// <summary>The currency the amounts are in.</summary>
    public string? Currency { get; init; }

    /// <summary>Adds every priced addend.</summary>
    /// <returns>
    /// The total, or <see langword="null"/> when nothing was priced — never
    /// <c>0</c>, which would claim the conversation was free.
    /// </returns>
    public decimal? Total()
        => this is { DurationCost: null, CharacterCost: null }
            ? null
            : (DurationCost ?? 0m) + (CharacterCost ?? 0m);
}

/// <summary>The reason a voice connection closed.</summary>
/// <remarks>
/// Written <strong>as a name</strong> in JSON; stored as <c>smallint</c> in
/// the database. The numeric values are <strong>stable</strong> and must not change.
/// </remarks>
[JsonConverter(typeof(JsonStringEnumConverter<VoiceSessionEndReason>))]
public enum VoiceSessionEndReason
{
    /// <summary>The client sent <c>stop</c> or closed the socket cleanly.</summary>
    Client = 0,

    /// <summary>The connection sat idle and timed out.</summary>
    IdleTimeout = 1,

    /// <summary>The connection reached the maximum allowed duration.</summary>
    DurationLimit = 2,

    /// <summary>An error closed the connection.</summary>
    Error = 3,

    /// <summary>The server is shutting down.</summary>
    ServerShutdown = 4,

    /// <summary>
    /// The session was created but nothing ever connected to it, and it timed out.
    /// </summary>
    /// <remarks>
    /// Deliberately not <see cref="Error"/>: an abandoned session is not a failure,
    /// and folding the two together would hide how often callers create sessions they
    /// never use.
    /// </remarks>
    Abandoned = 5,

    /// <summary>The provider closed the session.</summary>
    Provider = 6,
}

/// <summary>The filter for listing voice session records.</summary>
public sealed record VoiceSessionQuery
{
    /// <summary>The agent name filter; all agents if empty.</summary>
    public string? AgentName { get; init; }

    /// <summary>The session identifier filter; all sessions if empty.</summary>
    public string? SessionId { get; init; }

    /// <summary>The number of records to skip.</summary>
    public int Skip { get; init; }

    /// <summary>The maximum number of records to return.</summary>
    public int Take { get; init; } = 50;
}

/// <summary>Stores the summary record of real-time voice connections.</summary>
/// <remarks>
/// <para>
/// The store is <strong>for observability</strong> and does not break
/// functionality: a write error does not interrupt the conversation, it is only logged.
/// </para>
/// <para>
/// The contract lives in <c>AgentPrism.Abstractions</c> because the HTTP
/// layer sees these types while serving the voice endpoints.
/// </para>
/// </remarks>
public interface IVoiceSessionStore
{
    /// <summary>Adds or updates a voice session record.</summary>
    /// <param name="record">The record.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The completion task.</returns>
    ValueTask SaveAsync(VoiceSessionRecord record, CancellationToken cancellationToken = default);

    /// <summary>Lists a tenant's voice session records, newest first.</summary>
    /// <param name="tenantId">The tenant.</param>
    /// <param name="query">The filter.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The records.</returns>
    ValueTask<IReadOnlyList<VoiceSessionRecord>> QueryAsync(
        string tenantId,
        VoiceSessionQuery query,
        CancellationToken cancellationToken = default);
}
