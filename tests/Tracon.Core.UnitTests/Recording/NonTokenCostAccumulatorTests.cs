namespace Tracon.Core.UnitTests.Recording;

/// <summary>
/// The one rule of what adds into a run's non-token total, and what the run is told it did
/// not add.
/// </summary>
public sealed class NonTokenCostAccumulatorTests
{
    private static ToolCallUsage Item(
        decimal? cost,
        string? currency = "USD",
        bool estimated = false,
        string unit = ToolUsageUnits.Characters,
        decimal quantity = 10m)
        => new() { Unit = unit, Quantity = quantity, Cost = cost, Currency = currency, IsEstimated = estimated };

    [Fact]
    public void A_run_that_reported_nothing_has_no_total_not_a_zero()
    {
        new NonTokenCostAccumulator("USD").Seal().ShouldBeNull();
    }

    [Fact]
    public void Amounts_in_the_run_currency_add_up()
    {
        var accumulator = new NonTokenCostAccumulator("USD");

        accumulator.AddNodeItem(Item(0.25m)).ShouldBe(NonTokenAddStatus.Added);
        accumulator.AddNodeItem(Item(0.5m)).ShouldBe(NonTokenAddStatus.Added);
        accumulator.ReportToolMeasurement("call-1", Item(1m));

        var total = accumulator.Seal().ShouldNotBeNull();

        total.Amount.ShouldBe(1.75m);
        total.Currency.ShouldBe("USD");
        total.IsEstimated.ShouldBeFalse();
        total.UnsummedItemCount.ShouldBe(0);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("usd")]
    public void An_item_that_names_no_currency_or_the_run_currency_adds(string? label)
    {
        var accumulator = new NonTokenCostAccumulator("USD");

        accumulator.AddNodeItem(Item(2m, label));

        accumulator.Seal()!.Amount.ShouldBe(2m);
    }

    [Fact]
    public void An_item_in_another_currency_is_counted_and_never_added()
    {
        var accumulator = new NonTokenCostAccumulator("USD");

        accumulator.AddNodeItem(Item(3m));
        accumulator.AddNodeItem(Item(100m, "EUR"));

        var total = accumulator.Seal()!;

        total.Amount.ShouldBe(3m);
        total.UnsummedItemCount.ShouldBe(1);
    }

    [Fact]
    public void An_item_without_an_amount_is_counted_and_never_read_as_free()
    {
        var accumulator = new NonTokenCostAccumulator("USD");

        accumulator.AddNodeItem(Item(null));
        accumulator.ReportToolMeasurement("call-2", Item(null, unit: ToolUsageUnits.Seconds));

        var total = accumulator.Seal()!;

        // Reported, but nothing could be priced: unknown, not zero.
        total.Amount.ShouldBeNull();
        total.UnsummedItemCount.ShouldBe(2);
    }

    [Fact]
    public void A_zero_amount_is_a_priced_item_and_adds_as_zero()
    {
        var accumulator = new NonTokenCostAccumulator("USD");

        accumulator.AddNodeItem(Item(0m));

        accumulator.Seal()!.Amount.ShouldBe(0m);
    }

    [Fact]
    public void The_estimated_flag_rises_only_for_an_estimated_item_that_added()
    {
        var accumulator = new NonTokenCostAccumulator("USD");

        accumulator.AddNodeItem(Item(1m));
        accumulator.AddNodeItem(Item(null, estimated: true));
        accumulator.AddNodeItem(Item(1m, "EUR", estimated: true));

        accumulator.Seal()!.IsEstimated.ShouldBeFalse();

        var estimated = new NonTokenCostAccumulator("USD");
        estimated.AddNodeItem(Item(1m, estimated: true));

        estimated.Seal()!.IsEstimated.ShouldBeTrue();
    }

    [Fact]
    public void Without_a_configured_currency_only_an_unlabelled_item_adds()
    {
        var accumulator = new NonTokenCostAccumulator(currency: null);

        accumulator.AddNodeItem(Item(1m, currency: null));
        accumulator.AddNodeItem(Item(5m, "USD"));

        var total = accumulator.Seal()!;

        total.Amount.ShouldBe(1m);
        total.Currency.ShouldBeNull();
        total.UnsummedItemCount.ShouldBe(1);
    }

    [Fact]
    public void An_amount_that_adds_reaches_the_budget_and_one_that_does_not_does_not()
    {
        var budget = new AgentRunBudget();
        var accumulator = new NonTokenCostAccumulator("USD", budget);

        accumulator.AddNodeItem(Item(2m));
        accumulator.ReportToolMeasurement("call-3", Item(0.5m));
        accumulator.AddNodeItem(Item(100m, "EUR"));
        accumulator.AddNodeItem(Item(null));

        budget.ConsumedCost.ShouldBe(2.5m);
        budget.ConsumedTokens.ShouldBe(0);
    }

    [Fact]
    public void A_second_report_of_the_same_tool_call_replaces_the_first_and_is_never_added_to_it()
    {
        // 🚨 A tool that calls its provider more than once reports the TOTAL at the end:
        // the last report of a call stands (TraconToolUsage.Report). Counting both
        // would bill the same speech twice.
        var budget = new AgentRunBudget();
        var accumulator = new NonTokenCostAccumulator("USD", budget);

        accumulator.ReportToolMeasurement("call-1", Item(1m));
        accumulator.ReportToolMeasurement("call-1", Item(1.5m));
        accumulator.ReportToolMeasurement("call-2", Item(2m));

        accumulator.Seal()!.Amount.ShouldBe(3.5m);

        // The budget heard 1, then only the increase (0.5), then 2: a budget never takes an amount back.
        budget.ConsumedCost.ShouldBe(3.5m);
    }

    [Fact]
    public void A_revised_report_that_lowers_the_amount_lowers_the_total_but_not_the_budget()
    {
        var budget = new AgentRunBudget();
        var accumulator = new NonTokenCostAccumulator("USD", budget);

        accumulator.ReportToolMeasurement("call-1", Item(2m));
        accumulator.ReportToolMeasurement("call-1", Item(0.5m));

        accumulator.Seal()!.Amount.ShouldBe(0.5m);
        budget.ConsumedCost.ShouldBe(2m);
    }

    [Fact]
    public void A_replacement_that_loses_its_amount_is_counted_as_one_the_total_does_not_cover()
    {
        var accumulator = new NonTokenCostAccumulator("USD");

        accumulator.ReportToolMeasurement("call-1", Item(2m));
        accumulator.ReportToolMeasurement("call-1", Item(null));

        var total = accumulator.Seal()!;

        total.Amount.ShouldBeNull();
        total.UnsummedItemCount.ShouldBe(1);
    }

    [Fact]
    public void An_item_reported_after_the_total_was_read_changes_nothing()
    {
        var budget = new AgentRunBudget();
        var accumulator = new NonTokenCostAccumulator("USD", budget);
        accumulator.AddNodeItem(Item(1m));

        var first = accumulator.Seal();

        accumulator.AddNodeItem(Item(50m)).ShouldBe(NonTokenAddStatus.Late);
        accumulator.ReportToolMeasurement("call-4", Item(50m));

        accumulator.Seal().ShouldBe(first);
        budget.ConsumedCost.ShouldBe(1m);
    }

    [Fact]
    public void A_run_reports_at_most_a_thousand_node_items()
    {
        var accumulator = new NonTokenCostAccumulator("USD");

        for (var index = 0; index < NonTokenCostAccumulator.MaxNodeItems; index++)
        {
            accumulator.AddNodeItem(Item(0.001m)).ShouldBe(NonTokenAddStatus.Added);
        }

        accumulator.AddNodeItem(Item(0.001m)).ShouldBe(NonTokenAddStatus.Rejected);

        // The refused item is neither added nor counted.
        var total = accumulator.Seal()!;
        total.Amount.ShouldBe(1m);
        total.UnsummedItemCount.ShouldBe(0);
    }

    [Fact]
    public void The_limit_holds_after_the_total_was_read_too()
    {
        // A body that ignored its timeout and reports in a loop would otherwise write a row
        // per report for as long as it runs.
        var accumulator = new NonTokenCostAccumulator("USD");

        for (var index = 0; index < NonTokenCostAccumulator.MaxNodeItems - 1; index++)
        {
            accumulator.AddNodeItem(Item(0.001m)).ShouldBe(NonTokenAddStatus.Added);
        }

        var total = accumulator.Seal();

        accumulator.AddNodeItem(Item(1m)).ShouldBe(NonTokenAddStatus.Late);
        accumulator.AddNodeItem(Item(1m)).ShouldBe(NonTokenAddStatus.Rejected);
        accumulator.Seal().ShouldBe(total);
    }

    [Fact]
    public void Amounts_beyond_what_the_budget_counter_holds_still_exhaust_a_cost_ceiling()
    {
        var budget = new AgentRunBudget { MaxTotalCost = 1m };
        var accumulator = new NonTokenCostAccumulator("USD", budget);

        // Ten amounts of the largest size one item may carry: 1e10 in all, which the
        // nano-unit counter cannot hold.
        for (var index = 0; index < 10; index++)
        {
            accumulator.AddNodeItem(Item(NonTokenCostAccumulator.MaxAmount)).ShouldBe(NonTokenAddStatus.Added);
        }

        budget.IsCostBudgetExhausted.ShouldBeTrue();
    }

    [Fact]
    public void Tool_measurements_are_not_bounded_by_the_node_item_limit()
    {
        var accumulator = new NonTokenCostAccumulator("USD");

        for (var index = 0; index < NonTokenCostAccumulator.MaxNodeItems + 5; index++)
        {
            accumulator.ReportToolMeasurement($"call-{index}", Item(0.001m));
        }

        accumulator.AddNodeItem(Item(1m)).ShouldBe(NonTokenAddStatus.Added);
    }

    [Fact]
    public void A_sum_the_column_cannot_hold_is_counted_instead_of_truncated()
    {
        var accumulator = new NonTokenCostAccumulator("USD");

        for (var index = 0; index < 10; index++)
        {
            accumulator.ReportToolMeasurement($"call-{index}", Item(NonTokenCostAccumulator.MaxAmount));
        }

        var total = accumulator.Seal()!;

        total.Amount.ShouldNotBeNull();
        total.Amount.Value.ShouldBeLessThanOrEqualTo(NonTokenCostAccumulator.MaxTotal);
        total.UnsummedItemCount.ShouldBeGreaterThan(0);
    }

    [Fact]
    public void Concurrent_reports_lose_no_amount()
    {
        // 🚨 A node that fans out, and concurrent tool calls, report from several threads.
        // Without the lock the read-add-write of the sum loses updates; this fails then.
        var accumulator = new NonTokenCostAccumulator("USD");

        Parallel.For(
            0,
            64,
            new ParallelOptions { MaxDegreeOfParallelism = 16 },
            worker =>
            {
                for (var index = 0; index < 100; index++)
                {
                    accumulator.ReportToolMeasurement($"call-{worker}-{index}", Item(0.01m));
                }
            });

        accumulator.Seal()!.Amount.ShouldBe(64m);
    }

    [Theory]
    [MemberData(nameof(InvalidItems))]
    public void An_invalid_item_is_named_and_refused(ToolCallUsage usage)
    {
        NonTokenCostAccumulator.Validate(usage).ShouldNotBeNull();
    }

    [Fact]
    public void A_valid_item_passes_validation()
    {
        NonTokenCostAccumulator.Validate(Item(1m)).ShouldBeNull();
        NonTokenCostAccumulator.Validate(Item(null, currency: null)).ShouldBeNull();
        NonTokenCostAccumulator.Validate(Item(0m, quantity: 0m)).ShouldBeNull();
    }

    [Fact]
    public void A_tool_measurement_that_fails_validation_is_counted_not_added()
    {
        var accumulator = new NonTokenCostAccumulator("USD");

        accumulator.ReportToolMeasurement("call-8", Item(-5m));
        accumulator.ReportToolMeasurement("call-9", Item(1m));

        var total = accumulator.Seal()!;

        total.Amount.ShouldBe(1m);
        total.UnsummedItemCount.ShouldBe(1);
    }

    public static TheoryData<ToolCallUsage> InvalidItems() => new()
    {
        Item(1m, unit: string.Empty),
        Item(1m, unit: "  "),
        Item(1m, unit: new string('u', NonTokenCostAccumulator.MaxUnitLength + 1)),
        Item(1m, quantity: -1m),
        Item(1m, quantity: NonTokenCostAccumulator.MaxQuantity + 1m),
        Item(-0.01m),
        Item(NonTokenCostAccumulator.MaxAmount + 1m),
        Item(1m, currency: new string('c', NonTokenCostAccumulator.MaxCurrencyLength + 1)),
        new ToolCallUsage { Unit = "u", Quantity = 1m, Provider = new string('p', NonTokenCostAccumulator.MaxProviderLength + 1) },
        new ToolCallUsage { Unit = "u", Quantity = 1m, Model = new string('m', NonTokenCostAccumulator.MaxModelLength + 1) },
    };
}
