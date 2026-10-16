using System.ComponentModel;

namespace Tracon;

/// <remarks>
/// A failed validation is not an HTTP error: the request is valid and the answer is
/// "this definition is invalid". The HTTP response is therefore <c>200</c> even when
/// <c>Valid</c> is <see langword="false"/>.
/// </remarks>
[Description(
    "The result of a validation that builds an agent definition without saving it and\n" +
    "without calling any model.")]
internal sealed record AgentValidationReport
{
    [Description("Gets a value that is `true` when the definition carries no ValidationSeverity.Error.")]
    public required bool Valid { get; init; }

    [Description(
        "Gets a value that tells whether a check could not finish because a resource was\n" +
        "unreachable, an MCP server for example. It does not affect `Valid`.")]
    public required bool Inconclusive { get; init; }

    [Description("Gets every message found. Validation does not stop at the first error.")]
    public required IReadOnlyList<ValidationMessage> Messages { get; init; }
}
