using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shouldly;
using Tracon.Voice.UnitTests.Infrastructure;

namespace Tracon.Voice.UnitTests;

/// <summary>
/// A speech call is priced by the provider AND the model that did the work.
/// </summary>
/// <remarks>
/// F-298 (§198.5): the price came from the default instance's configured model and
/// a type check for the built-in client. A named synthesizer then had either no price
/// or the default's price under its own provider name; behind the tenant wrapper the
/// type check failed and the price went silently to <see langword="null"/>. The model
/// is now the one the provider reports, then the one the request named, and only the
/// default provider falls back to its configured model.
/// </remarks>
public sealed class VoicePricingTests
{
    [Fact]
    public async Task A_reported_model_is_priced_under_its_own_provider()
    {
        await using var services = Build();
        var pricing = services.GetRequiredService<IVoicePricingReader>();

        pricing.ForSynthesis("studio", "studio-v2", 1_000_000m).ShouldBe(12m);
        pricing.ForTranscription("studio", "studio-stt", TimeSpan.FromMinutes(2)).ShouldBe(1m);
    }

    [Fact]
    public async Task A_named_provider_without_a_model_does_not_borrow_the_default_models_price()
    {
        await using var services = Build();
        var pricing = services.GetRequiredService<IVoicePricingReader>();

        // 'studio' has a price for the default's configured model name, but the
        // configured model belongs to the default provider, not to 'studio'.
        pricing.ForSynthesis("studio", modelId: null, 1_000_000m).ShouldBeNull();
        pricing.ForTranscription("studio", modelId: null, TimeSpan.FromMinutes(1)).ShouldBeNull();
    }

    [Fact]
    public async Task The_default_provider_without_a_reported_model_keeps_its_configured_model()
    {
        await using var services = Build();
        var pricing = services.GetRequiredService<IVoicePricingReader>();

        pricing.ForSynthesis(VoiceProviderNames.ElevenLabs, modelId: null, 1_000_000m).ShouldBe(30m);
        pricing.ForCharacters(1_000_000m).ShouldBe(30m);
    }

    [Fact]
    public async Task The_built_in_client_reports_the_model_it_synthesized_with()
    {
        var handler = new StubHttpMessageHandler(_ => StubHttpMessageHandler.Binary([0xFF, 0xF3, 0x48, 0xC4]));
        using var client = new ElevenLabsSpeechClient(
            new VoiceOptions { ApiKey = "k", DefaultVoiceId = "voice-1" },
            new HttpClient(handler));

        var unnamed = await client.SynthesizeAsync(new SpeechRequest { Text = "hi" }, TestContext.Current.CancellationToken);
        var named = await client.SynthesizeAsync(
            new SpeechRequest { Text = "hi", ModelId = "eleven_flash_v2_5" },
            TestContext.Current.CancellationToken);

        unnamed.ModelId.ShouldBe(ElevenLabsSpeechClient.DefaultSynthesisModel);
        named.ModelId.ShouldBe("eleven_flash_v2_5");
    }

    [Fact]
    public async Task The_built_in_client_reports_the_model_it_transcribed_with()
    {
        var handler = new StubHttpMessageHandler(_ => StubHttpMessageHandler.Json(
            """{"text":"hello","language_code":"en","audio_duration_secs":2}"""));
        using var client = new ElevenLabsSpeechClient(
            new VoiceOptions { ApiKey = "k", TranscriptionModelId = "scribe_v1" },
            new HttpClient(handler));
        using var audio = new MemoryStream([1, 2, 3]);

        var transcript = await client.TranscribeAsync(audio, "audio/mpeg", cancellationToken: TestContext.Current.CancellationToken);

        transcript.ModelId.ShouldBe("scribe_v1");
    }

    private static ServiceProvider Build()
    {
        var services = new ServiceCollection();
        services.AddTracon().UseVoice(static options => options.ApiKey = "k");
        services.Configure<TraconOptions>(static options =>
        {
            options.Pricing.Voice[VoiceProviderNames.ElevenLabs] = new Dictionary<string, VoicePriceOverride>(StringComparer.OrdinalIgnoreCase)
            {
                [ElevenLabsSpeechClient.DefaultSynthesisModel] = new() { PerMillionCharacters = 30m },
            };
            options.Pricing.Voice["studio"] = new Dictionary<string, VoicePriceOverride>(StringComparer.OrdinalIgnoreCase)
            {
                ["studio-v2"] = new() { PerMillionCharacters = 12m },
                ["studio-stt"] = new() { PerMinute = 0.5m },
                [ElevenLabsSpeechClient.DefaultSynthesisModel] = new() { PerMillionCharacters = 99m },
                [ElevenLabsSpeechClient.DefaultTranscriptionModel] = new() { PerMinute = 99m },
            };
        });

        var provider = services.BuildServiceProvider();
        _ = provider.GetRequiredService<IOptions<TraconOptions>>().Value;

        return provider;
    }
}
