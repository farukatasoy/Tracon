using System.Globalization;
using System.Text.Json;

namespace Tracon;

/// <summary>
/// Thrown by <see cref="ModelProviderSettings.Validate(ModelBinding, string, IReadOnlyCollection{string})"/>. Internal and
/// unforgeable from outside this assembly: the model-call normalization
/// boundary in <c>Tracon.Core</c> trusts this exact type to carry a safe,
/// secret-free message, something a foreign or malicious <see cref="IModelProvider"/>
/// could not fake by throwing the public base <see cref="TraconException"/> directly.
/// </summary>
internal sealed class ProviderSettingsValidationException(string message) : TraconException(message);

/// <summary>
/// Helpers that read and validate a provider settings dictionary: the
/// <see cref="ModelBinding.ProviderSettings"/> of a model binding, and the
/// <c>ProviderSettings</c> of a speech request.
/// </summary>
/// <remarks>
/// <para>
/// Each provider package knows its own key prefix (<c>anthropic</c>, <c>google</c>,
/// <c>elevenlabs</c>) and carries the list of keys it supports. This type applies that
/// list, so the validation logic is not written again in every new provider package.
/// </para>
/// <para>
/// <strong>An unknown key is not ignored silently.</strong> A setting that is ignored
/// makes the user miss the behaviour they expect without seeing why — the same decision
/// as the one taken for <see cref="ModelBinding.ReasoningEffort"/>.
/// </para>
/// <para>
/// <strong>A refusal names the key, never the value.</strong> A value can be text a user
/// wrote or a credential pasted into the wrong field, and the message reaches logs and
/// run records. A value of the wrong type is described by its JSON kind.
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

        ValidateCore(
            binding.ProviderSettings,
            providerPrefix,
            supportedKeys,
            $"{nameof(ModelBinding)}.{nameof(ModelBinding.ProviderSettings)}",
            $"{nameof(ModelBinding)}.{nameof(ModelBinding.ProviderSettings)} can carry only the keys of the " +
            $"provider named in {nameof(ModelBinding)}.{nameof(ModelBinding.Provider)}; " +
            "the old settings must be cleared when the provider changes");
    }

    /// <summary>
    /// Validates that every key of a provider settings dictionary belongs to the given
    /// prefix and is supported.
    /// </summary>
    /// <param name="settings">
    /// The settings to check, for example <c>SpeechRequest.ProviderSettings</c>.
    /// </param>
    /// <param name="providerPrefix">The key prefix of the provider, for example <c>elevenlabs</c>.</param>
    /// <param name="supportedKeys">
    /// The full keys the provider supports, prefix included, for example
    /// <c>elevenlabs.stability</c>. Pass an empty list when the provider supports none.
    /// </param>
    /// <exception cref="ArgumentNullException">One of the parameters is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="providerPrefix"/> is empty.</exception>
    /// <exception cref="TraconException">
    /// The dictionary holds a key that belongs to another provider or that is not
    /// recognized. The message names those keys and lists the supported ones; it never
    /// carries a value.
    /// </exception>
    /// <remarks>
    /// The same rule as the <see cref="ModelBinding"/> overload, which runs the same code:
    /// keys are compared case-insensitively, a key of another provider is told apart from
    /// an unrecognized one, and nothing is ignored silently.
    /// <example>
    /// A speech provider that supports one setting checks a request before it calls its
    /// service:
    /// <code>
    /// ModelProviderSettings.Validate(
    ///     request.ProviderSettings,
    ///     "contoso",
    ///     ["contoso.pitch"]);
    /// </code>
    /// </example>
    /// </remarks>
    public static void Validate(
        IReadOnlyDictionary<string, JsonElement> settings,
        string providerPrefix,
        IReadOnlyCollection<string> supportedKeys)
    {
        ArgumentNullException.ThrowIfNull(settings);

        ValidateCore(
            settings,
            providerPrefix,
            supportedKeys,
            "ProviderSettings",
            "A request's ProviderSettings can carry only the keys of the provider that receives it");
    }

    /// <summary>Reads a setting as a boolean value.</summary>
    /// <param name="binding">The model binding.</param>
    /// <param name="key">The full key, for example <c>anthropic.promptCaching</c>.</param>
    /// <returns><see langword="null"/> when the setting is not defined.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="binding"/> is <see langword="null"/>.</exception>
    /// <exception cref="TraconException">The value is not a boolean value.</exception>
    public static bool? ReadBoolean(ModelBinding binding, string key)
    {
        ArgumentNullException.ThrowIfNull(binding);

        return ReadBoolean(binding.ProviderSettings, key);
    }

    /// <summary>Reads a setting as an integer.</summary>
    /// <param name="binding">The model binding.</param>
    /// <param name="key">The full key, for example <c>anthropic.thinking.budgetTokens</c>.</param>
    /// <returns><see langword="null"/> when the setting is not defined.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="binding"/> is <see langword="null"/>.</exception>
    /// <exception cref="TraconException">The value is not an integer.</exception>
    public static int? ReadInt32(ModelBinding binding, string key)
    {
        ArgumentNullException.ThrowIfNull(binding);

        return ReadInt32(binding.ProviderSettings, key);
    }

    /// <summary>Reads a setting as text.</summary>
    /// <param name="binding">The model binding.</param>
    /// <param name="key">The full key, for example <c>google.safety.harassment</c>.</param>
    /// <returns><see langword="null"/> when the setting is not defined or is empty.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="binding"/> is <see langword="null"/>.</exception>
    /// <exception cref="TraconException">The value is not text.</exception>
    public static string? ReadString(ModelBinding binding, string key)
    {
        ArgumentNullException.ThrowIfNull(binding);

        return ReadString(binding.ProviderSettings, key);
    }

    // The dictionary readers below are internal: a first-party provider package
    // (InternalsVisibleTo) reads a speech request's settings with the same type rule the
    // public binding readers apply. A third-party provider reads the JsonElement itself.

    /// <summary>Reads a setting of a settings dictionary as a boolean value.</summary>
    /// <param name="settings">The settings.</param>
    /// <param name="key">The full key.</param>
    /// <returns><see langword="null"/> when the setting is not defined.</returns>
    /// <exception cref="TraconException">The value is not a boolean value.</exception>
    internal static bool? ReadBoolean(IReadOnlyDictionary<string, JsonElement> settings, string key)
    {
        if (!TryGetValue(settings, key, out var value))
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

    /// <summary>Reads a setting of a settings dictionary as a 32-bit integer.</summary>
    /// <param name="settings">The settings.</param>
    /// <param name="key">The full key.</param>
    /// <returns><see langword="null"/> when the setting is not defined.</returns>
    /// <exception cref="TraconException">The value is not an integer.</exception>
    internal static int? ReadInt32(IReadOnlyDictionary<string, JsonElement> settings, string key)
    {
        if (!TryGetValue(settings, key, out var value))
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

    /// <summary>Reads a setting of a settings dictionary as a 64-bit integer.</summary>
    /// <param name="settings">The settings.</param>
    /// <param name="key">The full key.</param>
    /// <returns><see langword="null"/> when the setting is not defined.</returns>
    /// <exception cref="TraconException">The value is not an integer.</exception>
    internal static long? ReadInt64(IReadOnlyDictionary<string, JsonElement> settings, string key)
    {
        if (!TryGetValue(settings, key, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.Number when value.TryGetInt64(out var number) => number,
            JsonValueKind.String when long.TryParse(
                value.GetString(),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var parsed) => parsed,
            _ => throw TypeMismatch(key, "an integer", value),
        };
    }

    /// <summary>Reads a setting of a settings dictionary as a finite number.</summary>
    /// <param name="settings">The settings.</param>
    /// <param name="key">The full key.</param>
    /// <returns><see langword="null"/> when the setting is not defined.</returns>
    /// <exception cref="TraconException">The value is not a finite number.</exception>
    internal static double? ReadDouble(IReadOnlyDictionary<string, JsonElement> settings, string key)
    {
        if (!TryGetValue(settings, key, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.Number when value.TryGetDouble(out var number) && double.IsFinite(number) => number,
            JsonValueKind.String when double.TryParse(
                value.GetString(),
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var parsed) && double.IsFinite(parsed) => parsed,
            _ => throw TypeMismatch(key, "a number", value),
        };
    }

    /// <summary>Reads a setting of a settings dictionary as text.</summary>
    /// <param name="settings">The settings.</param>
    /// <param name="key">The full key.</param>
    /// <returns><see langword="null"/> when the setting is not defined or is empty.</returns>
    /// <exception cref="TraconException">The value is not text.</exception>
    internal static string? ReadString(IReadOnlyDictionary<string, JsonElement> settings, string key)
    {
        if (!TryGetValue(settings, key, out var value))
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

    /// <summary>Reads a setting of a settings dictionary as a list of non-blank texts.</summary>
    /// <param name="settings">The settings.</param>
    /// <param name="key">The full key.</param>
    /// <returns><see langword="null"/> when the setting is not defined.</returns>
    /// <exception cref="TraconException">The value is not a list of non-blank texts.</exception>
    internal static IReadOnlyList<string>? ReadStringList(IReadOnlyDictionary<string, JsonElement> settings, string key)
    {
        if (!TryGetValue(settings, key, out var value))
        {
            return null;
        }

        if (value.ValueKind is not JsonValueKind.Array)
        {
            throw TypeMismatch(key, "a list of text", value);
        }

        var items = new List<string>(value.GetArrayLength());

        foreach (var item in value.EnumerateArray())
        {
            if (item.ValueKind is not JsonValueKind.String || item.GetString() is not { } text || string.IsNullOrWhiteSpace(text))
            {
                throw TypeMismatch(key, "a list of non-blank text", item);
            }

            items.Add(text);
        }

        return items;
    }

    // One rule for both Validate overloads: a model binding and a speech request
    // differ only in what the message calls the dictionary.
    private static void ValidateCore(
        IReadOnlyDictionary<string, JsonElement> settings,
        string providerPrefix,
        IReadOnlyCollection<string> supportedKeys,
        string subject,
        string foreignHint)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerPrefix);
        ArgumentNullException.ThrowIfNull(supportedKeys);

        if (settings.Count == 0)
        {
            return;
        }

        var known = new HashSet<string>(supportedKeys, StringComparer.OrdinalIgnoreCase);
        var prefix = providerPrefix + ".";
        List<string>? foreignKeys = null;
        List<string>? unknownKeys = null;

        foreach (var key in settings.Keys)
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
                foreignHint);
        }

        if (unknownKeys is not null)
        {
            problems.Add($"these keys are not recognized: {string.Join(", ", unknownKeys)}");
        }

        throw new ProviderSettingsValidationException(
            $"{subject} is invalid: {string.Join("; and ", problems)}. {supported}.");
    }

    private static bool TryGetValue(IReadOnlyDictionary<string, JsonElement> settings, string key, out JsonElement value)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        if (settings.TryGetValue(key, out value))
        {
            return IsPresent(value);
        }

        foreach (var pair in settings)
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

    // ProviderSettingsValidationException, not the public base: the registry
    // masks any other exception a provider throws while it builds a client, and
    // the operator lost the key ("The model provider request failed.") for a
    // value of the wrong JSON type (measured through ModelProviderRegistry).
    // The value itself is never written: only its JSON kind.
    private static ProviderSettingsValidationException TypeMismatch(string key, string expected, JsonElement value)
        => new($"The '{key}' provider setting expects {expected}; the value given is {value.ValueKind}.");
}
