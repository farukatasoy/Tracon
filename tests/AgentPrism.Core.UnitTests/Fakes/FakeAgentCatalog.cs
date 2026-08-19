using Microsoft.Agents.AI;

namespace AgentPrism.Core.UnitTests.Fakes;

/// <summary>
/// A fake catalog that returns a fixed descriptor list. <see cref="ResolveAsync(string, string, CancellationToken)"/>
/// never produces a real agent — the call graph and validation tests only need
/// <see cref="ListAsync"/>.
/// </summary>
/// <remarks>
/// Also implements <see cref="IServiceProvider"/>: <see cref="CallableAgentResolver"/>
/// resolves the catalog lazily via <c>IServiceProvider.GetRequiredService&lt;IAgentCatalog&gt;()</c>;
/// this type serves the same instance for both.
/// </remarks>
internal sealed class FakeAgentCatalog(IReadOnlyList<AgentDescriptor> descriptors) : IAgentCatalog, IServiceProvider
{
    public object? GetService(Type serviceType)
        => serviceType == typeof(IAgentCatalog) ? this : null;

    public ValueTask<IReadOnlyList<AgentDescriptor>> ListAsync(CancellationToken cancellationToken = default)
        => new(descriptors);

    public ValueTask<AIAgent?> ResolveAsync(string agentName, string? culture, CancellationToken cancellationToken)
        => new((AIAgent?)null);

    public ValueTask<AIAgent?> ResolveAsync(string agentName, int? version, string? culture = null, CancellationToken cancellationToken = default)
        => ResolveAsync(agentName, culture, cancellationToken);
}
