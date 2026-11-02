using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Tracon;

/// <summary>
/// Reads a <see cref="TimeSpan"/> in a request body as the clock <c>[days.]hh:mm:ss</c>, the shape the published schema
/// describes, and writes it the way the framework does.
/// </summary>
/// <remarks>
/// <para>
/// <c>System.Text.Json</c> reads <c>"30"</c> as thirty days and <c>"00:30"</c> as thirty minutes. A function node saved with
/// <c>"timeout": "30"</c> got a month, and the schema of the field says
/// <c>^-?(\d+\.)?\d{2}:\d{2}:\d{2}(\.\d{1,7})?$</c>. The reading is the one the configuration keys use
/// (<see cref="ConfigurationValueKinds.TryTimeSpan"/>), so a value means the same in <c>appsettings.json</c> and in a body.
/// </para>
/// <para>
/// Attached to the request-body options (<see cref="RequestBodyBinding"/>) and not to the type, like
/// <see cref="ExplainedEnumConverter{TEnum}"/>: a stored definition was written by the framework's own converter and is read
/// by it, and nothing but a body changes. A nullable time span is covered by the framework's <c>NullableConverter</c>, which
/// wraps this one.
/// </para>
/// </remarks>
internal sealed class ClockTimeSpanConverter : JsonConverter<TimeSpan>
{
    /// <inheritdoc />
    public override TimeSpan Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        // Thrown with no path of its own on purpose: System.Text.Json appends the JSON path of the offending property to a
        // converter's JsonException, and RequestBodyBinding.Describe puts it in the problem's detail.
        if (reader.TokenType != JsonTokenType.String)
        {
            throw new JsonException($"The value must be a string holding {ConfigurationValueKinds.TimeSpanExpectation}.");
        }

        var text = reader.GetString();

        if (text is null || !ConfigurationValueKinds.TryTimeSpan(text, out var value))
        {
            throw new JsonException($"'{text}' is not {ConfigurationValueKinds.TimeSpanExpectation}.");
        }

        return value;
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, TimeSpan value, JsonSerializerOptions options)
        => writer.WriteStringValue(value.ToString("c", CultureInfo.InvariantCulture));
}
