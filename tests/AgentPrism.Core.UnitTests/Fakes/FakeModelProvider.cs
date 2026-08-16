using Microsoft.Extensions.AI;

namespace AgentPrism.Core.UnitTests.Fakes;

/// <summary>A fake single-model provider used in tests.</summary>
internal sealed class FakeModelProvider : IModelProvider
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

    public IChatClient CreateChatClient(ModelBinding binding)
    {
        LastBinding = binding;
        return _client;
    }
}
