using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Tracon.Core.UnitTests.Fakes;

namespace Tracon.Core.UnitTests.Models;

/// <summary>
/// A speech, transcription, live voice or image call that finds no price says so once for each model and kind of call.
/// </summary>
/// <remarks>
/// 🚨 What this closes: the lookups behind these four calls returned <see langword="null"/> without a word, and the models
/// are never in a provider's catalog, so the startup warning could not name them either. A consumer's measurement: two voice
/// models unpriced while 27 of 27 sessions recorded no cost, and nothing in the log said why.
/// </remarks>
public sealed class UnpricedMediaWarningsTests
{
    [Fact]
    public void A_model_with_no_price_is_warned_about_once_for_each_kind_of_call()
    {
        using var harness = new Harness();

        harness.Warnings.WarnOnce(MediaPriceKind.SpeechSynthesis, "elevenlabs", "eleven_v4_turbo");
        harness.Warnings.WarnOnce(MediaPriceKind.SpeechSynthesis, "elevenlabs", "eleven_v4_turbo");

        var line = harness.Logs.Warnings.ShouldHaveSingleItem();

        line.ShouldContain("eleven_v4_turbo");
        line.ShouldContain("elevenlabs");
        line.ShouldContain("speech synthesis");
        line.ShouldContain("Tracon:Pricing:Voice:elevenlabs:eleven_v4_turbo:PerMillionCharacters", customMessage: "the line must name the key to set");
    }

    [Fact]
    public void The_same_model_under_two_kinds_of_call_is_two_lines()
    {
        using var harness = new Harness();

        harness.Warnings.WarnOnce(MediaPriceKind.SpeechSynthesis, "studio", "studio-1");
        harness.Warnings.WarnOnce(MediaPriceKind.Transcription, "studio", "studio-1");
        harness.Warnings.WarnOnce(MediaPriceKind.LiveVoice, "studio", "studio-1");
        harness.Warnings.WarnOnce(MediaPriceKind.Image, "studio", "studio-1");

        harness.Logs.Warnings.Count.ShouldBe(4);
    }

    [Theory]
    [InlineData(null, "model")]
    [InlineData("", "model")]
    [InlineData("provider", null)]
    [InlineData("provider", "")]
    public void A_lookup_that_names_no_provider_or_no_model_says_nothing(string? provider, string? model)
    {
        using var harness = new Harness();

        harness.Warnings.WarnOnce(MediaPriceKind.SpeechSynthesis, provider, model);

        harness.Logs.Warnings.ShouldBeEmpty();
    }

    [Fact]
    public async Task Callers_that_ask_at_the_same_moment_write_one_line()
    {
        // 🚨 A sequential test cannot show that the guard works: it passes with the guard removed. Hold every caller at a
        // barrier and release them together, so the only thing between them and a second line is the set itself.
        const int Callers = 16;

        using var harness = new Harness();
        using var barrier = new Barrier(Callers);

        var tasks = Enumerable.Range(0, Callers).Select(_ => Task.Factory.StartNew(
            () =>
            {
                barrier.SignalAndWait(TestContext.Current.CancellationToken);
                harness.Warnings.WarnOnce(MediaPriceKind.Transcription, "elevenlabs", "scribe_v2");
            },
            TestContext.Current.CancellationToken,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default));

        await Task.WhenAll(tasks);

        harness.Logs.Warnings.ShouldHaveSingleItem();
    }

    [Fact]
    public void The_set_of_lines_already_written_is_bounded()
    {
        // A tenant that brings its own provider credentials chooses the model name, so the names are unbounded.
        using var harness = new Harness();

        for (var index = 0; index < 400; index++)
        {
            harness.Warnings.WarnOnce(MediaPriceKind.Image, "studio", $"model-{index}");
        }

        harness.Logs.Warnings.Count.ShouldBe(256);
    }

    [Fact]
    public void A_logger_that_drops_warnings_costs_the_lookup_nothing()
    {
        var warnings = new UnpricedMediaWarnings(new DisabledLogger());

        Should.NotThrow(() => warnings.WarnOnce(MediaPriceKind.Transcription, "elevenlabs", "scribe_v2"));
    }

    [Fact]
    public void An_image_model_with_no_price_is_warned_about_once_when_it_is_measured()
    {
        using var container = Container(static _ => { }, out var logs);
        var pricing = container.GetRequiredService<ImagePricing>();

        var first = pricing.Measure("openai", "gpt-image-1", "1024x1024", imageCount: 1, outputTokens: null);
        pricing.Measure("openai", "gpt-image-1", "1024x1024", imageCount: 1, outputTokens: null);

        first.Cost.ShouldBeNull();

        var line = logs.Warnings.ShouldHaveSingleItem();
        line.ShouldContain("gpt-image-1");
        line.ShouldContain("image generation");
        line.ShouldContain("Tracon:Pricing:Images:openai:gpt-image-1:PerImage");
    }

    [Fact]
    public void An_image_model_with_a_price_says_nothing()
    {
        using var container = Container(
            static options => options.Pricing.Images["openai"] = new Dictionary<string, ImagePriceOverride>(StringComparer.OrdinalIgnoreCase)
            {
                ["gpt-image-1"] = new() { PerImage = 0.04m },
            },
            out var logs);

        container.GetRequiredService<ImagePricing>().Measure("openai", "gpt-image-1", null, imageCount: 1, outputTokens: null);

        logs.Warnings.ShouldBeEmpty();
    }

    [Fact]
    public void An_image_model_with_a_token_rate_and_no_token_count_is_priced_and_says_nothing()
    {
        // The rate is configured; this one call has nothing to multiply it with. That is a missing measurement, not a missing
        // price, and a line that said otherwise would send the operator to the wrong key.
        using var container = Container(
            static options => options.Pricing.Images["openai"] = new Dictionary<string, ImagePriceOverride>(StringComparer.OrdinalIgnoreCase)
            {
                ["gpt-image-1"] = new() { OutputCostPerMillionTokens = 10m },
            },
            out var logs);

        container.GetRequiredService<ImagePricing>().Measure("openai", "gpt-image-1", null, imageCount: 1, outputTokens: null);

        logs.Warnings.ShouldBeEmpty();
    }

    [Fact]
    public void A_live_voice_model_with_no_price_is_warned_about_once_when_its_duration_is_priced()
    {
        using var container = Container(static _ => { }, out var logs);
        var pricing = container.GetRequiredService<VoiceDurationPricing>();

        pricing.Price("openai", "gpt-live-1", 28m).ShouldBeNull();
        pricing.Price("openai", "gpt-live-1", 31m).ShouldBeNull();

        var line = logs.Warnings.ShouldHaveSingleItem();
        line.ShouldContain("live voice");
        line.ShouldContain("Tracon:Pricing:Voice:openai:gpt-live-1:PerMinute");
    }

    [Fact]
    public void A_live_voice_session_with_no_duration_says_nothing()
    {
        // No duration is a measurement the provider did not report, not a missing price.
        using var container = Container(static _ => { }, out var logs);

        container.GetRequiredService<VoiceDurationPricing>().Price("openai", "gpt-live-1", seconds: null).ShouldBeNull();

        logs.Warnings.ShouldBeEmpty();
    }

    [Fact]
    public void A_synthesis_with_no_price_is_warned_about_once_and_a_priced_one_is_not()
    {
        using var logs = new WarningLogProvider();
        using var factory = LoggerFactory.Create(builder => builder.AddProvider(logs));
        var warnings = new UnpricedMediaWarnings(factory.CreateLogger<UnpricedMediaWarnings>());

        var pricing = new TraconPricingOptions();
        pricing.Voice["elevenlabs"] = new Dictionary<string, VoicePriceOverride>(StringComparer.OrdinalIgnoreCase)
        {
            ["eleven_flash_v2_5"] = new() { PerMillionCharacters = 30m },
            ["eleven_minutes_only"] = new() { PerMinute = 0.4m },
        };

        VoiceCharacterPricing.Price(pricing, "elevenlabs", "eleven_flash_v2_5", 1_000_000m, warnings).ShouldBe(30m);
        logs.Warnings.ShouldBeEmpty();

        VoiceCharacterPricing.Price(pricing, "elevenlabs", "eleven_v4_turbo", 10m, warnings).ShouldBeNull();
        VoiceCharacterPricing.Price(pricing, "elevenlabs", "eleven_v4_turbo", 10m, warnings).ShouldBeNull();

        // A model that carries only the per-minute rate is not priced for synthesis: its synthesis is recorded without a cost.
        VoiceCharacterPricing.Price(pricing, "elevenlabs", "eleven_minutes_only", 10m, warnings).ShouldBeNull();

        logs.Warnings.Count.ShouldBe(2);
    }

    [Fact]
    public async Task The_hosts_own_speech_calls_warn_when_the_synthesized_model_has_no_price()
    {
        // The service builds its own price lookup when no reader is registered; the warning must reach it too.
        using var container = Container(static _ => { }, out var logs, builder => builder.AddSpeechSynthesizer("studio", static _ => new StudioSynthesizer()));
        var speech = container.GetRequiredService<ISpeechSynthesisService>();

        await speech.SynthesizeAsync(new SpeechRequest { Text = "hello" }, "studio", TestContext.Current.CancellationToken);
        await speech.SynthesizeAsync(new SpeechRequest { Text = "again" }, "studio", TestContext.Current.CancellationToken);

        var line = logs.Warnings.ShouldHaveSingleItem();
        line.ShouldContain("studio-v2");
        line.ShouldContain("speech synthesis");
    }

    /// <summary>Composes the real container with a warning collector, the way a host does.</summary>
    private static ServiceProvider Container(
        Action<TraconOptions> configure,
        out WarningLogProvider logs,
        Action<ITraconBuilder>? compose = null)
    {
        logs = new WarningLogProvider();
        var collected = logs;

        var services = new ServiceCollection();
        services.AddLogging(builder =>
        {
            builder.SetMinimumLevel(LogLevel.Trace);
            builder.AddProvider(collected);
        });
        var tracon = services.AddTracon();
        compose?.Invoke(tracon);
        services.Configure(configure);

        // The live voice package registers this; the price lookup is what the case is about.
        services.TryAddSingleton<VoiceDurationPricing>();

        return services.BuildServiceProvider();
    }

    /// <summary>A harness that owns the logger factory the warnings write through.</summary>
    private sealed class Harness : IDisposable
    {
        private readonly ILoggerFactory _factory;

        public Harness()
        {
            Logs = new WarningLogProvider();
            _factory = LoggerFactory.Create(builder => builder.AddProvider(Logs));
            Warnings = new UnpricedMediaWarnings(_factory.CreateLogger<UnpricedMediaWarnings>());
        }

        public WarningLogProvider Logs { get; }

        public UnpricedMediaWarnings Warnings { get; }

        public void Dispose()
        {
            _factory.Dispose();
            Logs.Dispose();
        }
    }

    private sealed class DisabledLogger : ILogger<UnpricedMediaWarnings>
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
            => null;

        public bool IsEnabled(LogLevel logLevel) => false;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => throw new InvalidOperationException("A disabled level must not be written to.");
    }

    private sealed class StudioSynthesizer : ISpeechSynthesizer
    {
        public string ProviderName => "studio";

        public int MaxCharactersPerRequest => 1000;

        public ValueTask<SpeechAudio> SynthesizeAsync(SpeechRequest request, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(new SpeechAudio
            {
                Data = new byte[] { 1, 2, 3 },
                MediaType = "audio/mpeg",
                ModelId = "studio-v2",
                CharactersBilled = request.Text.Length,
            });

        public IAsyncEnumerable<ReadOnlyMemory<byte>> SynthesizeStreamingAsync(SpeechRequest request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public ValueTask<IReadOnlyList<VoiceDescriptor>> ListVoicesAsync(CancellationToken cancellationToken = default)
            => ValueTask.FromResult<IReadOnlyList<VoiceDescriptor>>([]);
    }
}
