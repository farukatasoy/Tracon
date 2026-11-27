using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shouldly;

namespace Tracon.Voice.UnitTests;

/// <summary>
/// A second ElevenLabs synthesizer has a concurrency limit of its own, so a batch that voices a podcast does not take the slots a
/// live conversation's turn waits for.
/// </summary>
/// <remarks>
/// The built-in client holds one limit for synthesis, dialogue and transcription. The cases hold that the named client is a client
/// of its own (its own pool), that it is the same provider to everything that decides by provider, and that it changes nothing of
/// the default.
/// </remarks>
public sealed class ElevenLabsNamedSynthesizerTests
{
    [Fact]
    public async Task The_named_synthesizer_is_a_client_of_its_own_and_the_default_is_untouched()
    {
        using var provider = Build(static voice => voice.AddElevenLabsSynthesizer("batch", static options => options.MaxConcurrentRequests = 4));

        var resolver = provider.GetRequiredService<ISpeechProviderResolver>();
        var batch = await resolver.GetSynthesizerAsync("batch", TestContext.Current.CancellationToken);
        var defaultOne = await resolver.GetSynthesizerAsync(name: null, TestContext.Current.CancellationToken);

        batch.ShouldNotBeSameAs(defaultOne, "one client would hold one pool, which is what the second one is for");
        batch.ProviderName.ShouldBe("elevenlabs", "the tenant's policy and key, and the price, are the provider's, not the name's");
        defaultOne.ShouldBeSameAs(provider.GetRequiredService<ISpeechSynthesizer>());

        provider.GetRequiredService<IOptions<VoiceOptions>>().Value.MaxConcurrentRequests
            .ShouldBe(2, "the change is the named client's and never the options of UseVoice");

        (await resolver.GetSynthesizerAsync("batch", TestContext.Current.CancellationToken)).ShouldBeSameAs(batch);
    }

    [Fact]
    public async Task The_named_synthesizer_starts_from_the_options_of_UseVoice()
    {
        using var provider = Build(static voice => voice.AddElevenLabsSynthesizer("batch"));

        var batch = await provider.GetRequiredService<ISpeechProviderResolver>().GetSynthesizerAsync("batch", TestContext.Current.CancellationToken);

        // The limit of a request is part of what the copy carries.
        batch.MaxCharactersPerRequest.ShouldBe(5000);
    }

    [Fact]
    public async Task Without_UseVoice_the_first_call_for_the_name_says_what_is_missing()
    {
        var services = new ServiceCollection();
        services.AddTracon().AddElevenLabsSynthesizer("batch");

        using var provider = services.BuildServiceProvider();

        var failure = await Should.ThrowAsync<TraconException>(
            async () => await provider.GetRequiredService<ISpeechProviderResolver>().GetSynthesizerAsync("batch", TestContext.Current.CancellationToken));

        failure.Message.ShouldContain("UseVoice", Case.Sensitive);
    }

    [Fact]
    public async Task Options_the_named_client_may_not_have_fail_at_the_first_call_and_name_the_option()
    {
        using var provider = Build(static voice => voice.AddElevenLabsSynthesizer("batch", static options => options.MaxConcurrentRequests = 0));

        var failure = await Should.ThrowAsync<TraconException>(
            async () => await provider.GetRequiredService<ISpeechProviderResolver>().GetSynthesizerAsync("batch", TestContext.Current.CancellationToken));

        failure.Message.ShouldContain("concurrent request limit", Case.Sensitive);
        failure.Message.ShouldContain("batch", Case.Sensitive);
    }

    [Fact]
    public void A_second_registration_of_the_same_name_is_refused()
    {
        var services = new ServiceCollection();

        Should.Throw<InvalidOperationException>(
            () => services.AddTracon()
                .UseVoice(static options => options.ApiKey = "k")
                .AddElevenLabsSynthesizer("batch")
                .AddElevenLabsSynthesizer("batch"));
    }

    [Fact]
    public async Task A_named_synthesizer_beside_your_own_default_takes_its_key_from_the_configuration_key_it_names()
    {
        // The host's own pair is registered first, so UseVoice asks for no key. The named synthesizer is always the built-in client:
        // it asks for a key of its own, a value or the name of a configuration key that holds one.
        var services = new ServiceCollection();
        var own = new OwnSpeechProvider();

        services.AddSingleton<ISpeechSynthesizer>(own);
        services.AddSingleton<ISpeechTranscriber>(own);
        services.AddSingleton<IConfiguration>(Configuration(("Voice:BatchKey", "batch-key-value")));

        services.AddTracon()
            .UseVoice(static options => options.DefaultVoiceId = "voice-1")
            .AddElevenLabsSynthesizer("batch", static options => options.ApiKeyConfigurationName = "Voice:BatchKey");

        await using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IStartupValidator>().Validate();

        var resolver = provider.GetRequiredService<ISpeechProviderResolver>();
        var batch = await resolver.GetSynthesizerAsync("batch", TestContext.Current.CancellationToken);
        var defaultOne = await resolver.GetSynthesizerAsync(name: null, TestContext.Current.CancellationToken);

        batch.ProviderName.ShouldBe("elevenlabs");
        batch.ShouldBeOfType<ElevenLabsSpeechClient>();
        defaultOne.ShouldBeSameAs(own, "the default stays the host's own whatever the named synthesizer is");
    }

    [Fact]
    public async Task A_named_synthesizer_whose_configuration_key_holds_no_key_says_which_key_is_empty_and_does_not_send_you_to_register_first()
    {
        var services = new ServiceCollection();
        var own = new OwnSpeechProvider();

        services.AddSingleton<ISpeechSynthesizer>(own);
        services.AddSingleton<ISpeechTranscriber>(own);
        services.AddSingleton<IConfiguration>(Configuration(("Voice:SomethingElse", "x")));

        services.AddTracon()
            .UseVoice(static options => options.DefaultVoiceId = "voice-1")
            .AddElevenLabsSynthesizer("batch", static options => options.ApiKeyConfigurationName = "Voice:BatchKey");

        await using var provider = services.BuildServiceProvider();

        // The host starts: the named synthesizer is not built until it is asked for.
        provider.GetRequiredService<IStartupValidator>().Validate();

        var failure = await Should.ThrowAsync<TraconException>(
            async () => await provider.GetRequiredService<ISpeechProviderResolver>().GetSynthesizerAsync("batch", TestContext.Current.CancellationToken));

        failure.Message.ShouldContain("'batch'", Case.Sensitive);
        failure.Message.ShouldContain("'Voice:BatchKey'", Case.Sensitive, "the configuration key that is empty is named");
        failure.Message.ShouldContain("ApiKey", Case.Sensitive);
        failure.Message.ShouldNotContain("BEFORE", Case.Sensitive, "registering your own provider first is no remedy for a named synthesizer, which is always the built-in client");
        failure.Message.ShouldNotContain("serves both", Case.Sensitive);
    }

    [Fact]
    public async Task A_named_synthesizer_whose_configuration_key_holds_an_unusable_key_names_the_key_and_never_shows_its_value()
    {
        // A line break inside the key cannot be sent in a header, so the value is refused. It is a secret: the failure says which
        // configuration key held it and shows no part of it.
        const string Head = "LEAK-CANARY-head";
        const string Tail = "tail-LEAK-CANARY";

        var services = new ServiceCollection();
        var own = new OwnSpeechProvider();

        services.AddSingleton<ISpeechSynthesizer>(own);
        services.AddSingleton<ISpeechTranscriber>(own);
        services.AddSingleton<IConfiguration>(Configuration(("Voice:BatchKey", Head + "\n" + Tail)));

        services.AddTracon()
            .UseVoice(static options => options.DefaultVoiceId = "voice-1")
            .AddElevenLabsSynthesizer("batch", static options => options.ApiKeyConfigurationName = "Voice:BatchKey");

        await using var provider = services.BuildServiceProvider();

        var failure = await Should.ThrowAsync<TraconException>(
            async () => await provider.GetRequiredService<ISpeechProviderResolver>().GetSynthesizerAsync("batch", TestContext.Current.CancellationToken));

        failure.Message.ShouldContain("'batch'", Case.Sensitive);
        failure.Message.ShouldContain("'Voice:BatchKey'", Case.Sensitive, "the configuration key that held the unusable value is named");
        failure.Message.ShouldNotContain("LEAK-CANARY", Case.Sensitive);
        failure.Message.ShouldNotContain(Head, Case.Sensitive);
        failure.Message.ShouldNotContain(Tail, Case.Sensitive);
        failure.ToString().ShouldNotContain("LEAK-CANARY", Case.Sensitive);
    }

    [Fact]
    public async Task A_named_synthesizer_with_no_key_at_all_says_it_needs_one_of_its_own()
    {
        var services = new ServiceCollection();
        var own = new OwnSpeechProvider();

        services.AddSingleton<ISpeechSynthesizer>(own);
        services.AddSingleton<ISpeechTranscriber>(own);

        services.AddTracon()
            .UseVoice(static options => options.DefaultVoiceId = "voice-1")
            .AddElevenLabsSynthesizer("batch");

        await using var provider = services.BuildServiceProvider();

        var failure = await Should.ThrowAsync<TraconException>(
            async () => await provider.GetRequiredService<ISpeechProviderResolver>().GetSynthesizerAsync("batch", TestContext.Current.CancellationToken));

        failure.Message.ShouldContain("'batch'", Case.Sensitive);
        failure.Message.ShouldContain("its own", Case.Sensitive);
        failure.Message.ShouldNotContain("BEFORE", Case.Sensitive);
    }

    [Fact]
    public async Task A_named_synthesizer_carries_its_own_repeat_limit_and_the_default_keeps_none()
    {
        var clock = new ManualClock();
        var services = new ServiceCollection();

        services.AddSingleton<TimeProvider>(clock);

        services.AddTracon()
            .UseVoice(static options => options.ApiKey = "k")
            .AddElevenLabsSynthesizer("batch", static options => options.MaxRetries = 3);

        await using var provider = services.BuildServiceProvider();

        var resolver = provider.GetRequiredService<ISpeechProviderResolver>();
        var batch = (ElevenLabsSpeechClient)await resolver.GetSynthesizerAsync("batch", TestContext.Current.CancellationToken);
        var defaultOne = (ElevenLabsSpeechClient)await resolver.GetSynthesizerAsync(name: null, TestContext.Current.CancellationToken);

        batch.MaxRetries.ShouldBe(3);
        defaultOne.MaxRetries.ShouldBe(0, "a batch's setting must not make a live conversation's request wait");
        provider.GetRequiredService<IOptions<VoiceOptions>>().Value.MaxRetries.ShouldBe(0);

        // The wait of a repeat runs on the clock the host registered, for the default client and the named one.
        batch.Clock.ShouldBeSameAs(clock);
        defaultOne.Clock.ShouldBeSameAs(clock);
    }

    [Fact]
    public async Task A_named_synthesizer_inherits_the_repeat_limit_of_UseVoice_unless_it_sets_its_own()
    {
        var services = new ServiceCollection();

        services.AddTracon()
            .UseVoice(static options =>
            {
                options.ApiKey = "k";
                options.MaxRetries = 2;
            })
            .AddElevenLabsSynthesizer("inherits")
            .AddElevenLabsSynthesizer("own", static options => options.MaxRetries = 5);

        await using var provider = services.BuildServiceProvider();

        var resolver = provider.GetRequiredService<ISpeechProviderResolver>();

        ((ElevenLabsSpeechClient)await resolver.GetSynthesizerAsync("inherits", TestContext.Current.CancellationToken)).MaxRetries.ShouldBe(2);
        ((ElevenLabsSpeechClient)await resolver.GetSynthesizerAsync("own", TestContext.Current.CancellationToken)).MaxRetries.ShouldBe(5);
    }

    [Fact]
    public async Task A_repeat_limit_out_of_range_fails_the_named_synthesizer_at_its_first_call_and_names_the_option()
    {
        using var provider = Build(static voice => voice.AddElevenLabsSynthesizer("batch", static options => options.MaxRetries = 11));

        var failure = await Should.ThrowAsync<TraconException>(
            async () => await provider.GetRequiredService<ISpeechProviderResolver>().GetSynthesizerAsync("batch", TestContext.Current.CancellationToken));

        failure.Message.ShouldContain("MaxRetries", Case.Sensitive);
        failure.Message.ShouldContain("batch", Case.Sensitive);
    }

    private static IConfiguration Configuration(params (string Key, string Value)[] values)
        => new ConfigurationBuilder()
            .AddInMemoryCollection(values.ToDictionary(static pair => pair.Key, static pair => (string?)pair.Value, StringComparer.Ordinal))
            .Build();

    /// <summary>A host's own provider: both contracts, never called by these cases.</summary>
    private sealed class OwnSpeechProvider : ISpeechSynthesizer, ISpeechTranscriber
    {
        public string ProviderName => "own";

        public int MaxCharactersPerRequest => 5000;

        public ValueTask<SpeechAudio> SynthesizeAsync(SpeechRequest request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public IAsyncEnumerable<ReadOnlyMemory<byte>> SynthesizeStreamingAsync(SpeechRequest request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public ValueTask<IReadOnlyList<VoiceDescriptor>> ListVoicesAsync(CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public ValueTask<SpeechTranscript> TranscribeAsync(
            Stream audio,
            string mediaType,
            SpeechTranscriptionOptions? options = null,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private static ServiceProvider Build(Action<ITraconBuilder> configure)
    {
        var services = new ServiceCollection();
        var builder = services.AddTracon().UseVoice(static options => options.ApiKey = "k");

        configure(builder);

        return services.BuildServiceProvider();
    }
}
