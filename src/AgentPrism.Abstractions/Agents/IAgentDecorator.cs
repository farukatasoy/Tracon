using Microsoft.Agents.AI;

namespace AgentPrism;

/// <summary>
/// Katalogdan cozulen her agent'a uygulanan bir sarmalayici. AgentPrism
/// calistirma kaydini bu mekanizma ile ekler; tuketici kendi sarmalayicisini
/// ayni sekilde kaydedebilir.
/// </summary>
/// <remarks>
/// <para>
/// Sarmalayici, Microsoft Agent Framework middleware'i <em>degildir</em>.
/// MAF middleware zinciri agent'a ozgudur ve <c>HarnessAgent</c> kendi ic
/// dekoratorlerini ekler. Dis sarmalayici, harness dahil her agent tipinde
/// ayni sekilde calisir. Gerekce: <c>docs/KARARLAR.md</c>, ilgili karar.
/// </para>
/// </remarks>
public interface IAgentDecorator
{
    /// <summary>
    /// Uygulama sirasi. Kucuk deger <em>ice</em>, buyuk deger <em>disa</em> sarilir.
    /// Calistirma kaydi 0 kullanir, boylece en distaki sarmalayici olur.
    /// </summary>
    int Order { get; }

    /// <summary>Agent'i sarmalar.</summary>
    /// <param name="agent">Sarmalanacak agent.</param>
    /// <param name="descriptor">Agent'in katalog ozeti.</param>
    /// <returns>Sarmalanmis agent.</returns>
    AIAgent Decorate(AIAgent agent, AgentDescriptor descriptor);
}
