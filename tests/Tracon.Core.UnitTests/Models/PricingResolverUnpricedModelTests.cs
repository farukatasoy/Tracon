using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Tracon.Core.UnitTests.Models;

/// <summary>
/// A model that no price source covers is recorded with empty cost columns, and the resolver says so once for each model.
/// </summary>
/// <remarks>
/// The startup warning lists only the models a provider's catalog names. A definition may bind a model the catalog does
/// not list, so the run being recorded is the first place that sees the missing price for it.
/// </remarks>
public sealed class PricingResolverUnpricedModelTests
{
    [Fact]
    public void A_model_with_no_price_is_recorded_unknown_and_warned_about_once()
    {
        var logger = new RecordingLogger<RunPricingResolver>();
        var resolver = CreateResolver(new TraconOptions(), logger);

        var first = resolver.Resolve("openai", "gpt-unlisted", Usage());
        var second = resolver.Resolve("openai", "gpt-unlisted", Usage());

        first.ShouldNotBeNull().Source.ShouldBe(PricingSource.Unknown);
        second.ShouldNotBeNull().Source.ShouldBe(PricingSource.Unknown);

        var warning = logger.Warnings.ShouldHaveSingleItem();
        warning.ShouldContain("gpt-unlisted");
        warning.ShouldContain("openai");
        warning.ShouldContain("Tracon:Pricing");
    }

    [Fact]
    public void Each_unpriced_model_is_warned_about_on_its_own()
    {
        var logger = new RecordingLogger<RunPricingResolver>();
        var resolver = CreateResolver(new TraconOptions(), logger);

        resolver.Resolve("openai", "model-a", Usage());
        resolver.Resolve("openai", "model-b", Usage());
        resolver.Resolve("anthropic", "model-a", Usage());

        logger.Warnings.Count.ShouldBe(3);
    }

    [Fact]
    public void A_model_priced_in_the_configuration_is_not_warned_about()
    {
        var logger = new RecordingLogger<RunPricingResolver>();
        var resolver = CreateResolver(
            new TraconOptions
            {
                Pricing = new TraconPricingOptions
                {
                    Providers =
                    {
                        ["openai"] = new Dictionary<string, ModelPriceOverride>(StringComparer.OrdinalIgnoreCase)
                        {
                            ["gpt-unlisted"] = new() { InputCostPerMillionTokens = 1m, OutputCostPerMillionTokens = 2m },
                        },
                    },
                },
            },
            logger);

        var cost = resolver.Resolve("openai", "gpt-unlisted", Usage());

        cost.ShouldNotBeNull().Source.ShouldBe(PricingSource.Configuration);
        logger.Warnings.ShouldBeEmpty();
    }

    [Fact]
    public void A_lookup_that_names_no_provider_says_nothing()
    {
        var logger = new RecordingLogger<RunPricingResolver>();
        var resolver = CreateResolver(new TraconOptions(), logger);

        resolver.Resolve(provider: null, "gpt-unlisted", Usage()).ShouldNotBeNull().Source.ShouldBe(PricingSource.Unknown);

        logger.Warnings.ShouldBeEmpty();
    }

    private static RunPricingResolver CreateResolver(TraconOptions options, RecordingLogger<RunPricingResolver> logger)
        => new(new ModelProviderRegistry([]), Options.Create(options), logger);

    private static RunUsage Usage() => new() { InputTokens = 10, OutputTokens = 5 };

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        public List<string> Warnings { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
            => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Warning)
            {
                Warnings.Add(formatter(state, exception));
            }
        }
    }
}
