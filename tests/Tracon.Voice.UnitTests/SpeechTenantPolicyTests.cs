using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Tracon.Voice.UnitTests;

/// <summary>
/// The three agent tools (<c>speak</c>, <c>transcribe</c>, <c>list_voices</c>) check the
/// run tenant's egress policy and own credential before the provider is reached.
/// </summary>
/// <remarks>
/// <para>
/// The tools resolved <see cref="ISpeechSynthesizer"/> and
/// <see cref="ISpeechTranscriber"/> straight from the container. A tenant whose policy
/// did not allow the speech provider still had its text and audio sent there, and a
/// tenant with its own key was billed to the host.
/// </para>
/// <para>
/// The tools are taken from the registrations <c>UseVoice()</c> wrote, invoked with an
/// argument set that carries NO service provider (as Microsoft Agent Framework calls
/// them), inside a run scope of the tenant. The provider counts its calls; every
/// refusal asserts the count stays <c>0</c>.
/// </para>
/// </remarks>
public sealed class SpeechTenantPolicyTests
{
    private const string Tenant = "acme";
    private const string Provider = "contoso";

    [Theory]
    [InlineData(SpeakTool.ToolName)]
    [InlineData(TranscribeTool.ToolName)]
    [InlineData(ListVoicesTool.ToolName)]
    public async Task A_tool_refuses_a_speech_provider_the_run_tenants_policy_forbids(string toolName)
    {
        var provider = new CountingProvider();
        await using var services = Build(provider);

        await services.GetRequiredService<ITenantEgressPolicyStore>()
            .UpsertAsync(Tenant, ["openai"], TestContext.Current.CancellationToken);

        var attachmentId = await SaveAudioAsync(services);

        var exception = await Should.ThrowAsync<TraconException>(
            async () => await InvokeAsync(services, toolName, attachmentId));

        exception.Message.ShouldContain($"speech provider '{Provider}'");
        provider.Calls.ShouldBe(0, $"'{toolName}' reached a provider the tenant's policy forbids");
    }

    [Theory]
    [InlineData(SpeakTool.ToolName)]
    [InlineData(TranscribeTool.ToolName)]
    [InlineData(ListVoicesTool.ToolName)]
    public async Task A_tool_refuses_a_tenant_whose_own_key_the_provider_cannot_use(string toolName)
    {
        var provider = new CountingProvider();
        await using var services = Build(
            provider,
            new ConfigurationBuilder()
                .AddInMemoryCollection([new($"Tracon:ProviderKeys:{Tenant}:Contoso", "acme-contoso-value")])
                .Build());

        await services.GetRequiredService<ITenantProviderBindingStore>().UpsertAsync(
            new TenantProviderBinding
            {
                TenantId = Tenant,
                ProviderName = Provider,
                ApiKeyConfigurationName = $"Tracon:ProviderKeys:{Tenant}:Contoso",
                UpdatedAt = DateTimeOffset.UtcNow,
            },
            TestContext.Current.CancellationToken);

        var attachmentId = await SaveAudioAsync(services);

        var exception = await Should.ThrowAsync<TraconException>(
            async () => await InvokeAsync(services, toolName, attachmentId));

        exception.Message.ShouldContain("own credential");
        exception.Message.ShouldNotContain("acme-contoso-value");
        provider.Calls.ShouldBe(0, $"'{toolName}' served a bound tenant on the host's key");
    }

    [Theory]
    [InlineData(SpeakTool.ToolName)]
    [InlineData(TranscribeTool.ToolName)]
    [InlineData(ListVoicesTool.ToolName)]
    public async Task A_tool_reaches_the_provider_when_the_policy_names_it(string toolName)
    {
        var provider = new CountingProvider();
        await using var services = Build(provider);

        await services.GetRequiredService<ITenantEgressPolicyStore>()
            .UpsertAsync(Tenant, [Provider], TestContext.Current.CancellationToken);

        var attachmentId = await SaveAudioAsync(services);

        _ = await InvokeAsync(services, toolName, attachmentId);

        provider.Calls.ShouldBe(1);
    }

    private static ServiceProvider Build(CountingProvider provider, IConfiguration? configuration = null)
    {
        var services = new ServiceCollection();

        // The consumer's own provider, registered first so UseVoice keeps it.
        services.AddSingleton<ISpeechSynthesizer>(provider);
        services.AddSingleton<ISpeechTranscriber>(provider);

        if (configuration is not null)
        {
            services.AddSingleton(configuration);
        }

        services.AddLogging();
        services.AddTracon().UseVoice(static _ => { });

        return services.BuildServiceProvider();
    }

    private static async Task<Guid> SaveAudioAsync(ServiceProvider services)
    {
        var descriptor = await services.GetRequiredService<IAttachmentStore>().SaveAsync(
            new AttachmentContent
            {
                TenantId = Tenant,
                FileName = "question.mp3",
                MediaType = "audio/mpeg",
                Data = CountingProvider.Mp3,
            },
            TestContext.Current.CancellationToken);

        return descriptor.Id;
    }

    private static async Task<object?> InvokeAsync(ServiceProvider services, string toolName, Guid attachmentId)
    {
        var tool = (AIFunction)services.GetServices<TraconToolRegistration>()
            .Single(registration => string.Equals(registration.Function.Name, toolName, StringComparison.Ordinal))
            .Function;

        // No Services on the arguments: the tool must work from what it took at setup.
        var arguments = new AIFunctionArguments(
            new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["text"] = "hello",
                ["attachmentId"] = attachmentId.ToString(),
            },
            StringComparer.Ordinal);

        TraconRunContext.SetCurrent(new AgentRunScope
        {
            RunId = Guid.NewGuid(),
            RootRunId = Guid.NewGuid(),
            TenantId = Tenant,
            SessionId = "session-1",
            AgentName = "voice-agent",
        });

        try
        {
            return await tool.InvokeAsync(arguments, TestContext.Current.CancellationToken);
        }
        finally
        {
            TraconRunContext.SetCurrent(null);
        }
    }

    /// <summary>A consumer's speech provider that counts every call that reaches it.</summary>
    private sealed class CountingProvider : ISpeechSynthesizer, ISpeechTranscriber
    {
        /// <summary>A valid MP3 first frame (ID3 tag + frame sync).</summary>
        public static readonly byte[] Mp3 = [0x49, 0x44, 0x33, 0x04, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0xFF, 0xFB, 0x90, 0x00];

        private int _calls;

        public int Calls => Volatile.Read(ref _calls);

        public string ProviderName => Provider;

        public int MaxCharactersPerRequest => 5000;

        public ValueTask<SpeechAudio> SynthesizeAsync(SpeechRequest request, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _calls);

            return ValueTask.FromResult(new SpeechAudio { Data = Mp3, MediaType = "audio/mpeg" });
        }

        public IAsyncEnumerable<ReadOnlyMemory<byte>> SynthesizeStreamingAsync(
            SpeechRequest request,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public ValueTask<IReadOnlyList<VoiceDescriptor>> ListVoicesAsync(CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _calls);

            return ValueTask.FromResult<IReadOnlyList<VoiceDescriptor>>(
                [new VoiceDescriptor { VoiceId = "v1", Name = "Voice" }]);
        }

        public ValueTask<SpeechTranscript> TranscribeAsync(
            Stream audio,
            string mediaType,
            SpeechTranscriptionOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _calls);

            return ValueTask.FromResult(new SpeechTranscript { Text = "hello" });
        }
    }
}
