using System.Text.Json;

namespace AgentPrism;

/// <summary>
/// Detailed view of a single session: metadata and chat history.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Messages"/> is Microsoft Agent Framework's <c>ChatMessage</c>
/// array, produced with <c>Microsoft.Extensions.AI</c> serialization
/// settings. AgentPrism does not layer its own parallel type hierarchy on top
/// of this (the MAF pass-through rule); the JSON shape is therefore MAF's documented shape.
/// </para>
/// <para>
/// <see cref="State"/> is the serialized form of the session and is
/// <strong>opaque</strong>. Its content belongs to MAF; AgentPrism does not
/// interpret it.
/// </para>
/// </remarks>
public sealed record SessionDetailResponse
{
    /// <summary>Session identifier.</summary>
    public required string Id { get; init; }

    /// <summary>Agent the session belongs to.</summary>
    public required string AgentName { get; init; }

    /// <summary>Tenant identifier.</summary>
    public string? TenantId { get; init; }

    /// <summary>Creation time.</summary>
    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>Last update time.</summary>
    public required DateTimeOffset UpdatedAt { get; init; }

    /// <summary>
    /// Chat history. Returns <see langword="null"/> if the history could not
    /// be read (e.g. the agent is no longer in the catalog).
    /// </summary>
    public JsonElement? Messages { get; init; }

    /// <summary>Serialized session state.</summary>
    public required JsonElement State { get; init; }
}
