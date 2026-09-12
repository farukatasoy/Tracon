using System.Text.Json;
using Microsoft.Agents.AI;

namespace Tracon;

/// <summary>
/// Converts the declarative check definitions in <see cref="EvalSuite.Checks"/>
/// into <see cref="EvalCheck"/> instances.
/// </summary>
/// <remarks>
/// <para>
/// The six built-in check kinds map directly to
/// <c>Microsoft.Agents.AI.EvalChecks</c> factories: <c>nonEmpty</c>,
/// <c>containsExpected</c>, <c>keywords</c>, <c>toolCalled</c>,
/// <c>toolCallsPresent</c>, <c>hasImageContent</c>. Any other kind name is
/// looked up among the custom checks registered with
/// <c>ITraconBuilder.AddEvalCheck(...)</c>; if not found there either, a
/// <see cref="TraconException"/> is thrown (the code-only tools rule - checks are declarative,
/// never silently ignored).
/// </para>
/// <para>
/// <c>toolCallArgsMatch</c> is intentionally not supported: <see cref="EvalCase"/>
/// only carries the expected tool <em>names</em>, not the expected arguments
/// needed for a full argument match.
/// </para>
/// </remarks>
internal sealed class EvalCheckRegistry
{
    private const string NonEmptyKind = "nonEmpty";
    private const string ContainsExpectedKind = "containsExpected";
    private const string KeywordsKind = "keywords";
    private const string ToolCalledKind = "toolCalled";
    private const string ToolCallsPresentKind = "toolCallsPresent";
    private const string HasImageContentKind = "hasImageContent";

    private static readonly string[] BuiltInKinds =
    [
        NonEmptyKind,
        ContainsExpectedKind,
        KeywordsKind,
        ToolCalledKind,
        ToolCallsPresentKind,
        HasImageContentKind,
    ];

    /// <summary>
    /// The built-in spelling of <paramref name="kind"/>, or <paramref name="kind"/>
    /// itself when no built-in matches.
    /// </summary>
    /// <remarks>
    /// Custom checks are looked up case-INSENSITIVELY; matching built-in names
    /// case-SENSITIVELY made one name mean two things ("nonempty" the custom
    /// check, "nonEmpty" the built-in one). One comparer on both sides, plus
    /// the shadowing guard in the constructor, closes that.
    /// </remarks>
    private static string Canonical(string kind)
        => Array.Find(BuiltInKinds, builtIn => string.Equals(builtIn, kind, StringComparison.OrdinalIgnoreCase))
           ?? kind;

    private readonly Dictionary<string, EvalCheck> _custom;

    /// <summary>Creates a new registry from a set of registrations.</summary>
    /// <param name="registrations">Custom check registrations.</param>
    /// <exception cref="ArgumentNullException"><paramref name="registrations"/> is <see langword="null"/>.</exception>
    /// <exception cref="TraconException">The same kind name is registered more than once.</exception>
    public EvalCheckRegistry(IEnumerable<TraconEvalCheckRegistration> registrations)
    {
        ArgumentNullException.ThrowIfNull(registrations);

        _custom = new Dictionary<string, EvalCheck>(StringComparer.OrdinalIgnoreCase);

        foreach (var registration in registrations)
        {
            if (Array.Exists(BuiltInKinds, kind => string.Equals(kind, registration.Kind, StringComparison.OrdinalIgnoreCase)))
            {
                throw new TraconException(
                    $"The eval check kind '{registration.Kind}' is built in and cannot be replaced. " +
                    "Shadowing a built-in kind would make the same suite mean different things in " +
                    "two applications. Register the check under a name of your own.");
            }

            if (!_custom.TryAdd(registration.Kind, registration.Check))
            {
                throw new TraconException(
                    $"Multiple custom eval checks named '{registration.Kind}' have been registered. " +
                    "Check kind names must be unique.");
            }
        }
    }

    /// <summary>
    /// Converts a suite's <see cref="EvalSuite.Checks"/> field into an array of
    /// <see cref="EvalCheck"/> instances.
    /// </summary>
    /// <param name="checks">JSON array carrying the check definitions.</param>
    /// <returns>The ordered list of checks. Returns an empty list when the payload is undefined or empty.</returns>
    /// <exception cref="TraconException">
    /// A definition does not carry a <c>kind</c> field, or points to an unknown kind name.
    /// </exception>
    public IReadOnlyList<EvalCheck> BuildChecks(JsonElement checks)
    {
        if (checks.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
        {
            return [];
        }

        if (checks.ValueKind != JsonValueKind.Array)
        {
            throw new TraconException("The 'checks' field must be a JSON array.");
        }

        var result = new List<EvalCheck>();

        foreach (var spec in checks.EnumerateArray())
        {
            result.Add(BuildCheck(spec));
        }

        return result;
    }

    private EvalCheck BuildCheck(JsonElement spec)
    {
        if (!spec.TryGetProperty("kind", out var kindElement) || kindElement.ValueKind != JsonValueKind.String)
        {
            throw new TraconException("Every check definition must carry a 'kind' (string) field.");
        }

        var kind = kindElement.GetString()!;

        switch (Canonical(kind))
        {
            case NonEmptyKind:
                return EvalChecks.NonEmpty(GetInt(spec, "minLength", 1));

            case ContainsExpectedKind:
                return EvalChecks.ContainsExpected(GetBool(spec, "caseSensitive", false));

            case KeywordsKind:
                var keywords = GetStringArray(spec, "values");
                return spec.TryGetProperty("caseSensitive", out _)
                    ? EvalChecks.KeywordCheck(GetBool(spec, "caseSensitive", false), keywords)
                    : EvalChecks.KeywordCheck(keywords);

            case ToolCalledKind:
                var tools = GetStringArray(spec, "tools");
                var mode = GetString(spec, "mode", "all") switch
                {
                    "any" => ToolCalledMode.Any,
                    "all" => ToolCalledMode.All,
                    var other => throw new TraconException(
                        $"'{other}' is not a valid 'toolCalled' mode. Valid values: 'all', 'any'."),
                };
                return EvalChecks.ToolCalledCheck(mode, tools);

            case ToolCallsPresentKind:
                return EvalChecks.ToolCallsPresent();

            case HasImageContentKind:
                return EvalChecks.HasImageContent();

            default:
                if (_custom.TryGetValue(kind, out var custom))
                {
                    return custom;
                }

                throw new TraconException(
                    $"Unknown check kind: '{kind}'. Built-in kinds: {string.Join(", ", BuiltInKinds)}. " +
                    $"If this is a custom check, it must be registered with " +
                    $"'ITraconBuilder.AddEvalCheck(\"{kind}\", ...)'.");
        }
    }

    private static int GetInt(JsonElement spec, string property, int fallback)
        => spec.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Number
            ? value.GetInt32()
            : fallback;

    private static bool GetBool(JsonElement spec, string property, bool fallback)
        => spec.TryGetProperty(property, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean()
            : fallback;

    private static string GetString(JsonElement spec, string property, string fallback)
        => spec.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()!
            : fallback;

    private static string[] GetStringArray(JsonElement spec, string property)
    {
        if (!spec.TryGetProperty(property, out var value) || value.ValueKind != JsonValueKind.Array)
        {
            throw new TraconException($"Check definition must carry a '{property}' array.");
        }

        return [.. value.EnumerateArray().Select(static element => element.GetString()!)];
    }
}
