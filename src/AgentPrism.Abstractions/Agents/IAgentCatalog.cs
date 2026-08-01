using Microsoft.Agents.AI;

namespace AgentPrism;

/// <summary>
/// Tum agent kaynaklarini tek bir gorunumde birlestiren katalog.
/// Arayuz ve HTTP katmani agent'lara yalnizca bu arayuz uzerinden erisir.
/// </summary>
public interface IAgentCatalog
{
    /// <summary>
    /// Tum kaynaklardaki agent'lari listeler. Ad cakismasinda onceligi yuksek
    /// kaynak kazanir ve dusuk oncelikli olan listeye eklenmez.
    /// </summary>
    /// <param name="cancellationToken">Iptal belirteci.</param>
    /// <returns>Ada gore siralanmis agent ozetleri.</returns>
    ValueTask<IReadOnlyList<AgentDescriptor>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Adi verilen agent'i cozer. Donen agent, calistirma kaydi sarmalayicisi
    /// ile sarilmistir; her calistirma <see cref="IRunStore"/> icine yazilir.
    /// </summary>
    /// <param name="agentName">Agent adi.</param>
    /// <param name="cancellationToken">Iptal belirteci.</param>
    /// <returns>Agent; hicbir kaynakta bulunamazsa <see langword="null"/>.</returns>
    ValueTask<AIAgent?> ResolveAsync(string agentName, CancellationToken cancellationToken = default);
}
