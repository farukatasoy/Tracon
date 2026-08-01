using Microsoft.Extensions.AI;

namespace AgentPrism.Core.UnitTests.Fakes;

/// <summary>Testlerde tekrar eden nesneleri ureten yardimcilar.</summary>
internal static class TestData
{
    public static ModelBinding Binding(string provider = "fake", string model = "fake-model")
        => new() { Provider = provider, Model = model };

    public static AgentDefinition Definition(
        string name = "test-agent",
        IReadOnlyList<string>? toolNames = null,
        HarnessSettings? harness = null)
        => new()
        {
            Name = name,
            Instructions = "Sen bir test agent'isin.",
            Model = Binding(),
            ToolNames = toolNames ?? [],
            Harness = harness,
        };

    public static AIFunction Tool(string name, string description = "test tool")
        => AIFunctionFactory.Create(() => "sonuc", name, description);

    public static ToolRegistry Registry(params AIFunction[] tools)
        => new(tools.Select(static tool => new AgentPrismToolRegistration(tool)));

    public static ModelProviderRegistry Providers(params IModelProvider[] providers)
        => new(providers);
}
