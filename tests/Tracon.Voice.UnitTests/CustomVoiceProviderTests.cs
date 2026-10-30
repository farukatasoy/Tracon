using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shouldly;

namespace Tracon.Voice.UnitTests;

/// <summary>
/// Verifies <c>UseVoice(...)</c> with a consumer's own speech provider, and the
/// startup validation of the settings.
/// </summary>
/// <remarks>
/// <para>
/// The site documents "register your implementation first; <c>TryAdd</c>
/// preserves it" with no API key. The validator still required the ElevenLabs
/// provider name and a key, so the first <c>IOptions&lt;VoiceOptions&gt;.Value</c>
/// read threw. Pricing, the <c>speak</c> limit and the health check also read
/// the ElevenLabs settings instead of the bound implementation.
/// </para>
/// <para>
/// The tests compose the real <c>AddTracon().UseVoice(...)</c> registration and
/// read <see cref="IStartupValidator"/>: the defect lived in the DI composition,
/// which a validator called by hand does not see.
/// </para>
/// </remarks>
public sealed class CustomVoiceProviderTests
{
    private const string ProviderName = "contoso";

    [Fact]
    public void Built_in_client_with_an_unstorable_format_fails_at_startup()
    {
        var services = new ServiceCollection();
        services.AddTracon().UseVoice(options =>
        {
            options.ApiKey = "k";
            options.OutputFormat = "pcm_16000";
        });

        using var provider = services.BuildServiceProvider();

        Should.Throw<OptionsValidationException>(() => provider.GetRequiredService<IStartupValidator>().Validate())
            .Message.ShouldContain("pcm_16000");
    }

    [Fact]
    public void Built_in_client_without_a_key_fails_at_startup()
    {
        var services = new ServiceCollection();
        services.AddTracon().UseVoice(options => options.DefaultVoiceId = "voice-1");

        using var provider = services.BuildServiceProvider();

        Should.Throw<OptionsValidationException>(() => provider.GetRequiredService<IStartupValidator>().Validate())
            .Message.ShouldContain("user-secrets");
    }

    [Fact]
    public void The_documented_custom_provider_example_starts_without_a_key()
    {
        // The example on the site's voice guide ("Use your own speech provider"),
        // as written. Keep the two in step.
        var services = new ServiceCollection();
        services.AddSingleton<ISpeechSynthesizer, ContosoSpeechProvider>();
        services.AddSingleton<ISpeechTranscriber, ContosoSpeechProvider>();

        services.AddTracon().UseVoice(options =>
        {
            options.DefaultVoiceId = "customer-care";
            options.SynthesisModelId = "contoso-tts-1";
        });

        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IStartupValidator>().Validate();
        provider.GetRequiredService<IOptions<VoiceOptions>>().Value.DefaultVoiceId.ShouldBe("customer-care");
    }

    [Fact]
    public void A_provider_registered_AFTER_UseVoice_fails_at_startup_and_says_to_register_it_first()
    {
        // UseVoice can only see what was registered before it. A later
        // registration replaces the built-in client in resolution but not in
        // validation; the startup message must name the fix.
        var services = new ServiceCollection();
        services.AddTracon().UseVoice(options => options.DefaultVoiceId = "customer-care");
        services.AddSingleton<ISpeechSynthesizer, ContosoSpeechProvider>();
        services.AddSingleton<ISpeechTranscriber, ContosoSpeechProvider>();

        using var provider = services.BuildServiceProvider();

        Should.Throw<OptionsValidationException>(() => provider.GetRequiredService<IStartupValidator>().Validate())
            .Message.ShouldContain("BEFORE calling UseVoice");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task A_named_synthesizer_does_not_change_which_provider_UseVoice_binds(bool namedFirst)
    {
        // A named registration is keyed: it must neither stop UseVoice's TryAdd nor
        // count as "the consumer bound its own provider". If it did, the built-in
        // client would lose its startup checks in one registration order only.
        var named = new ContosoSpeechProvider();
        var services = new ServiceCollection();
        var builder = services.AddTracon();

        if (namedFirst)
        {
            builder.AddSpeechSynthesizer("contoso", _ => named);
        }

        builder.UseVoice(static options => options.DefaultVoiceId = "voice-1");

        if (!namedFirst)
        {
            builder.AddSpeechSynthesizer("contoso", _ => named);
        }

        await using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<VoiceProviderBinding>().UsesBuiltInClient.ShouldBeTrue();

        // Without a key the built-in client still fails at startup, in both orders.
        Should.Throw<OptionsValidationException>(() => provider.GetRequiredService<IStartupValidator>().Validate())
            .Message.ShouldContain("user-secrets");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task The_default_and_a_named_synthesizer_resolve_to_their_own_instances(bool namedFirst)
    {
        var named = new ContosoSpeechProvider();
        var services = new ServiceCollection();
        var builder = services.AddTracon();

        if (namedFirst)
        {
            builder.AddSpeechSynthesizer("contoso", _ => named);
        }

        builder.UseVoice(static options => options.ApiKey = "k");

        if (!namedFirst)
        {
            builder.AddSpeechSynthesizer("contoso", _ => named);
        }

        await using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IStartupValidator>().Validate();

        var resolver = provider.GetRequiredService<ISpeechProviderResolver>();

        (await resolver.GetSynthesizerAsync(cancellationToken: TestContext.Current.CancellationToken))
            .ShouldBeOfType<ElevenLabsSpeechClient>();
        (await resolver.GetSynthesizerAsync("contoso", TestContext.Current.CancellationToken))
            .ShouldBeSameAs(named);
    }

    [Fact]
    public void Synthesis_price_is_keyed_by_the_bound_synthesizers_provider_name()
    {
        // The key only keeps the pre-fix code from failing on validation, so the
        // assertion below isolates the price key.
        using var provider = BuildCustomSynthesizerProvider(
            new ContosoSpeechProvider(),
            options =>
            {
                options.ApiKey = "k";
                options.SynthesisModelId = "contoso-tts-1";
            });

        provider.GetRequiredService<IVoicePricingReader>().ForCharacters(1_000_000m).ShouldBe(30m);
    }

    [Fact]
    public async Task Speak_applies_the_bound_synthesizers_character_limit()
    {
        // The contract says the tool AND the HTTP endpoint read
        // ISpeechSynthesizer.MaxCharactersPerRequest. The setting stays at its
        // default (5,000) to prove the tool does not read it.
        using var provider = BuildCustomSynthesizerProvider(
            new ContosoSpeechProvider { MaxCharactersPerRequest = 10 },
            options => options.ApiKey = "k");

        var tool = new SpeakTool(provider);
        var arguments = new AIFunctionArguments(
            new Dictionary<string, object?>(StringComparer.Ordinal) { ["text"] = "twenty characters!!!" },
            StringComparer.Ordinal);

        var exception = await Should.ThrowAsync<TraconException>(
            async () => await tool.InvokeAsync(arguments, TestContext.Current.CancellationToken));

        exception.Message.ShouldContain("the limit is 10");
    }

    [Fact]
    public async Task Health_check_reports_the_custom_providers_name()
    {
        // The key and the closed loopback address only keep the pre-fix code
        // from failing on validation or reaching the network.
        var services = new ServiceCollection();
        services.AddSingleton<ISpeechSynthesizer, ContosoSpeechProvider>();
        services.AddSingleton<ISpeechTranscriber, ContosoSpeechProvider>();
        services.AddTracon().UseVoice(options =>
        {
            options.ApiKey = "k";
            options.Endpoint = new Uri("http://127.0.0.1:1/");
        });

        using var provider = services.BuildServiceProvider();

        var health = await provider.GetRequiredService<IVoiceHealthCheck>()
            .CheckHealthAsync(TestContext.Current.CancellationToken);

        health.ProviderName.ShouldBe(ProviderName);
        health.IsHealthy.ShouldBeTrue();
        health.VoiceCount.ShouldBe(1);
    }

    [Fact]
    public void Transcription_price_is_keyed_by_the_bound_transcribers_provider_name()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ISpeechSynthesizer, ContosoSpeechProvider>();
        services.AddSingleton<ISpeechTranscriber, ContosoSpeechProvider>();
        services.AddTracon().UseVoice(options => options.TranscriptionModelId = "contoso-stt-1");
        services.Configure<TraconOptions>(options =>
            options.Pricing.Voice[ProviderName] = new Dictionary<string, VoicePriceOverride>(StringComparer.OrdinalIgnoreCase)
            {
                ["contoso-stt-1"] = new() { PerMinute = 0.5m },
            });

        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<VoicePricing>()
            .ForTranscription(provider.GetRequiredService<ISpeechTranscriber>().ProviderName, modelId: null, TimeSpan.FromMinutes(2))
            .ShouldBe(1m);
    }

    [Fact]
    public void A_custom_provider_without_a_model_does_NOT_borrow_the_ElevenLabs_default_price()
    {
        // Tracon never invents a price: the built-in default model is the
        // built-in client's, not the consumer's.
        var services = new ServiceCollection();
        services.AddSingleton<ISpeechSynthesizer, ContosoSpeechProvider>();
        services.AddSingleton<ISpeechTranscriber, ContosoSpeechProvider>();
        services.AddTracon().UseVoice(static _ => { });
        services.Configure<TraconOptions>(options =>
            options.Pricing.Voice[ProviderName] = new Dictionary<string, VoicePriceOverride>(StringComparer.OrdinalIgnoreCase)
            {
                [ElevenLabsSpeechClient.DefaultSynthesisModel] = new() { PerMillionCharacters = 30m },
            });

        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IVoicePricingReader>().ForCharacters(1_000_000m).ShouldBeNull();
    }

    [Fact]
    public void The_built_in_client_without_a_model_is_priced_under_its_default_model()
    {
        var services = new ServiceCollection();
        services.AddTracon().UseVoice(static options => options.ApiKey = "k");
        services.Configure<TraconOptions>(options =>
            options.Pricing.Voice[VoiceProviderNames.ElevenLabs] = new Dictionary<string, VoicePriceOverride>(StringComparer.OrdinalIgnoreCase)
            {
                [ElevenLabsSpeechClient.DefaultSynthesisModel] = new() { PerMillionCharacters = 30m },
            });

        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IVoicePricingReader>().ForCharacters(1_000_000m).ShouldBe(30m);
    }

    [Fact]
    public void A_synthesizer_with_its_own_health_check_is_used_as_is()
    {
        var synthesizer = new SelfCheckingSynthesizer();
        var services = new ServiceCollection();
        services.AddSingleton<ISpeechSynthesizer>(synthesizer);
        services.AddSingleton<ISpeechTranscriber, ContosoSpeechProvider>();
        services.AddTracon().UseVoice(static _ => { });

        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IVoiceHealthCheck>().ShouldBeSameAs(synthesizer);
    }

    [Fact]
    public async Task A_failing_voice_list_is_unhealthy_and_carries_only_the_exception_type()
    {
        var check = new SynthesizerVoiceHealthCheck(new FailingListSynthesizer());

        var health = await check.CheckHealthAsync(TestContext.Current.CancellationToken);

        health.IsHealthy.ShouldBeFalse();
        health.ProviderName.ShouldBe(ProviderName);
        health.Detail.ShouldNotBeNull().ShouldContain(nameof(HttpRequestException));
        health.Detail.ShouldNotContain("secret-host");
    }

    private static ServiceProvider BuildCustomSynthesizerProvider(
        ContosoSpeechProvider synthesizer,
        Action<VoiceOptions> configure)
    {
        var services = new ServiceCollection();
        services.AddSingleton<ISpeechSynthesizer>(synthesizer);
        services.AddTracon().UseVoice(configure);
        services.Configure<TraconOptions>(options =>
            options.Pricing.Voice[ProviderName] = new Dictionary<string, VoicePriceOverride>(StringComparer.OrdinalIgnoreCase)
            {
                ["contoso-tts-1"] = new() { PerMillionCharacters = 30m },
            });

        return services.BuildServiceProvider();
    }

    /// <summary>A consumer's own provider: one class, both contracts.</summary>
    private sealed class ContosoSpeechProvider : ISpeechSynthesizer, ISpeechTranscriber
    {
        public string ProviderName => CustomVoiceProviderTests.ProviderName;

        public int MaxCharactersPerRequest { get; init; } = 5000;

        public ValueTask<SpeechAudio> SynthesizeAsync(
            SpeechRequest request,
            CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("A request over the limit should never reach the provider.");

        public IAsyncEnumerable<ReadOnlyMemory<byte>> SynthesizeStreamingAsync(
            SpeechRequest request,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public ValueTask<IReadOnlyList<VoiceDescriptor>> ListVoicesAsync(
            CancellationToken cancellationToken = default)
            => ValueTask.FromResult<IReadOnlyList<VoiceDescriptor>>(
                [new VoiceDescriptor { VoiceId = "customer-care", Name = "Customer care" }]);

        public ValueTask<SpeechTranscript> TranscribeAsync(
            Stream audio,
            string mediaType,
            SpeechTranscriptionOptions? options = null,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    /// <summary>A synthesizer that brings its own health check.</summary>
    private sealed class SelfCheckingSynthesizer : ISpeechSynthesizer, IVoiceHealthCheck
    {
        public string ProviderName => CustomVoiceProviderTests.ProviderName;

        public int MaxCharactersPerRequest => 5000;

        public ValueTask<SpeechAudio> SynthesizeAsync(
            SpeechRequest request,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public IAsyncEnumerable<ReadOnlyMemory<byte>> SynthesizeStreamingAsync(
            SpeechRequest request,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public ValueTask<IReadOnlyList<VoiceDescriptor>> ListVoicesAsync(
            CancellationToken cancellationToken = default)
            => ValueTask.FromResult<IReadOnlyList<VoiceDescriptor>>([]);

        public ValueTask<VoiceHealth> CheckHealthAsync(CancellationToken cancellationToken = default)
            => ValueTask.FromResult(new VoiceHealth
            {
                ProviderName = ProviderName,
                IsHealthy = true,
                Latency = TimeSpan.Zero,
                CheckedAt = DateTimeOffset.UnixEpoch,
            });
    }

    /// <summary>A synthesizer whose voice list fails with an address in the message.</summary>
    private sealed class FailingListSynthesizer : ISpeechSynthesizer
    {
        public string ProviderName => CustomVoiceProviderTests.ProviderName;

        public int MaxCharactersPerRequest => 5000;

        public ValueTask<SpeechAudio> SynthesizeAsync(
            SpeechRequest request,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public IAsyncEnumerable<ReadOnlyMemory<byte>> SynthesizeStreamingAsync(
            SpeechRequest request,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public ValueTask<IReadOnlyList<VoiceDescriptor>> ListVoicesAsync(
            CancellationToken cancellationToken = default)
            => throw new HttpRequestException("Connection refused (secret-host:443)");
    }
}
