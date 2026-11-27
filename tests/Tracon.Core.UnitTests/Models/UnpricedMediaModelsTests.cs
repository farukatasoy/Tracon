using Microsoft.Extensions.Options;

namespace Tracon.Core.UnitTests.Models;

/// <summary>
/// The rule that decides whether a configured speech or image model is priced, and the list it produces for the report.
/// </summary>
/// <remarks>
/// The same rule prices the call itself, so a model the report calls priced is a model whose calls carry a cost.
/// </remarks>
public sealed class UnpricedMediaModelsTests
{
    [Fact]
    public void A_voice_model_is_priced_for_the_kind_whose_rate_it_carries()
    {
        var pricing = new TraconPricingOptions();
        pricing.Voice["studio"] = new Dictionary<string, VoicePriceOverride>(StringComparer.OrdinalIgnoreCase)
        {
            ["by-character"] = new() { PerMillionCharacters = 30m },
            ["by-minute"] = new() { PerMinute = 0.4m },
        };

        MediaPriceLookup.IsPriced(MediaPriceKind.SpeechSynthesis, pricing, "studio", "by-character").ShouldBeTrue();
        MediaPriceLookup.IsPriced(MediaPriceKind.Transcription, pricing, "studio", "by-character").ShouldBeFalse();
        MediaPriceLookup.IsPriced(MediaPriceKind.LiveVoice, pricing, "studio", "by-character").ShouldBeFalse();

        MediaPriceLookup.IsPriced(MediaPriceKind.Transcription, pricing, "studio", "by-minute").ShouldBeTrue();
        MediaPriceLookup.IsPriced(MediaPriceKind.LiveVoice, pricing, "studio", "by-minute").ShouldBeTrue();
        MediaPriceLookup.IsPriced(MediaPriceKind.SpeechSynthesis, pricing, "studio", "by-minute").ShouldBeFalse();
    }

    [Fact]
    public void An_image_model_is_priced_by_either_of_its_rates_and_not_by_an_empty_entry()
    {
        var pricing = new TraconPricingOptions();
        pricing.Images["studio"] = new Dictionary<string, ImagePriceOverride>(StringComparer.OrdinalIgnoreCase)
        {
            ["per-image"] = new() { PerImage = 0.04m },
            ["per-token"] = new() { OutputCostPerMillionTokens = 10m },
            ["empty"] = new(),
        };

        MediaPriceLookup.IsPriced(MediaPriceKind.Image, pricing, "studio", "per-image").ShouldBeTrue();
        MediaPriceLookup.IsPriced(MediaPriceKind.Image, pricing, "studio", "per-token").ShouldBeTrue();
        MediaPriceLookup.IsPriced(MediaPriceKind.Image, pricing, "studio", "empty").ShouldBeFalse();
        MediaPriceLookup.IsPriced(MediaPriceKind.Image, pricing, "studio", "absent").ShouldBeFalse();
    }

    [Fact]
    public void Names_compare_without_case_as_the_price_table_does()
    {
        var pricing = new TraconPricingOptions();
        pricing.Voice["ElevenLabs"] = new Dictionary<string, VoicePriceOverride>(StringComparer.OrdinalIgnoreCase)
        {
            ["Scribe_V2"] = new() { PerMinute = 0.4m },
        };

        MediaPriceLookup.IsPriced(MediaPriceKind.Transcription, pricing, "elevenlabs", "scribe_v2").ShouldBeTrue();
    }

    [Fact]
    public void A_voice_price_never_prices_an_image_model_of_the_same_name()
    {
        var pricing = new TraconPricingOptions();
        pricing.Voice["studio"] = new Dictionary<string, VoicePriceOverride>(StringComparer.OrdinalIgnoreCase)
        {
            ["shared-name"] = new() { PerMillionCharacters = 30m, PerMinute = 0.4m },
        };

        MediaPriceLookup.IsPriced(MediaPriceKind.Image, pricing, "studio", "shared-name").ShouldBeFalse();
    }

    [Fact]
    public void The_list_names_each_unpriced_model_once_in_kind_provider_and_model_order()
    {
        var pricing = new TraconPricingOptions();
        pricing.Voice["elevenlabs"] = new Dictionary<string, VoicePriceOverride>(StringComparer.OrdinalIgnoreCase)
        {
            ["priced"] = new() { PerMillionCharacters = 30m },
        };

        var unpriced = UnpricedMediaModels.Find(
            [
                new FixedSource(
                    new ConfiguredMediaModel(MediaPriceKind.Image, "openai", "gpt-image-1"),
                    new ConfiguredMediaModel(MediaPriceKind.Transcription, "elevenlabs", "scribe_v2"),
                    new ConfiguredMediaModel(MediaPriceKind.SpeechSynthesis, "elevenlabs", "priced"),
                    new ConfiguredMediaModel(MediaPriceKind.SpeechSynthesis, "elevenlabs", "eleven_v4_turbo")),
                new FixedSource(
                    new ConfiguredMediaModel(MediaPriceKind.Transcription, "elevenlabs", "scribe_v2")),
            ],
            pricing);

        unpriced.ShouldBe(
        [
            "elevenlabs/eleven_v4_turbo (speech synthesis)",
            "elevenlabs/scribe_v2 (transcription)",
            "openai/gpt-image-1 (image generation)",
        ]);
    }

    [Fact]
    public void No_source_and_no_configured_model_are_an_empty_list()
    {
        UnpricedMediaModels.Find([], new TraconPricingOptions()).ShouldBeEmpty();
        UnpricedMediaModels.Find([new FixedSource()], new TraconPricingOptions()).ShouldBeEmpty();
    }

    [Fact]
    public void The_image_source_names_each_provider_model_while_image_generation_is_on()
    {
        var options = new TraconImageOptions { Enabled = true };
        options.Providers["openai"] = new TraconImageProviderOptions { Model = "gpt-image-1" };
        options.Providers["google"] = new TraconImageProviderOptions { Model = "imagen-x" };
        options.Providers["unset"] = new TraconImageProviderOptions();

        new ConfiguredImageModels(Options.Create(options)).ConfiguredModels().ShouldBe(
        [
            new ConfiguredMediaModel(MediaPriceKind.Image, "openai", "gpt-image-1"),
            new ConfiguredMediaModel(MediaPriceKind.Image, "google", "imagen-x"),
        ]);
    }

    [Fact]
    public void The_image_source_names_nothing_while_image_generation_is_off()
    {
        // Nothing draws with a model that is configured but switched off, so no call can be recorded without its cost.
        var options = new TraconImageOptions();
        options.Providers["openai"] = new TraconImageProviderOptions { Model = "gpt-image-1" };

        new ConfiguredImageModels(Options.Create(options)).ConfiguredModels().ShouldBeEmpty();
    }

    private sealed class FixedSource(params ConfiguredMediaModel[] models) : IConfiguredMediaModelSource
    {
        public IReadOnlyList<ConfiguredMediaModel> ConfiguredModels() => models;
    }
}
