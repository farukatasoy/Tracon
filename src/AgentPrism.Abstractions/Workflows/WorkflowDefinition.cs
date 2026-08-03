namespace AgentPrism;

/// <summary>
/// Arayuzden veya kodda tanimlanmis bir workflow'un tam tanimi.
/// </summary>
/// <remarks>
/// <para>
/// Tanim bir <strong>graftir</strong>, kod degildir: yalnizca katalogdaki
/// agent'lari birbirine baglar. Kullanici yeni davranis yazmaz, var olani
/// diziler. Serbest graf (ozel <c>Executor</c> tipleri) yalnizca kodda,
/// <c>AddWorkflow(name, factory)</c> ile tanimlanir.
/// </para>
/// <para>
/// <see cref="AgentNames"/> yalnizca <em>ad</em> listesidir. Her ad katalogda
/// cozulebilen bir agent'a karsilik gelmelidir; gelmezse derleme hata verir.
/// Ayni kural <see cref="AgentDefinition.ToolNames"/> icin gecerlidir ve ayni
/// guvenlik sinirini cizer.
/// </para>
/// </remarks>
public sealed record WorkflowDefinition
{
    /// <summary>Workflow'un benzersiz adi. Katalogda ve API yollarinda anahtardir.</summary>
    public required string Name { get; init; }

    /// <summary>Arayuzde gosterilecek ad. Bos birakilirsa <see cref="Name"/> kullanilir.</summary>
    public string? DisplayName { get; init; }

    /// <summary>Workflow'un ne yaptigini anlatan kisa aciklama.</summary>
    public string? Description { get; init; }

    /// <summary>Kullanilacak hazir desen.</summary>
    public required WorkflowKind Kind { get; init; }

    /// <summary>
    /// Grafa girecek agent adlari. Sira <see cref="WorkflowKind.Sequential"/>
    /// icin anlamlidir; digerlerinde katilimci kumesini belirler.
    /// </summary>
    public IReadOnlyList<string> AgentNames { get; init; } = [];

    /// <summary>
    /// Yonetici agent'in adi. <see cref="WorkflowKind.Magentic"/> icin zorunlu,
    /// diger desenlerde kullanilmaz.
    /// </summary>
    public string? ManagerAgentName { get; init; }

    /// <summary>
    /// En fazla tur sayisi. <see cref="WorkflowKind.GroupChat"/>,
    /// <see cref="WorkflowKind.Handoff"/> ve <see cref="WorkflowKind.Magentic"/>
    /// desenlerinde sonsuz donguye karsi tek korumadir.
    /// </summary>
    public int? MaxIterations { get; init; }

    /// <summary>
    /// Devretme kararini modele anlatan ek talimat.
    /// Yalnizca <see cref="WorkflowKind.Handoff"/> icin kullanilir.
    /// </summary>
    public string? HandoffInstructions { get; init; }

    /// <summary>
    /// Yonetici agent'in kurdugu plan, yurutmeye baslamadan once bir insana
    /// onaylatilsin mi. Yalnizca <see cref="WorkflowKind.Magentic"/> icindir.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Acikken Microsoft Agent Framework ilk super-step sonunda bir dis istek
    /// yayinlar; calistirma <see cref="RunStatus.AwaitingInput"/> olur ve durumu
    /// bir kontrol noktasina yazilir. Yanit
    /// <c>POST /api/workflows/runs/{runId}/respond</c> ile verilir: plan
    /// onaylanir ya da bir duzeltme metniyle geri gonderilir.
    /// </para>
    /// <para>
    /// 🚨 <strong>Maliyet.</strong> Yonetici agent her turda yeniden calisir;
    /// duzeltme istegi plani bastan kurdurur. Varsayilan <see langword="false"/>
    /// olmasi bilincli: bir tanim acikca istemeden calistirma yarim kalmaz
    /// (K1 - sifir surpriz).
    /// </para>
    /// </remarks>
    public bool RequirePlanApproval { get; init; }

    /// <summary>Tanimin ait oldugu kiraci. Kodda tanimli workflow'larda <see langword="null"/>.</summary>
    public string? TenantId { get; init; }

    /// <summary>Tanim surumu. Her kayitta bir artar.</summary>
    public int Version { get; init; } = 1;

    /// <summary>Son degistirilme zamani (UTC).</summary>
    public DateTimeOffset? UpdatedAt { get; init; }
}
