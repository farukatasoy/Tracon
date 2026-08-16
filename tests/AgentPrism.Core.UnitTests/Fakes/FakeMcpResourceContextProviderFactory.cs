using Microsoft.Agents.AI;

namespace AgentPrism.Core.UnitTests.Fakes;

/// <summary>
/// A fake <see cref="IMcpResourceContextProviderFactory"/>. Makes no real MCP
/// connection; it exists only to verify whether the compiler
/// (<see cref="AgentDefinitionCompiler"/>) calls the factory.
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
