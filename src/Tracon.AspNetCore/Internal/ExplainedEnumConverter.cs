using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Tracon;

/// <summary>
/// Reads an enum exactly as <see cref="JsonStringEnumConverter{TEnum}"/> does,
/// and when the value is refused says which value was refused and which ones
/// are accepted.
/// </summary>
/// <typeparam name="TEnum">The enum this converter reads and writes.</typeparam>
/// <remarks>
/// <para>
/// The stock message is <c>"The JSON value could not be converted to
/// Tracon.CompactionStrategyKind. Path: $.compaction.strategy | LineNumber: 0 |
/// BytePositionInLine: 99."</c>. Of that, only the path appears anywhere in the
/// request the caller wrote; the CLR type name and the byte offset do not, and
/// neither the refused value nor the accepted ones are named at all. The only
/// way forward was to go and search the documentation. Every enum-typed field
/// in every contract answered that way, because the conversion fails during
/// deserialization, before any Tracon validation code runs — unlike
/// <c>reasoningEffort</c>, which is taken as a string and resolved by Tracon,
/// and which consequently already produced a good message.
/// </para>
/// <para>
/// Parsing is deliberately NOT reimplemented. This converter delegates to the
/// one <see cref="JsonStringEnumConverter{TEnum}"/> builds and replaces only
/// the message that converter throws, so naming policy, integer values, and
/// casing stay exactly what they were.
/// </para>
/// <para>
/// 🚨 This is attached to the REQUEST-BODY options
/// (<see cref="RequestBodyBinding"/>), never to the enum types themselves. A
/// <c>[JsonConverter]</c> attribute would reach every path, including schema
/// generation — and it was measured there: <c>JsonSchemaExporter</c> recognizes
/// only the framework's own enum converter, so the published OpenAPI document
/// lost the <c>enum</c> list of all 42 enums (287 lines) and every generated
/// client would have lost its unions with it. An options-level converter takes
/// precedence over a type-level attribute (the documented resolution order is
/// property attribute, then options collection, then type attribute), which is
/// exactly the reach this needs: request bodies, and nothing else.
/// </para>
/// </remarks>
internal sealed class ExplainedEnumConverter<TEnum> : JsonConverter<TEnum>
    where TEnum : struct, Enum
{
    private static readonly JsonStringEnumConverter<TEnum> Factory = new();

    private static readonly ConditionalWeakTable<JsonSerializerOptions, JsonConverter<TEnum>> Inner = new();

    /// <summary>The accepted names, in declaration order, as one comma-separated list.</summary>
    /// <remarks>
    /// <see cref="Enum.GetNames{TEnum}()"/> resolves through the generic
    /// constraint, so it carries no trimming or AOT warning. The names are the
    /// wire values: <see cref="JsonStringEnumConverter{TEnum}"/> is constructed
    /// with no naming policy, here and on the types themselves.
    /// </remarks>
    private static readonly string ValidValues = string.Join(", ", Enum.GetNames<TEnum>());

    /// <inheritdoc />
    public override TEnum Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        // Utf8JsonReader is a struct, so this copy still points at the value
        // token wherever the inner converter leaves the live reader.
        var snapshot = reader;

        try
        {
            return Resolve(options).Read(ref reader, typeToConvert, options);
        }
        catch (JsonException)
        {
            // Thrown with no path of its own on purpose: System.Text.Json
            // appends the JSON path of the offending property to a converter's
            // JsonException, and that path is the one part of the stock message
            // worth keeping.
            throw new JsonException(Explain(ref snapshot));
        }
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, TEnum value, JsonSerializerOptions options)
        => Resolve(options).Write(writer, value, options);

    private static string Explain(ref Utf8JsonReader snapshot)
    {
        var rejected = snapshot.TokenType switch
        {
            JsonTokenType.String => $"'{snapshot.GetString()}'",
            JsonTokenType.Null => "null",
            JsonTokenType.Number or JsonTokenType.True or JsonTokenType.False => $"'{RawText(ref snapshot)}'",
            _ => "the given value",
        };

        return $"{rejected} is not a valid value. The valid values are: {ValidValues}.";
    }

    /// <summary>Reads a non-string token back as text, across a segmented buffer too.</summary>
    private static string RawText(ref Utf8JsonReader snapshot)
        => Encoding.UTF8.GetString(
            snapshot.HasValueSequence ? snapshot.ValueSequence.ToArray() : snapshot.ValueSpan.ToArray());

    private static JsonConverter<TEnum> Resolve(JsonSerializerOptions options)
        => Inner.GetValue(
            options,
            static source => (JsonConverter<TEnum>)Factory.CreateConverter(typeof(TEnum), source)!);
}

/// <summary>
/// Builds an <see cref="ExplainedEnumConverter{TEnum}"/> for every enum, so a
/// single registration covers each of the contracts' enum-typed fields rather
/// than one attribute per enum.
/// </summary>
/// <remarks>
/// A nullable enum is handled by the framework's own
/// <c>NullableConverter</c>, which wraps the converter this factory returns for
/// the underlying type — so <c>CompactionStrategyKind?</c> is covered without a
/// second branch.
/// </remarks>
internal sealed class ExplainedEnumConverterFactory : JsonConverterFactory
{
    /// <inheritdoc />
    public override bool CanConvert(Type typeToConvert)
    {
        ArgumentNullException.ThrowIfNull(typeToConvert);

        return typeToConvert.IsEnum;
    }

    /// <inheritdoc />
    [UnconditionalSuppressMessage(
        "AOT",
        "IL3050:RequiresDynamicCode",
        Justification = "The constructed type closes a generic over an enum, which is a value type; " +
                        "the runtime shares no code for it but the instantiation is reachable from " +
                        "every enum the contracts declare, all of which are rooted by the same " +
                        "JsonSerializerContext that serializes them.")]
    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(typeToConvert);

        return (JsonConverter)Activator.CreateInstance(
            typeof(ExplainedEnumConverter<>).MakeGenericType(typeToConvert))!;
    }
}
