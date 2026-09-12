namespace Tracon;

/// <summary>
/// Provider name registered by the <c>UseGoogle()</c> call, and the
/// <see cref="ModelBinding.ProviderSettings"/> keys.
/// </summary>
/// <remarks>
/// <para>
/// These names are <strong>stable</strong>. Agent definitions are stored in the
/// database with these names; changing them breaks stored definitions.
/// </para>
/// <para>
/// The name is <c>google</c>, not <c>gemini</c>: the same package may cover
/// Vertex AI in the future and must not be locked to the model family's name.
/// </para>
/// </remarks>
public static class GoogleProviderNames
{
    /// <summary>Provider using the Gemini Developer API: <c>google</c>.</summary>
    public const string Google = "google";

    /// <summary>Prefix for provider-specific settings: <c>google</c>.</summary>
    public const string SettingsPrefix = "google";

    /// <summary>Harassment threshold: <c>google.safety.harassment</c> (text).</summary>
    public const string SafetyHarassmentSetting = "google.safety.harassment";

    /// <summary>Hate speech threshold: <c>google.safety.hateSpeech</c> (text).</summary>
    public const string SafetyHateSpeechSetting = "google.safety.hateSpeech";

    /// <summary>Sexually explicit content threshold: <c>google.safety.sexuallyExplicit</c> (text).</summary>
    public const string SafetySexuallyExplicitSetting = "google.safety.sexuallyExplicit";

    /// <summary>Dangerous content threshold: <c>google.safety.dangerousContent</c> (text).</summary>
    public const string SafetyDangerousContentSetting = "google.safety.dangerousContent";

    /// <summary>Civic integrity threshold: <c>google.safety.civicIntegrity</c> (text).</summary>
    public const string SafetyCivicIntegritySetting = "google.safety.civicIntegrity";

    /// <summary>
    /// Thinking budget: <c>google.thinking.budgetTokens</c> (integer).
    /// </summary>
    /// <remarks>
    /// The valid range is <c>[-1, 65535]</c>; <c>-1</c> means "leave it to the
    /// model" and <c>0</c> turns thinking off. An out-of-range value causes the
    /// request to be rejected — measured against the live API.
    /// </remarks>
    public const string ThinkingBudgetTokensSetting = "google.thinking.budgetTokens";

    /// <summary>
    /// Whether the thinking summary is returned in the response:
    /// <c>google.thinking.includeThoughts</c> (boolean).
    /// </summary>
    public const string ThinkingIncludeThoughtsSetting = "google.thinking.includeThoughts";

    /// <summary>All setting keys this provider supports.</summary>
    /// <remarks>
    /// A key absent from this list is not silently ignored; it raises a compile
    /// error, and the error message prints this list.
    /// </remarks>
    public static IReadOnlyList<string> SupportedSettings { get; } =
    [
        SafetyHarassmentSetting,
        SafetyHateSpeechSetting,
        SafetySexuallyExplicitSetting,
        SafetyDangerousContentSetting,
        SafetyCivicIntegritySetting,
        ThinkingBudgetTokensSetting,
        ThinkingIncludeThoughtsSetting,
    ];
}
