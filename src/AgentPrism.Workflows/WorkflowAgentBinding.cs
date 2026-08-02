using Microsoft.Agents.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AgentPrism;

/// <summary>
/// Kodda tanimlanan bir workflow grafina katalogdaki agent'lari baglar.
/// </summary>
/// <remarks>
/// <para>
/// 🚨 <strong>Katalogdan alinan agent'i grafa DOGRUDAN koymayin.</strong>
/// <c>IAgentCatalog.ResolveAsync</c> calistirma kaydi sarmalayicisini tasiyan
/// bir agent dondurur, ancak Microsoft Agent Framework onu <c>options = null</c>
/// ile cagirir; sarmalayici agac bilgisini gelen ayarlardan okuyamaz ve KENDI
/// kok satirini acar. Sonuc: workflow calistirmasi bos gorunur, agent'lar
/// listede bagimsiz koklerdir ve waterfall dogru cizilmez. Olculdu (Faz 15):
/// ornek uygulamada agac uc satir yerine bir satir dondu.
/// </para>
/// <para>
/// Bu yardimci agent'i <see cref="ChildAgentInvoker"/> ile sarar. Sarmalayici
/// agac bilgisini ortam kapsamindan okur, derinlik/butce/kiraci sinirlarini
/// uygular ve alt calistirmayi workflow satirinin altina baglar. Arayuzden
/// tanimlanan workflow'larda ayni sarmalama derleyici tarafindan yapilir.
/// </para>
/// <example>
/// <code>
/// agentPrism.AddWorkflow("ozetle-ve-cevir", services =>
///     AgentWorkflowBuilder.BuildSequential("ozetle-ve-cevir",
///     [
///         services.GetWorkflowAgent("ozetle-ve-cevir", "ozetleyici"),
///         services.GetWorkflowAgent("ozetle-ve-cevir", "cevirmen"),
///     ]));
/// </code>
/// </example>
/// </remarks>
public static class WorkflowAgentBinding
{
    /// <summary>
    /// Katalogdaki bir agent'i, workflow grafina konabilecek bicimde sarar.
    /// </summary>
    /// <param name="services">Servis saglayici.</param>
    /// <param name="workflowName">Sarmalayan workflow'un adi. Hata mesajlarinda gorunur.</param>
    /// <param name="agentName">Baglanacak agent'in adi.</param>
    /// <param name="description">
    /// Agent'in ne yaptigini anlatan aciklama. <c>GroupChat</c> ve
    /// <c>Magentic</c> desenlerinde modele gonderilen katilimci listesine girer;
    /// bos birakilirsa yonetici agent'in ne zaman kimi cagiracagini bilmesi
    /// zorlasir.
    /// </param>
    /// <returns>Grafa konabilecek agent.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> <see langword="null"/> ise.</exception>
    /// <exception cref="ArgumentException">Adlardan biri bos ise.</exception>
    /// <remarks>
    /// <para>
    /// Agent <strong>gec cozulur</strong>: bu cagri katalogu okumaz, yalnizca
    /// bir sarmalayici kurar. Boylece agent tanimi degistiginde derlenmis
    /// workflow bayatlamaz ve fabrikanin es zamanli olmasi sorun cikarmaz.
    /// </para>
    /// <para>
    /// Ayni <c>(workflowName, agentName)</c> cifti icin <strong>ayni ornek</strong>
    /// doner. Bu bilinclidir: Microsoft Agent Framework executor kimliklerini
    /// agent ornegin kimliginden turetir ve her cagride yeni bir ornek
    /// dondurmek kontrol noktalarini uyumsuz yapardi.
    /// </para>
    /// </remarks>
    public static AIAgent GetWorkflowAgent(
        this IServiceProvider services,
        string workflowName,
        string agentName,
        string? description = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowName);
        ArgumentException.ThrowIfNullOrWhiteSpace(agentName);

        return services.GetRequiredService<WorkflowAgentCache>().Get(workflowName, agentName, description);
    }
}
