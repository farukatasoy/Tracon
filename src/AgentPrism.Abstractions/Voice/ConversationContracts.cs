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
    /// The tenant. 🚨 Resolved when the connection is established and
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
    /// AgentPrism does not fabricate a duration (K-032).
    /// </summary>
    public decimal? InputSeconds { get; init; }

    /// <summary>The total characters synthesized to speech.</summary>
    public long? OutputChars { get; init; }

    /// <summary>Why the connection closed.</summary>
    public VoiceSessionEndReason? EndReason { get; init; }

    /// <summary>The actor who opened the connection.</summary>
    public string? CreatedBy { get; init; }
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
/// layer sees these types while serving the voice endpoints (the K-174 pattern).
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
