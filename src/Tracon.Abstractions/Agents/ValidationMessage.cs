using System.ComponentModel;

namespace Tracon;

/// <summary>A single finding produced by the validation of an agent definition.</summary>
/// <remarks>
/// <see cref="Message"/> comes from the server and is not translated; the user
/// interface shows its own heading based on <see cref="Code"/> alone.
/// </remarks>
[Description("A single finding produced by the validation of an agent definition.")]
public sealed record ValidationMessage
{
    /// <summary>Gets the severity of the finding.</summary>
    [Description("Gets the severity of the finding.")]
    public required ValidationSeverity Severity { get; init; }

    /// <summary>
    /// Gets the stable machine-readable code, for example <c>unknown_tool</c> or <c>cycle</c>. A code never changes
    /// inside a major version; a new code is a minor change.
    /// </summary>
    [Description("Gets the stable machine-readable code, for example `unknown_tool` or `cycle`.")]
    public required string Code { get; init; }

    /// <summary>Gets the human-readable description. It is not translated.</summary>
    [Description("Gets the human-readable description. It is not translated.")]
    public required string Message { get; init; }

    /// <summary>
    /// Gets the path of the offending field inside the definition, for example <c>toolNames[2]</c>. It is
    /// <see langword="null"/> when the finding points at no field.
    /// </summary>
    [Description(
        "Gets the path of the offending field inside the definition, for example\n" +
        "`toolNames[2]`. It is `null` when the finding points at no field.")]
    public string? Path { get; init; }
}
