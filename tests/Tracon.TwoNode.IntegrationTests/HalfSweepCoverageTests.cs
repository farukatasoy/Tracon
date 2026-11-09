using Tracon.Tests.Workflows;

namespace Tracon.TwoNode.IntegrationTests;

/// <summary>
/// The half of the cancellation sweep that SQL Server runs on a plain local run still reaches every graph and every start,
/// and both ways to cancel.
/// </summary>
/// <remarks>
/// The half is chosen by a rule on the combinations, and a rule can change and still return the same number of rows. This
/// case reads the rule's result, with no server: if it dropped one way to cancel everywhere, the operator's cancel would
/// not be swept on SQL Server on any run but CI's.
/// </remarks>
public sealed class HalfSweepCoverageTests
{
    private static List<(SweepGraph Graph, SweepStart Start, bool OperatorCancel)> Rows(TheoryData<SweepGraph, SweepStart, bool> data)
        => [.. data.Select(static row => (row.Data.Item1, row.Data.Item2, row.Data.Item3))];

    [Fact]
    public void The_half_holds_each_graph_and_start_once_and_both_ways_to_cancel()
    {
        var all = Rows(WorkflowCancellationSweepAcceptanceContract.Combinations());
        var half = Rows(WorkflowCancellationSweepAcceptanceContract.HalfOfCombinations());

        // Every graph and start of the full set is in the half exactly once; only the way to cancel is chosen.
        half.Count.ShouldBe(all.Select(static row => (row.Graph, row.Start)).Distinct().Count());
        half.Select(static row => (row.Graph, row.Start)).Distinct().Count().ShouldBe(half.Count);
        half.ShouldAllBe(row => all.Contains(row));

        // Both ways to cancel are swept, for a fresh run and for a resumed one.
        half.Select(static row => row.OperatorCancel).Distinct().Count().ShouldBe(2);
        half.Where(static row => row.Start == SweepStart.Fresh).Select(static row => row.OperatorCancel).Distinct().Count().ShouldBe(2);
        half.Where(static row => row.Start == SweepStart.Resume).Select(static row => row.OperatorCancel).Distinct().Count().ShouldBe(2);
    }
}
