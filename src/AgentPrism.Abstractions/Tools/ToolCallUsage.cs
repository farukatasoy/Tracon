namespace AgentPrism;

/// <summary>
/// Tek bir tool cagrisinin token DISI olcumu ve maliyeti.
/// </summary>
/// <remarks>
/// <para>
/// Faz 20'nin maliyet modeli token varsayar ve <c>runs</c> tablosuna yazar. Bir
/// tool token harcamayabilir ama yine de ucret uretebilir: metinden ses uretimi
/// <em>karakter</em>, sesten metin cevrimi <em>saniye</em> ile faturalanir.
/// Bu olcumler token maliyetiyle <strong>toplanmaz</strong> — iki farkli birim
/// toplanamaz. Raporlarda ayri kalem olarak gosterilir.
/// </para>
/// <para>
/// Olcumu tool'un kendisi bildirir: <c>AgentPrismToolUsage.Report(...)</c>.
/// Gerekce: <c>docs/28-SES-TOOLLARI.md</c>, bolum 28.5.
/// </para>
/// </remarks>
public sealed record ToolCallUsage
{
    /// <summary>Olcum birimi. Bilinen degerler icin <see cref="ToolUsageUnits"/>.</summary>
    public required string Unit { get; init; }

    /// <summary>Faturalanan miktar.</summary>
    public required decimal Quantity { get; init; }

    /// <summary>
    /// Hesaplanan tutar. Yapilandirmada bu tool icin fiyat yoksa
    /// <see langword="null"/> kalir — sifir <strong>degil</strong> (K-032).
    /// </summary>
    public decimal? Cost { get; init; }

    /// <summary>Para birimi. <c>AgentPrism:Pricing:Currency</c>'den gelir.</summary>
    public string? Currency { get; init; }

    /// <summary>
    /// Miktarin olculmus mu yoksa tahmin mi oldugu.
    /// </summary>
    /// <remarks>
    /// Saglayici faturalanan miktari bildirmezse tool bir tahmin uretir
    /// (ornegin metnin karakter sayisi). Tahmini olcum gibi gostermek fiyat
    /// uydurmaktir; arayuz iki durumu ayirt ederek gosterir.
    /// </remarks>
    public bool IsEstimated { get; init; }
}

/// <summary>
/// <see cref="ToolCallUsage.Unit"/> icin bilinen birim adlari.
/// </summary>
/// <remarks>
/// Liste kapali <strong>degildir</strong>: alan serbest metindir ve bir tool
/// kendi birimini bildirebilir. Bu sabitler yalnizca AgentPrism'in kendi
/// tool'larinin kullandigi adlari tek yerde tutar.
/// </remarks>
public static class ToolUsageUnits
{
    /// <summary>Karakter. Metinden ses uretiminde kullanilir.</summary>
    public const string Characters = "characters";

    /// <summary>Saniye. Sesten metin cevriminde kullanilir.</summary>
    public const string Seconds = "seconds";
}
