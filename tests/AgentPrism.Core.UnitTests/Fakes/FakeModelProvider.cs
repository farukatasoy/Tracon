using Microsoft.Extensions.AI;

namespace AgentPrism.Core.UnitTests.Fakes;

/// <summary>Testlerde kullanilan, tek modelli sahte saglayici.</summary>
internal sealed class FakeModelProvider : IModelProvider
{
    private readonly FakeChatClient _client;

    public FakeModelProvider(FakeChatClient? client = null, string name = "fake")
    {
        _client = client ?? new FakeChatClient();
        Name = name;
    }

    public string Name { get; }

    public IReadOnlyList<ModelDescriptor> Models { get; } =
    [
        new ModelDescriptor { Name = "fake-model", ContextWindowTokens = 8_192, MaxOutputTokens = 1_024 },
    ];

    /// <summary>Son istenen baglanti. Testler secenek eslemesini bunun uzerinden dogrular.</summary>
    public ModelBinding? LastBinding { get; private set; }

    public IChatClient CreateChatClient(ModelBinding binding)
    {
        LastBinding = binding;
        return _client;
    }
}
