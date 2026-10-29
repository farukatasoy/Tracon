using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// Phase 262 (F-765): a queued agent run that an operator cancels while it runs is not tried
/// again.
/// </summary>
/// <remarks>
/// The endpoint used to stop the handler first and cancel the job second. In between, the
/// worker read the job as <c>Running</c>, took the stopped handler for a failure and, with
/// <c>AsyncRun.MaxAttempts</c> above 1, leased the job again: the model was called twice and
/// the run, closed <c>Canceled</c>, was written <c>Running</c> again. With the default of one
/// attempt the job showed <c>Failed</c>, so the operator's cancel looked like an error.
/// </remarks>
public sealed class QueuedRunCancelRetryTests
{
    private static readonly Uri RunUri = new("/tracon/api/agents/kod-agent/run", UriKind.Relative);

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    public async Task A_queued_run_cancelled_while_it_runs_ends_Cancelled_and_is_not_tried_again(int maxAttempts)
    {
        var hanging = new HangingModelProvider("hanging", respectsCancellation: true);
        await using var host = await TraconTestHost.StartAsync(
            configureTracon: builder => builder
                .AddModelProvider(hanging)
                .AddAgent(TestData.Definition() with { Model = new ModelBinding { Provider = hanging.Name, Model = "hanging-1" } })
                .UseScheduling(static options => options.PollInterval = TimeSpan.FromMilliseconds(200)),
            configureServices: services => services.Configure<TraconAsyncRunOptions>(options => options.MaxAttempts = maxAttempts));

        using var request = new HttpRequestMessage(HttpMethod.Post, RunUri) { Content = JsonContent.Create(new AgentRunRequest { Message = "hello" }) };
        request.Headers.Add("Prefer", "respond-async");

        using var accepted = await host.Client.SendAsync(request);
        accepted.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        var runId = (await TraconTestHost.ReadJsonAsync(accepted)).GetProperty("runId").GetGuid();

        await hanging.Started.WaitAsync(TimeSpan.FromSeconds(30));

        using var cancelled = await host.Client.PostAsync(
            new Uri($"/tracon/api/runs/{runId}/cancel", UriKind.Relative), content: null);
        cancelled.StatusCode.ShouldBe(HttpStatusCode.Accepted, await cancelled.Content.ReadAsStringAsync());

        await host.WaitForRunStatusAsync(runId, nameof(RunStatus.Canceled));

        // The job was cancelled before the handler stopped, so it is terminal at once: the store
        // refuses a retry on a Cancelled job. Before the fix it read Running here, then Pending.
        var job = await host.Services.GetRequiredService<IJobStore>().GetAsync("default", runId);

        job.ShouldNotBeNull().Status.ShouldBe(JobStatus.Cancelled);
        job.Attempt.ShouldBe(1);
        hanging.Calls.ShouldBe(1);

        using var run = await host.Client.GetAsync(new Uri($"/tracon/api/runs/{runId}", UriKind.Relative));
        (await TraconTestHost.ReadJsonAsync(run)).GetProperty("status").GetString().ShouldBe(nameof(RunStatus.Canceled));
    }
}
