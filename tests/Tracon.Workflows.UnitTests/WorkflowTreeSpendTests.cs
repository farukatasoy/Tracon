namespace Tracon.Workflows.UnitTests;

/// <summary>
/// <see cref="WorkflowTreeSpend"/>: the workflow's own in-memory snapshot is added to the
/// store's tree total only when the store does not hold it.
/// </summary>
public sealed class WorkflowTreeSpendTests
{
    private static RunNonTokenCost Cost(decimal? amount, string currency = "USD", int unsummed = 0, bool estimated = false)
        => new() { Amount = amount, Currency = currency, UnsummedItemCount = unsummed, IsEstimated = estimated };

    private static RunRecord Row(RunNonTokenCost? own, RunNonTokenCost? tree)
        => new()
        {
            Id = Guid.NewGuid(),
            AgentName = "flow",
            Status = RunStatus.Completed,
            StartedAt = DateTimeOffset.UtcNow,
            NonTokenCost = own,
            TreeNonTokenCost = tree,
        };

    [Fact]
    public void A_row_that_holds_its_snapshot_is_not_counted_twice()
    {
        var own = Cost(1m);
        var tree = Cost(3m);

        WorkflowTreeSpend.NonTokenCost(Row(own, tree), own).ShouldBeSameAs(tree);
    }

    [Fact]
    public void A_row_without_its_snapshot_gets_the_in_memory_one_added_to_the_children()
    {
        var result = WorkflowTreeSpend.NonTokenCost(
            Row(own: null, tree: Cost(2m, unsummed: 1)),
            Cost(1m, unsummed: 2, estimated: true)).ShouldNotBeNull();

        result.Amount.ShouldBe(3m);
        result.Currency.ShouldBe("USD");
        result.IsEstimated.ShouldBeTrue();
        result.UnsummedItemCount.ShouldBe(3);
    }

    [Fact]
    public void A_tree_the_store_reports_nothing_for_takes_the_in_memory_snapshot_alone()
    {
        var own = Cost(1m);

        WorkflowTreeSpend.NonTokenCost(Row(own: null, tree: null), own).ShouldBeSameAs(own);
    }

    [Fact]
    public void A_store_that_cannot_answer_leaves_the_in_memory_snapshot()
    {
        var own = Cost(1m);

        WorkflowTreeSpend.NonTokenCost(record: null, own).ShouldBeSameAs(own);
    }

    [Fact]
    public void A_run_that_reported_nothing_leaves_the_stores_tree_as_it_is()
    {
        var tree = Cost(2m);

        WorkflowTreeSpend.NonTokenCost(Row(own: null, tree: tree), own: null).ShouldBeSameAs(tree);
        WorkflowTreeSpend.NonTokenCost(Row(own: null, tree: null), own: null).ShouldBeNull();
        WorkflowTreeSpend.NonTokenCost(record: null, own: null).ShouldBeNull();
    }

    [Fact]
    public void An_amount_in_another_currency_leaves_the_tree_without_an_amount()
    {
        // The store leaves a tree in two currencies without an amount; the lost
        // snapshot added here follows the same rule instead of keeping the store's amount
        // and calling the rest "unsummed", which read as a smaller bill.
        var result = WorkflowTreeSpend.NonTokenCost(
            Row(own: null, tree: Cost(2m, "USD")),
            Cost(1m, "EUR")).ShouldNotBeNull();

        result.Amount.ShouldBeNull();
        result.Currency.ShouldBeNull();
        result.CurrencyCount.ShouldBe(2);
        RunSpend.Total((RunTreeCost?)null, result).ShouldBeNull();
    }

    [Fact]
    public void A_tree_the_store_already_left_without_an_amount_stays_without_one()
    {
        var result = WorkflowTreeSpend.NonTokenCost(
            Row(own: null, tree: new RunNonTokenCost { CurrencyCount = 2 }),
            Cost(1m, "USD")).ShouldNotBeNull();

        result.Amount.ShouldBeNull();
        result.CurrencyCount.ShouldBe(2);
    }

    [Fact]
    public void Two_snapshots_with_no_amount_stay_without_an_amount()
    {
        var result = WorkflowTreeSpend.NonTokenCost(
            Row(own: null, tree: Cost(null, unsummed: 1)),
            Cost(null, unsummed: 2)).ShouldNotBeNull();

        result.Amount.ShouldBeNull();
        result.UnsummedItemCount.ShouldBe(3);
    }
}
