using System.Text.Json;

namespace AgentPrism;

/// <summary>Bir workflow tanimini kaydetme istegi.</summary>
/// <remarks>
/// Ad <em>yoldan</em> gelir, govdeden degil. Iki kaynak olmasi, ikisinin
/// celismesi durumunda hangisinin kazandigini sormaya yol acardi.
/// </remarks>
public sealed record WorkflowSaveRequest
{
    /// <summary>Arayuzde gosterilecek ad.</summary>
    public string? DisplayName { get; init; }

    /// <summary>Workflow'un ne yaptigini anlatan kisa aciklama.</summary>
    public string? Description { get; init; }

    /// <summary>Kullanilacak hazir desen.</summary>
    public WorkflowKind Kind { get; init; }

    /// <summary>Grafa girecek agent adlari.</summary>
    public IReadOnlyList<string>? AgentNames { get; init; }

    /// <summary>Yonetici agent'in adi. Yalnizca <see cref="WorkflowKind.Magentic"/> icin.</summary>
    public string? ManagerAgentName { get; init; }

    /// <summary>En fazla tur sayisi.</summary>
    public int? MaxIterations { get; init; }

    /// <summary>Devretme talimati. Yalnizca <see cref="WorkflowKind.Handoff"/> icin.</summary>
    public string? HandoffInstructions { get; init; }

    /// <summary>
    /// Yonetici agent'in plani insana onaylatilsin mi.
    /// Yalnizca <see cref="WorkflowKind.Magentic"/> icin.
    /// </summary>
    public bool RequirePlanApproval { get; init; }
}

/// <summary>Bir workflow'u calistirma istegi.</summary>
public sealed record WorkflowRunHttpRequest
{
    /// <summary>Grafa girecek kullanici mesaji.</summary>
    public string? Message { get; init; }

    /// <summary>
    /// Yurutme oturumunun kimligi. Bos birakilirsa uretilir. Kontrol noktalari
    /// bu deger altinda gruplanir.
    /// </summary>
    public string? SessionId { get; init; }
}

/// <summary>Bir workflow'u kontrol noktasindan sürdürme istegi.</summary>
public sealed record WorkflowResumeHttpRequest
{
    /// <summary>
    /// Devam edilecek kontrol noktasinin kimligi. Bos birakilirsa calistirmanin
    /// en son kontrol noktasi kullanilir.
    /// </summary>
    public string? CheckpointId { get; init; }
}

/// <summary>Bekleyen bir insan girdisi istegine verilen yanit.</summary>
/// <remarks>
/// Hangi alanin okunacagini portun yanit tipi belirler; sunucu bunu
/// <see cref="WorkflowPendingRequest.Form"/> alaninda bildirir. Cevrilemeyen bir
/// yanit <c>400</c> ile reddedilir - yanlis tipte bir yaniti sessizce kabul
/// etmek, yurutmeyi anlasilmaz bir noktada bozardi.
/// </remarks>
public sealed record WorkflowRespondHttpRequest
{
    /// <summary>Yanitlanan istegin kimligi.</summary>
    public required string RequestId { get; init; }

    /// <summary>
    /// Evet/hayir yaniti. Plan onayinda <see langword="true"/> plani onaylar,
    /// <see langword="false"/> ise <see cref="Text"/> alanindaki duzeltmeyle
    /// geri gonderir.
    /// </summary>
    public bool? Approved { get; init; }

    /// <summary>Metin yaniti; plan onayinda duzeltme talimatidir.</summary>
    public string? Text { get; init; }

    /// <summary>Serbest yanit govdesi. Portun yanit tipine cozulur.</summary>
    public JsonElement? Data { get; init; }

    /// <summary>
    /// Sürdürulecek kontrol noktasinin kimligi. Bos birakilirsa calistirmanin
    /// en son kontrol noktasi kullanilir.
    /// </summary>
    public string? CheckpointId { get; init; }
}
