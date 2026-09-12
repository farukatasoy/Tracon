using Microsoft.Extensions.Options;

namespace Tracon.Core.UnitTests.Images;

/// <summary>Verifies that image cost reporting never invents a price or unit.</summary>
public sealed class ImagePricingTests
{
    [Fact]
    public void Missing_price_keeps_cost_null_and_reports_the_image_count()
    {
        var measurement = Pricing().Measure("openai", "gpt-image-1", "1024x1024", imageCount: 2, outputTokens: null);

        measurement.Unit.ShouldBe(ToolUsageUnits.Images);
        measurement.Quantity.ShouldBe(2m);
        measurement.Cost.ShouldBeNull();
    }

    [Fact]
    public void Per_image_price_applies_the_configured_size_multiplier()
    {
        var measurement = Pricing(new ImagePriceOverride
        {
            PerImage = 0.04m,
            SizeMultipliers = { ["1024x1024"] = 2m },
        }).Measure("openai", "gpt-image-1", "1024x1024", imageCount: 2, outputTokens: null);

        measurement.Unit.ShouldBe(ToolUsageUnits.Images);
        measurement.Quantity.ShouldBe(2m);
        measurement.Cost.ShouldBe(0.16m);
        measurement.Currency.ShouldBe("USD");
    }

    [Fact]
    public void Token_priced_model_reports_provider_output_tokens()
    {
        var measurement = Pricing(new ImagePriceOverride
        {
            OutputCostPerMillionTokens = 10m,
        }).Measure("openai", "gpt-image-1", size: null, imageCount: 1, outputTokens: 4_000);

        measurement.Unit.ShouldBe(ToolUsageUnits.Tokens);
        measurement.Quantity.ShouldBe(4_000m);
        measurement.Cost.ShouldBe(0.04m);
    }

    [Fact]
    public void Token_price_without_provider_tokens_does_not_fabricate_a_zero_token_measurement()
    {
        var measurement = Pricing(new ImagePriceOverride
        {
            OutputCostPerMillionTokens = 10m,
        }).Measure("openai", "gpt-image-1", size: null, imageCount: 1, outputTokens: null);

        measurement.Unit.ShouldBe(ToolUsageUnits.Images);
        measurement.Quantity.ShouldBe(1m);
        measurement.Cost.ShouldBeNull();
    }

    private static ImagePricing Pricing(ImagePriceOverride? price = null)
    {
        var options = new TraconOptions();
        options.Pricing.Currency = "USD";

        if (price is not null)
        {
            options.Pricing.Images["openai"] = new Dictionary<string, ImagePriceOverride>(StringComparer.OrdinalIgnoreCase)
            {
                ["gpt-image-1"] = price,
            };
        }

        return new ImagePricing(Options.Create(options));
    }
}
