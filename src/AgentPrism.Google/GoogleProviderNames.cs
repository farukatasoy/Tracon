namespace AgentPrism;

/// <summary>
/// <c>UseGoogle()</c> cagrisinin kaydettigi saglayici adi ve
/// <see cref="ModelBinding.ProviderSettings"/> anahtarlari.
/// </summary>
/// <remarks>
/// <para>
/// Bu adlar <strong>kararlidir</strong>. Agent tanimlari veritabaninda bu adlarla
/// saklanir; degistirmek kayitli tanimlari bozar.
/// </para>
/// <para>
/// Ad <c>gemini</c> degil <c>google</c>'dir: ayni paket ileride Vertex AI'yi de
/// kapsayabilir ve model ailesinin adina kilitlenmemelidir.
/// </para>
/// </remarks>
public static class GoogleProviderNames
{
    /// <summary>Gemini Developer API kullanan saglayici: <c>google</c>.</summary>
    public const string Google = "google";

    /// <summary>Saglayiciya ozgu ayarlarin oneki: <c>google</c>.</summary>
    public const string SettingsPrefix = "google";

    /// <summary>Taciz esigi: <c>google.safety.harassment</c> (metin).</summary>
    public const string SafetyHarassmentSetting = "google.safety.harassment";

    /// <summary>Nefret soylemi esigi: <c>google.safety.hateSpeech</c> (metin).</summary>
    public const string SafetyHateSpeechSetting = "google.safety.hateSpeech";

    /// <summary>Cinsel icerik esigi: <c>google.safety.sexuallyExplicit</c> (metin).</summary>
    public const string SafetySexuallyExplicitSetting = "google.safety.sexuallyExplicit";

    /// <summary>Tehlikeli icerik esigi: <c>google.safety.dangerousContent</c> (metin).</summary>
    public const string SafetyDangerousContentSetting = "google.safety.dangerousContent";

    /// <summary>Sivil butunluk esigi: <c>google.safety.civicIntegrity</c> (metin).</summary>
    public const string SafetyCivicIntegritySetting = "google.safety.civicIntegrity";

    /// <summary>
    /// Dusunme butcesi: <c>google.thinking.budgetTokens</c> (tam sayi).
    /// </summary>
    /// <remarks>
    /// Gecerli aralik <c>[-1, 65535]</c>'tir; <c>-1</c> "modele birak" anlamina gelir
    /// ve <c>0</c> dusunmeyi kapatir. Aralik disi bir deger istegi reddettirir —
    /// olculdu (2026-08-05).
    /// </remarks>
    public const string ThinkingBudgetTokensSetting = "google.thinking.budgetTokens";

    /// <summary>
    /// Dusunme ozetinin yanitta dondurulup dondurulmeyecegi:
    /// <c>google.thinking.includeThoughts</c> (mantiksal).
    /// </summary>
    public const string ThinkingIncludeThoughtsSetting = "google.thinking.includeThoughts";

    /// <summary>Bu saglayicinin destekledigi tum ayar anahtarlari.</summary>
    /// <remarks>
    /// Bu listede olmayan bir anahtar sessizce yok sayilmaz; derleme hatasi verir
    /// ve hata mesaji bu listeyi yazar.
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
