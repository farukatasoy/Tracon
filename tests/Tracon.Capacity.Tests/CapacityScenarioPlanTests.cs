using Tracon.CapacityDriver;

namespace Tracon.Capacity.Tests;

/// <summary>
/// Which scenario a request runs. A cell that lists three scenarios and issues a fixed number of requests must run each of
/// them equally often however the workers happen to interleave, or a measurement that needs one of them (the queue wait needs
/// a queued request) is missing from some runs of the same cell.
/// </summary>
/// <remarks>
/// The choice used to read the worker that took the request as well as its index. Which worker takes which index is a matter
/// of timing: of the 4 096 ways two workers can split twelve requests, 3 750 left a scenario out or over-represented and 16
/// ran no queued request at all, and a cell that ran none was marked invalid for a missing queue wait. The function now takes
/// the index alone, so there is no timing left to depend on; these tests pin that contract and the balance it gives.
/// </remarks>
public sealed class CapacityScenarioPlanTests
{
    private static readonly string[] Three = [CapacityScenario.Buffered, CapacityScenario.Streaming, CapacityScenario.Queued];

    [Fact]
    public void The_smoke_cell_of_twelve_requests_runs_each_of_its_three_scenarios_four_times()
    {
        var counts = Enumerable.Range(0, 12)
            .GroupBy(index => CapacityScenario.ForRequest(Three, index), StringComparer.Ordinal)
            .ToDictionary(static group => group.Key, static group => group.Count(), StringComparer.Ordinal);

        counts.Keys.ShouldBe(Three, ignoreOrder: true);
        counts.Values.ShouldAllBe(static count => count == 4);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void Any_budget_that_is_a_multiple_of_the_scenario_count_runs_every_scenario_equally_often(int scenarioCount)
    {
        var scenarios = Three.Take(scenarioCount).ToArray();

        // The closed loop's budget is runs per tenant times tenants times scenarios: always a multiple of the count.
        foreach (var budget in Enumerable.Range(1, 40).Select(multiple => multiple * scenarioCount))
        {
            var counts = Enumerable.Range(0, budget)
                .GroupBy(index => CapacityScenario.ForRequest(scenarios, index), StringComparer.Ordinal)
                .ToDictionary(static group => group.Key, static group => group.Count(), StringComparer.Ordinal);

            counts.Keys.ShouldBe(scenarios, ignoreOrder: true);
            counts.Values.ShouldAllBe(count => count == budget / scenarioCount, $"budget {budget}");
        }
    }

    [Fact]
    public void A_request_s_scenario_is_the_same_whenever_and_wherever_it_is_asked_for()
    {
        var first = Enumerable.Range(0, 30).Select(index => CapacityScenario.ForRequest(Three, index)).ToArray();
        var again = Enumerable.Range(0, 30).AsParallel().AsOrdered().Select(index => CapacityScenario.ForRequest(Three, index)).ToArray();

        again.ShouldBe(first);
    }
}
