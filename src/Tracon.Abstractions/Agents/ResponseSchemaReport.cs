using System.ComponentModel;
using System.Text.Json;

namespace Tracon;

/// <summary>The response schema as one model provider's client sends it to the model.</summary>
/// <remarks>
/// One entry per binding that reaches a provider adapter which states its schema through
/// <see cref="IResponseSchemaSupport"/>: the agent's own binding first, then each fallback link.
/// </remarks>
[Description("The response schema as one model provider's client sends it to the model.")]
public sealed record ResponseSchemaReport
{
    /// <summary>Gets the provider name, for example <c>openai</c>.</summary>
    [Description("Gets the provider name, for example `openai`.")]
    public required string Provider { get; init; }

    /// <summary>Gets the model name of the binding or the fallback link.</summary>
    [Description("Gets the model name of the binding or the fallback link.")]
    public required string Model { get; init; }

    /// <summary>
    /// Gets a value that is <see langword="true"/> when the client asks the provider to enforce the schema while
    /// the model generates. A fallback link is described without the primary binding's provider settings, because
    /// the link receives none of them.
    /// </summary>
    [Description(
        "Gets a value that is `true` when the client asks the provider to enforce the schema while\n" +
        "the model generates. A fallback link is described without the primary binding's provider\n" +
        "settings, because the link receives none of them.")]
    public required bool Strict { get; init; }

    /// <summary>
    /// Gets the schema the client sends. It can differ from the one in the definition: the OpenAI clients close every
    /// object, make every property required and move bounds into <c>description</c>.
    /// </summary>
    [Description(
        "Gets the schema the client sends. It can differ from the one in the definition: the OpenAI\n" +
        "clients close every object, make every property required and move bounds into `description`.")]
    public required JsonElement Schema { get; init; }
}
