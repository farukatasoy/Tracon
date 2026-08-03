using Microsoft.Agents.AI;

namespace AgentPrism;

/// <summary>
/// Bir calistirmanin kimligini, agactaki yerini ve butcesini cagiranin
/// belirlemesini saglayan calistirma ayarlari.
/// </summary>
/// <remarks>
/// <para>
/// Calistirma kaydini yazan sarmalayici kimligi normalde kendisi uretir ve disari
/// bildirmez. Akisli bir uc ise kimligi <strong>ilk cerceveden once</strong> bilmek
/// zorundadir: istemci, akan yaniti calistirma kaydiyla ancak o zaman
/// iliskilendirebilir. Kimligi cagiranin uretmesi bu sorunu ek bir bildirim kanali
/// veya ortam durumu olmadan cozer ve <see cref="RunStartInfo.RunId"/> ile ayni
/// yaklasimi surdurur.
/// </para>
/// <para>
/// <see cref="ParentRunId"/>, <see cref="RootRunId"/>, <see cref="Depth"/> ve
/// <see cref="Budget"/> alanlarini bir agent baska bir agent'i cagirdiginda
/// <c>ChildAgentInvoker</c> doldurur. Elle doldurmak gerekmez; doldurulursa
/// calistirma agacin belirtilen yerine yerlesir.
/// </para>
/// <para>
/// Bu tip <c>ChatOptions</c> <em>tasimaz</em>. Ornekleme ayarlarini calistirma
/// basina degistirmek gerekiyorsa Microsoft Agent Framework'un
/// <c>ChatClientAgentRunOptions</c> tipi kullanilir; iki tip birlikte kullanilamaz.
/// AgentPrism kendi uclarinda ornekleme ayarlarini agent tanimindan cozer, bu
/// yuzden pratikte bir kisit olusturmaz.
/// </para>
/// </remarks>
public sealed class AgentPrismRunOptions : AgentRunOptions
{
    /// <summary>Yeni bir ayar nesnesi olusturur.</summary>
    public AgentPrismRunOptions()
    {
    }

    private AgentPrismRunOptions(AgentPrismRunOptions other)
        : base(other)
    {
        RunId = other.RunId;
        ParentRunId = other.ParentRunId;
        RootRunId = other.RootRunId;
        Depth = other.Depth;
        Budget = other.Budget;
        Kind = other.Kind;
    }

    /// <summary>
    /// Kullanilacak calistirma kimligi. <see langword="null"/> ise kimlik calistirma
    /// kaydini yazan sarmalayici tarafindan uretilir.
    /// </summary>
    /// <remarks>
    /// Deger zaman sirali olmalidir; <c>AgentPrismId.NewId()</c> bunu saglar.
    /// <c>Guid.NewGuid()</c> ile uretilen rastgele bir kimlik depolama index'ini
    /// parcalar.
    /// </remarks>
    public Guid? RunId { get; init; }

    /// <summary>
    /// Bu calistirmayi baslatan calistirmanin kimligi. Kok calistirmada
    /// <see langword="null"/>.
    /// </summary>
    public Guid? ParentRunId { get; init; }

    /// <summary>
    /// Agacin kokundeki calistirmanin kimligi. <see langword="null"/> ise
    /// calistirmanin kendisi koktur.
    /// </summary>
    /// <remarks>
    /// Deger denormalize edilmistir: bir agacin tamamini <see cref="ParentRunId"/>
    /// uzerinden cekmek ozyinelemeli sorgu gerektirir, kok kimligiyle tek indeksli
    /// sorgu yeter. Gerekce: docs/KARARLAR.md, karar K-094.
    /// </remarks>
    public Guid? RootRunId { get; init; }

    /// <summary>Agactaki derinlik. Kok calistirma 0'dir.</summary>
    public int Depth { get; init; }

    /// <summary>
    /// Agac boyunca <strong>paylasilan</strong> butce. <see langword="null"/> ise
    /// kok calistirmayi acan sarmalayici ayarlardan bir butce uretir.
    /// </summary>
    /// <remarks>
    /// Nesne agactaki her calistirmada <em>ayni ornektir</em>. Kopyalanirsa her
    /// dal kendi butcesini alir ve sinir anlamini yitirir.
    /// </remarks>
    public AgentRunBudget? Budget { get; init; }

    /// <summary>
    /// Bu calistirmanin turu. <see langword="null"/> ise sarmalayici
    /// <see cref="RunKind.Agent"/> varsayar.
    /// </summary>
    /// <remarks>
    /// Eval is isleyicisi (Faz 18) her vaka calistirmasinda <see cref="RunKind.Eval"/>
    /// verir; boylece <see cref="IRunStore.GetStatisticsAsync"/> bu sentetik
    /// cagrilari ozetten haric tutabilir.
    /// </remarks>
    public RunKind? Kind { get; init; }

    /// <inheritdoc />
    /// <remarks>
    /// Kopyalama bes alanin tamamini korur. Aksi halde ayarlari kopyalayan bir ara
    /// katman kimligi sessizce dusurur ve sarmalayici kendi kimligini uretirdi -
    /// istemciye bildirilen kimlik ise artik hicbir kayda karsilik gelmezdi. Ayni
    /// tuzak agac alanlari icin daha sinsidir: dusen bir <see cref="Depth"/> degeri
    /// ozyineleme korumasini sessizce devre disi birakir.
    /// </remarks>
    public override AgentRunOptions Clone() => new AgentPrismRunOptions(this);
}
