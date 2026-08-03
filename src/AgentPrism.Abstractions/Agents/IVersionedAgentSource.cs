using Microsoft.Agents.AI;

namespace AgentPrism;

/// <summary>
/// Belirli bir tanim surumunu cozebilen bir <see cref="IAgentSource"/>. Yalnizca
/// surum gecmisi tutan kaynaklar (veritabani kaynagi) uygular; kod kaynaginin
/// surum kavrami olmadigi icin bu arayuzu uygulamasina gerek yoktur (karar K-003).
/// </summary>
public interface IVersionedAgentSource : IAgentSource
{
    /// <summary>Adi ve belirli bir surumu cozer.</summary>
    /// <param name="agentName">Agent adi.</param>
    /// <param name="version">Istenen tanim surumu.</param>
    /// <param name="cancellationToken">Iptal belirteci.</param>
    /// <returns>Agent; bu kaynakta yoksa veya o surum mevcut degilse <see langword="null"/>.</returns>
    ValueTask<AIAgent?> ResolveVersionAsync(string agentName, int version, CancellationToken cancellationToken = default);
}
