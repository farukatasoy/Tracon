using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;
using static Tracon.AspNetCore.FunctionalTests.Infrastructure.NodeUsageHost;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// A run that is cancelled keeps what its node had already spent, and the tenant's quota counts it.
/// </summary>
/// <remarks>
/// The speech was synthesized and the provider billed it whatever happens to the run one line
/// later. An item written at the end of the run, or only for a run that completed, would lose
/// exactly the spend a cancelled run is most likely to hide.
/// </remarks>
public sealed class NodeUsageCancellationTests
{
    [Fact]
    public async Task A_cancelled_run_keeps_the_item_its_node_reported_and_counts_it_in_the_quota()
    {
        var reported = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var host = await StartAsync(builder => builder
            .AddWorkflowFunction<List<ChatMessage>, List<ChatMessage>>(
                "render",
                _ => async (messages, context, cancellationToken) =>
                {
                    await context.ReportUsageAsync(Speech(1m));
                    reported.SetResult();

                    // Waits for the run to be cancelled.
                    await Task.Delay(Timeout.Infinite, cancellationToken); // delay: simulated

                    return messages;
                }));

        await SaveAsync(host, Workflow, Function("render"));

        var runId = TraconId.NewId();
        using var cancellation = new CancellationTokenSource();
        var runner = host.Services.GetRequiredService<IWorkflowRunner>();

        var drain = Task.Run(async () =>
        {
            try
            {
                await foreach (var _ in runner.RunStreamingAsync(
                                   new WorkflowRunRequest { WorkflowName = Workflow, Message = "go", RunId = runId },
                                   cancellation.Token))
                {
                    // Drain.
                }
            }
            catch (OperationCanceledException)
            {
                // The caller cancelled; the row must still be closed.
            }
        });

        await reported.Task.WaitAsync(TimeSpan.FromSeconds(30));
        await cancellation.CancelAsync();
        await drain.WaitAsync(TimeSpan.FromSeconds(30));

        var record = await RecordAsync(host, runId);

        record.Status.ShouldBe(RunStatus.Canceled);

        (await UsageAsync(host, runId)).ShouldHaveSingleItem().Usage.Cost.ShouldBe(1m);
        record.NonTokenCost.ShouldNotBeNull().Amount.ShouldBe(1m);

        // Consumption is real the moment it is spent: the cancelled run's quota entry has it.
        (await DailyCounterAsync(host, Workflow)).Cost.ShouldBe(1m);
    }
}
