namespace AgentPrism.Core.UnitTests.Models;

/// <summary>
/// K-483's class guard at the type level: <see cref="RunCost.Total"/> and
/// <see cref="RunTreeCost.Total"/> sum only the three cost terms — the applied
/// unit-price fields added in phase 132 (F-175) are RATES, not amounts, and
/// must never enter the sum.
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
    public void RunTreeCost_Total_sums_only_the_three_cost_terms()
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
