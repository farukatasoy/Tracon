using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AgentPrism.Core.UnitTests.Fakes;

/// <summary>Testlerde tekrar eden nesneleri ureten yardimcilar.</summary>
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
            Instructions = "Sen bir test agent'isin.",
            Model = Binding(),
            ToolNames = toolNames ?? [],
            Harness = harness,
            McpResourceUris = mcpResourceUris ?? [],
        };

    public static AIFunction Tool(string name, string description = "test tool")
        => AIFunctionFactory.Create(() => "sonuc", name, description);

    public static ToolRegistry Registry(params AIFunction[] tools)
        => new(tools.Select(static tool => new AgentPrismToolRegistration(tool)));

    public static ModelProviderRegistry Providers(params IModelProvider[] providers)
        => new(providers);

    /// <summary>Icerik guard'i takilmis bir saglayici defteri kurar.</summary>
    public static ModelProviderRegistry Providers(ContentGuardPipeline guards, params IModelProvider[] providers)
        => new(providers, contentGuards: guards);

    /// <summary>
    /// Verilen guard'lardan bir denetim boru hatti kurar.
    /// </summary>
    /// <param name="auditLog">
    /// Engelleme kararlarinin yazilacagi defter. Verilmezse yeni bir bellek ici
    /// defter kullanilir.
    /// </param>
    /// <param name="options">Boru hatti ayarlari.</param>
    /// <param name="guards">Kayitli guard'lar. Bos birakilirsa boru hatti pasiftir.</param>
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
