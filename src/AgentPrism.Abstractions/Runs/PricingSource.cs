using System.Text.Json.Serialization;

namespace AgentPrism;

/// <summary>Bir calistirma maliyetinin fiyati nereden aldigini bildirir.</summary>
/// <remarks>
/// JSON'da <strong>ad olarak</strong> yazilir; veritabaninda <c>smallint</c>
/// olarak saklanir. Deger sirasi degistirilemez (bkz. <c>runs.pricing_source</c>).
/// </remarks>
[JsonConverter(typeof(JsonStringEnumConverter<PricingSource>))]
public enum PricingSource
{
    /// <summary>Fiyat model kataloğundan (<see cref="ModelDescriptor"/>) geldi.</summary>
    Catalog = 0,

    /// <summary>Fiyat <c>AgentPrism:Pricing</c> yapilandirmasindan geldi.</summary>
    Configuration = 1,

    /// <summary>
    /// Fiyat hicbir kaynakta tanimli degil. Maliyet alanlari bu durumda
    /// <see langword="null"/>'dur — <strong>sifir degil</strong>: sifir, modelin
    /// bedava oldugu anlamina gelirdi.
    /// </summary>
    Unknown = 2,
}
