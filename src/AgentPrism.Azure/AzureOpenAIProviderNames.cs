namespace AgentPrism;

/// <summary>
/// <c>UseAzureOpenAI()</c> cagrisinin kaydettigi saglayici adi ve
/// <see cref="ModelBinding.ProviderSettings"/> anahtarlari.
/// </summary>
/// <remarks>
/// <para>
/// Bu adlar <strong>kararlidir</strong>. Agent tanimlari veritabaninda bu adlarla
/// saklanir; degistirmek kayitli tanimlari bozar.
/// </para>
/// <para>
/// Ad <c>azure</c> degil <c>azure-openai</c>'dir: paket ileride baska bir Azure
/// model servisini de kaydedebilir ve o zaman <c>azure</c> adi neyi gosterdigini
/// soylemez olurdu. Gerekce: <c>docs/KARARLAR.md</c>, karar K-210.
/// </para>
/// </remarks>
public static class AzureOpenAIProviderNames
{
    /// <summary>Azure OpenAI Chat Completions ucunu kullanan saglayici: <c>azure-openai</c>.</summary>
    public const string AzureOpenAI = "azure-openai";

    /// <summary>Saglayiciya ozgu ayarlarin oneki: <c>azure-openai</c>.</summary>
    public const string SettingsPrefix = "azure-openai";

    /// <summary>
    /// Bu saglayicinin destekledigi ayar anahtarlari. <strong>Bugun bostur.</strong>
    /// </summary>
    /// <remarks>
    /// <para>
    /// Liste bilincli olarak bostur. Azure'un sohbet istegine ekleyebilecegi
    /// ozel alanlar (veri kaynaklari, <c>max_completion_tokens</c> anahtari)
    /// <c>Azure.AI.OpenAI.Chat.AzureChatExtensions</c> uzerinden yazilir; olculdu
    /// (2026-08-05) ki bu uzantilarin <strong>tamami</strong> kullandigimiz OpenAI
    /// SDK surumuyle calisma aninda <c>MissingMethodException</c> verir. Calismayan
    /// bir ayari sunmak, hic sunmamaktan kotudur. Ayrinti: karar K-211.
    /// </para>
    /// <para>
    /// Liste bos oldugu icin <see cref="ModelProviderSettings.Validate"/> herhangi
    /// bir anahtari reddeder ve mesajinda "hicbir ek ayar desteklemiyor" der.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<string> SupportedSettings { get; } = [];
}
