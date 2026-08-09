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

    /// <summary>
    /// Bir saklama suprusu (Faz 25). <c>TargetName</c> ya belirli bir
    /// <see cref="RetentionTargets"/> degeri ya da tum etkin politikalari
    /// isleyen <c>"*"</c>'tir.
    /// </summary>
    Retention = 4,

    /// <summary>
    /// Kuyruga alinmis (dayanikli) tek bir agent calistirmasi (Faz 46).
    /// </summary>
    /// <remarks>
    /// <c>Prefer: respond-async</c> ile baslatilan calistirmalar bu tur altinda
    /// kosar. <see cref="AgentBatch"/>'ten farkli olarak oge kumesi ISTEMEZ ve
    /// calistirma kimligi cagiran tarafindan (HTTP katmani) ONCEDEN uretilir —
    /// <c>JobRecord.Id</c> calistirma kimligiyle ayni deger tasir.
    /// </remarks>
    AgentRun = 5,

    /// <summary>
    /// Orneklenmis bir uretim calistirmasini puanlar (Faz 49).
    /// </summary>
    /// <remarks>
    /// Yuk bos veya tanilama amaclidir; puanlanacak calistirmanin kimligi is
    /// ogesinin (<see cref="JobItemRecord.Input"/>) kendisidir — <see cref="Eval"/>
    /// isinin vaka kimligini tasima deseniyle aynidir.
    /// </remarks>
    OnlineEval = 6,

    /// <summary>
    /// Bir onay kararindan sonra kuyruga alinmis calistirmayi surduren
    /// (Faz 55) YENI bir calistirma.
    /// </summary>
    /// <remarks>
    /// <see cref="AgentRun"/>'dan AYRIDIR: <c>AwaitingApproval</c> ile kapanmis
    /// eski calistirma satiri BIR DAHA DEGISMEZ (K-014, <see cref="RunStatus.AwaitingInput"/>
    /// ile ayni ilke); bu is YENI bir <c>RunId</c> ile YENI bir <c>runs</c>
    /// satiri acar. Yuk, karari verilmis bir <see cref="PendingApproval"/>
    /// kaydinin kimligini tasir.
    /// </remarks>
    ApprovalResume = 7,
}
