using Microsoft.Extensions.AI;

namespace Tracon.Core.UnitTests.Fakes;

/// <summary>
/// A fake provider that implements <see cref="IModelProviderConfigurationDiagnostics"/>.
/// Used to test the case where multiple providers report the SAME key.
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
        Hint = resolved ? null : $"dotnet user-secrets set \"{key}\" \"<key>\"",
    };
}
