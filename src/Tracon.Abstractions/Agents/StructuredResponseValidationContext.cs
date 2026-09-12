using System.Text.Json;

namespace Tracon;

/// <summary>The response an <see cref="IStructuredResponseValidator"/> checks.</summary>
public sealed record StructuredResponseValidationContext
{
    /// <summary>Gets the name of the agent that produced the response.</summary>
    public required string AgentName { get; init; }

    /// <summary>Gets the identity of the run.</summary>
    public required Guid RunId { get; init; }

    /// <summary>Gets the session the run belongs to. <see langword="null"/> for a sessionless run.</summary>
    public string? SessionId { get; init; }

    /// <summary>Gets the model provider that produced the response, or <see langword="null"/> when unknown.</summary>
    public string? Provider { get; init; }

    /// <summary>Gets the model that produced the response, or <see langword="null"/> when unknown.</summary>
    public string? Model { get; init; }

    /// <summary>Gets the requested response format.</summary>
    public required AgentResponseFormatKind Kind { get; init; }

    /// <summary>
    /// Gets the requested JSON schema. Populated only when <see cref="Kind"/> is
    /// <see cref="AgentResponseFormatKind.JsonSchema"/>.
    /// </summary>
    public JsonElement? Schema { get; init; }

    /// <summary>Gets the name of the schema, if the agent's definition supplied one.</summary>
    public string? SchemaName { get; init; }

    /// <summary>
    /// Gets the response text. Already confirmed non-empty and parseable as JSON —
    /// Tracon's own well-formedness check runs before this validator is called.
    /// </summary>
    public required string ResponseText { get; init; }
}
