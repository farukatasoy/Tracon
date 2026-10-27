namespace Tracon.Core.UnitTests.Models;

/// <summary>
/// K-483's class guard at the type level: <see cref="RunCost.Total"/> and
/// <see cref="RunTreeCost.Total"/> sum the four cost terms (input, output, cache
/// read and cache write) — the applied unit-price fields added in phase 132 (F-175)
/// are RATES, not amounts, and must never enter the sum.
/// </summary>
public sealed class RunCostTotalTests
{
    [Fact]
    public void RunCost_Total_ignores_the_unit_price_fields()
    {
        var cost = new RunCost
        {
            InputCost = 1m,
            InputPricePerMillionTokens = 1_000m,
            OutputCost = 2m,
            OutputPricePerMillionTokens = 2_000m,
            CachedInputCost = 0.5m,
            CachedInputPricePerMillionTokens = 500m,
            Currency = "USD",
            Source = PricingSource.Catalog,
        };

        // 1 + 2 + 0.5 = 3.5, NOT the unit prices added on top.
        cost.Total().ShouldBe(3.5m);
    }

    [Fact]
    public void RunCost_Total_is_null_when_no_cost_term_is_known_even_with_unit_prices_set()
    {
        // A defensive case: even if a resolver bug set a unit price with no
        // corresponding cost, Total() must still report "unpriced", not 0.
        var cost = new RunCost
        {
            InputPricePerMillionTokens = 1m,
            Source = PricingSource.Unknown,
        };

        cost.Total().ShouldBeNull();
    }

    [Fact]
    public void RunCost_Total_adds_the_cache_write_charge_as_the_fourth_term_and_ignores_its_rates()
    {
        var cost = new RunCost
        {
            InputCost = 1m,
            OutputCost = 2m,
            CachedInputCost = 0.5m,
            CacheWriteCost = 0.25m,
            CacheWritePricePerMillionTokens = 1_250m,
            CacheWriteLongPricePerMillionTokens = 2_000m,
            Currency = "USD",
            Source = PricingSource.Catalog,
        };

        // 1 + 2 + 0.5 + 0.25, NOT the write rates added on top.
        cost.Total().ShouldBe(3.75m);
    }

    [Fact]
    public void RunCost_Total_is_the_cache_write_charge_alone_when_it_is_the_only_priced_term()
    {
        // A cost whose only known term is the write is still a priced run, not an unpriced one.
        new RunCost { CacheWriteCost = 0.25m, Source = PricingSource.Catalog }.Total().ShouldBe(0.25m);
    }

    [Fact]
    public void RunTreeCost_Total_adds_the_cache_write_charge_as_the_fourth_term()
    {
        var treeCost = new RunTreeCost
        {
            InputCost = 4m,
            OutputCost = 5m,
            CachedInputCost = 1m,
            CacheWriteCost = 2m,
            Currency = "USD",
        };

        treeCost.Total().ShouldBe(12m);
        new RunTreeCost { CacheWriteCost = 2m }.Total().ShouldBe(2m);
        new RunTreeCost().Total().ShouldBeNull();
    }

    [Fact]
    public void RunTreeCost_Total_without_a_cache_write_charge_is_the_sum_of_the_other_three_terms()
    {
        var treeCost = new RunTreeCost
        {
            InputCost = 4m,
            OutputCost = 5m,
            CachedInputCost = 1m,
            Currency = "USD",
        };

        treeCost.Total().ShouldBe(10m);
    }
}
