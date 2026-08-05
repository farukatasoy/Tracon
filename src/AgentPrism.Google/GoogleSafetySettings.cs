using Google.GenAI.Types;

namespace AgentPrism;

/// <summary>
/// <c>google.safety.*</c> ayarlarini Gemini'nin <see cref="SafetySetting"/>
/// listesine cevirir.
/// </summary>
/// <remarks>
/// Esik degeri taninmazsa <strong>sessizce yok sayilmaz</strong>: gecerli degerleri
/// listeleyen bir hata atilir. Yanlis yazilmis bir esik, guvenlik davranisini
/// kullanicinin bilmedigi bicimde degistirirdi.
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

    /// <summary>Baglantidaki guvenlik esiklerini okur.</summary>
    /// <param name="binding">Model baglantisi.</param>
    /// <returns>Tanimlanmis esikler. Hicbiri verilmemisse bos liste.</returns>
    /// <exception cref="AgentPrismException">Bir esik degeri taninmiyorsa.</exception>
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

        throw new AgentPrismException(
            $"'{setting}' ayarinin degeri taninmiyor: '{value}'. Gecerli degerler: " +
            $"{string.Join(", ", HarmBlockThreshold.AllValues.Select(static candidate => candidate.Value))}.");
    }
}
