using System.ComponentModel;

namespace Tracon;

/// <remarks>
/// <see cref="Message"/> comes from the server and is not translated; the user
/// interface shows its own heading based on <see cref="Code"/> alone.
/// </remarks>
[Description("A single finding produced by the validation of an agent definition.")]
internal sealed record ValidationMessage
{
    [Description("Gets the severity of the finding.")]
    public required ValidationSeverity Severity { get; init; }

    [Description("Gets the stable machine-readable code, for example `unknown_tool` or `cycle`.")]
    public required string Code { get; init; }

    [Description("Gets the human-readable description. It is not translated.")]
    public required string Message { get; init; }

    [Description(
        "Gets the path of the offending field inside the definition, for example\n" +
        "`toolNames[2]`. It is `null` when the finding points at no field.")]
    public string? Path { get; init; }
}
