using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// <c>POST /api/jobs/{id}/cancel</c> reaches the running handler's token, through the
/// real host and its real worker (failure modes 14 and 16), and
/// <c>GET /api/jobs/{id}</c> shows a job's own time limit.
/// </summary>
/// <remarks>
/// Before that change, the endpoint only wrote the store; the description said the worker
/// "checks the cancellation request between items", and a handler that did not ask ran
/// on. The worker now reads a running job's status every <c>PollInterval</c> and cancels
/// the token. Another tenant's cancel must not reach it.
/// </remarks>
public sealed class JobCancelEndpointTests
{
    private const string Key = "contoso.slow";

    [Fact]
    public async Task A_cancel_through_the_endpoint_reaches_the_running_handlers_token()
    {
        await using var host = await StartAsync();
        var probe = host.Services.GetRequiredService<SlowProbe>();
        var id = await QueueAsync(host, "first");

        await probe.Started("first").WaitAsync(TimeSpan.FromSeconds(30));

        using var cancelled = await host.Client.PostAsync($"/tracon/api/jobs/{id}/cancel", content: null);

        cancelled.StatusCode.ShouldBe(HttpStatusCode.NoContent, await cancelled.Content.ReadAsStringAsync());

        // One poll interval or so; the bound is a failure bound.
        await probe.Cancelled("first").WaitAsync(TimeSpan.FromSeconds(30));

        var job = await QueuedWorkflowHarness.WaitForJobAsync(host, id);

        job.Status.ShouldBe(JobStatus.Cancelled);
        job.ErrorMessage.ShouldBeNull();
    }

    [Fact]
    public async Task Another_tenants_cancel_is_a_404_and_does_not_reach_the_handler()
    {
        await using var host = await StartAsync(tenancy: true);
        var probe = host.Services.GetRequiredService<SlowProbe>();
        var id = await QueueAsync(host, "guarded", tenant: "acme");

        await probe.Started("guarded").WaitAsync(TimeSpan.FromSeconds(30));

        using (var foreign = new HttpRequestMessage(HttpMethod.Post, $"/tracon/api/jobs/{id}/cancel"))
        {
            foreign.Headers.Add("X-Tracon-Tenant", "globex");

            using var response = await host.Client.SendAsync(foreign);

            response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        }

        // Several poll intervals pass; the watch reads the job as Running each time.
        await Task.Delay(TimeSpan.FromMilliseconds(500), TestContext.Current.CancellationToken); // delay: negative

        probe.Cancelled("guarded").IsCompleted.ShouldBeFalse("another tenant's cancel must not reach this job");

        var store = host.Services.GetRequiredService<IJobStore>();
        (await store.GetAsync("acme", id, TestContext.Current.CancellationToken))!.Status.ShouldBe(JobStatus.Running);

        // The owner's cancel still works.
        using (var own = new HttpRequestMessage(HttpMethod.Post, $"/tracon/api/jobs/{id}/cancel"))
        {
            own.Headers.Add("X-Tracon-Tenant", "acme");

            using var response = await host.Client.SendAsync(own);

            response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        }

        await probe.Cancelled("guarded").WaitAsync(TimeSpan.FromSeconds(30));
    }

    [Fact]
    public async Task The_job_endpoint_shows_a_jobs_own_time_limit()
    {
        await using var host = await StartAsync();
        var probe = host.Services.GetRequiredService<SlowProbe>();
        var id = await QueueAsync(host, "bounded", maxDuration: TimeSpan.FromMinutes(30));

        await probe.Started("bounded").WaitAsync(TimeSpan.FromSeconds(30));

        using var response = await host.Client.GetAsync($"/tracon/api/jobs/{id}");
        var body = await TraconTestHost.ReadJsonAsync(response);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        body.GetProperty("job").GetProperty("maxDuration").GetString().ShouldBe("00:30:00");

        using var cancelled = await host.Client.PostAsync($"/tracon/api/jobs/{id}/cancel", content: null);
        await probe.Cancelled("bounded").WaitAsync(TimeSpan.FromSeconds(30));
    }

    private static Task<TraconTestHost> StartAsync(bool tenancy = false)
        => TraconTestHost.StartAsync(
            builder =>
            {
                builder.AddJobHandler<SlowHandler>(Key);

                if (tenancy)
                {
                    builder.UseTenancy(static options =>
                    {
                        options.Enabled = true;
                        options.AllowHeaderResolution = true;
                    });
                }
            },
            configureServices: static services =>
            {
                services.AddSingleton<SlowProbe>();
                services.Configure<TraconSchedulingOptions>(static options => options.PollInterval = TimeSpan.FromMilliseconds(50));
            });

    private static async Task<Guid> QueueAsync(TraconTestHost host, string target, string? tenant = null, TimeSpan? maxDuration = null)
    {
        var job = await host.Services.GetRequiredService<IJobDispatcher>().EnqueueAsync(
            new JobRequest
            {
                TenantId = tenant ?? host.Services.GetRequiredService<ITenantContext>().TenantId,
                HandlerKey = Key,
                TargetName = target,
                MaxAttempts = 1,
                MaxDuration = maxDuration,
            },
            TestContext.Current.CancellationToken);

        return job.Id;
    }

    /// <summary>What each running job's handler saw.</summary>
    private sealed class SlowProbe
    {
        private readonly ConcurrentDictionary<string, (TaskCompletionSource Started, TaskCompletionSource Cancelled)> _calls = new(StringComparer.Ordinal);

        public Task Started(string target) => For(target).Started.Task;

        public Task Cancelled(string target) => For(target).Cancelled.Task;

        public (TaskCompletionSource Started, TaskCompletionSource Cancelled) For(string target)
            => _calls.GetOrAdd(target, static _ => (
                new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously),
                new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)));
    }

    /// <summary>Waits on its token; records when it started and when the token fell.</summary>
    private sealed class SlowHandler(SlowProbe probe) : IJobHandler
    {
        public async ValueTask ExecuteAsync(JobContext context, CancellationToken cancellationToken = default)
        {
            var call = probe.For(context.Job.TargetName);

            using var registration = cancellationToken.Register(static state => ((TaskCompletionSource)state!).TrySetResult(), call.Cancelled);

            call.Started.TrySetResult();

            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false); // delay: simulated
        }
    }
}
