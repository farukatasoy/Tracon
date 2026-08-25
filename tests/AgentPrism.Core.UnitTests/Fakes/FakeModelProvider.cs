using Microsoft.Extensions.AI;

namespace AgentPrism.Core.UnitTests.Fakes;

/// <summary>A fake single-model provider used in tests.</summary>
internal sealed class FakeModelProvider : ITenantCredentialModelProvider
{
    private readonly IChatClient _client;

    public FakeModelProvider(IChatClient? client = null, string name = "fake", IReadOnlyList<ModelDescriptor>? models = null)
    {
        _client = client ?? new FakeChatClient();
        Name = name;
        Models = models ??
        [
            new ModelDescriptor { Name = "fake-model", ContextWindowTokens = 8_192, MaxOutputTokens = 1_024 },
        ];
    }

    public string Name { get; }

    public IReadOnlyList<ModelDescriptor> Models { get; }

    /// <summary>The last requested binding. Tests verify option mapping through this.</summary>
    public ModelBinding? LastBinding { get; private set; }

    /// <summary>The last credential passed in (phase 65, BYOK). Tests verify tenant resolution through this.</summary>
    public ModelProviderCredential? LastCredential { get; private set; }

    public IChatClient CreateChatClient(ModelBinding binding)
        => CreateChatClientCore(binding, credential: null);

    public IChatClient CreateChatClient(ModelBinding binding, ModelProviderCredential credential)
        => CreateChatClientCore(binding, credential);

    private IChatClient CreateChatClientCore(ModelBinding binding, ModelProviderCredential? credential)
    {
        LastBinding = binding;
        LastCredential = credential;
        return _client;
    }
}
