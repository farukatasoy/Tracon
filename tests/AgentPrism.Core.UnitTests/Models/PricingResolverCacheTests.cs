using AgentPrism.Core.UnitTests.Fakes;
using Microsoft.Extensions.Options;

namespace AgentPrism.Core.UnitTests.Models;

/// <summary>
/// Verifies how cached input tokens are priced (phase 68): the computation is
/// subtractive, an undefined cache rate changes nothing, and a self-contradicting
/// usage record never produces a negative charge.
/// </summary>
public sealed class PricingResolverCacheTests
{
    [Fact]
    public void Cached_tokens_are_subtracted_from_the_full_price_input()
    {
        // 300k of the 1M input tokens came from the cache. At 10/M input and 1/M
        // cache read: 700k * 10 / 1M = 7, plus 300k * 1 / 1M = 0.3.
        var resolver = CreateResolver(CachePricedModel());

        var cost = resolver.Resolve("openai", "gpt-x", Usage(input: 1_000_000, output: 0, cached: 300_000));

        cost.ShouldNotBeNull();
        cost.InputCost.ShouldBe(7m);
        cost.CachedInputCost.ShouldBe(0.3m);
    }

    [Fact]
    public void A_cache_hit_costs_strictly_less_than_the_same_call_without_one()
    {
        var resolver = CreateResolver(CachePricedModel());

        var withCache = resolver.Resolve("openai", "gpt-x", Usage(1_000_000, 0, cached: 900_000));
        var withoutCache = resolver.Resolve("openai", "gpt-x", Usage(1_000_000, 0, cached: null));

        Total(withCache).ShouldBeLessThan(Total(withoutCache));
    }

    [Fact]
    public void An_undefined_cache_rate_leaves_the_cost_exactly_as_it_was()
    {
        // 🚨 The regression this guards: a model with no cache rate must produce
        // byte-for-byte the value it produced before cache pricing existed, even
        // when the provider DOES report cached tokens.
        var resolver = CreateResolver(new ModelDescriptor
        {
            Name = "gpt-x",
            InputCostPerMillionTokens = 10m,
            OutputCostPerMillionTokens = 20m,
        });

        var cost = resolver.Resolve("openai", "gpt-x", Usage(1_000_000, 1_000_000, cached: 900_000));

        cost.ShouldNotBeNull();
        cost.InputCost.ShouldBe(10m);
        cost.OutputCost.ShouldBe(20m);
        cost.CachedInputCost.ShouldBeNull();
    }

    [Fact]
    public void An_undefined_cache_rate_does_not_make_the_pricing_source_unknown()
    {
        // PricingSource.Unknown means the MODEL price is missing. A missing cache
        // rate is a different fault and must not be reported as the same one.
        var resolver = CreateResolver(new ModelDescriptor
        {
            Name = "gpt-x",
            InputCostPerMillionTokens = 10m,
        });

        var cost = resolver.Resolve("openai", "gpt-x", Usage(1_000, 1_000, cached: 500));

        cost.ShouldNotBeNull();
        cost.Source.ShouldBe(PricingSource.Catalog);
        cost.Source.ShouldNotBe(PricingSource.Unknown);
    }

    [Fact]
    public void A_zero_cache_rate_is_honoured_and_is_not_treated_as_undefined()
    {
        // 0 states "cache reads are free"; null states "no rate is known". The
        // two must not collapse.
        var resolver = CreateResolver(new ModelDescriptor
        {
            Name = "gpt-x",
            InputCostPerMillionTokens = 10m,
            CachedInputCostPerMillionTokens = 0m,
        });

        var cost = resolver.Resolve("openai", "gpt-x", Usage(1_000_000, 0, cached: 400_000));

        cost.ShouldNotBeNull();
        cost.CachedInputCost.ShouldBe(0m);
        cost.InputCost.ShouldBe(6m);
    }

    [Fact]
    public void A_defined_rate_produces_no_cache_charge_when_the_provider_reported_nothing()
    {
        // 🚨 Distinct from "no rate configured": here the rate EXISTS but the
        // provider stayed silent. A 0m charge would claim an observed cache miss;
        // null says nothing was measured. Same rule as RunUsage's counters.
        var resolver = CreateResolver(CachePricedModel());

        var cost = resolver.Resolve("openai", "gpt-x", Usage(1_000_000, 0, cached: null));

        cost.ShouldNotBeNull();
        cost.CachedInputCost.ShouldBeNull();
        cost.InputCost.ShouldBe(10m);
    }

    [Fact]
    public void A_reported_zero_cache_count_does_produce_a_zero_charge()
    {
        // The other half of the same rule: a provider that reports 0 MEASURED a
        // cache miss, and that measurement survives into the cost record.
        var resolver = CreateResolver(CachePricedModel());

        var cost = resolver.Resolve("openai", "gpt-x", Usage(1_000_000, 0, cached: 0));

        cost.ShouldNotBeNull();
        cost.CachedInputCost.ShouldBe(0m);
        cost.InputCost.ShouldBe(10m);
    }

    [Fact]
    public void Total_adds_all_three_terms_and_stays_null_when_nothing_was_priced()
    {
        // The seam that made the audit's first finding possible: every runtime
        // total (quota, metric, webhook) goes through RunCost.Total().
        var resolver = CreateResolver(CachePricedModel());

        var cost = resolver.Resolve("openai", "gpt-x", Usage(1_000_000, 1_000_000, cached: 400_000));

        cost.ShouldNotBeNull();
        cost.Total().ShouldBe((cost.InputCost ?? 0) + (cost.OutputCost ?? 0) + (cost.CachedInputCost ?? 0));

        // 🚨 The load-bearing half: a two-term sum would be SMALLER.
        cost.Total()!.Value.ShouldBeGreaterThan((cost.InputCost ?? 0) + (cost.OutputCost ?? 0));

        new RunCost { Source = PricingSource.Unknown }.Total().ShouldBeNull();
    }

    [Fact]
    public void More_cached_tokens_than_input_tokens_prices_the_whole_input_at_full_rate()
    {
        // A provider contradicting its own contract is a DATA fault. The count is
        // not clamped into shape: the whole input is charged at the full rate,
        // which can over-report but never under-report or go negative.
        var resolver = CreateResolver(CachePricedModel());

        var cost = resolver.Resolve("openai", "gpt-x", Usage(input: 100, output: 0, cached: 500));

        cost.ShouldNotBeNull();
        cost.InputCost.ShouldBe(100m * 10m / 1_000_000m);
        cost.CachedInputCost.ShouldBeNull();
        Total(cost).ShouldBeGreaterThan(0m);
    }

    [Fact]
    public void Configured_pricing_carries_the_cache_rate_too()
    {
        var provider = new FakeModelProvider(name: "openai", models: [new ModelDescriptor { Name = "gpt-x" }]);
        var resolver = new RunPricingResolver(
            new ModelProviderRegistry([provider]),
            Options.Create(new AgentPrismOptions
            {
                Pricing = new AgentPrismPricingOptions
                {
                    Currency = "USD",
                    Providers =
                    {
                        ["openai"] = new Dictionary<string, ModelPriceOverride>(StringComparer.OrdinalIgnoreCase)
                        {
                            ["gpt-x"] = new()
                            {
                                InputCostPerMillionTokens = 10m,
                                CachedInputCostPerMillionTokens = 1m,
                            },
                        },
                    },
                },
            }));

        var cost = resolver.Resolve("openai", "gpt-x", Usage(1_000_000, 0, cached: 500_000));

        cost.ShouldNotBeNull();
        cost.Source.ShouldBe(PricingSource.Configuration);
        cost.InputCost.ShouldBe(5m);
        cost.CachedInputCost.ShouldBe(0.5m);
    }

    [Fact]
    public void Reasoning_tokens_are_priced_at_the_output_rate_and_add_nothing_extra()
    {
        // Reasoning tokens are counted INSIDE OutputTokens, so recording them must
        // not change the total (open question 2: today's output rate, field open).
        var resolver = CreateResolver(CachePricedModel());

        var withReasoning = resolver.Resolve(
            "openai",
            "gpt-x",
            Usage(0, 1_000_000, cached: null) with { ReasoningTokens = 400_000 });

        var withoutReasoning = resolver.Resolve("openai", "gpt-x", Usage(0, 1_000_000, cached: null));

        Total(withReasoning).ShouldBe(Total(withoutReasoning));
    }

    private static decimal Total(RunCost? cost)
        => (cost?.InputCost ?? 0) + (cost?.OutputCost ?? 0) + (cost?.CachedInputCost ?? 0);

    private static ModelDescriptor CachePricedModel()
        => new()
        {
            Name = "gpt-x",
            InputCostPerMillionTokens = 10m,
            OutputCostPerMillionTokens = 20m,
            CachedInputCostPerMillionTokens = 1m,
        };

    private static RunUsage Usage(long input, long output, long? cached)
        => new()
        {
            InputTokens = input,
            OutputTokens = output,
            TotalTokens = input + output,
            CachedInputTokens = cached,
        };

    private static RunPricingResolver CreateResolver(ModelDescriptor model)
        => new(
            new ModelProviderRegistry([new FakeModelProvider(name: "openai", models: [model])]),
            Options.Create(new AgentPrismOptions
            {
                Pricing = new AgentPrismPricingOptions { Currency = "USD" },
            }));
}
