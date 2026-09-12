namespace Tracon;

/// <summary>The outcome of a tool arguments validation check.</summary>
public sealed record ToolArgumentsValidationResult
{
    private ToolArgumentsValidationResult(bool isValid, string? reason)
    {
        IsValid = isValid;
        Reason = reason;
    }

    /// <summary>A result allowing the call.</summary>
    public static ToolArgumentsValidationResult Valid { get; } = new(true, null);

    /// <summary>Gets whether the call's arguments are valid.</summary>
    public bool IsValid { get; }

    /// <summary>
    /// Gets the reason shown to the model when the call is rejected.
    /// <see langword="null"/> when <see cref="IsValid"/> is <see langword="true"/>.
    /// </summary>
    /// <remarks>
    /// This text is sent to the model, not shown to the user directly. It
    /// must never carry an argument <strong>value</strong> — a rejected
    /// call's record is written to a persistent run event, and an argument
    /// can carry a <c>secret</c>.
    /// </remarks>
    public string? Reason { get; }

    /// <summary>Creates a result that rejects the call.</summary>
    /// <param name="reason">The reason shown to the model. Must not carry argument values.</param>
    /// <returns>A rejecting result.</returns>
    /// <exception cref="ArgumentException"><paramref name="reason"/> is empty or whitespace.</exception>
    public static ToolArgumentsValidationResult Invalid(string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        return new ToolArgumentsValidationResult(false, reason);
    }
}
