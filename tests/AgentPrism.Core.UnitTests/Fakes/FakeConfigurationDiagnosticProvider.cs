using Microsoft.Extensions.AI;

namespace AgentPrism.Core.UnitTests.Fakes;

/// <summary>
/// <see cref="IModelProviderConfigurationDiagnostics"/> uygulayan sahte saglayici.
/// Birden fazla saglayicinin AYNI anahtari bildirdigi durumu test etmek icindir.
/// </summary>
internal sealed class FakeConfigurationDiagnosticProvider(string name, string key, bool resolved)
    : IModelProvider, IModelProviderConfigurationDiagnostics
{
    public string Name { get; } = name;

    public IReadOnlyList<ModelDescriptor> Models { get; } =
        [new ModelDescriptor { Name = "fake-model", ContextWindowTokens = 8_192, MaxOutputTokens = 1_024 }];

    public IChatClient CreateChatClient(ModelBinding binding) => new FakeChatClient();

    public ConfigurationDiagnostic? GetConfigurationDiagnostic() => new ConfigurationDiagnostic
    {
        Key = key,
        Resolved = resolved,
        Hint = resolved ? null : $"dotnet user-secrets set \"{key}\" \"<anahtar>\"",
    };
}
