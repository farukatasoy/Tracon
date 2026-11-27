using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Shouldly;
using Tracon.Voice.UnitTests.Infrastructure;

namespace Tracon.Voice.UnitTests;

/// <summary>
/// The speech models the host configured are named in the diagnostics when no price covers them, and a call that finds no
/// price for its model says so once.
/// </summary>
/// <remarks>
/// 🚨 What this closes: a consumer measured two voice models unpriced (<c>scribe_v2</c>, <c>eleven_v4_turbo</c>) while 27 of 27
/// voice sessions recorded no cost. The report listed only catalog models, and a voice model is never in a catalog.
/// </remarks>
public sealed class VoiceMediaPricingDiagnosticsTests
{
    [Fact]
    public async Task The_configured_synthesis_and_transcription_models_are_named_when_no_price_covers_them()
    {
        await using var provider = Build(static options =>
        {
            options.SynthesisModelId = "eleven_v4_turbo";
            options.TranscriptionModelId = "scribe_v2";
        });

        (await UnpricedAsync(provider)).ShouldBe(
        [
            "elevenlabs/eleven_v4_turbo (speech synthesis)",
            "elevenlabs/scribe_v2 (transcription)",
        ]);
    }

    [Fact]
    public async Task A_priced_model_is_not_named_and_an_unpriced_one_beside_it_is()
    {
        await using var provider = Build(
            static options =>
            {
                options.SynthesisModelId = "eleven_v4_turbo";
                options.TranscriptionModelId = "scribe_v2";
            },
            static pricing => pricing.Voice["elevenlabs"] = new Dictionary<string, VoicePriceOverride>(StringComparer.OrdinalIgnoreCase)
            {
                ["eleven_v4_turbo"] = new() { PerMillionCharacters = 30m },
            });

        (await UnpricedAsync(provider)).ShouldBe(["elevenlabs/scribe_v2 (transcription)"]);
    }

    [Fact]
    public async Task A_model_priced_for_the_other_unit_is_still_unpriced_for_this_one()
    {
        // A per-minute rate prices a transcription, not a synthesis: the synthesis would be recorded without a cost.
        await using var provider = Build(
            static options =>
            {
                options.SynthesisModelId = "eleven_v4_turbo";
                options.TranscriptionModelId = "scribe_v2";
            },
            static pricing => pricing.Voice["elevenlabs"] = new Dictionary<string, VoicePriceOverride>(StringComparer.OrdinalIgnoreCase)
            {
                ["eleven_v4_turbo"] = new() { PerMinute = 0.3m },
                ["scribe_v2"] = new() { PerMinute = 0.4m },
            });

        (await UnpricedAsync(provider)).ShouldBe(["elevenlabs/eleven_v4_turbo (speech synthesis)"]);
    }

    [Fact]
    public async Task The_built_in_clients_own_default_models_are_named_when_the_host_sets_none()
    {
        // The model a call is priced under is the one the client reports; with none configured that is the client's default.
        await using var provider = Build(static _ => { });

        (await UnpricedAsync(provider)).ShouldBe(
        [
            $"elevenlabs/{ElevenLabsSpeechClient.DefaultSynthesisModel} (speech synthesis)",
            $"elevenlabs/{ElevenLabsSpeechClient.DefaultTranscriptionModel} (transcription)",
        ]);
    }

    [Fact]
    public async Task A_consumers_own_provider_is_named_by_its_own_provider_name_not_by_the_provider_setting()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ISpeechSynthesizer, ContosoSpeechProvider>();
        services.AddSingleton<ISpeechTranscriber, ContosoSpeechProvider>();
        services.AddTracon().UseVoice(static options =>
        {
            options.SynthesisModelId = "contoso-tts-1";
            options.TranscriptionModelId = "contoso-stt-1";
        });

        await using var provider = services.BuildServiceProvider();

        (await UnpricedAsync(provider)).ShouldBe(
        [
            "contoso/contoso-tts-1 (speech synthesis)",
            "contoso/contoso-stt-1 (transcription)",
        ]);
    }

    [Fact]
    public async Task A_consumers_own_provider_with_no_model_configured_names_no_model()
    {
        // The default model belongs to the built-in client only. A provider of the host's own that names none has nothing to
        // name, and borrowing the built-in client's default would blame it for a model it never runs.
        var services = new ServiceCollection();
        services.AddSingleton<ISpeechSynthesizer, ContosoSpeechProvider>();
        services.AddSingleton<ISpeechTranscriber, ContosoSpeechProvider>();
        services.AddTracon().UseVoice(static _ => { });

        await using var provider = services.BuildServiceProvider();

        (await UnpricedAsync(provider)).ShouldBeEmpty();
    }

    [Fact]
    public async Task A_synthesis_and_a_transcription_with_no_price_are_each_warned_about_once()
    {
        var logs = new RecordingLoggerProvider();
        await using var provider = Build(
            static options =>
            {
                options.SynthesisModelId = "eleven_v4_turbo";
                options.TranscriptionModelId = "scribe_v2";
            },
            configureLogging: logging => logging.AddProvider(logs));
        var pricing = provider.GetRequiredService<IVoicePricingReader>();

        pricing.ForSynthesis("elevenlabs", "eleven_v4_turbo", 100m).ShouldBeNull();
        pricing.ForSynthesis("elevenlabs", "eleven_v4_turbo", 200m).ShouldBeNull();
        pricing.ForTranscription("elevenlabs", "scribe_v2", TimeSpan.FromMinutes(1)).ShouldBeNull();
        pricing.ForTranscription("elevenlabs", "scribe_v2", TimeSpan.FromMinutes(2)).ShouldBeNull();

        var warnings = logs.Entries.Where(static entry => entry.StartsWith("Warning", StringComparison.Ordinal)
                                                          && entry.Contains("has no price in Tracon:Pricing", StringComparison.Ordinal))
            .ToList();

        warnings.Count.ShouldBe(2);
        warnings.ShouldContain(static entry => entry.Contains("eleven_v4_turbo", StringComparison.Ordinal)
                                               && entry.Contains("PerMillionCharacters", StringComparison.Ordinal));
        warnings.ShouldContain(static entry => entry.Contains("scribe_v2", StringComparison.Ordinal)
                                               && entry.Contains("PerMinute", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_priced_synthesis_and_transcription_say_nothing()
    {
        var logs = new RecordingLoggerProvider();
        await using var provider = Build(
            static options =>
            {
                options.SynthesisModelId = "eleven_v4_turbo";
                options.TranscriptionModelId = "scribe_v2";
            },
            static pricing => pricing.Voice["elevenlabs"] = new Dictionary<string, VoicePriceOverride>(StringComparer.OrdinalIgnoreCase)
            {
                ["eleven_v4_turbo"] = new() { PerMillionCharacters = 30m },
                ["scribe_v2"] = new() { PerMinute = 0.4m },
            },
            logging => logging.AddProvider(logs));
        var pricing = provider.GetRequiredService<IVoicePricingReader>();

        pricing.ForSynthesis("elevenlabs", "eleven_v4_turbo", 1_000_000m).ShouldBe(30m);
        pricing.ForTranscription("elevenlabs", "scribe_v2", TimeSpan.FromMinutes(1)).ShouldBe(0.4m);

        logs.Entries.ShouldNotContain(static entry => entry.Contains("has no price in Tracon:Pricing", StringComparison.Ordinal));
    }

    /// <summary>The unpriced configured media models the diagnostics report names.</summary>
    private static async Task<IReadOnlyList<string>> UnpricedAsync(ServiceProvider provider)
    {
        var report = await provider.GetRequiredService<TraconDiagnosticsCollector>().CollectAsync(TestContext.Current.CancellationToken);

        return report.Pricing.UnpricedMediaModels;
    }

    private static ServiceProvider Build(
        Action<VoiceOptions> configureVoice,
        Action<TraconPricingOptions>? configurePricing = null,
        Action<ILoggingBuilder>? configureLogging = null)
    {
        var services = new ServiceCollection();

        services.AddLogging(logging =>
        {
            logging.SetMinimumLevel(LogLevel.Trace);
            configureLogging?.Invoke(logging);
        });
        services.AddTracon().UseVoice(options =>
        {
            options.ApiKey = "k";
            configureVoice(options);
        });

        if (configurePricing is not null)
        {
            services.Configure<TraconOptions>(options => configurePricing(options.Pricing));
        }

        var provider = services.BuildServiceProvider();
        _ = provider.GetRequiredService<IOptions<TraconOptions>>().Value;

        return provider;
    }

    /// <summary>A consumer's own provider: one class, both contracts.</summary>
    private sealed class ContosoSpeechProvider : ISpeechSynthesizer, ISpeechTranscriber
    {
        public string ProviderName => "contoso";

        public int MaxCharactersPerRequest => 5000;

        public ValueTask<SpeechAudio> SynthesizeAsync(SpeechRequest request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public IAsyncEnumerable<ReadOnlyMemory<byte>> SynthesizeStreamingAsync(
            SpeechRequest request,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public ValueTask<IReadOnlyList<VoiceDescriptor>> ListVoicesAsync(CancellationToken cancellationToken = default)
            => ValueTask.FromResult<IReadOnlyList<VoiceDescriptor>>([]);

        public ValueTask<SpeechTranscript> TranscribeAsync(
            Stream audio,
            string mediaType,
            SpeechTranscriptionOptions? options = null,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }
}
