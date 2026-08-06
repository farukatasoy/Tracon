using Microsoft.Agents.AI;

namespace AgentPrism.Core.UnitTests.Fakes;

/// <summary>
/// Sabit bir ozet listesi dondüren sahte katalog. <see cref="ResolveAsync(string, CancellationToken)"/>
/// hicbir zaman gercek bir agent uretmez — cagri grafigi ve dogrulama testleri
/// yalnizca <see cref="ListAsync"/>'e ihtiyac duyar.
/// </summary>
/// <remarks>
/// <see cref="IServiceProvider"/> de uygular: <see cref="CallableAgentResolver"/>
/// katalogu <c>IServiceProvider.GetRequiredService&lt;IAgentCatalog&gt;()</c> ile
/// gec cozer; bu tip ayni ornegi ikisi icin de sunar.
/// </remarks>
internal sealed class FakeAgentCatalog(IReadOnlyList<AgentDescriptor> descriptors) : IAgentCatalog, IServiceProvider
{
    public object? GetService(Type serviceType)
        => serviceType == typeof(IAgentCatalog) ? this : null;

    public ValueTask<IReadOnlyList<AgentDescriptor>> ListAsync(CancellationToken cancellationToken = default)
        => new(descriptors);

    public ValueTask<AIAgent?> ResolveAsync(string agentName, CancellationToken cancellationToken = default)
        => new((AIAgent?)null);

    public ValueTask<AIAgent?> ResolveAsync(string agentName, int? version, CancellationToken cancellationToken = default)
        => ResolveAsync(agentName, cancellationToken);
}
