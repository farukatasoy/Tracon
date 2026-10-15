using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.AI;

namespace Tracon;

/// <summary>
/// Reads, checks and describes the settings of a function node - the one place
/// that knows how, so the save-time check, the compile-time read and the exported
/// schema cannot disagree.
/// </summary>
/// <remarks>
/// <para>
/// <strong>The check IS the read.</strong> Validation deserializes the settings into
/// the function's <c>TSettings</c> with the strict options below, and the function
/// receives the object that read produced. There are not two rules (a schema
/// validator and a deserializer) that could drift apart: tracon ships no JSON
/// Schema validator on purpose (<see cref="IStructuredResponseValidator"/>).
/// </para>
/// <para>
/// Strict means: an unknown field is refused (a misspelled name must not silently
/// keep its default), a <c>required</c> member must be present, a number must be a
/// JSON number, an enum must be written by name, and a <c>null</c> is refused where
/// the member's type does not allow it. Names are camelCase and matched without
/// regard to case, like every other body Tracon reads.
/// </para>
/// <para>
/// The exported schema is made with the SAME options, so its property names, enum
/// values and <c>additionalProperties: false</c> describe what the read accepts.
/// </para>
/// </remarks>
internal static partial class WorkflowFunctionSettings
{
    private static readonly JsonSerializerOptions StrictOptions = CreateOptions();

    private static readonly JsonElement EmptyObject = JsonDocument.Parse("{}").RootElement.Clone();

    /// <summary>
    /// The schema reported for a settings type whose real schema could not be produced:
    /// an object, with no fields listed.
    /// </summary>
    public static JsonElement UnknownSchema { get; } = JsonDocument.Parse("""{"type":"object"}""").RootElement.Clone();

    /// <summary>Reads the node's settings into the function's settings type.</summary>
    /// <typeparam name="T">The function's settings type.</typeparam>
    /// <param name="functionName">The function (and node) name, for the message.</param>
    /// <param name="settings">The node's settings; <see langword="null"/> is read as <c>{}</c>.</param>
    /// <returns>The settings object the function receives.</returns>
    /// <exception cref="WorkflowFunctionSettingsException">The settings do not fit <typeparamref name="T"/>.</exception>
    public static T Deserialize<T>(string functionName, JsonElement? settings)
        where T : class
        => (T)Deserialize(typeof(T), functionName, settings);

    /// <summary>Reads the node's settings into <paramref name="settingsType"/>.</summary>
    /// <param name="settingsType">The function's settings type.</param>
    /// <param name="functionName">The function (and node) name, for the message.</param>
    /// <param name="settings">The node's settings; <see langword="null"/> is read as <c>{}</c>.</param>
    /// <returns>The settings object the function receives.</returns>
    /// <exception cref="WorkflowFunctionSettingsException">The settings do not fit the type.</exception>
    public static object Deserialize(Type settingsType, string functionName, JsonElement? settings)
    {
        try
        {
            return settings.GetValueOrDefault(EmptyObject).Deserialize(settingsType, StrictOptions)
                   ?? throw new WorkflowFunctionSettingsException(Invalid(functionName, "the settings read as null."));
        }

        // JsonException is the usual refusal. NotSupportedException is the same
        // refusal for a member the serializer cannot set at all (a System.Type
        // member given a value): the operator wrote a value for something that
        // cannot hold one.
        catch (Exception exception) when (exception is JsonException or NotSupportedException)
        {
            throw new WorkflowFunctionSettingsException(Invalid(functionName, Describe(exception)), exception);
        }

        // The settings type is the consumer's own code, and a setter or a constructor in it can
        // refuse a value by throwing (a range check, say). That is the operator's input being
        // refused too, not a server error. The exception's own message stays out of the answer:
        // a message written elsewhere never reaches a response or a stored run error (it can
        // carry the value). The type name says what kind of refusal it was, and the exception
        // itself travels as the inner exception, which the callers log. No token governs this
        // synchronous read, so there is no cancellation to tell apart.
        catch (Exception exception) when (exception is not (WorkflowFunctionSettingsException or OutOfMemoryException))
        {
            throw new WorkflowFunctionSettingsException(
                Invalid(functionName, $"the settings type rejected a value ({exception.GetType().Name})."),
                exception);
        }
    }

    /// <summary>
    /// Whether the settings error came from the consumer's own code - a setter or a constructor
    /// that threw - rather than from the serializer refusing the shape of the settings.
    /// </summary>
    /// <param name="exception">The error <see cref="Deserialize(Type, string, JsonElement?)"/> raised.</param>
    /// <returns><see langword="true"/> when the inner exception is the consumer's.</returns>
    public static bool IsRaisedByConsumerCode(WorkflowFunctionSettingsException exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        return exception.InnerException is { } inner && inner is not (JsonException or NotSupportedException);
    }

    /// <summary>
    /// Proves the settings type is one the serializer can create, when its function is
    /// registered.
    /// </summary>
    /// <param name="settingsType">The function's settings type.</param>
    /// <param name="functionName">The function name, for the message.</param>
    /// <exception cref="TraconException">The type can never be read (an interface or an abstract class, for example).</exception>
    /// <remarks>
    /// Without this, such a type would be blamed on the operator at every save: the
    /// serializer's "abstract types are not supported" would read as "your settings are
    /// invalid". A <see cref="JsonException"/> here is fine - the type is readable, it just
    /// refuses an empty object because it has a required member.
    /// </remarks>
    public static void EnsureReadable(Type settingsType, string functionName)
    {
        try
        {
            _ = EmptyObject.Deserialize(settingsType, StrictOptions);
        }
        catch (JsonException)
        {
            // Readable. A required member makes {} invalid, which is a legitimate shape.
        }
        catch (Exception exception) when (exception is NotSupportedException or InvalidOperationException)
        {
            // The serializer's own sentence travels in the inner exception, which the host prints at startup.
            throw new TraconException(
                $"The settings type '{settingsType.Name}' of workflow function '{functionName}' cannot be read by " +
                $"System.Text.Json ({exception.GetType().Name}), so no settings could ever be given to the function. " +
                "Use a concrete class or record with a public constructor.",
                exception);
        }
    }

    /// <summary>Writes a typed settings value as the JSON element a node carries.</summary>
    /// <typeparam name="T">The function's settings type.</typeparam>
    /// <param name="settings">The value.</param>
    /// <returns>A detached element that reads back into <typeparamref name="T"/>.</returns>
    /// <exception cref="JsonException">The value cannot be written.</exception>
    /// <remarks>
    /// The same options as the read, so what a graph built in code hands over and what an operator saves
    /// are checked by one rule.
    /// </remarks>
    public static JsonElement Serialize<T>(T settings)
        where T : class
        => JsonSerializer.SerializeToElement(settings, StrictOptions);

    /// <summary>Produces the JSON schema of the settings type, for display.</summary>
    /// <param name="settingsType">The function's settings type.</param>
    /// <returns>A detached schema element.</returns>
    public static JsonElement CreateSchema(Type settingsType)
        => AIJsonUtilities.CreateJsonSchema(settingsType, serializerOptions: StrictOptions).Clone();

    /// <summary>The message a node gets when its function declares no settings.</summary>
    /// <param name="functionName">The function (and node) name.</param>
    /// <returns>The message.</returns>
    public static string NoSettings(string functionName)
        => $"Function node '{functionName}' carries settings, but the function declares none. " +
           "Remove the node's 'settings', or register the function with a settings type: " +
           "AddWorkflowFunction<TInput, TOutput, TSettings>().";

    private static string Invalid(string functionName, string detail)
        => $"Function node '{functionName}' has invalid settings: {detail}";

    /// <summary>
    /// The serializer's own sentence, without the line and byte position it appends
    /// (the settings are a parsed element, so a position means nothing), and with the
    /// JSON path of the offending value said once, at the end.
    /// </summary>
    private static string Describe(Exception exception)
    {
        var message = exception.Message;
        var path = (exception as JsonException)?.Path;

        if (PositionSuffix().Match(message) is { Success: true } suffix)
        {
            path ??= suffix.Groups["path"].Value.TrimEnd('.');
            message = message[..suffix.Index].TrimEnd();
        }

        // "$" is the object itself; only a deeper path says something the message does not.
        return path is { Length: > 1 } ? $"{message} Path: {path}" : message;
    }

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            NumberHandling = JsonNumberHandling.Strict,
            RespectNullableAnnotations = true,
            RespectRequiredConstructorParameters = true,
        };

        // By name only: the exported schema lists the names, and "speed": 1 is a
        // typing slip the operator should hear about, not a value that happens to work.
        options.Converters.Add(new JsonStringEnumConverter(allowIntegerValues: false));

        return options;
    }

    // The serializer appends " Path: $.field | LineNumber: 0 | BytePositionInLine: 34." to
    // most of its messages (and a bare " Path: $.field" to some).
    [GeneratedRegex(
        @"\s*Path: (?<path>\S+)(\s*\|\s*LineNumber: \d+)?(\s*\|\s*BytePositionInLine: \d+)?\.?\s*$",
        RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture,
        matchTimeoutMilliseconds: 1000)]
    private static partial Regex PositionSuffix();
}
