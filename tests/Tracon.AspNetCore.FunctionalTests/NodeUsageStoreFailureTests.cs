using Microsoft.Extensions.AI;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;
using static Tracon.AspNetCore.FunctionalTests.Infrastructure.NodeUsageHost;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// A store that cannot write a reported item never stops the node or the run.
/// </summary>
/// <remarks>
/// Observability does not break functionality: the same rule that lets a failed event write
/// disable the run's recording and nothing else. The node's work is real and its output is the
/// workflow's result.
/// </remarks>
public sealed class NodeUsageStoreFailureTests
{
    [Fact]
    public async Task A_store_that_cannot_write_an_item_does_not_stop_the_node_or_the_run()
    {
        var finished = false;
        var results = new List<bool>();

        await using var host = await StartAsync(builder =>
        {
            builder.Services.ThrowFrom<IRunStore>(
                nameof(IRunStore.RecordNonTokenUsageAsync),
                static () => new InvalidOperationException("the table is unavailable"));

            builder.AddWorkflowFunction<List<ChatMessage>, List<ChatMessage>>(
                "render",
                _ => async (messages, context, _) =>
                {
                    results.Add(await context.ReportUsageAsync(Speech(0.5m)));
                    results.Add(await context.ReportUsageAsync(Speech(0.5m)));
                    finished = true;

                    return messages;
                });
        });

        await SaveAsync(host, Workflow, Function("render"));

        // The stream still ends with the workflow's own result; RunAsync reads it to the end.
        var runId = await RunAsync(host);

        finished.ShouldBeTrue("the node did not run to its end");

        // The node was not told its report failed: the item was counted, the write was lost.
        results.ShouldBe([true, true]);

        host.Logs.AllText.ShouldContain("a non-token usage item could not be recorded", Case.Sensitive);

        runId.ShouldNotBe(Guid.Empty);
    }
}
