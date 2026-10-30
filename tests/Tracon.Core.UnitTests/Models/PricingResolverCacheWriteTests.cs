using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tracon.Core.UnitTests.Fakes;

namespace Tracon.Core.UnitTests.Models;

/// <summary>
/// Verifies how input tokens written into the provider's prompt cache are priced: the
/// computation is subtractive, each time-to-live class has its own rate, and an undefined rate
/// changes nothing.
/// </summary>
/// <remarks>
/// The written tokens are counted INSIDE <see cref="RunUsage.InputTokens"/>, like the cached ones.
/// The load-bearing assertions are the two that would otherwise go unseen: a token priced twice
/// (once as input, once as a write), and a cost that moves when no write rate is configured.
/// </remarks>
public sealed class PricingResolverCacheWriteTests
{
    [Fact]
    public void Written_tokens_are_subtracted_from_the_full_price_input_and_charged_at_their_own_rates()
    {
        // 1M input tokens: 300k read from the cache, 200k written short, 100k written long, 400k plain.
        // At 10/M input, 1/M read, 12.5/M short write, 20/M long write:
        // 400k * 10 / 1M = 4, 300k * 1 / 1M = 0.3, 200k * 12.5 / 1M + 100k * 20 / 1M = 2.5 + 2.
        var resolver = CreateResolver(WritePricedModel());

        var cost = resolver.Resolve(
            "anthropic",
            "claude-x",
            Usage(input: 1_000_000, cached: 300_000, shortWrite: 200_000, longWrite: 100_000));

        cost.ShouldNotBeNull();
        cost.InputCost.ShouldBe(4m);
        cost.CachedInputCost.ShouldBe(0.3m);
        cost.CacheWriteCost.ShouldBe(4.5m);
        cost.CacheWritePricePerMillionTokens.ShouldBe(12.5m);
        cost.CacheWriteLongPricePerMillionTokens.ShouldBe(20m);
        cost.Total().ShouldBe(4m + 0.3m + 4.5m);
    }

    [Fact]
    public void No_token_is_priced_twice()
    {
        // Every input token is priced exactly once: plain + read + short write + long write = input.
        // At a rate of 1 for every class, the priced input must equal the input tokens.
        var resolver = CreateResolver(new ModelDescriptor
        {
            Name = "claude-x",
            InputCostPerMillionTokens = 1m,
            CachedInputCostPerMillionTokens = 1m,
            CacheWriteCostPerMillionTokens = 1m,
            CacheWriteLongCostPerMillionTokens = 1m,
        });

        var cost = resolver.Resolve(
            "anthropic",
            "claude-x",
            Usage(input: 1_000_000, cached: 250_000, shortWrite: 300_000, longWrite: 150_000));

        cost.ShouldNotBeNull();
        cost.Total().ShouldBe(1m, "the four classes must partition the input; a token counted twice would exceed 1");
    }

    [Fact]
    public void A_write_priced_above_the_input_rate_costs_more_than_the_same_call_without_that_rate()
    {
        // The fault this change closes: the written tokens were priced as plain input.
        var priced = CreateResolver(WritePricedModel());
        var unpriced = CreateResolver(WritePricedModel() with
        {
            CacheWriteCostPerMillionTokens = null,
            CacheWriteLongCostPerMillionTokens = null,
        });

        var usage = Usage(input: 1_000_000, cached: null, shortWrite: 500_000, longWrite: null);

        priced.Resolve("anthropic", "claude-x", usage)!.Total()!.Value
            .ShouldBeGreaterThan(unpriced.Resolve("anthropic", "claude-x", usage)!.Total()!.Value);
    }

    [Fact]
    public void Without_a_write_rate_the_cost_is_exactly_what_it_was_before_the_write_counters_existed()
    {
        // 🚨 The regression this guards: a model with no write rate must produce
        // byte-for-byte the value it produced before write pricing existed, even when the
        // provider DOES report written tokens.
        var resolver = CreateResolver(CachePricedModelWithoutWriteRates());

        var withWrites = resolver.Resolve(
            "anthropic",
            "claude-x",
            Usage(input: 1_000_000, cached: 300_000, shortWrite: 200_000, longWrite: 100_000));

        var withoutWrites = resolver.Resolve(
            "anthropic",
            "claude-x",
            Usage(input: 1_000_000, cached: 300_000, shortWrite: null, longWrite: null));

        withWrites.ShouldNotBeNull();
        withWrites.ShouldBe(withoutWrites, "an undefined write rate must not change one field of the cost");
        withWrites.CacheWriteCost.ShouldBeNull();
        withWrites.CacheWritePricePerMillionTokens.ShouldBeNull();
        withWrites.CacheWriteLongPricePerMillionTokens.ShouldBeNull();
        withWrites.InputCost.ShouldBe(7m, "700k tokens (everything but the read) stay at the input rate");
    }

    [Fact]
    public void An_undefined_write_rate_does_not_make_the_pricing_source_unknown()
    {
        var resolver = CreateResolver(CachePricedModelWithoutWriteRates());

        var cost = resolver.Resolve("anthropic", "claude-x", Usage(1_000, cached: null, shortWrite: 500, longWrite: null));

        cost.ShouldNotBeNull();
        cost.Source.ShouldBe(PricingSource.Catalog);
    }

    [Fact]
    public void Only_the_class_with_a_rate_is_priced_and_the_other_stays_in_the_input()
    {
        // The short rate is defined, the long one is not: the 100k long-lived tokens stay at the
        // plain input rate, in the same run.
        var resolver = CreateResolver(WritePricedModel() with { CacheWriteLongCostPerMillionTokens = null });

        var cost = resolver.Resolve(
            "anthropic",
            "claude-x",
            Usage(input: 1_000_000, cached: null, shortWrite: 200_000, longWrite: 100_000));

        cost.ShouldNotBeNull();
        cost.InputCost.ShouldBe(8m, "only the 200k short writes leave the input; the 100k long writes stay at the input rate");
        cost.CacheWriteCost.ShouldBe(2.5m);
        cost.CacheWritePricePerMillionTokens.ShouldBe(12.5m);
        cost.CacheWriteLongPricePerMillionTokens.ShouldBeNull();
    }

    [Fact]
    public void A_class_the_provider_reported_nothing_for_produces_no_charge_and_no_snapshot()
    {
        // 🚨 Distinct from "no rate configured": here the rate EXISTS but the provider stayed
        // silent. A 0m charge would claim an observed absence; null says nothing was measured.
        var resolver = CreateResolver(WritePricedModel());

        var cost = resolver.Resolve(
            "anthropic",
            "claude-x",
            Usage(input: 1_000_000, cached: null, shortWrite: 200_000, longWrite: null));

        cost.ShouldNotBeNull();
        cost.CacheWriteCost.ShouldBe(2.5m);
        cost.CacheWritePricePerMillionTokens.ShouldBe(12.5m);
        cost.CacheWriteLongPricePerMillionTokens.ShouldBeNull();

        var silent = resolver.Resolve("anthropic", "claude-x", Usage(1_000_000, cached: null, shortWrite: null, longWrite: null));

        silent.ShouldNotBeNull();
        silent.CacheWriteCost.ShouldBeNull();
        silent.InputCost.ShouldBe(10m);
    }

    [Fact]
    public void A_reported_zero_write_count_does_produce_a_zero_charge()
    {
        var resolver = CreateResolver(WritePricedModel());

        var cost = resolver.Resolve("anthropic", "claude-x", Usage(1_000_000, cached: null, shortWrite: 0, longWrite: null));

        cost.ShouldNotBeNull();
        cost.CacheWriteCost.ShouldBe(0m);
        cost.InputCost.ShouldBe(10m);
    }

    [Fact]
    public void A_zero_write_rate_is_honoured_and_is_not_treated_as_undefined()
    {
        // 0 states "cache writes are free"; null states "no rate is known". The two must not collapse.
        var resolver = CreateResolver(WritePricedModel() with { CacheWriteCostPerMillionTokens = 0m });

        var cost = resolver.Resolve("anthropic", "claude-x", Usage(1_000_000, cached: null, shortWrite: 400_000, longWrite: null));

        cost.ShouldNotBeNull();
        cost.CacheWriteCost.ShouldBe(0m);
        cost.InputCost.ShouldBe(6m);
    }

    [Fact]
    public void More_written_and_cached_tokens_than_input_tokens_keeps_the_written_tokens_in_the_input()
    {
        // A provider contradicting its own contract is a DATA fault: nothing is clamped into
        // shape, the write rates are ignored and the whole input is charged at the full rate.
        var resolver = CreateResolver(WritePricedModel());

        var cost = resolver.Resolve(
            "anthropic",
            "claude-x",
            Usage(input: 100, cached: 60, shortWrite: 50, longWrite: null));

        cost.ShouldNotBeNull();
        cost.CacheWriteCost.ShouldBeNull();
        cost.InputCost.ShouldBe(40m * 10m / 1_000_000m, "the read (60) is still priced; only the contradicting writes are not");
        cost.CachedInputCost.ShouldBe(60m * 1m / 1_000_000m);
    }

    [Fact]
    public void The_contradiction_is_logged_so_the_ignored_rate_is_not_silent()
    {
        var logger = new RecordingLogger<RunPricingResolver>();
        var resolver = CreateResolver(WritePricedModel(), logger);

        resolver.Resolve("anthropic", "claude-x", Usage(input: 10, cached: null, shortWrite: 50, longWrite: null));

        logger.Warnings.ShouldHaveSingleItem().ShouldContain("cache-written");
    }

    [Fact]
    public void Written_tokens_with_no_input_total_are_a_contradiction_and_stay_unpriced()
    {
        var resolver = CreateResolver(WritePricedModel());

        var cost = resolver.Resolve("anthropic", "claude-x", new RunUsage { CacheWriteInputTokens = 5, OutputTokens = 1 });

        cost.ShouldNotBeNull();
        cost.CacheWriteCost.ShouldBeNull();
    }

    [Fact]
    public void Configured_pricing_carries_the_write_rates_too()
    {
        var provider = new FakeModelProvider(name: "anthropic", models: [new ModelDescriptor { Name = "claude-x" }]);
        var resolver = new RunPricingResolver(
            new ModelProviderRegistry([provider]),
            Options.Create(new TraconOptions
            {
                Pricing = new TraconPricingOptions
                {
                    Currency = "USD",
                    Providers =
                    {
                        ["anthropic"] = new Dictionary<string, ModelPriceOverride>(StringComparer.OrdinalIgnoreCase)
                        {
                            ["claude-x"] = new()
                            {
                                InputCostPerMillionTokens = 10m,
                                CacheWriteCostPerMillionTokens = 12.5m,
                                CacheWriteLongCostPerMillionTokens = 20m,
                            },
                        },
                    },
                },
            }));

        var cost = resolver.Resolve(
            "anthropic",
            "claude-x",
            Usage(input: 1_000_000, cached: null, shortWrite: 200_000, longWrite: 100_000));

        cost.ShouldNotBeNull();
        cost.Source.ShouldBe(PricingSource.Configuration);
        cost.InputCost.ShouldBe(7m);
        cost.CacheWriteCost.ShouldBe(4.5m);
    }

    [Fact]
    public void The_catalog_price_wins_over_the_configured_one_for_the_write_rates_as_for_the_others()
    {
        // The priority order is unchanged: a model priced in the catalog never reads Tracon:Pricing.
        var provider = new FakeModelProvider(name: "anthropic", models: [CachePricedModelWithoutWriteRates()]);
        var resolver = new RunPricingResolver(
            new ModelProviderRegistry([provider]),
            Options.Create(new TraconOptions
            {
                Pricing = new TraconPricingOptions
                {
                    Currency = "USD",
                    Providers =
                    {
                        ["anthropic"] = new Dictionary<string, ModelPriceOverride>(StringComparer.OrdinalIgnoreCase)
                        {
                            ["claude-x"] = new()
                            {
                                InputCostPerMillionTokens = 99m,
                                CacheWriteCostPerMillionTokens = 99m,
                            },
                        },
                    },
                },
            }));

        var cost = resolver.Resolve("anthropic", "claude-x", Usage(1_000_000, cached: null, shortWrite: 200_000, longWrite: null));

        cost.ShouldNotBeNull();
        cost.Source.ShouldBe(PricingSource.Catalog);
        cost.CacheWriteCost.ShouldBeNull("the catalog entry has no write rate and wins");
        cost.InputCost.ShouldBe(10m);
    }

    [Fact]
    public void Every_total_adds_the_write_charge_as_the_fourth_term()
    {
        // The seam every runtime total goes through (quota, metric, webhook, statistics).
        var resolver = CreateResolver(WritePricedModel());

        var cost = resolver.Resolve("anthropic", "claude-x", Usage(1_000_000, cached: 400_000, shortWrite: 200_000, longWrite: null))!;

        cost.Total().ShouldBe((cost.InputCost ?? 0) + (cost.OutputCost ?? 0) + (cost.CachedInputCost ?? 0) + (cost.CacheWriteCost ?? 0));

        // 🚨 The load-bearing half: a three-term sum would be SMALLER.
        cost.Total()!.Value.ShouldBeGreaterThan((cost.InputCost ?? 0) + (cost.OutputCost ?? 0) + (cost.CachedInputCost ?? 0));
    }

    private static ModelDescriptor WritePricedModel()
        => new()
        {
            Name = "claude-x",
            InputCostPerMillionTokens = 10m,
            OutputCostPerMillionTokens = 20m,
            CachedInputCostPerMillionTokens = 1m,
            CacheWriteCostPerMillionTokens = 12.5m,
            CacheWriteLongCostPerMillionTokens = 20m,
        };

    private static ModelDescriptor CachePricedModelWithoutWriteRates()
        => WritePricedModel() with
        {
            CacheWriteCostPerMillionTokens = null,
            CacheWriteLongCostPerMillionTokens = null,
        };

    private static RunUsage Usage(long input, long? cached, long? shortWrite, long? longWrite)
        => new()
        {
            InputTokens = input,
            OutputTokens = 0,
            TotalTokens = input,
            CachedInputTokens = cached,
            CacheWriteInputTokens = shortWrite,
            CacheWriteLongInputTokens = longWrite,
        };

    private static RunPricingResolver CreateResolver(ModelDescriptor model, ILogger<RunPricingResolver>? logger = null)
        => new(
            new ModelProviderRegistry([new FakeModelProvider(name: "anthropic", models: [model])]),
            Options.Create(new TraconOptions
            {
                Pricing = new TraconPricingOptions { Currency = "USD" },
            }),
            logger);

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
