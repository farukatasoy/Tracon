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
    public void Enabled_images_require_provider_model_and_positive_limit()
    {
        var options = new TraconImageOptions { Enabled = true, MaxImagesPerRequest = 0 };

        var result = new TraconImageOptionsValidator().Validate(null, options);

        result.Succeeded.ShouldBeFalse();
        result.Failures!.ShouldContain(failure => failure.Contains(nameof(TraconImageOptions.Provider), StringComparison.Ordinal));
        result.Failures!.ShouldContain(failure => failure.Contains(nameof(TraconImageOptions.Model), StringComparison.Ordinal));
        result.Failures!.ShouldContain(failure => failure.Contains(nameof(TraconImageOptions.MaxImagesPerRequest), StringComparison.Ordinal));
    }
}
