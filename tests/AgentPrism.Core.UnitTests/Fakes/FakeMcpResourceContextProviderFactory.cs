using Microsoft.Agents.AI;

namespace AgentPrism.Core.UnitTests.Fakes;

/// <summary>
/// <see cref="IMcpResourceContextProviderFactory"/> sahtesi. Gercek MCP
/// baglantisi kurmaz; yalniz derleyicinin (<see cref="AgentDefinitionCompiler"/>)
/// fabrikayi cagirip cagirmadigini dogrulamak icindir.
/// </summary>
internal sealed class FakeMcpResourceContextProviderFactory : IMcpResourceContextProviderFactory
{
    public IReadOnlyList<string>? LastResourceReferences { get; private set; }

    public string? LastTenantId { get; private set; }

    public AIContextProvider Create(IReadOnlyList<string> resourceReferences, string tenantId)
    {
        LastResourceReferences = resourceReferences;
        LastTenantId = tenantId;

        return new NoOpAIContextProvider();
    }

    private sealed class NoOpAIContextProvider : AIContextProvider
    {
        protected override ValueTask<AIContext> ProvideAIContextAsync(InvokingContext context, CancellationToken cancellationToken = default)
            => new(new AIContext());
    }
}
