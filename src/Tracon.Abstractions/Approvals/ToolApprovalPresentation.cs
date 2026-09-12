namespace Tracon;

/// <summary>
/// A human-readable projection of one tool-approval request, produced by a consumer's
/// <see cref="IToolApprovalPresenter"/>.
/// </summary>
/// <remarks>
/// <para>
/// Every field is optional: a presenter may resolve only part of what it knows (for
/// example the entity's name but not its type), and an unresolved field is left
/// <see langword="null"/> rather than filled with a placeholder. The raw tool call
/// arguments this presentation was built from are never replaced by it — see
/// <see cref="PendingApproval.Arguments"/>, which stays populated alongside this record.
/// </para>
/// </remarks>
public sealed record ToolApprovalPresentation
{
    /// <summary>Gets the kind of entity the call acts on (for example <c>"skill"</c> or <c>"order"</c>).</summary>
    public string? EntityType { get; init; }

    /// <summary>Gets the entity's own identifier, as read from the call's arguments.</summary>
    public string? EntityId { get; init; }

    /// <summary>Gets the entity's human-readable name, resolved from <c>EntityId</c>.</summary>
    public string? EntityName { get; init; }

    /// <summary>Gets a free-form sentence describing the call, for a reviewer who has none of the above.</summary>
    public string? Message { get; init; }
}
