using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shouldly;

namespace Tracon.Voice.UnitTests;

/// <summary>
/// The dialogue capability comes from the bound synthesizer, through the resolver only:
/// <c>UseVoice()</c> registers no <see cref="ISpeechDialogueSynthesizer"/> of its own.
/// </summary>
/// <remarks>
/// <para>
/// Faz 203 (Açık Soru 7-B): <see cref="ISpeechProviderResolver.GetDialogueSynthesizerAsync"/>
/// reads the capability from the registered synthesizer instance and applies the
/// tenant's checks. A container registration of the capability would be a second way to
/// reach the provider that skips both (the plan's § 203.1 "Kayıt" proposed one).
/// </para>
/// <para>
/// The tests compose the real <c>AddTracon().UseVoice(...)</c> registration and read
/// <see cref="IStartupValidator"/>: the K-930/F-336 lesson is that a defect in the DI
/// composition is invisible to a validator or a resolver built by hand.
/// </para>
/// </remarks>
public sealed class VoiceDialogueRegistrationTests
{
    [Fact]
    public async Task The_built_in_client_voices_dialogue_through_the_resolver()
    {
        await using var provider = Build(static services => services.AddTracon().UseVoice(static options => options.ApiKey = "k"));

        var dialogue = await provider.GetRequiredService<ISpeechProviderResolver>()
            .GetDialogueSynthesizerAsync(cancellationToken: TestContext.Current.CancellationToken);

        dialogue.ShouldBeOfType<ElevenLabsSpeechClient>();
        dialogue.ShouldBeSameAs(provider.GetRequiredService<ISpeechSynthesizer>(), "one instance, one concurrency counter");
        dialogue.ProviderName.ShouldBe(ElevenLabsProviderNames.ElevenLabs);
    }

    [Fact]
    public async Task The_capability_is_not_registered_where_it_would_skip_the_tenant_checks()
    {
        await using var provider = Build(static services => services.AddTracon().UseVoice(static options => options.ApiKey = "k"));

        provider.GetService<ISpeechDialogueSynthesizer>().ShouldBeNull(
            "a container registration of the capability would reach the provider without the tenant's checks");
    }

    [Fact]
    public async Task A_host_synthesizer_without_dialogue_is_reported_not_replaced_by_the_built_in_client()
    {
        var host = new SingleVoiceSynthesizer();
        await using var provider = Build(services =>
        {
            services.AddSingleton<ISpeechSynthesizer>(host);
            services.AddTracon().UseVoice(static options => options.ApiKey = "k");
        });

        provider.GetRequiredService<IStartupValidator>().Validate();

        var exception = await Should.ThrowAsync<TraconException>(
            async () => await provider.GetRequiredService<ISpeechProviderResolver>()
                .GetDialogueSynthesizerAsync(cancellationToken: TestContext.Current.CancellationToken));

        exception.Message.ShouldContain($"provider '{SingleVoiceSynthesizer.Name}'");
        exception.Message.ShouldContain("cannot voice a dialogue");
        exception.Message.ShouldNotContain(ElevenLabsProviderNames.ElevenLabs);
        host.Calls.ShouldBe(0);
    }

    [Fact]
    public async Task A_named_synthesizer_without_dialogue_is_reported_by_its_name_and_provider()
    {
        await using var provider = Build(static services => services.AddTracon()
            .AddSpeechSynthesizer("Studio", static _ => new SingleVoiceSynthesizer())
            .UseVoice(static options => options.ApiKey = "k"));

        var exception = await Should.ThrowAsync<TraconException>(
            async () => await provider.GetRequiredService<ISpeechProviderResolver>()
                .GetDialogueSynthesizerAsync("Studio", TestContext.Current.CancellationToken));

        exception.Message.ShouldContain("synthesizer 'studio'");
        exception.Message.ShouldContain($"provider '{SingleVoiceSynthesizer.Name}'");
    }

    [Fact]
    public async Task A_host_synthesizer_with_dialogue_wins_over_the_built_in_client()
    {
        var host = new DialogueSynthesizer();
        await using var provider = Build(services =>
        {
            services.AddSingleton<ISpeechSynthesizer>(host);
            services.AddTracon().UseVoice(static options => options.ApiKey = "k");
        });

        var dialogue = await provider.GetRequiredService<ISpeechProviderResolver>()
            .GetDialogueSynthesizerAsync(cancellationToken: TestContext.Current.CancellationToken);

        dialogue.ShouldBeSameAs(host);
    }

    [Fact]
    public async Task An_unknown_name_is_reported_like_the_other_speech_calls()
    {
        await using var provider = Build(static services => services.AddTracon().UseVoice(static options => options.ApiKey = "k"));

        var exception = await Should.ThrowAsync<TraconException>(
            async () => await provider.GetRequiredService<ISpeechProviderResolver>()
                .GetDialogueSynthesizerAsync("missing", TestContext.Current.CancellationToken));

        exception.Message.ShouldContain("No speech synthesizer is registered under the name 'missing'");
    }

    [Fact]
    public async Task The_dialogue_settings_are_read_from_configuration()
    {
        var section = new ConfigurationBuilder()
            .AddInMemoryCollection(
            [
                new("Tracon:Voice:ApiKey", "k"),
                new("Tracon:Voice:DialogueModelId", "eleven_v4"),
                new("Tracon:Voice:MaxDialogueCharactersPerRequest", "1900"),
            ])
            .Build()
            .GetSection(VoiceOptions.SectionName);

        await using var provider = Build(services => services.AddTracon().UseVoice(section));
        provider.GetRequiredService<IStartupValidator>().Validate();

        var options = provider.GetRequiredService<IOptions<VoiceOptions>>().Value;
        options.DialogueModelId.ShouldBe("eleven_v4");
        options.MaxDialogueCharactersPerRequest.ShouldBe(1900);

        var dialogue = await provider.GetRequiredService<ISpeechProviderResolver>()
            .GetDialogueSynthesizerAsync(cancellationToken: TestContext.Current.CancellationToken);

        dialogue.DialogueCapabilities.MaxCharactersPerRequest.ShouldBe(1900);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public async Task A_dialogue_character_limit_that_is_not_positive_stops_the_host(int limit)
    {
        await using var provider = Build(services => services.AddTracon().UseVoice(options =>
        {
            options.ApiKey = "k";
            options.MaxDialogueCharactersPerRequest = limit;
        }));

        Should.Throw<OptionsValidationException>(() => provider.GetRequiredService<IStartupValidator>().Validate())
            .Message.ShouldContain("dialogue character limit");
    }

    [Fact]
    public async Task A_bound_tenant_is_refused_by_the_built_in_client_and_never_served_on_the_host_key()
    {
        // K-930, 4-A: the built-in client takes no tenant credential, for dialogue too.
        const string keyName = "Tracon:ProviderKeys:acme:ElevenLabs";
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection([new(keyName, "acme-elevenlabs-value")])
            .Build();

        await using var provider = Build(services =>
        {
            services.AddSingleton<IConfiguration>(configuration);
            services.AddTracon().UseVoice(static options => options.ApiKey = "host-key");
        });

        await provider.GetRequiredService<ITenantProviderBindingStore>().UpsertAsync(
            new TenantProviderBinding
            {
                TenantId = "acme",
                ProviderName = ElevenLabsProviderNames.ElevenLabs,
                ApiKeyConfigurationName = keyName,
                UpdatedAt = DateTimeOffset.UtcNow,
            },
            TestContext.Current.CancellationToken);

        TraconRunContext.SetCurrent(new AgentRunScope
        {
            RunId = Guid.NewGuid(),
            RootRunId = Guid.NewGuid(),
            TenantId = "acme",
            SessionId = "session-1",
            AgentName = "podcast",
        });

        try
        {
            var exception = await Should.ThrowAsync<TraconException>(
                async () => await provider.GetRequiredService<ISpeechProviderResolver>()
                    .GetDialogueSynthesizerAsync(cancellationToken: TestContext.Current.CancellationToken));

            exception.Message.ShouldContain("own credential");
            exception.Message.ShouldContain("dialogue synthesizer");
            exception.Message.ShouldNotContain("acme-elevenlabs-value");
        }
        finally
        {
            TraconRunContext.SetCurrent(null);
        }
    }

    private static ServiceProvider Build(Action<IServiceCollection> configure)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        configure(services);

        return services.BuildServiceProvider();
    }

    /// <summary>A host's synthesizer that speaks one voice per request and nothing more.</summary>
    private sealed class SingleVoiceSynthesizer : ISpeechSynthesizer
    {
        public const string Name = "contoso-single";

        private int _calls;

        public int Calls => Volatile.Read(ref _calls);

        public string ProviderName => Name;

        public int MaxCharactersPerRequest => 1000;

        public ValueTask<SpeechAudio> SynthesizeAsync(SpeechRequest request, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _calls);

            return ValueTask.FromResult(new SpeechAudio { Data = new byte[] { 1 }, MediaType = "audio/mpeg" });
        }

        public IAsyncEnumerable<ReadOnlyMemory<byte>> SynthesizeStreamingAsync(SpeechRequest request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public ValueTask<IReadOnlyList<VoiceDescriptor>> ListVoicesAsync(CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _calls);

            return ValueTask.FromResult<IReadOnlyList<VoiceDescriptor>>([]);
        }
    }

    /// <summary>A host's synthesizer that also voices dialogue.</summary>
    private sealed class DialogueSynthesizer : ISpeechSynthesizer, ISpeechDialogueSynthesizer
    {
        public string ProviderName => "contoso-dialogue";

        public int MaxCharactersPerRequest => 1000;

        public SpeechDialogueCapabilities DialogueCapabilities { get; } = new() { MaxVoices = 2, MaxCharactersPerRequest = 1000 };

        public ValueTask<SpeechAudio> SynthesizeAsync(SpeechRequest request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public IAsyncEnumerable<ReadOnlyMemory<byte>> SynthesizeStreamingAsync(SpeechRequest request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public ValueTask<IReadOnlyList<VoiceDescriptor>> ListVoicesAsync(CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public ValueTask<SpeechDialogueAudio> SynthesizeDialogueAsync(SpeechDialogueRequest request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }
}
