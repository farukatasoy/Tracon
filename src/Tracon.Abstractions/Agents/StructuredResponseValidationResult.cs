namespace Tracon;

/// <summary>The outcome of a structured response validation check.</summary>
public sealed record StructuredResponseValidationResult
{
    private StructuredResponseValidationResult(bool isValid, string? reason)
    {
        IsValid = isValid;
        Reason = reason;
    }

    /// <summary>A result accepting the response.</summary>
    public static StructuredResponseValidationResult Valid { get; } = new(true, null);

    /// <summary>Gets whether the response is valid.</summary>
    public bool IsValid { get; }

    /// <summary>
    /// Gets the reason written to the run record when the response is rejected.
    /// <see langword="null"/> when <see cref="IsValid"/> is <see langword="true"/>.
    /// </summary>
    /// <remarks>
    /// This text becomes part of a persistent run event and, through the
    /// run's error message, can reach an external response. It must never
    /// carry the response text itself — a rejected response's record is
    /// permanent, and the reason is meant to say <em>why</em> the response
    /// was rejected, not repeat what it said.
    /// </remarks>
    public string? Reason { get; }

    /// <summary>Creates a result that rejects the response.</summary>
    /// <param name="reason">The reason written to the run record. Must not carry the response text.</param>
    /// <returns>A rejecting result.</returns>
    /// <exception cref="ArgumentException"><paramref name="reason"/> is empty or whitespace.</exception>
    public static StructuredResponseValidationResult Invalid(string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        return new StructuredResponseValidationResult(false, reason);
    }
}
