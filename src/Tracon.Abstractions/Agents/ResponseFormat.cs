using System.Text.Json;
using System.Text.Json.Serialization;

namespace Tracon;

/// <summary>The requested format of an agent response.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<AgentResponseFormatKind>))]
public enum AgentResponseFormatKind
{
    /// <summary>Plain text, asked for explicitly.</summary>
    Text = 0,

    /// <summary>A valid JSON document; no schema is enforced.</summary>
    Json = 1,

    /// <summary>A document that matches the given JSON schema.</summary>
    JsonSchema = 2,
}

/// <summary>The definition of a structured output.</summary>
/// <remarks>
/// <para>
/// When <see cref="ModelBinding.ResponseFormat"/> is <see langword="null"/> today's
/// behaviour does not change and no format constraint is sent to the provider. This
/// type takes effect only when the user asks for a format explicitly.
/// </para>
/// <para>
/// Validation happens while the agent is built (<c>AgentDefinitionCompiler</c>):
/// <see cref="Schema"/> cannot be empty while <see cref="Kind"/> is
/// <see cref="AgentResponseFormatKind.JsonSchema"/>, and it cannot be populated in the
/// other modes. <see cref="Schema"/> must be a JSON <strong>object</strong>; its content
/// is not validated.
/// </para>
/// </remarks>
public sealed record AgentResponseFormat
{
    /// <summary>Gets the requested format.</summary>
    public required AgentResponseFormatKind Kind { get; init; }

    /// <summary>
    /// Gets the JSON schema. It is populated only for
    /// <see cref="AgentResponseFormatKind.JsonSchema"/> and must be a JSON
    /// <strong>object</strong>.
    /// </summary>
    public JsonElement? Schema { get; init; }

    /// <summary>Gets the name of the schema. The provider can pass it on to the model.</summary>
    public string? SchemaName { get; init; }

    /// <summary>Gets the description of the schema.</summary>
    public string? SchemaDescription { get; init; }
}
