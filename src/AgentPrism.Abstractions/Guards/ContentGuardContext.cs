namespace AgentPrism;

/// <summary>The direction of a content guard check.</summary>
/// <remarks>
/// The value is not written to JSON and not stored in the database; it only
/// appears as a name in the run event's text and in the audit trail.
/// </remarks>
public enum ContentGuardDirection
{
    /// <summary>Content going to the model. Tool results are also this direction.</summary>
    Input = 0,

    /// <summary>Content coming from the model.</summary>
    Output = 1,
}

/// <summary>Where the text handed to an <see cref="IContentGuard"/> came from.</summary>
/// <remarks>
/// The trust level behind these values is not uniform: a user message can only
/// poison the sender's own session, while a tool result can carry text another
/// tenant's data wrote into a shared system (prompt injection). A guard that
/// wants to apply different rules per source reads this value; the built-in
/// <c>PatternContentGuard</c> deliberately does not — it applies the same
/// patterns regardless of source.
/// </remarks>
public enum ContentGuardSource
{
    /// <summary>
    /// The source could not be determined. This is never a reason to relax a
    /// security decision — treat it at least as strictly as the most sensitive
    /// known source.
    /// </summary>
    Unknown = 0,

    /// <summary>Text a user typed.</summary>
    UserMessage = 1,

    /// <summary>The result of a tool call. See <see cref="ContentGuardContext.ToolName"/>.</summary>
    ToolResult = 2,

    /// <summary>Text extracted from a document (for example, retrieved for RAG).</summary>
    Document = 3,

    /// <summary>Text the model itself generated.</summary>
    ModelOutput = 4,

    /// <summary>Text loaded from a skill's own resource file.</summary>
    SkillResource = 5,
}

/// <summary>The context of an <see cref="IContentGuard"/> call.</summary>
/// <remarks>
/// The context carries <strong>a single piece of text</strong>, not a message
/// list. If messages were concatenated into one text, a false pattern could
/// match at the boundary between two messages (example: if one message ends
/// with <c>4539</c> and the next starts with <c>5787…</c>, an invalid card
/// number would "be found").
/// </remarks>
public sealed record ContentGuardContext
{
    /// <summary>The direction of the check.</summary>
    public required ContentGuardDirection Direction { get; init; }

    /// <summary>The text to check.</summary>
    public required string Text { get; init; }

    /// <summary>
    /// The run identifier. <see langword="null"/> if called outside a run's scope.
    /// </summary>
    public Guid? RunId { get; init; }

    /// <summary>The run's tenant. <see langword="null"/> if unknown.</summary>
    public string? TenantId { get; init; }

    /// <summary>The name of the agent executing the run. <see langword="null"/> if unknown.</summary>
    public string? AgentName { get; init; }

    /// <summary>The identifier of the model being called. <see langword="null"/> if unknown.</summary>
    public string? ModelId { get; init; }

    /// <summary>The source of the inspected text.</summary>
    /// <remarks>
    /// Defaults to <see cref="ContentGuardSource.Unknown"/> — code built before
    /// this field existed leaves it at that value, and that is deliberate: a
    /// guard must treat an unclassified source as at least as sensitive as a
    /// tool result, never as an implicit "allow".
    /// </remarks>
    public ContentGuardSource Source { get; init; }

    /// <summary>
    /// The name of the tool whose result is being inspected. <see langword="null"/>
    /// when <see cref="Source"/> is not <see cref="ContentGuardSource.ToolResult"/>.
    /// </summary>
    /// <remarks>
    /// Can also be <see langword="null"/> <strong>while <see cref="Source"/> is
    /// still <see cref="ContentGuardSource.ToolResult"/></strong>: resolving the
    /// name requires the matching <c>FunctionCallContent</c> to still be present
    /// earlier in the same message list, and a many-turn conversation can have
    /// dropped it. A security decision must key off <see cref="Source"/>, never
    /// off this field's presence.
    /// </remarks>
    public string? ToolName { get; init; }
}
