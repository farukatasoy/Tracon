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

    /// <summary>
    /// Adi ve <strong>belirli bir tanim surumunu</strong> cozer. Kod kaynakli agent'larda
    /// (<see cref="AgentDefinitionOrigin.Code"/>) surum gecmisi yoktur; <paramref name="version"/>
    /// verilirse ve agent kod kaynakliysa <see cref="AgentPrismException"/> firlatilir.
    /// </summary>
    /// <param name="agentName">Agent adi.</param>
    /// <param name="version">
    /// Istenen tanim surumu. <see langword="null"/> ise <see cref="ResolveAsync(string, CancellationToken)"/>
    /// ile ayni davranir (guncel surum).
    /// </param>
    /// <param name="cancellationToken">Iptal belirteci.</param>
    /// <returns>Agent; bulunamazsa <see langword="null"/>.</returns>
    /// <exception cref="AgentPrismException">
    /// <paramref name="version"/> verilmis ve agent kod kaynakliysa, veya o surum mevcut degilse.
    /// </exception>
    ValueTask<AIAgent?> ResolveAsync(string agentName, int? version, CancellationToken cancellationToken = default);
}
