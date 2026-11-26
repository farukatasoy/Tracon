using System.Text.Json;

namespace Tracon.Core.UnitTests.Images;

/// <summary>Verifies fail-fast validation for image configuration.</summary>
public sealed class ImageOptionsValidationTests
{
    [Fact]
    public void Image_price_cannot_mix_per_image_and_token_rates()
    {
        var options = new TraconOptions();
        options.Pricing.Images["openai"] = new Dictionary<string, ImagePriceOverride>(StringComparer.Ordinal)
        {
            ["gpt-image-1"] = new ImagePriceOverride
            {
                PerImage = 0.04m,
                OutputCostPerMillionTokens = 10m,
            },
        };

        var result = new TraconOptionsValidator().Validate(null, options);

        result.Succeeded.ShouldBeFalse();
        result.Failures!.ShouldContain(failure => failure.Contains("cannot contain both", StringComparison.Ordinal));
    }

    [Fact]
    public void Enabled_images_require_a_provider_and_a_positive_limit()
    {
        var options = new TraconImageOptions { Enabled = true, MaxImagesPerRequest = 0 };

        var result = new TraconImageOptionsValidator().Validate(null, options);

        result.Succeeded.ShouldBeFalse();
        result.Failures!.ShouldContain(failure => failure.Contains(nameof(TraconImageOptions.Provider), StringComparison.Ordinal));
        result.Failures!.ShouldContain(failure => failure.Contains(nameof(TraconImageOptions.MaxImagesPerRequest), StringComparison.Ordinal));
    }

    [Fact]
    public void Enabled_images_require_the_model_of_the_default_provider()
    {
        var options = new TraconImageOptions { Enabled = true, Provider = "custom" };

        var result = new TraconImageOptionsValidator().Validate(null, options);

        result.Succeeded.ShouldBeFalse();
        result.Failures!.ShouldHaveSingleItem().ShouldContain("Providers[custom].Model");

        options.Providers["custom"] = new TraconImageProviderOptions { Model = "image-1" };

        new TraconImageOptionsValidator().Validate(null, options).Succeeded.ShouldBeTrue();
    }

    [Fact]
    public void One_registered_provider_is_the_default_and_two_need_a_name()
    {
        var one = new TraconImageOptionsValidator([new ImageProviderRegistration("openai")]);
        var two = new TraconImageOptionsValidator([new ImageProviderRegistration("openai"), new ImageProviderRegistration("google")]);

        var options = new TraconImageOptions { Enabled = true };
        options.Providers["openai"] = new TraconImageProviderOptions { Model = "gpt-image-1" };
        options.Providers["google"] = new TraconImageProviderOptions { Model = "gemini-image" };

        one.Validate(null, options).Succeeded.ShouldBeTrue("the only registered provider is the default and needs no name");

        var refused = two.Validate(null, options);

        refused.Succeeded.ShouldBeFalse();
        refused.Failures!.ShouldHaveSingleItem().ShouldContain("more than one image provider is registered");

        options.Provider = "google";

        two.Validate(null, options).Succeeded.ShouldBeTrue("naming the default is the way out");
    }

    [Fact]
    public void A_provider_setting_the_provider_does_not_support_fails_at_startup()
    {
        var options = new TraconImageOptions();
        options.Providers["openai"] = new TraconImageProviderOptions();
        options.Providers["openai"].ProviderSettings["openai.quality"] = JsonSerializer.SerializeToElement("low");
        options.Providers["google"] = new TraconImageProviderOptions();
        options.Providers["google"].ProviderSettings["openai.quality"] = JsonSerializer.SerializeToElement("low");

        var result = new TraconImageOptionsValidator().Validate(null, options);

        result.Succeeded.ShouldBeFalse();
        result.Failures!.ShouldHaveSingleItem().ShouldContain("Providers[google]");
    }

    [Fact]
    public void A_quality_that_is_not_text_fails_at_startup_and_the_message_never_carries_the_value()
    {
        var options = new TraconImageOptions();
        options.Providers["openai"] = new TraconImageProviderOptions();
        options.Providers["openai"].ProviderSettings["openai.quality"] = JsonSerializer.SerializeToElement(4242);

        var result = new TraconImageOptionsValidator().Validate(null, options);

        result.Succeeded.ShouldBeFalse();
        result.Failures!.ShouldHaveSingleItem().ShouldNotContain("4242", Case.Sensitive);
    }
}
