using System.Net;
using Microsoft.Extensions.AI;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;
using static Tracon.AspNetCore.FunctionalTests.Infrastructure.NodeUsageHost;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// A workflow function node reports what it spent that is not tokens, and the run records,
/// lists and totals it.
/// </summary>
/// <remarks>
/// <para>
/// 🚨 F-299: a node that synthesized speech, rendered video or used a GPU could report
/// nothing, so none of it reached the run's total, its budget or the tenant's quota; the
/// host kept its own ledger. The cases run through the real host and the real graph:
/// <c>IWorkflowContext</c> carries neither the executor's identity nor a service provider,
/// so the node's name and the run's scope reach the handler only through the ambient
/// flow, and only the real engine shows whether they do.
/// </para>
/// </remarks>
public sealed class NodeUsageTests
{
    [Fact]
    public async Task Two_nodes_report_and_every_item_lands_on_its_own_node_and_run()
    {
        await using var host = await StartAsync(builder => builder
            .AddWorkflowFunction<List<ChatMessage>, List<ChatMessage>>(
                "narrate",
                _ => async (messages, context, _) =>
                {
                    (await context.ReportUsageAsync(Speech(0.03m, characters: 100m))).ShouldBeTrue();
                    (await context.ReportUsageAsync(new ToolCallUsage { Unit = ToolUsageUnits.Seconds, Quantity = 5m }))
                        .ShouldBeTrue();

                    return messages;
                })
            .AddWorkflowFunction<List<ChatMessage>, List<ChatMessage>>(
                "render",
                _ => async (messages, context, _) =>
                {
                    (await context.ReportUsageAsync(new ToolCallUsage
                    {
                        Unit = "gpu-seconds",
                        Quantity = 30m,
                        Cost = 0.9m,
                        Currency = "USD",
                        Provider = "runpod",
                        Model = "a100",
                        IsEstimated = true,
                    })).ShouldBeTrue();

                    return messages;
                }));

        await SaveAsync(host, Workflow, Function("narrate"), Function("render"));

        var runId = await RunAsync(host);

        var items = await UsageAsync(host, runId);

        // The order they were reported in, each under the node that reported it.
        items.Select(static item => item.NodeName).ShouldBe(["narrate", "narrate", "render"]);
        items.ShouldAllBe(item => item.RunId == runId);

        items[0].Usage.Unit.ShouldBe(ToolUsageUnits.Characters);
        items[0].Usage.Quantity.ShouldBe(100m);
        items[0].Usage.Cost.ShouldBe(0.03m);
        items[0].Usage.Provider.ShouldBe("elevenlabs");
        items[0].Usage.Model.ShouldBe("eleven_multilingual_v2");
        items[1].Usage.Cost.ShouldBeNull();
        items[2].Usage.Unit.ShouldBe("gpu-seconds");
        items[2].Usage.IsEstimated.ShouldBeTrue();

        var record = await RecordAsync(host, runId);

        record.Status.ShouldBe(RunStatus.Completed);

        // The total adds the two amounts; the item with no amount is counted, not read as free.
        record.NonTokenCost.ShouldNotBeNull();
        record.NonTokenCost.Amount.ShouldBe(0.93m);
        record.NonTokenCost.Currency.ShouldBe("USD");
        record.NonTokenCost.IsEstimated.ShouldBeTrue();
        record.NonTokenCost.UnsummedItemCount.ShouldBe(1);

        // The workflow row has no tokens of its own: its tree total is the node amount.
        record.TreeNonTokenCost.ShouldNotBeNull().Amount.ShouldBe(0.93m);
        RunSpend.Total(record.TreeCost, record.TreeNonTokenCost).ShouldBe(0.93m);
    }

    [Fact]
    public async Task An_item_in_another_currency_is_listed_and_counted_as_not_added()
    {
        await using var host = await StartAsync(builder => builder
            .AddWorkflowFunction<List<ChatMessage>, List<ChatMessage>>(
                "narrate",
                _ => async (messages, context, _) =>
                {
                    await context.ReportUsageAsync(Speech(1m));
                    await context.ReportUsageAsync(Speech(100m, currency: "EUR"));

                    return messages;
                }));

        await SaveAsync(host, Workflow, Function("narrate"));

        var runId = await RunAsync(host);

        (await UsageAsync(host, runId)).Count.ShouldBe(2);

        var total = (await RecordAsync(host, runId)).NonTokenCost.ShouldNotBeNull();

        // A sum across currencies is not an amount: only the setup's currency adds.
        total.Amount.ShouldBe(1m);
        total.UnsummedItemCount.ShouldBe(1);
    }

    [Fact]
    public async Task A_retried_node_keeps_the_items_of_its_failed_attempt()
    {
        var attempts = 0;

        await using var host = await StartAsync(builder => builder
            .AddWorkflowFunction<List<ChatMessage>, List<ChatMessage>>(
                "render",
                _ => async (messages, context, _) =>
                {
                    var attempt = Interlocked.Increment(ref attempts);

                    // Reported BEFORE the failure: the provider billed the attempt.
                    await context.ReportUsageAsync(Speech(0.5m));

                    if (attempt == 1)
                    {
                        throw new HttpRequestException(
                            "Response status code does not indicate success: 503 (ServiceUnavailable).",
                            inner: null,
                            HttpStatusCode.ServiceUnavailable);
                    }

                    return messages;
                },
                retryPolicy: new WorkflowNodeRetryPolicy { MaxAttempts = 3, InitialDelay = TimeSpan.FromMilliseconds(1) }));

        await SaveAsync(host, Workflow, Function("render"));

        var runId = await RunAsync(host);

        Volatile.Read(ref attempts).ShouldBe(2);

        var items = await UsageAsync(host, runId);

        items.Count.ShouldBe(2);
        items.ShouldAllBe(item => string.Equals(item.NodeName, "render", StringComparison.Ordinal));

        var record = await RecordAsync(host, runId);

        record.Status.ShouldBe(RunStatus.Completed);
        record.NonTokenCost.ShouldNotBeNull().Amount.ShouldBe(1m);
    }

    [Fact]
    public async Task Invalid_items_are_refused_without_stopping_the_node_or_the_run()
    {
        var results = new List<bool>();

        await using var host = await StartAsync(builder => builder
            .AddWorkflowFunction<List<ChatMessage>, List<ChatMessage>>(
                "narrate",
                _ => async (messages, context, _) =>
                {
                    results.Add(await context.ReportUsageAsync(new ToolCallUsage { Unit = string.Empty, Quantity = 1m }));
                    results.Add(await context.ReportUsageAsync(new ToolCallUsage { Unit = "chars", Quantity = -1m }));
                    results.Add(await context.ReportUsageAsync(new ToolCallUsage { Unit = "chars", Quantity = 1m, Cost = -0.5m }));
                    results.Add(await context.ReportUsageAsync(new ToolCallUsage
                    {
                        Unit = "chars",
                        Quantity = decimal.MaxValue,
                        Cost = 1m,
                    }));
                    results.Add(await context.ReportUsageAsync(Speech(0.25m)));

                    return messages;
                }));

        await SaveAsync(host, Workflow, Function("narrate"));

        var runId = await RunAsync(host);

        results.ShouldBe([false, false, false, false, true]);

        // Only the valid item was recorded and counted; the node finished normally.
        (await UsageAsync(host, runId)).ShouldHaveSingleItem();

        var record = await RecordAsync(host, runId);

        record.Status.ShouldBe(RunStatus.Completed);
        record.NonTokenCost.ShouldNotBeNull().Amount.ShouldBe(0.25m);
        record.NonTokenCost.UnsummedItemCount.ShouldBe(0);

        host.Logs.AllText.ShouldContain("refused a non-token usage item", Case.Sensitive);
    }

    [Fact]
    public async Task A_node_that_reports_after_its_timeout_keeps_the_item_in_the_detail_and_out_of_the_total()
    {
        var lateReported = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var host = await StartAsync(builder => builder
            .AddWorkflowFunction<List<ChatMessage>, List<ChatMessage>>(
                "render",
                _ => async (messages, context, _) =>
                {
                    await context.ReportUsageAsync(Speech(0.1m));

                    // 🚨 Ignores its token on purpose: the run stopped waiting for this body at the
                    // timeout, and the body outlives the run's closed row (K-806's case).
                    await Task.Delay(TimeSpan.FromSeconds(2), CancellationToken.None);

                    (await context.ReportUsageAsync(Speech(5m))).ShouldBeTrue();
                    lateReported.SetResult();

                    return messages;
                }));

        await SaveAsync(host, Workflow, Function("render", timeout: TimeSpan.FromMilliseconds(150)));

        var runId = await RunAsync(host);

        var closed = await RecordAsync(host, runId);

        closed.Status.ShouldBe(RunStatus.Failed);
        closed.NonTokenCost.ShouldNotBeNull().Amount.ShouldBe(0.1m);

        await lateReported.Task.WaitAsync(TimeSpan.FromSeconds(30));

        // The late item is in the detail ...
        (await UsageAsync(host, runId)).Select(static item => item.Usage.Cost).ShouldBe([0.1m, 5m]);

        // ... and the total, the quota entry and the webhook already written do not move.
        (await RecordAsync(host, runId)).NonTokenCost.ShouldNotBeNull().Amount.ShouldBe(0.1m);
        (await DailyCounterAsync(host, Workflow)).Cost.ShouldBe(0.1m);

        host.Logs.AllText.ShouldContain("after the run's total was written", Case.Sensitive);
    }

    [Fact]
    public async Task A_run_whose_nodes_report_nothing_has_no_total()
    {
        await using var host = await StartAsync(builder => builder
            .AddWorkflowFunction<List<ChatMessage>, List<ChatMessage>>(
                "tidy",
                _ => (messages, _, _) => Pass(messages)));

        await SaveAsync(host, Workflow, Function("tidy"));

        var runId = await RunAsync(host);

        var record = await RecordAsync(host, runId);

        record.Status.ShouldBe(RunStatus.Completed);
        record.NonTokenCost.ShouldBeNull();
        record.TreeNonTokenCost.ShouldBeNull();
        (await UsageAsync(host, runId)).ShouldBeEmpty();
    }
}
