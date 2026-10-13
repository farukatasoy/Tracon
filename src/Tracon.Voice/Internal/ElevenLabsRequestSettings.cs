using System.Text.Json;

namespace Tracon;

/// <summary>
/// The <c>elevenlabs.*</c> provider settings of one speech or dialogue request, read and
/// checked before the service is called.
/// </summary>
/// <remarks>
/// <para>
/// The key rule (prefix, unknown key, case) is <see cref="ModelProviderSettings.Validate(IReadOnlyDictionary{string, JsonElement}, string, IReadOnlyCollection{string})"/>;
/// the type rule is its dictionary readers. This type adds the provider's own limits:
/// the ranges its OpenAPI document states, and the keys a dialogue does not take.
/// </para>
/// <para>
/// No message carries a value. A value can be the user's text
/// (<c>elevenlabs.previousText</c>), and the message reaches logs and run records.
/// </para>
/// </remarks>
internal sealed record ElevenLabsRequestSettings
{
    /// <summary>The largest seed the provider accepts.</summary>
    private const long MaxSeed = uint.MaxValue;

    /// <summary>The most request ids the provider accepts for continuity.</summary>
    private const int MaxPreviousRequestIds = 3;

    /// <summary>The longest continuity text a dialogue request accepts.</summary>
    private const int MaxDialogueContextCharacters = 100;

    /// <summary>Gets the settings of a request that sets none.</summary>
    public static ElevenLabsRequestSettings None { get; } = new();

    public double? Stability { get; init; }

    public double? SimilarityBoost { get; init; }

    public double? Style { get; init; }

    public bool? UseSpeakerBoost { get; init; }

    public double? Speed { get; init; }

    public long? Seed { get; init; }

    public string? PreviousText { get; init; }

    public string? NextText { get; init; }

    public IReadOnlyList<string>? PreviousRequestIds { get; init; }

    /// <summary>Reads the settings of a single-voice request.</summary>
    /// <param name="settings">The request's settings.</param>
    /// <returns>The settings.</returns>
    /// <exception cref="TraconException">A key is unknown, or a value has the wrong type or range.</exception>
    public static ElevenLabsRequestSettings ReadSpeech(IReadOnlyDictionary<string, JsonElement> settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        if (settings.Count == 0)
        {
            return None;
        }

        ModelProviderSettings.Validate(
            settings,
            ElevenLabsProviderNames.SettingsPrefix,
            ElevenLabsProviderNames.SupportedSettings);

        return Read(settings);
    }

    /// <summary>Reads the settings of a dialogue request.</summary>
    /// <param name="settings">The request's settings.</param>
    /// <returns>The settings.</returns>
    /// <exception cref="TraconException">
    /// A key is unknown or has no effect on a dialogue, or a value has the wrong type or
    /// range.
    /// </exception>
    public static ElevenLabsRequestSettings ReadDialogue(IReadOnlyDictionary<string, JsonElement> settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        if (settings.Count == 0)
        {
            return None;
        }

        // A single-voice key is a real ElevenLabs setting, so "not recognized" would
        // send the author the wrong way. The dialogue service accepts an unknown field
        // without an error (measured 2026-10-13); the key is refused here instead.
        var singleVoiceOnly = settings.Keys
            .Where(static key =>
                ElevenLabsProviderNames.SupportedSettings.Contains(key, StringComparer.OrdinalIgnoreCase) &&
                !ElevenLabsProviderNames.DialogueSupportedSettings.Contains(key, StringComparer.OrdinalIgnoreCase))
            .ToList();

        if (singleVoiceOnly.Count > 0)
        {
            throw new ProviderSettingsValidationException(
                $"ProviderSettings is invalid: these keys have no effect on a dialogue: {string.Join(", ", singleVoiceOnly)}. " +
                "The dialogue service takes only: " +
                $"{string.Join(", ", ElevenLabsProviderNames.DialogueSupportedSettings.Order(StringComparer.Ordinal))}.");
        }

        ModelProviderSettings.Validate(
            settings,
            ElevenLabsProviderNames.SettingsPrefix,
            ElevenLabsProviderNames.DialogueSupportedSettings);

        var read = Read(settings);

        CheckDialogueContext(ElevenLabsProviderNames.PreviousTextSetting, read.PreviousText);
        CheckDialogueContext(ElevenLabsProviderNames.NextTextSetting, read.NextText);

        return read;
    }

    /// <summary>Builds the single-voice <c>voice_settings</c> object, or <see langword="null"/> when none is set.</summary>
    /// <returns>The object to send.</returns>
    public ElevenLabsVoiceSettings? ToVoiceSettings()
        => Stability is null && SimilarityBoost is null && Style is null && UseSpeakerBoost is null && Speed is null
            ? null
            : new ElevenLabsVoiceSettings
            {
                Stability = Stability,
                SimilarityBoost = SimilarityBoost,
                Style = Style,
                UseSpeakerBoost = UseSpeakerBoost,
                Speed = Speed,
            };

    /// <summary>Builds the dialogue <c>settings</c> object, or <see langword="null"/> when none is set.</summary>
    /// <returns>The object to send.</returns>
    public ElevenLabsDialogueSettings? ToDialogueSettings()
        => Stability is null && SimilarityBoost is null
            ? null
            : new ElevenLabsDialogueSettings { Stability = Stability, Similarity = SimilarityBoost };

    private static ElevenLabsRequestSettings Read(IReadOnlyDictionary<string, JsonElement> settings)
    {
        var seed = ModelProviderSettings.ReadInt64(settings, ElevenLabsProviderNames.SeedSetting);

        if (seed is < 0 or > MaxSeed)
        {
            throw Invalid(ElevenLabsProviderNames.SeedSetting, $"must be an integer from 0 to {MaxSeed}");
        }

        var previousRequestIds = ModelProviderSettings.ReadStringList(settings, ElevenLabsProviderNames.PreviousRequestIdsSetting);

        if (previousRequestIds is { Count: > MaxPreviousRequestIds })
        {
            throw Invalid(
                ElevenLabsProviderNames.PreviousRequestIdsSetting,
                $"takes at most {MaxPreviousRequestIds} request ids; it has {previousRequestIds.Count}");
        }

        return new ElevenLabsRequestSettings
        {
            Stability = ReadUnit(settings, ElevenLabsProviderNames.StabilitySetting),
            SimilarityBoost = ReadUnit(settings, ElevenLabsProviderNames.SimilarityBoostSetting),
            Style = ModelProviderSettings.ReadDouble(settings, ElevenLabsProviderNames.StyleSetting),
            UseSpeakerBoost = ModelProviderSettings.ReadBoolean(settings, ElevenLabsProviderNames.UseSpeakerBoostSetting),
            Speed = ModelProviderSettings.ReadDouble(settings, ElevenLabsProviderNames.SpeedSetting),
            Seed = seed,
            PreviousText = ModelProviderSettings.ReadString(settings, ElevenLabsProviderNames.PreviousTextSetting),
            NextText = ModelProviderSettings.ReadString(settings, ElevenLabsProviderNames.NextTextSetting),
            PreviousRequestIds = previousRequestIds,
        };
    }

    /// <summary>Reads a number the provider bounds to 0..1 (its OpenAPI document's range).</summary>
    private static double? ReadUnit(IReadOnlyDictionary<string, JsonElement> settings, string key)
    {
        var value = ModelProviderSettings.ReadDouble(settings, key);

        return value is < 0 or > 1
            ? throw Invalid(key, "must be a number from 0 to 1")
            : value;
    }

    private static void CheckDialogueContext(string key, string? text)
    {
        if (text is { Length: > MaxDialogueContextCharacters })
        {
            throw Invalid(
                key,
                $"takes at most {MaxDialogueContextCharacters} characters on a dialogue; it has {text.Length}");
        }
    }

    // The count and the limit are written; the value never is.
    private static ProviderSettingsValidationException Invalid(string key, string rule)
        => new($"The '{key}' provider setting {rule}.");
}
