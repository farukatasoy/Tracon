namespace Tracon.Core.UnitTests.Fakes;

/// <summary>Builds the definition lookup the production container builds, over the sources a test needs.</summary>
internal static class TestLookups
{
    /// <summary>A lookup over the database source of one store: the shape the factory had before the lookup walked sources.</summary>
    /// <param name="definitions">The definition store.</param>
    /// <param name="compiler">The compiler the source holds.</param>
    /// <returns>The lookup.</returns>
    public static AgentDefinitionLookup OverStore(IAgentDefinitionStore definitions, AgentDefinitionCompiler compiler)
        => new([new DefinitionStoreAgentSource(definitions, compiler, new CompiledAgentCache(), new FixedTenantContext("default"))]);

    /// <summary>A lookup over no source: no agent has a definition.</summary>
    /// <returns>The lookup.</returns>
    public static AgentDefinitionLookup Empty() => new([]);
}
