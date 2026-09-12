namespace Tracon;

/// <summary>A single finding produced by the validation of an agent definition.</summary>
/// <remarks>
/// <see cref="Message"/> comes from the server and is not translated; the user
/// interface shows its own heading based on <see cref="Code"/> alone.
/// </remarks>
public sealed record ValidationMessage
{
    /// <summary>Gets the severity of the finding.</summary>
    public required ValidationSeverity Severity { get; init; }

    /// <summary>
    /// Gets the stable machine-readable code, for example <c>unknown_tool</c> or <c>cycle</c>.
    /// </summary>
    public required string Code { get; init; }

    /// <summary>Gets the human-readable description. It is not translated.</summary>
    public required string Message { get; init; }

    /// <summary>
    /// Gets the path of the offending field inside the definition, for example
    /// <c>toolNames[2]</c>. It is <see langword="null"/> when the finding points at no field.
    /// </summary>
    public string? Path { get; init; }
}
