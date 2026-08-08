using System.Text.Json.Serialization;

namespace AgentPrism;

/// <summary>Bir API anahtarinin acabilecegi yetki kapsami.</summary>
/// <remarks>
/// <para>
/// Kapsam <strong>rol politikalarinin yerine gecmez</strong>, onlari daraltir.
/// Bir anahtarin etkili yetkisi <c>rol ∩ kapsam</c> kumesidir
/// (docs/53-KIRACI-API-ANAHTARLARI.md, bolum 53.3).
/// </para>
/// <para>
/// Kapsam listesi <strong>kapalidir</strong>: serbest metin kapsam kabul
/// edilmez, bilinmeyen bir deger olusturma aninda reddedilir. Bu tipe yeni
/// bir uye eklemek kirici DEGILDIR; listeyi arayuzden genisletilebilir hale
/// getirmek ayri, bilincli bir karar gerektirir (bir yetki dili bir guvenlik
/// yuzeyidir).
/// </para>
/// </remarks>
[JsonConverter(typeof(JsonStringEnumConverter<ApiKeyScope>))]
public enum ApiKeyScope
{
    /// <summary>Calistirma okuma, olay akisi, istatistik.</summary>
    RunsRead = 0,

    /// <summary>Calistirma baslatma, iptal, onay verme.</summary>
    RunsWrite = 1,

    /// <summary>Katalog ve tanim okuma.</summary>
    AgentsRead = 2,

    /// <summary>Tanim yazma, surum geri alma.</summary>
    AgentsAdmin = 3,

    /// <summary>
    /// Dis yuzey (MCP sunucusu, A2A). Ayri tutulur: bir ic otomasyon anahtari
    /// disa acik yuzeyi kendiliginden acmamalidir.
    /// </summary>
    ExternalInvoke = 4,
}
