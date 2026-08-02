using Microsoft.Agents.AI;

namespace AgentPrism;

/// <summary>
/// Bir calistirmanin kimligini cagiranin belirlemesini saglayan calistirma ayarlari.
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
        => RunId = other.RunId;

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

    /// <inheritdoc />
    /// <remarks>
    /// Kopyalama <see cref="RunId"/> degerini korur. Aksi halde ayarlari kopyalayan
    /// bir ara katman kimligi sessizce dusurur ve sarmalayici kendi kimligini
    /// uretirdi - istemciye bildirilen kimlik ise artik hicbir kayda karsilik gelmezdi.
    /// </remarks>
    public override AgentRunOptions Clone() => new AgentPrismRunOptions(this);
}
