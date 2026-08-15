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
}
