using Microsoft.Agents.AI;

namespace AgentPrism;

/// <summary>
/// Katalogun agent topladigi bir kaynak. AgentPrism birden cok kaynagi ayni
/// katalogda birlestirir: kodda tanimlananlar, veritabaninda saklananlar ve
/// (Faz 4'ten itibaren) Microsoft Agent Framework barindirma kayitlari.
/// </summary>
/// <remarks>
/// <para>
/// Bu soyutlama, <c>AgentPrism.Core</c>'un onsurum durumundaki
/// <c>Microsoft.Agents.AI.Hosting</c> paketine bagimli olmasini engeller.
/// MAF barindirma kayitlarini katalogda gostermek isteyen kopru,
/// <c>AgentPrism.AspNetCore</c> icinde bu arayuzu uygular.
/// Gerekce: <c>docs/KARARLAR.md</c>, karar K-008 ve K-019.
/// </para>
/// </remarks>
public interface IAgentSource
{
    /// <summary>Kaynagin adi. Tanilama ve arayuz rozetleri icin kullanilir.</summary>
    string Name { get; }

    /// <summary>
    /// Cozum onceligi. Kucuk deger once denenir. Ayni ada sahip iki agent
    /// varsa oncelikli kaynak kazanir.
    /// </summary>
    /// <remarks>
    /// Kullanilan degerler: kod kaynagi 0, MAF barindirma kaynagi 10,
    /// veritabani kaynagi 100.
    /// </remarks>
    int Priority { get; }

    /// <summary>Bu kaynaktaki tum agent'lari listeler.</summary>
    /// <param name="cancellationToken">Iptal belirteci.</param>
    /// <returns>Agent ozetleri.</returns>
    ValueTask<IReadOnlyList<AgentDescriptor>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>Adi verilen agent'i cozer ve calistirilabilir hale getirir.</summary>
    /// <param name="agentName">Agent adi.</param>
    /// <param name="cancellationToken">Iptal belirteci.</param>
    /// <returns>Agent; bu kaynakta yoksa <see langword="null"/>.</returns>
    ValueTask<AIAgent?> ResolveAsync(string agentName, CancellationToken cancellationToken = default);
}
