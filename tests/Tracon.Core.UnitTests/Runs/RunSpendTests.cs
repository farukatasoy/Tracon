namespace Tracon.Core.UnitTests.Runs;

/// <summary>
/// <see cref="RunSpend"/> is the ONE place a run's token cost and its reported amount are
/// added; every decision about money takes its number from it.
/// </summary>
public sealed class RunSpendTests
{
    private static RunCost Tokens(decimal input, decimal output, decimal cached = 0m, string? currency = "USD")
        => new()
        {
            InputCost = input,
            OutputCost = output,
            CachedInputCost = cached == 0m ? null : cached,
            Currency = currency,
            Source = PricingSource.Configuration,
        };

    private static RunNonTokenCost Reported(decimal? amount, string? currency = "USD", int unsummed = 0)
        => new() { Amount = amount, Currency = currency, UnsummedItemCount = unsummed };

    [Fact]
    public void The_two_totals_add_including_the_cache_charge()
    {
        RunSpend.Total(Tokens(1m, 2m, cached: 0.5m), Reported(4m)).ShouldBe(7.5m);
    }

    [Fact]
    public void A_run_that_reported_nothing_is_its_token_total()
    {
        RunSpend.Total(Tokens(1m, 2m), nonTokenCost: null).ShouldBe(3m);
    }

    [Fact]
    public void A_run_with_no_priced_tokens_is_its_reported_amount()
    {
        RunSpend.Total(cost: (RunCost?)null, Reported(4m)).ShouldBe(4m);
        RunSpend.Total(new RunCost { Source = PricingSource.Unknown }, Reported(4m)).ShouldBe(4m);
    }

    [Fact]
    public void Neither_side_priced_is_null_not_zero()
    {
        RunSpend.Total(cost: (RunCost?)null, nonTokenCost: null).ShouldBeNull();
        RunSpend.Total(cost: (RunCost?)null, Reported(amount: null, unsummed: 3)).ShouldBeNull();
        RunSpend.Total(new RunCost { Source = PricingSource.Unknown }, nonTokenCost: null).ShouldBeNull();
    }

    [Fact]
    public void Items_that_carried_no_amount_add_nothing()
    {
        RunSpend.Total(Tokens(1m, 2m), Reported(amount: null, unsummed: 3)).ShouldBe(3m);
    }

    [Fact]
    public void Amounts_in_two_currencies_are_never_added()
    {
        // The token total stands alone; the reported amount is left out, not converted.
        RunSpend.Total(Tokens(1m, 2m, currency: "USD"), Reported(4m, "EUR")).ShouldBe(3m);
    }

    [Theory]
    [InlineData("usd", "USD")]
    [InlineData(null, null)]
    public void The_same_currency_adds_whatever_its_case(string? tokens, string? reported)
    {
        RunSpend.Total(Tokens(1m, 2m, currency: tokens), Reported(4m, reported)).ShouldBe(7m);
    }

    [Fact]
    public void The_tree_overload_adds_the_same_two_terms()
    {
        var tree = new RunTreeCost { InputCost = 1m, OutputCost = 2m, CachedInputCost = 0.5m, Currency = "USD" };

        RunSpend.Total(tree, Reported(4m)).ShouldBe(7.5m);
        RunSpend.Total(tree, nonTokenCost: null).ShouldBe(3.5m);
        RunSpend.Total(cost: (RunTreeCost?)null, Reported(4m)).ShouldBe(4m);
        RunSpend.Total(tree, Reported(4m, "EUR")).ShouldBe(3.5m);
    }
}
