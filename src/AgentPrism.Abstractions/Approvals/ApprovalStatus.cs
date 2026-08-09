using System.Text.Json.Serialization;

namespace AgentPrism;

/// <summary>Bir bekleyen onay isteginin durumu.</summary>
/// <remarks>
/// JSON'da <strong>ad olarak</strong> yazilir, sayi olarak degil — gerekce
/// <see cref="RunStatus"/> ile aynidir. Veritabaninda <c>smallint</c> olarak
/// saklanir; deger sirasi degistirilemez.
/// </remarks>
[JsonConverter(typeof(JsonStringEnumConverter<ApprovalStatus>))]
public enum ApprovalStatus
{
    /// <summary>Karar bekleniyor.</summary>
    Pending = 0,

    /// <summary>Operator onayladi.</summary>
    Approved = 1,

    /// <summary>Operator reddetti.</summary>
    Rejected = 2,

    /// <summary>Karar verilmeden suresi doldu.</summary>
    Expired = 3,
}
