using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AgentPrism.Core.UnitTests.Fakes;

/// <summary>Helpers that produce objects that repeat across tests.</summary>
internal static class TestData
{
    public static ModelBinding Binding(string provider = "fake", string model = "fake-model")
        => new() { Provider = provider, Model = model };

    public static AgentDefinition Definition(
        string name = "test-agent",
        IReadOnlyList<string>? toolNames = null,
        HarnessSettings? harness = null,
        IReadOnlyList<string>? mcpResourceUris = null)
        => new()
        {
            Name = name,
            Instructions = "You are a test agent.",
            Model = Binding(),
            ToolNames = toolNames ?? [],
            Harness = harness,
            McpResourceUris = mcpResourceUris ?? [],
        };

    public static AIFunction Tool(string name, string description = "test tool")
        => AIFunctionFactory.Create(() => "result", name, description);

    public static ToolRegistry Registry(params AIFunction[] tools)
        => new(tools.Select(static tool => new AgentPrismToolRegistration(tool)));

    public static ModelProviderRegistry Providers(params IModelProvider[] providers)
        => new(providers);

    /// <summary>Sets up a provider registry with a content guard attached.</summary>
    public static ModelProviderRegistry Providers(ContentGuardPipeline guards, params IModelProvider[] providers)
        => new(providers, contentGuards: guards);

    /// <summary>
    /// Sets up an audit pipeline from the given guards.
    /// </summary>
    /// <param name="auditLog">
    /// The log that block decisions are written to. When omitted, a new
    /// in-memory log is used.
    /// </param>
    /// <param name="options">The pipeline settings.</param>
    /// <param name="guards">The registered guards. The pipeline is inactive when this is empty.</param>
    public static ContentGuardPipeline ContentGuards(
        IAuditLog? auditLog = null,
        AgentPrismContentGuardOptions? options = null,
        params IContentGuard[] guards)
        => new(
            guards,
            new StaticOptionsMonitor<AgentPrismContentGuardOptions>(options ?? new AgentPrismContentGuardOptions()),
            auditLog ?? new InMemoryAuditLog(),
            new AmbientAuditActorResolver(Options.Create(new AgentPrismOptions())),
            FixedTenantContext.Default,
            NullLoggerFactory.Instance);
}
