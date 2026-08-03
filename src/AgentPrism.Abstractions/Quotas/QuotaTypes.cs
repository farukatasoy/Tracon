using System.Text.Json.Serialization;

namespace AgentPrism;

/// <summary>Bir kotanin hangi olcute gore asildigi.</summary>
/// <remarks>
/// JSON'da ad olarak yazilir. Deger sirasi <strong>degistirilemez</strong> —
/// yalnizca sona eklenir.
/// </remarks>
[JsonConverter(typeof(JsonStringEnumConverter<QuotaMetric>))]
public enum QuotaMetric
{
    /// <summary>Calistirma sayisi.</summary>
    Runs = 0,

    /// <summary>Toplam token (girdi + cikti).</summary>
    Tokens = 1,

    /// <summary>Toplam para tutari.</summary>
    Cost = 2,
}

/// <summary>Bir kiraci veya agent icin tanimlanmis kota kurali.</summary>
/// <remarks>
/// <para>
/// Kota <strong>gun/ay</strong> olcegindedir ve veritabaninda sayilir. Ani yuku
/// duzleştiren <strong>hiz siniri</strong> ayri bir mekanizmadir ve bellekte
/// yasar; ikisi karistirilmamalidir (K-158).
/// </para>
/// <para>
/// Uc sinirin ucu de <see langword="null"/> olabilir: yalnizca dolu olanlar
/// uygulanir. Ucu de bossa kural hicbir sey yapmaz.
/// </para>
/// </remarks>
public sealed record QuotaDefinition
{
    /// <summary>Kural kimligi.</summary>
    public required Guid Id { get; init; }

    /// <summary>Kuralin ait oldugu kiraci.</summary>
    public required string TenantId { get; init; }

    /// <summary>
    /// Kuralin baglandigi agent. <see langword="null"/> ise kural kiracinin
    /// tum calistirmalarina uygulanir.
    /// </summary>
    public string? AgentName { get; init; }

    /// <summary>Sayacin sifirlanma araligi.</summary>
    public required QuotaPeriod Period { get; init; }

    /// <summary>Donem basina en fazla calistirma sayisi. <see langword="null"/> ise sinirsiz.</summary>
    public long? MaxRuns { get; init; }

    /// <summary>Donem basina en fazla token. <see langword="null"/> ise sinirsiz.</summary>
    public long? MaxTokens { get; init; }

    /// <summary>
    /// Donem basina en fazla para tutari. <see langword="null"/> ise sinirsiz.
    /// </summary>
    /// <remarks>
    /// Fiyati tanimsiz bir modelde bu sinir <strong>uygulanamaz</strong>: maliyet
    /// bilinmedigi icin kota token'a duser. Bkz. Faz 20,
    /// <see cref="PricingSource.Unknown"/>.
    /// </remarks>
    public decimal? MaxCost { get; init; }

    /// <summary>Kural etkin mi.</summary>
    public bool Enabled { get; init; } = true;

    /// <summary>Olusturulma zamani (UTC).</summary>
    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>Son guncelleme zamani (UTC).</summary>
    public required DateTimeOffset UpdatedAt { get; init; }
}

/// <summary>Bir kapsamin gecerli donemdeki tuketimi.</summary>
public sealed record QuotaUsageRecord
{
    /// <summary>Kiraci kimligi.</summary>
    public required string TenantId { get; init; }

    /// <summary>
    /// Agent adi. Bos dize (<c>""</c>) kiraci geneli sayaci anlamina gelir.
    /// </summary>
    public required string AgentName { get; init; }

    /// <summary>Sayacin araligi.</summary>
    public required QuotaPeriod Period { get; init; }

    /// <summary>Donemin ilk gunu (yerel saat diliminde).</summary>
    public required DateOnly PeriodStart { get; init; }

    /// <summary>Donemde tamamlanan calistirma sayisi.</summary>
    public long Runs { get; init; }

    /// <summary>Donemde harcanan toplam token.</summary>
    public long Tokens { get; init; }

    /// <summary>
    /// Donemde harcanan toplam tutar. Fiyati tanimsiz calistirmalar bu toplama
    /// <strong>katilmaz</strong> (sifir olarak da eklenmez).
    /// </summary>
    public decimal Cost { get; init; }

    /// <summary>Son guncelleme zamani (UTC).</summary>
    public required DateTimeOffset UpdatedAt { get; init; }
}

/// <summary>Bir kota denetiminin sonucu.</summary>
/// <remarks>
/// Denetim calistirma <strong>baslamadan once</strong> yapilir; tuketim
/// calistirma bittikten sonra yazilir. Bu yuzden eszamanli calistirmalar kotayi
/// bir miktar asabilir — kota <strong>yaklasiktir</strong> (K-159).
/// </remarks>
public sealed record QuotaDecision
{
    /// <summary>Hicbir kuralin asilmadigini bildiren sonuc.</summary>
    public static QuotaDecision Allowed { get; } = new() { IsAllowed = true };

    /// <summary>Calistirmaya izin verildi mi.</summary>
    public required bool IsAllowed { get; init; }

    /// <summary>Asilan olcut. Izin verildiyse <see langword="null"/>.</summary>
    public QuotaMetric? Metric { get; init; }

    /// <summary>Asilan kuralin kapsami: agent adi veya kiraci geneli icin <see langword="null"/>.</summary>
    public string? AgentName { get; init; }

    /// <summary>Asilan kuralin donemi. Izin verildiyse <see langword="null"/>.</summary>
    public QuotaPeriod? Period { get; init; }

    /// <summary>Asilan sinir degeri.</summary>
    public decimal? Limit { get; init; }

    /// <summary>Denetim anindaki tuketim.</summary>
    public decimal? Used { get; init; }

    /// <summary>Sayacin sifirlanacagi zaman (UTC). Istemciye <c>Retry-After</c> olarak yansir.</summary>
    public DateTimeOffset? ResetsAt { get; init; }

    /// <summary>
    /// Para cinsi bir kural, fiyati tanimsiz oldugu icin token'a dustuyse
    /// <see langword="true"/>.
    /// </summary>
    public bool CostFellBackToTokens { get; init; }

    /// <summary>Kullaniciya gosterilecek aciklama. Izin verildiyse <see langword="null"/>.</summary>
    public string? Reason { get; init; }
}

/// <summary>Tamamlanmis bir calistirmanin kota sayaclarina eklenecek tuketimi.</summary>
public sealed record QuotaConsumption
{
    /// <summary>Kiraci kimligi.</summary>
    public required string TenantId { get; init; }

    /// <summary>Calistirmayi yapan agent'in adi.</summary>
    public required string AgentName { get; init; }

    /// <summary>Eklenecek calistirma sayisi. Normalde 1.</summary>
    public long Runs { get; init; } = 1;

    /// <summary>Eklenecek toplam token.</summary>
    public long Tokens { get; init; }

    /// <summary>
    /// Eklenecek tutar. Fiyat tanimsizsa <see langword="null"/> — sifir
    /// <strong>degil</strong> (Faz 20 kurali).
    /// </summary>
    public decimal? Cost { get; init; }

    /// <summary>Tuketimin gerceklestigi an (UTC). Donem hesabi bundan yapilir.</summary>
    public required DateTimeOffset OccurredAt { get; init; }
}

/// <summary>Kota kullanimini sorgulamak icin filtre.</summary>
public sealed record QuotaUsageQuery
{
    /// <summary>Kiraci kimligi.</summary>
    public required string TenantId { get; init; }

    /// <summary>Yalnizca bu agent'in sayaclarini getirir. Bos dize kiraci genelidir.</summary>
    public string? AgentName { get; init; }

    /// <summary>Yalnizca bu araligin sayaclarini getirir.</summary>
    public QuotaPeriod? Period { get; init; }

    /// <summary>Sayaclarin ait oldugu an (UTC). Verilmezse simdiki zaman kullanilir.</summary>
    public DateTimeOffset? AsOf { get; init; }
}
