using System.Text.Json.Serialization;

namespace AgentPrism;

/// <summary>Bir kotanin sayacinin hangi araliklarla sifirlandigi.</summary>
/// <remarks>
/// JSON'da ad olarak yazilir, veritabaninda <c>smallint</c> olarak saklanir.
/// Deger sirasi <strong>degistirilemez</strong> — yalnizca sona eklenir; mevcut
/// satirlar sayisal degeri referans alir.
/// </remarks>
[JsonConverter(typeof(JsonStringEnumConverter<QuotaPeriod>))]
public enum QuotaPeriod
{
    /// <summary>Sayac her gun yerel gece yarisinda sifirlanir.</summary>
    Daily = 0,

    /// <summary>Sayac her ayin ilk gunu yerel gece yarisinda sifirlanir.</summary>
    Monthly = 1,
}
