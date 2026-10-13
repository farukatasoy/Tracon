using Microsoft.Extensions.AI;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;
using static Tracon.AspNetCore.FunctionalTests.Infrastructure.NodeUsageHost;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// Items reported at the same time go to the run and the node that reported them, and none is lost.
/// </summary>
/// <remarks>
/// The run's scope and the node's name are ambient values. A flow that leaked one run's
/// scope into another, or a total that lost an update, shows only when the reports really
/// overlap, so a barrier holds both runs inside their nodes until both have reported.
/// </remarks>
public sealed class NodeUsageConcurrencyTests
{
    [Fact]
    public async Task Two_concurrent_runs_never_mix_their_items()
    {
        using var barrier = new Barrier(2);

        await using var host = await StartAsync(builder => builder
            .AddWorkflowFunction<List<ChatMessage>, List<ChatMessage>>(
                "narrate",
                _ => async (messages, context, _) =>
                {
                    // The run's message picks the amount: run A reports 1, run B reports 2.
                    var amount = string.Equals(messages[^1].Text, "A", StringComparison.Ordinal) ? 1m : 2m;

                    for (var index = 0; index < 10; index++)
                    {
                        if (index == 5)
                        {
                            // Neither run goes on until BOTH have reported five items.
                            barrier.SignalAndWait(TimeSpan.FromSeconds(20), CancellationToken.None);
                        }

                        (await context.ReportUsageAsync(Speech(amount))).ShouldBeTrue();
                    }

                    return messages;
                }));

        await SaveAsync(host, Workflow, Function("narrate"));

        var runs = await Task.WhenAll(RunAsync(host, message: "A"), RunAsync(host, message: "B"));

        var itemsA = await UsageAsync(host, runs[0]);
        var itemsB = await UsageAsync(host, runs[1]);

        itemsA.Count.ShouldBe(10);
        itemsB.Count.ShouldBe(10);
        itemsA.ShouldAllBe(item => item.RunId == runs[0] && item.Usage.Cost == 1m);
        itemsB.ShouldAllBe(item => item.RunId == runs[1] && item.Usage.Cost == 2m);

        (await RecordAsync(host, runs[0])).NonTokenCost.ShouldNotBeNull().Amount.ShouldBe(10m);
        (await RecordAsync(host, runs[1])).NonTokenCost.ShouldNotBeNull().Amount.ShouldBe(20m);
    }

    [Fact]
    public async Task A_node_that_fans_out_loses_no_item_and_no_amount()
    {
        await using var host = await StartAsync(builder => builder
            .AddWorkflowFunction<List<ChatMessage>, List<ChatMessage>>(
                "narrate",
                _ => async (messages, context, _) =>
                {
                    // 🚨 Fifty tasks, started from the node, report at once. Each reaches the run
                    // through the flow it inherited, not through a field.
                    var reports = Enumerable.Range(0, 50)
                        .Select(_ => Task.Run(async () => await context.ReportUsageAsync(Speech(0.01m))))
                        .ToArray();

                    (await Task.WhenAll(reports)).ShouldAllBe(static reported => reported);

                    return messages;
                }));

        await SaveAsync(host, Workflow, Function("narrate"));

        var runId = await RunAsync(host);

        var items = await UsageAsync(host, runId);

        items.Count.ShouldBe(50);
        items.ShouldAllBe(item => string.Equals(item.NodeName, "narrate", StringComparison.Ordinal));
        items.Select(static item => item.Id).Distinct().Count().ShouldBe(50);

        (await RecordAsync(host, runId)).NonTokenCost.ShouldNotBeNull().Amount.ShouldBe(0.5m);
    }
}
