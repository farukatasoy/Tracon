using System.Text.Json.Serialization;

namespace AgentPrism;

/// <summary>Bir isin hangi hedefi calistirdigi.</summary>
/// <remarks>
/// JSON'da ad olarak yazilir, veritabaninda <c>smallint</c> olarak saklanir.
/// Deger sirasi <strong>degistirilemez</strong> — yalnizca sona eklenir; mevcut
/// satirlar sayisal degeri referans alir.
/// </remarks>
[JsonConverter(typeof(JsonStringEnumConverter<JobKind>))]
public enum JobKind
{
    /// <summary>Bir agent'i bir girdi kumesi uzerinde toplu calistirir.</summary>
    AgentBatch = 0,

    /// <summary>Bir workflow'u calistirir veya bekleyen bir workflow'u yanitlar.</summary>
    Workflow = 1,

    /// <summary>
    /// Bir degerlendirme (eval) kosusu. Faz 18 kendi <c>IJobHandler</c>
    /// uygulamasini bu deger icin ekler.
    /// </summary>
    Eval = 2,

    /// <summary>
    /// Tek bir webhook teslim denemesi (Faz 21). Yuk, teslim kaydinin
    /// kimligini tasir; govde <c>webhook_deliveries</c> tablosundan okunur.
    /// </summary>
    WebhookDelivery = 3,
}
