using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
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

    [Fact]
    public async Task A_store_that_cannot_write_an_item_still_counts_it_in_the_tenants_quota_and_the_webhook()
    {
        // The run's recording stops at the first failed write, so its own snapshot never
        // reaches the store. What the node spent is real money all the same: the quota entry
        // and the webhook take the node's share from memory and the rest of the tree from the
        // store, and are not short by the part the store lost.
        var publisher = new CapturingPublisher();

        await using var host = await StartAsync(
            builder =>
            {
                builder.Services.ThrowFrom<IRunStore>(
                    nameof(IRunStore.RecordNonTokenUsageAsync),
                    static () => new InvalidOperationException("the table is unavailable"));

                builder.AddWorkflowFunction<List<ChatMessage>, List<ChatMessage>>(
                    "render",
                    _ => async (messages, context, _) =>
                    {
                        await context.ReportUsageAsync(Speech(0.5m));
                        await context.ReportUsageAsync(Speech(0.5m));

                        return messages;
                    });
            },
            services => services.AddSingleton<IWebhookPublisher>(publisher));

        await SaveAsync(host, Workflow, Function("render"));
        await RunAsync(host);

        (await DailyCounterAsync(host, Workflow)).Cost.ShouldBe(1m);

        var run = publisher.Completed.ShouldHaveSingleItem();

        run.Cost.ShouldBe(1m);
        run.NonTokenCost.ShouldBe(1m);
    }

    private sealed class CapturingPublisher : IWebhookPublisher
    {
        private readonly List<WebhookRunSummary> _completed = [];

        public IReadOnlyList<WebhookRunSummary> Completed
        {
            get
            {
                lock (_completed)
                {
                    return [.. _completed];
                }
            }
        }

        public ValueTask<int> PublishAsync(
            string tenantId,
            string eventType,
            WebhookEventPayload payload,
            CancellationToken cancellationToken = default)
        {
            if (string.Equals(eventType, WebhookEvents.RunCompleted, StringComparison.Ordinal) && payload.Run is { } run)
            {
                lock (_completed)
                {
                    _completed.Add(run);
                }
            }

            return new ValueTask<int>(1);
        }
    }
}
