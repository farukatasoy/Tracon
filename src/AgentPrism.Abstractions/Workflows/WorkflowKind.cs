using System.Text.Json.Serialization;

namespace AgentPrism;

/// <summary>
/// Arayuzden tanimlanabilen hazir workflow desenleri.
/// </summary>
/// <remarks>
/// <para>
/// Bu liste bilerek <strong>kapalidir</strong>. Arayuzden tanimlanan bir workflow
/// yalnizca katalogdaki agent'lari birbirine baglar; yeni davranis uretmez.
/// Serbest graf (ozel <c>Executor</c> tipleri) yalnizca kodda tanimlanir.
/// Gerekce: tasarim kurali K2 - "tool'lar yalnizca kodda tanimlanir".
/// </para>
/// <para>
/// JSON'da <strong>ad olarak</strong> yazilir (<c>"Sequential"</c>), sayi olarak
/// degil. Veritabaninda da ad olarak saklanir: <c>workflows.definition</c> bir
/// JSON belgesidir ve deger sirasi degistiginde eski satirlarin okunamaz hale
/// gelmesi kabul edilemez.
/// </para>
/// </remarks>
[JsonConverter(typeof(JsonStringEnumConverter<WorkflowKind>))]
public enum WorkflowKind
{
    /// <summary>Agent'lar sirayla calisir; her birinin ciktisi sonrakinin girdisidir.</summary>
    Sequential = 0,

    /// <summary>Agent'lar ayni anda calisir; sonuclar birlestirilir.</summary>
    Concurrent = 1,

    /// <summary>
    /// Ilk agent isi baslatir ve gerektiginde baska bir agent'a devreder.
    /// Devretme kararini modelin kendisi verir.
    /// </summary>
    Handoff = 2,

    /// <summary>
    /// Bir yonetici, katilimci agent'lar arasinda sirayi dagitir.
    /// <see cref="WorkflowDefinition.MaxIterations"/> tur sayisini sinirlar.
    /// </summary>
    GroupChat = 3,

    /// <summary>
    /// Yonetici agent bir plan kurar, ilerlemeyi izler ve gerektiginde yeniden
    /// planlar. <see cref="WorkflowDefinition.ManagerAgentName"/> zorunludur.
    /// </summary>
    Magentic = 4,
}
