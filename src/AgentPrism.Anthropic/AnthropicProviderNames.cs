namespace AgentPrism;

/// <summary>
/// <c>UseAnthropic()</c> cagrisinin kaydettigi saglayici adi ve
/// <see cref="ModelBinding.ProviderSettings"/> anahtarlari.
/// </summary>
/// <remarks>
/// Bu adlar <strong>kararlidir</strong>. Agent tanimlari veritabaninda bu adlarla
/// saklanir; degistirmek kayitli tanimlari bozar.
/// </remarks>
public static class AnthropicProviderNames
{
    /// <summary>Anthropic Messages API kullanan saglayici: <c>anthropic</c>.</summary>
    public const string Anthropic = "anthropic";

    /// <summary>
    /// Saglayiciya ozgu ayarlarin oneki: <c>anthropic</c>.
    /// </summary>
    public const string SettingsPrefix = "anthropic";

    /// <summary>
    /// Prompt caching'i acan ayar anahtari: <c>anthropic.promptCaching</c> (mantiksal).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Varsayilan <strong>kapali</strong>. Acmak maliyeti dusurur ama davranisi
    /// degistirir: istek gövdesine <c>cache_control</c> eklenir, kisa istemler icin
    /// onbellek olusturma ucreti normal girdi ucretinden yuksektir ve model
    /// yalnizca belirli bir token esiginin uzerindeki istemleri onbellege alir.
    /// Sessiz bir varsayilan olmamasi icin acikca istenir.
    /// </para>
    /// <para>
    /// Olculdu (2026-08-05, <c>claude-haiku-4-5-20251001</c>): acik oldugunda yanit
    /// <c>cache_creation_input_tokens=4209</c> bildirdi; kapaliyken bu sayac hic gelmedi.
    /// </para>
    /// </remarks>
    public const string PromptCachingSetting = "anthropic.promptCaching";

    /// <summary>
    /// Genisletilmis dusunme butcesini veren ayar anahtari:
    /// <c>anthropic.thinking.budgetTokens</c> (tam sayi).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Deger <see cref="ModelBinding.MaxOutputTokens"/> degerinden <strong>kucuk</strong>
    /// olmalidir; aksi halde Anthropic istegi reddeder.
    /// </para>
    /// <para>
    /// 🚨 Dusunme acikken Anthropic <see cref="ModelBinding.Temperature"/> degerinin
    /// yalnizca 1 olmasina izin verir. Baska bir sicaklik verilirse istek
    /// <c>invalid_request_error</c> ile reddedilir — olculdu (2026-08-05).
    /// </para>
    /// </remarks>
    public const string ThinkingBudgetTokensSetting = "anthropic.thinking.budgetTokens";

    /// <summary>Bu saglayicinin destekledigi tum ayar anahtarlari.</summary>
    /// <remarks>
    /// Bu listede olmayan bir anahtar sessizce yok sayilmaz; derleme hatasi verir
    /// ve hata mesaji bu listeyi yazar.
    /// </remarks>
    public static IReadOnlyList<string> SupportedSettings { get; } =
    [
        PromptCachingSetting,
        ThinkingBudgetTokensSetting,
    ];
}
