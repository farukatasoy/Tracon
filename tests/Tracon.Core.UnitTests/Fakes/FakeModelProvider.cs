using Microsoft.Extensions.AI;

namespace Tracon.Core.UnitTests.Fakes;

/// <summary>A fake single-model provider used in tests.</summary>
internal sealed class FakeModelProvider : ITenantCredentialModelProvider, ISamplingSupport, IHostedToolSupport
{
    private readonly IChatClient _client;

    public FakeModelProvider(
        IChatClient? client = null,
        string name = "fake",
        IReadOnlyList<ModelDescriptor>? models = null,
        SamplingFields? supportedSamplingFields = null,
        HostedToolKinds? supportedHostedTools = null)
    {
        _client = client ?? new FakeChatClient();
        Name = name;
        SupportedSamplingFields = supportedSamplingFields ?? AllSamplingFields;
        SupportedHostedTools = supportedHostedTools ?? AllHostedTools;
        Models = models ??
        [
            new ModelDescriptor { Name = "fake-model", ContextWindowTokens = 8_192, MaxOutputTokens = 1_024 },
        ];
    }

    /// <summary>Every field: a fake sends nothing, so a test limits it only when it tests the limit.</summary>
    public const SamplingFields AllSamplingFields =
        SamplingFields.Seed
        | SamplingFields.TopK
        | SamplingFields.StopSequences
        | SamplingFields.FrequencyPenalty
        | SamplingFields.PresencePenalty
        | SamplingFields.ToolMode
        | SamplingFields.AllowMultipleToolCalls
        | SamplingFields.ReasoningOutput;

    /// <summary>Every kind: a fake runs no tool, so a test limits it only when it tests the limit.</summary>
    public const HostedToolKinds AllHostedTools =
        HostedToolKinds.WebSearch | HostedToolKinds.CodeInterpreter | HostedToolKinds.FileSearch;

    public string Name { get; }

    public SamplingFields SupportedSamplingFields { get; }

    public HostedToolKinds SupportedHostedTools { get; }

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
