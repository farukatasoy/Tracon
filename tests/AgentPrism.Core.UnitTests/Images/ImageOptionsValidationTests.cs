namespace AgentPrism.Core.UnitTests.Images;

/// <summary>Verifies fail-fast validation for image configuration.</summary>
public sealed class ImageOptionsValidationTests
{
    [Fact]
    public void Image_price_cannot_mix_per_image_and_token_rates()
    {
        var options = new AgentPrismOptions();
        options.Pricing.Images["openai"] = new Dictionary<string, ImagePriceOverride>(StringComparer.Ordinal)
        {
            ["gpt-image-1"] = new ImagePriceOverride
            {
                PerImage = 0.04m,
                OutputCostPerMillionTokens = 10m,
            },
        };

        var result = new AgentPrismOptionsValidator().Validate(null, options);

        result.Succeeded.ShouldBeFalse();
        result.Failures!.ShouldContain(failure => failure.Contains("cannot contain both", StringComparison.Ordinal));
    }

    [Fact]
    public void Enabled_images_require_provider_model_and_positive_limit()
    {
        var options = new AgentPrismImageOptions { Enabled = true, MaxImagesPerRequest = 0 };

        var result = new AgentPrismImageOptionsValidator().Validate(null, options);

        result.Succeeded.ShouldBeFalse();
        result.Failures!.ShouldContain(failure => failure.Contains(nameof(AgentPrismImageOptions.Provider), StringComparison.Ordinal));
        result.Failures!.ShouldContain(failure => failure.Contains(nameof(AgentPrismImageOptions.Model), StringComparison.Ordinal));
        result.Failures!.ShouldContain(failure => failure.Contains(nameof(AgentPrismImageOptions.MaxImagesPerRequest), StringComparison.Ordinal));
    }
}
