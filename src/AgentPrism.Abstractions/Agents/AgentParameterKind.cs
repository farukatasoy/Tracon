using System.Text.Json.Serialization;

namespace AgentPrism;

/// <summary>The data kind of an <see cref="AgentParameter"/>.</summary>
/// <remarks>
/// <para>
/// Deliberately scalar-only: <c>string</c>, <c>number</c>, <c>bool</c>. A list
/// need is carried as a single <see cref="Text"/> value; the library does not
/// pick a separator on the consumer's behalf.
/// </para>
/// <para>
/// Written to JSON <strong>by name</strong> (<c>"Text"</c>), not by number -
/// the same convention every other JSON-facing enum in this package follows.
/// </para>
/// </remarks>
[JsonConverter(typeof(JsonStringEnumConverter<AgentParameterKind>))]
public enum AgentParameterKind
{
    /// <summary>A text value.</summary>
    Text = 0,

    /// <summary>A numeric value.</summary>
    Number = 1,

    /// <summary>A boolean value.</summary>
    Boolean = 2,
}
