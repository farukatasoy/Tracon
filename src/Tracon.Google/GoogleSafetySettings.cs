using Google.GenAI.Types;

namespace Tracon;

/// <summary>
/// Translates <c>google.safety.*</c> settings into Gemini's <see cref="SafetySetting"/>
/// list.
/// </summary>
/// <remarks>
/// An unrecognized threshold value is <strong>not silently ignored</strong>: an
/// error listing the valid values is thrown. A misspelled threshold would change
/// safety behavior in a way the user is unaware of.
/// </remarks>
internal static class GoogleSafetySettings
{
    private static readonly (string Setting, HarmCategory Category)[] Mappings =
    [
        (GoogleProviderNames.SafetyHarassmentSetting, HarmCategory.HarmCategoryHarassment),
        (GoogleProviderNames.SafetyHateSpeechSetting, HarmCategory.HarmCategoryHateSpeech),
        (GoogleProviderNames.SafetySexuallyExplicitSetting, HarmCategory.HarmCategorySexuallyExplicit),
        (GoogleProviderNames.SafetyDangerousContentSetting, HarmCategory.HarmCategoryDangerousContent),
        (GoogleProviderNames.SafetyCivicIntegritySetting, HarmCategory.HarmCategoryCivicIntegrity),
    ];

    /// <summary>Reads the safety thresholds from the binding.</summary>
    /// <param name="binding">Model binding.</param>
    /// <returns>Defined thresholds. Empty list when none are given.</returns>
    /// <exception cref="TraconException">A threshold value is not recognized.</exception>
    public static IReadOnlyList<SafetySetting> Read(ModelBinding binding)
    {
        List<SafetySetting>? settings = null;

        foreach (var (setting, category) in Mappings)
        {
            if (ModelProviderSettings.ReadString(binding, setting) is not { } threshold)
            {
                continue;
            }

            (settings ??= []).Add(new SafetySetting
            {
                Category = category,
                Threshold = ParseThreshold(setting, threshold),
            });
        }

        return settings is null ? [] : settings;
    }

    private static HarmBlockThreshold ParseThreshold(string setting, string value)
    {
        foreach (var candidate in HarmBlockThreshold.AllValues)
        {
            if (string.Equals(candidate.Value, value, StringComparison.OrdinalIgnoreCase))
            {
                return candidate;
            }
        }

        throw new ProviderSettingsValidationException(
            $"'{setting}' has an unrecognized value: '{value}'. Valid values: " +
            $"{string.Join(", ", HarmBlockThreshold.AllValues.Select(static candidate => candidate.Value))}.");
    }
}
