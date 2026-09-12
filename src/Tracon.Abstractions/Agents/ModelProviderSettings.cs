using System.Globalization;
using System.Text.Json;

namespace Tracon;

/// <summary>
/// Thrown by <see cref="ModelProviderSettings.Validate"/>. Internal and
/// unforgeable from outside this assembly: the model-call normalization
/// boundary in <c>Tracon.Core</c> trusts this exact type to carry a safe,
/// secret-free message, something a foreign or malicious <see cref="IModelProvider"/>
/// could not fake by throwing the public base <see cref="TraconException"/> directly.
/// </summary>
internal sealed class ProviderSettingsValidationException(string message) : TraconException(message);

/// <summary>
/// Helpers that read and validate the <see cref="ModelBinding.ProviderSettings"/>
/// dictionary.
/// </summary>
/// <remarks>
/// <para>
/// Each provider package knows its own key prefix (<c>anthropic</c>, <c>google</c>) and
/// carries the list of keys it supports. This type applies that list, so the validation
/// logic is not written again in every new provider package.
/// </para>
/// <para>
/// <strong>An unknown key is not ignored silently.</strong> A setting that is ignored
/// makes the user miss the behaviour they expect without seeing why — the same decision
/// as the one taken for <see cref="ModelBinding.ReasoningEffort"/>.
/// </para>
/// <para>
/// Keys are compared case insensitively. When the dictionary is read back from a
/// <c>jsonb</c> column it arrives with the default (ordinal) comparer; the lookup is
/// therefore not left to the comparer of the dictionary itself.
/// </para>
/// </remarks>
public static class ModelProviderSettings
{
    /// <summary>
    /// Validates that every provider setting in the binding belongs to the given prefix
    /// and is supported.
    /// </summary>
    /// <param name="binding">The model binding to check.</param>
    /// <param name="providerPrefix">The key prefix of the provider, for example <c>anthropic</c>.</param>
    /// <param name="supportedKeys">
    /// The full keys the provider supports, prefix included, for example
    /// <c>anthropic.promptCaching</c>.
    /// </param>
    /// <exception cref="ArgumentNullException">One of the parameters is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="providerPrefix"/> is empty.</exception>
    /// <exception cref="TraconException">
    /// The dictionary holds a key that belongs to another provider or that is not
    /// recognized. The message lists the supported keys.
    /// </exception>
    public static void Validate(
        ModelBinding binding,
        string providerPrefix,
        IReadOnlyCollection<string> supportedKeys)
    {
        ArgumentNullException.ThrowIfNull(binding);
        ArgumentException.ThrowIfNullOrWhiteSpace(providerPrefix);
        ArgumentNullException.ThrowIfNull(supportedKeys);

        if (binding.ProviderSettings.Count == 0)
        {
            return;
        }

        var known = new HashSet<string>(supportedKeys, StringComparer.OrdinalIgnoreCase);
        var prefix = providerPrefix + ".";
        List<string>? foreignKeys = null;
        List<string>? unknownKeys = null;

        foreach (var key in binding.ProviderSettings.Keys)
        {
            if (!key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                (foreignKeys ??= []).Add(key);
            }
            else if (!known.Contains(key))
            {
                (unknownKeys ??= []).Add(key);
            }
        }

        if (foreignKeys is null && unknownKeys is null)
        {
            return;
        }

        var supported = supportedKeys.Count == 0
            ? $"the '{providerPrefix}' provider supports no extra settings"
            : $"Supported keys: {string.Join(", ", supportedKeys.Order(StringComparer.Ordinal))}";

        // The two error classes are told apart: a wrong prefix means "you wrote the
        // setting on another provider", while an unrecognized key is a typo or an
        // unsupported feature. Joining the two in one message sends the user the
        // wrong way.
        var problems = new List<string>(2);

        if (foreignKeys is not null)
        {
            problems.Add(
                $"these keys do not belong to the '{providerPrefix}' provider: {string.Join(", ", foreignKeys)}. " +
                $"{nameof(ModelBinding)}.{nameof(ModelBinding.ProviderSettings)} can carry only the keys of the " +
                $"provider named in {nameof(ModelBinding)}.{nameof(ModelBinding.Provider)}; " +
                "the old settings must be cleared when the provider changes");
        }

        if (unknownKeys is not null)
        {
            problems.Add($"these keys are not recognized: {string.Join(", ", unknownKeys)}");
        }

        throw new ProviderSettingsValidationException(
            $"{nameof(ModelBinding)}.{nameof(ModelBinding.ProviderSettings)} is invalid: " +
            $"{string.Join("; and ", problems)}. {supported}.");
    }

    /// <summary>Reads a setting as a boolean value.</summary>
    /// <param name="binding">The model binding.</param>
    /// <param name="key">The full key, for example <c>anthropic.promptCaching</c>.</param>
    /// <returns><see langword="null"/> when the setting is not defined.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="binding"/> is <see langword="null"/>.</exception>
    /// <exception cref="TraconException">The value is not a boolean value.</exception>
    public static bool? ReadBoolean(ModelBinding binding, string key)
    {
        if (!TryGetValue(binding, key, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,

            // Configuration providers other than appsettings.json (environment
            // variables and the command line) carry every value as text; rejecting
            // the text "true" would give the user an error they cannot explain.
            JsonValueKind.String when bool.TryParse(value.GetString(), out var parsed) => parsed,
            _ => throw TypeMismatch(key, "a boolean (true/false)", value),
        };
    }

    /// <summary>Reads a setting as an integer.</summary>
    /// <param name="binding">The model binding.</param>
    /// <param name="key">The full key, for example <c>anthropic.thinking.budgetTokens</c>.</param>
    /// <returns><see langword="null"/> when the setting is not defined.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="binding"/> is <see langword="null"/>.</exception>
    /// <exception cref="TraconException">The value is not an integer.</exception>
    public static int? ReadInt32(ModelBinding binding, string key)
    {
        if (!TryGetValue(binding, key, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.Number when value.TryGetInt32(out var number) => number,
            JsonValueKind.String when int.TryParse(
                value.GetString(),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var parsed) => parsed,
            _ => throw TypeMismatch(key, "an integer", value),
        };
    }

    /// <summary>Reads a setting as text.</summary>
    /// <param name="binding">The model binding.</param>
    /// <param name="key">The full key, for example <c>google.safety.harassment</c>.</param>
    /// <returns><see langword="null"/> when the setting is not defined or is empty.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="binding"/> is <see langword="null"/>.</exception>
    /// <exception cref="TraconException">The value is not text.</exception>
    public static string? ReadString(ModelBinding binding, string key)
    {
        if (!TryGetValue(binding, key, out var value))
        {
            return null;
        }

        if (value.ValueKind is not JsonValueKind.String)
        {
            throw TypeMismatch(key, "text", value);
        }

        var text = value.GetString();
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }

    private static bool TryGetValue(ModelBinding binding, string key, out JsonElement value)
    {
        ArgumentNullException.ThrowIfNull(binding);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        if (binding.ProviderSettings.TryGetValue(key, out value))
        {
            return IsPresent(value);
        }

        foreach (var pair in binding.ProviderSettings)
        {
            if (string.Equals(pair.Key, key, StringComparison.OrdinalIgnoreCase))
            {
                value = pair.Value;
                return IsPresent(value);
            }
        }

        value = default;
        return false;
    }

    // The ValueKind of an unassigned JsonElement is Undefined; it means the same as
    // "absent" and must not turn into an exception on the read path.
    private static bool IsPresent(JsonElement value)
        => value.ValueKind is not (JsonValueKind.Undefined or JsonValueKind.Null);

    private static TraconException TypeMismatch(string key, string expected, JsonElement value)
        => new($"The '{key}' provider setting expects {expected}; the value given is {value.ValueKind}.");
}
