using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// Phase 262 (F-765, class scan): cancelling one run of a batch job through
/// <c>POST /api/runs/{id}/cancel</c> must not make the worker run the batch again.
/// </summary>
/// <remarks>
/// A batch job's id is not the id of the runs it opens, so the endpoint's job-first order has
/// no job to cancel there. The item's run stops through the local source and the handler
/// records the item as failed; the handler returns, and the worker has nothing to retry. This
/// pins that, because the single-run fix would not catch it if a batch handler let the
/// cancellation escape.
/// </remarks>
public sealed class BatchItemRunCancelTests
{
    [Fact]
    public async Task A_batch_item_run_cancelled_through_the_endpoint_fails_the_item_and_the_batch_is_not_run_again()
    {
        var hanging = new HangingModelProvider("hanging", respectsCancellation: true);
        await using var host = await TraconTestHost.StartAsync(
            configureTracon: builder => builder
                .AddModelProvider(hanging)
                .AddAgent(TestData.Definition() with { Model = new ModelBinding { Provider = hanging.Name, Model = "hanging-1" } })
                .UseScheduling(static options =>
                {
                    options.RunWorker = true;
                    options.PollInterval = TimeSpan.FromMilliseconds(100);
                    options.MaxAttempts = 3;
                }));

        using var saved = await host.Client.PutAsJsonAsync(
            new Uri("/tracon/api/schedules/batch-probe", UriKind.Relative),
            new JobScheduleSaveRequest
            {
                HandlerKey = JobHandlerKeys.AgentBatch,
                TargetName = "kod-agent",
                Cron = "0 3 * * *",
                TimeZone = "UTC",
                Payload = JsonDocument.Parse("""["only item"]""").RootElement,
                Enabled = true,
            });
        saved.EnsureSuccessStatusCode();

        using var triggered = await host.Client.PostAsJsonAsync(
            new Uri("/tracon/api/schedules/batch-probe/trigger", UriKind.Relative), new { });
        triggered.EnsureSuccessStatusCode();

        await hanging.Started.WaitAsync(TimeSpan.FromSeconds(30));

        var itemRun = (await WaitUntil.ValueAsync(
            async () => await host.Services.GetRequiredService<IRunStore>()
                .QueryRunsAsync(new RunQuery { TenantId = "default", Status = RunStatus.Running, Take = 10 }),
            static runs => runs.Count > 0,
            "the item's run to open"))[0];

        using var cancelled = await host.Client.PostAsync(
            new Uri($"/tracon/api/runs/{itemRun.Id}/cancel", UriKind.Relative), content: null);

        cancelled.StatusCode.ShouldBe(HttpStatusCode.Accepted, await cancelled.Content.ReadAsStringAsync());

        await host.WaitForRunStatusAsync(itemRun.Id, nameof(RunStatus.Canceled));

        var jobs = host.Services.GetRequiredService<IJobStore>();
        var job = await WaitUntil.ValueAsync(
            async () => (await jobs.QueryAsync(new JobQuery { TenantId = "default", Take = 10 })).SingleOrDefault(),
            static candidate => candidate is { Status: JobStatus.Completed or JobStatus.Failed or JobStatus.Cancelled },
            "the batch job to finish");

        // The handler returned normally: one attempt, one model call, no second run of the batch.
        job!.Attempt.ShouldBe(1);
        hanging.Calls.ShouldBe(1);

        var runs = await host.Services.GetRequiredService<IRunStore>().QueryRunsAsync(new RunQuery { TenantId = "default", Take = 10 });

        runs.ShouldHaveSingleItem().Status.ShouldBe(RunStatus.Canceled);
    }
}
