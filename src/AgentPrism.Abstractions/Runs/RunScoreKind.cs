using System.Text.Json.Serialization;

namespace AgentPrism;

/// <summary>Bir <see cref="RunScore"/>'un tasidigi degerin bicimi.</summary>
/// <remarks>
/// JSON'da <strong>ad olarak</strong> yazilir; veritabaninda <c>smallint</c>
/// olarak saklanir. Sayisal degerler <strong>kararlidir</strong> ve
/// degistirilemez.
/// </remarks>
[JsonConverter(typeof(JsonStringEnumConverter<RunScoreKind>))]
public enum RunScoreKind
{
    /// <summary>Ikili puan: <see cref="RunScore.Value"/> 0 (olumsuz) veya 1 (olumlu).</summary>
    Binary = 1,

    /// <summary>Yildiz puani: <see cref="RunScore.Value"/> 1 ile 5 arasi.</summary>
    Stars = 2,

    /// <summary>
    /// 0-100 arasi tamsayi yuzde puan. Model tabanli yargic (Faz 49) bunu uretir.
    /// </summary>
    Numeric = 3,
}
