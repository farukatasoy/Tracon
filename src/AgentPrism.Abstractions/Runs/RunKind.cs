using System.Text.Json.Serialization;

namespace AgentPrism;

/// <summary>Bir <c>runs</c> satirinin neyi kaydettigini bildirir.</summary>
/// <remarks>
/// <para>
/// Workflow calistirmalari icin ayri bir tablo <strong>acilmaz</strong>. Runs
/// ekrani, SSE akisi, kiraci filtreleri, istatistikler ve waterfall zaten
/// <c>runs</c> uzerine kuruludur; ikinci bir kayit hatti hepsini ikiye
/// katlardi. Ayrim bu sutunla yapilir.
/// </para>
/// <para>
/// JSON'da <strong>ad olarak</strong> yazilir; veritabaninda <c>smallint</c>
/// olarak saklanir. Deger sirasi degistirilemez.
/// </para>
/// </remarks>
[JsonConverter(typeof(JsonStringEnumConverter<RunKind>))]
public enum RunKind
{
    /// <summary>Tek bir agent'in calistirmasi.</summary>
    Agent = 0,

    /// <summary>
    /// Bir workflow'un calistirmasi. Icinde cagrilan her agent, Faz 12'nin
    /// <c>parent_run_id</c> mekanizmasiyla bu satirin altina baglanir.
    /// </summary>
    Workflow = 1,
}
