using Microsoft.Extensions.AI;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;
using static Tracon.AspNetCore.FunctionalTests.Infrastructure.NodeUsageHost;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// A node that reports without bound, or with amounts no column can hold, is stopped at a limit
/// that is visible, not silently.
/// </summary>
public sealed class NodeUsageLimitsTests
{
    [Fact]
    public async Task A_run_records_at_most_a_thousand_items_and_the_node_is_told_about_the_next()
    {
        var accepted = 0;
        var refused = 0;

        await using var host = await StartAsync(builder => builder
            .AddWorkflowFunction<List<ChatMessage>, List<ChatMessage>>(
                "narrate",
                _ => async (messages, context, _) =>
                {
                    for (var index = 0; index < 1_005; index++)
                    {
                        if (await context.ReportUsageAsync(Speech(0.001m)))
                        {
                            accepted++;
                        }
                        else
                        {
                            refused++;
                        }
                    }

                    return messages;
                }));

        await SaveAsync(host, Workflow, Function("narrate"));

        var runId = await RunAsync(host);

        accepted.ShouldBe(1_000);
        refused.ShouldBe(5);

        (await UsageAsync(host, runId)).Count.ShouldBe(1_000);

        var total = (await RecordAsync(host, runId)).NonTokenCost.ShouldNotBeNull();

        // The refused items are neither added nor counted: the node was told, and the log says why.
        total.Amount.ShouldBe(1m);
        total.UnsummedItemCount.ShouldBe(0);

        host.Logs.AllText.ShouldContain("the most a run can", Case.Sensitive);
    }

    [Fact]
    public async Task A_sum_beyond_what_the_column_holds_is_counted_and_never_truncated()
    {
        await using var host = await StartAsync(builder => builder
            .AddWorkflowFunction<List<ChatMessage>, List<ChatMessage>>(
                "render",
                _ => async (messages, context, _) =>
                {
                    // Ten items of the largest amount one item can carry: 10^10 in all, past
                    // numeric(20,10)'s ten integer digits.
                    for (var index = 0; index < 10; index++)
                    {
                        await context.ReportUsageAsync(Speech(1_000_000_000m));
                    }

                    return messages;
                }));

        await SaveAsync(host, Workflow, Function("render"));

        var runId = await RunAsync(host);

        (await UsageAsync(host, runId)).Count.ShouldBe(10);

        var total = (await RecordAsync(host, runId)).NonTokenCost.ShouldNotBeNull();

        total.Amount.ShouldBe(9_000_000_000m);
        total.UnsummedItemCount.ShouldBe(1);
    }
}
