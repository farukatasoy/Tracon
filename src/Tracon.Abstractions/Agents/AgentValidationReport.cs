using System.ComponentModel;

namespace Tracon;

/// <summary>
/// The result of validating an agent definition: every finding, and whether the definition can be built.
/// </summary>
/// <remarks>
/// <para>
/// A failed validation is not an HTTP error: the request is valid and the answer is
/// "this definition is invalid". The HTTP response is therefore <c>200</c> even when
/// <see cref="Valid"/> is <see langword="false"/>.
/// </para>
/// <para>
/// <see cref="IAgentDefinitionValidator"/> returns this type from .NET code, and
/// <c>POST /api/agents/validate</c> returns the same shape as JSON.
/// </para>
/// </remarks>
[Description(
    "The result of a validation that builds an agent definition without saving it and\n" +
    "without calling any model.")]
public sealed record AgentValidationReport
{
    /// <summary>Gets a value that is <see langword="true"/> when no finding has <see cref="ValidationSeverity.Error"/>.</summary>
    [Description("Gets a value that is `true` when the definition carries no ValidationSeverity.Error.")]
    public required bool Valid { get; init; }

    /// <summary>
    /// Gets a value that tells whether a check could not finish because a resource was unreachable, an MCP server for
    /// example. It does not affect <c>Valid</c>.
    /// </summary>
    [Description(
        "Gets a value that tells whether a check could not finish because a resource was\n" +
        "unreachable, an MCP server for example. It does not affect `Valid`.")]
    public required bool Inconclusive { get; init; }

    /// <summary>Gets every finding. Validation does not stop at the first error.</summary>
    [Description("Gets every message found. Validation does not stop at the first error.")]
    public required IReadOnlyList<ValidationMessage> Messages { get; init; }

    /// <summary>
    /// Gets the response schema each model provider's client sends, for a definition whose response format is a JSON
    /// schema. It is empty when the definition has no such format, and it lists only the providers that state their
    /// schema.
    /// </summary>
    [Description(
        "Gets the response schema each model provider's client sends, for a definition whose response\n" +
        "format is a JSON schema. It is empty when the definition has no such format, and it lists only\n" +
        "the providers that state their schema.")]
    public required IReadOnlyList<ResponseSchemaReport> ResponseSchemas { get; init; }
}
