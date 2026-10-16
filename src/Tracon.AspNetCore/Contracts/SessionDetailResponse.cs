using System.ComponentModel;
using System.Text.Json;

namespace Tracon;

/// <remarks>
/// <para>
/// <see cref="Messages"/> is Microsoft Agent Framework's <c>ChatMessage</c>
/// array, produced with <c>Microsoft.Extensions.AI</c> serialization
/// settings. Tracon does not layer its own parallel type hierarchy on top
/// of this (the MAF pass-through rule); the JSON shape is therefore MAF's documented shape.
/// </para>
/// <para>
/// <see cref="State"/> is the serialized form of the session and is
/// <strong>opaque</strong>. Its content belongs to MAF; Tracon does not
/// interpret it.
/// </para>
/// </remarks>
[Description("Detailed view of a single session: metadata and chat history.")]
internal sealed record SessionDetailResponse
{
    [Description("Session identifier.")]
    public required string Id { get; init; }

    [Description("Agent the session belongs to.")]
    public required string AgentName { get; init; }

    [Description("Tenant identifier.")]
    public string? TenantId { get; init; }

    [Description("Creation time.")]
    public required DateTimeOffset CreatedAt { get; init; }

    [Description("Last update time.")]
    public required DateTimeOffset UpdatedAt { get; init; }

    [Description(
        "Chat history. Returns `null` if the history could not\n" +
        "be read (e.g. the agent is no longer in the catalog).")]
    public JsonElement? Messages { get; init; }

    [Description("Serialized session state.")]
    public required JsonElement State { get; init; }
}
