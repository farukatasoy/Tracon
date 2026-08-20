namespace AgentPrism;

/// <summary>The decision a guard can make.</summary>
/// <remarks>
/// The ordering is <strong>meaningful</strong>: when multiple guards run, the
/// largest value wins. A new decision must be placed in order of severity.
/// </remarks>
public enum ContentGuardAction
{
    /// <summary>The content passes unchanged.</summary>
    Allow = 0,

    /// <summary>The content passes after being modified. The run continues.</summary>
    Mask = 1,

    /// <summary>The content is blocked. The run becomes <c>Failed</c>.</summary>
    Block = 2,
}

/// <summary>The result of an <see cref="IContentGuard"/> check.</summary>
/// <remarks>
/// The result <strong>does not carry the blocked content</strong>. It only
/// carries the matched rule's name and the block reason; both are written to
/// the audit trail and the run event. Blocked content is sensitive by
/// definition, and writing it into an audit trail makes the problem
/// <em>permanent</em>.
/// </remarks>
public sealed record ContentGuardResult
{
    /// <summary>The content passes unchanged.</summary>
    /// <remarks>
    /// A single instance is shared: this is the value guards return most
    /// often, and it must not allocate on the hot path.
    /// </remarks>
    public static ContentGuardResult Allow { get; } = new() { Action = ContentGuardAction.Allow };

    /// <summary>The decision made.</summary>
    public required ContentGuardAction Action { get; init; }

    /// <summary>
    /// The new text to send to the model (or return to the client). Populated
    /// only for <see cref="ContentGuardAction.Mask"/>.
    /// </summary>
    public string? MaskedText { get; init; }

    /// <summary>The matched rule's name. Does not carry the matched CONTENT.</summary>
    public string? RuleName { get; init; }

    /// <summary>The block reason. Does not carry the blocked TEXT.</summary>
    public string? Reason { get; init; }

    /// <summary>The content passes after being modified.</summary>
    /// <param name="maskedText">The new form of the content.</param>
    /// <param name="ruleName">The matched rule's name.</param>
    /// <returns>A mask decision.</returns>
    public static ContentGuardResult Mask(string maskedText, string ruleName) => new()
    {
        Action = ContentGuardAction.Mask,
        MaskedText = maskedText,
        RuleName = ruleName,
    };

    /// <summary>The content is blocked; the run becomes <c>Failed</c>.</summary>
    /// <param name="ruleName">The matched rule's name.</param>
    /// <param name="reason">The block reason. MUST NOT carry the blocked text.</param>
    /// <returns>A block decision.</returns>
    public static ContentGuardResult Block(string ruleName, string reason) => new()
    {
        Action = ContentGuardAction.Block,
        RuleName = ruleName,
        Reason = reason,
    };
}
