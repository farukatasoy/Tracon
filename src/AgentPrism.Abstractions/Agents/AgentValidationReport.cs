namespace AgentPrism;

/// <summary>
/// The result of a validation that builds an agent definition without saving it and
/// without calling any model.
/// </summary>
/// <remarks>
/// A failed validation is not an HTTP error: the request is valid and the answer is
/// "this definition is invalid". The HTTP response is therefore <c>200</c> even when
/// <c>Valid</c> is <see langword="false"/>.
/// </remarks>
public sealed record AgentValidationReport
{
    /// <summary>
    /// Gets a value that is <see langword="true"/> when the definition carries no <see
    /// cref="ValidationSeverity.Error"/>.
    /// </summary>
    public required bool Valid { get; init; }

    /// <summary>
    /// Gets a value that tells whether a check could not finish because a resource was
    /// unreachable, an MCP server for example. It does not affect <c>Valid</c>.
    /// </summary>
    public required bool Inconclusive { get; init; }

    /// <summary>Gets every message found. Validation does not stop at the first error.</summary>
    public required IReadOnlyList<ValidationMessage> Messages { get; init; }
}
