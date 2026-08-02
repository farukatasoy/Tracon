using Microsoft.Agents.AI.Workflows;

namespace AgentPrism;

/// <summary>
/// Kodda fabrika ile tanimlanmis bir workflow kaydi.
/// </summary>
/// <param name="Name">Workflow adi.</param>
/// <param name="Description">Kisa aciklama.</param>
/// <param name="Factory">Grafi kuran fabrika.</param>
/// <remarks>
/// Kodda tanimli workflow <strong>serbest graftir</strong>: ozel
/// <c>Executor</c> tipleri, kosullu kenarlar ve alt workflow'lar
/// kullanabilir. Bu, tasarim kurali K2'yi bozmaz - kod derleme zamaninda
/// yazilmistir. Arayuzden tanimlanan workflow ise yalnizca katalogdaki
/// agent'lari hazir desenlerle diziler.
/// </remarks>
internal sealed record CodeWorkflowRegistration(
    string Name,
    string? Description,
    Func<IServiceProvider, Workflow> Factory);
