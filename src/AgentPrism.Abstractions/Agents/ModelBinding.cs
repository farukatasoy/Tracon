using System.Text.Json;

namespace AgentPrism;

/// <summary>
/// Bir agent'in hangi saglayici ve model ile calisacagini belirler.
/// Kimlik bilgisi <em>icermez</em>; API anahtari yapilandirmadan cozulur.
/// </summary>
public sealed record ModelBinding
{
    /// <summary>Saglayici adi. Ornek: <c>openai</c>.</summary>
    public required string Provider { get; init; }

    /// <summary>Model adi. Ornek: <c>gpt-5.4-mini</c>.</summary>
    public required string Model { get; init; }

    /// <summary>Ornekleme sicakligi. <see langword="null"/> ise saglayici varsayilani kullanilir.</summary>
    public float? Temperature { get; init; }

    /// <summary>Ureteceklerin ust token siniri. <see langword="null"/> ise saglayici varsayilani kullanilir.</summary>
    public int? MaxOutputTokens { get; init; }

    /// <summary>Nucleus ornekleme esigi. <see langword="null"/> ise saglayici varsayilani kullanilir.</summary>
    public float? TopP { get; init; }

    /// <summary>
    /// Akil yurutme cabasi seviyesi. Destekleyen modellerde kullanilir; digerlerinde
    /// saglayici tarafindan yok sayilir.
    /// </summary>
    /// <remarks>
    /// Gecerli degerler <c>Microsoft.Extensions.AI.ReasoningEffort</c> adlaridir:
    /// <c>None</c>, <c>Low</c>, <c>Medium</c>, <c>High</c>, <c>ExtraHigh</c>.
    /// Karsilastirma buyuk/kucuk harfe duyarli degildir. Taninmayan bir deger
    /// derleme sirasinda <c>AgentPrismCompilationException</c> ile reddedilir;
    /// sessizce yok sayilmaz.
    /// </remarks>
    public string? ReasoningEffort { get; init; }

    /// <summary>
    /// Saglayiciya ozgu ek ayarlar. Anahtar <c>{saglayici}.{ayar}</c> bicimindedir.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Anthropic'in prompt caching'i veya Gemini'nin guvenlik esikleri bu sozlesmenin
    /// sabit alanlarina sigmaz. Onlari <see cref="ModelBinding"/> govdesine eklemek
    /// <c>AgentPrism.Abstractions</c>'a bir saticinin kavramini sizdirirdi. Bu sozluk
    /// sozlesmeyi temiz tutar: her saglayici yalnizca kendi onekini okur.
    /// </para>
    /// <para>
    /// <strong>Bilinmeyen bir anahtar sessizce yok sayilmaz.</strong> Saglayici
    /// tanimadigi bir anahtar gorurse derleme hatasi verir ve destekledigi anahtarlari
    /// listeler. Gerekce <see cref="ReasoningEffort"/> ile aynidir (karar K-034):
    /// sessizce yok sayilan bir ayar, kullanicinin bekledigi davranisi almamasina ve
    /// sebebini gorememesine yol acar.
    /// </para>
    /// <para>
    /// Anahtar karsilastirmasi <see cref="StringComparer.OrdinalIgnoreCase"/> ile
    /// yapilir. Okuma icin <see cref="ModelProviderSettings"/> yardimcilarini kullanin.
    /// </para>
    /// <example>
    /// <code language="json">
    /// "ProviderSettings": {
    ///   "anthropic.promptCaching": true,
    ///   "anthropic.thinking.budgetTokens": 8000,
    ///   "google.safety.harassment": "BLOCK_ONLY_HIGH"
    /// }
    /// </code>
    /// </example>
    /// </remarks>
    public IReadOnlyDictionary<string, JsonElement> ProviderSettings { get; init; }
        = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Istenen cikti bicimi. <see langword="null"/> ise bugunku davranis degismez:
    /// saglayiciya hicbir bicim kisiti gonderilmez.
    /// </summary>
    /// <remarks>
    /// <see cref="AgentResponseFormatKind.Text"/>, <see langword="null"/>'dan
    /// farklidir: <see langword="null"/> "hicbir sey soyleme", <c>Text</c> "acikca
    /// duz metin iste" demektir. Gecersiz bir kombinasyon (ornegin sema olmadan
    /// <see cref="AgentResponseFormatKind.JsonSchema"/>) derleme aninda
    /// <c>AgentPrismCompilationException</c> ile reddedilir.
    /// </remarks>
    public AgentResponseFormat? ResponseFormat { get; init; }
}
