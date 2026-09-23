using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>Tests for the graceful-shutdown drain gate on the run endpoint (Phase 87.6).</summary>
public sealed class DrainTests
{
    private const string PreferHeaderName = "Prefer";

    private static readonly Uri Run = new("/tracon/api/agents/kod-agent/run", UriKind.Relative);

    [Fact]
    public async Task Not_draining_by_default_run_is_accepted()
    {
        await using var host = await TraconTestHost.StartAsync(
            static builder => builder.AddAgent(TestData.Definition()));

        using var response = await host.Client.PostAsJsonAsync(Run, new AgentRunRequest { Message = "hello" });

        response.StatusCode.ShouldNotBe(HttpStatusCode.ServiceUnavailable);
    }

    [Fact]
    public async Task Draining_rejects_a_new_streaming_run_with_503()
    {
        await using var host = await TraconTestHost.StartAsync(
            static builder => builder.AddAgent(TestData.Definition()),
            configureServices: static services => services.AddSingleton<ITraconDrainState>(new FixedDrainState(isDraining: true)));

        using var response = await host.Client.PostAsJsonAsync(Run, new AgentRunRequest { Message = "hello" });

        response.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);

        var problem = await TraconTestHost.ReadJsonAsync(response);
        problem.GetProperty("title").GetString().ShouldBe("Service is shutting down");
    }

    [Fact]
    public async Task Draining_rejects_a_new_queued_run_with_503()
    {
        await using var host = await TraconTestHost.StartAsync(
            static builder => builder.AddAgent(TestData.Definition()),
            configureServices: static services =>
            {
                services.UseScheduling(static options => options.RunWorker = false);
                services.AddSingleton<ITraconDrainState>(new FixedDrainState(isDraining: true));
            });

        using var request = new HttpRequestMessage(HttpMethod.Post, Run)
        {
            Content = JsonContent.Create(new AgentRunRequest { Message = "hello" }),
        };
        request.Headers.Add(PreferHeaderName, "respond-async");

        using var response = await host.Client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
    }

    [Fact]
    public async Task Draining_stops_the_worker_from_leasing_a_pending_job_then_resumes_once_it_ends()
    {
        var drainState = new MutableDrainState();

        await using var host = await TraconTestHost.StartAsync(
            static builder => builder.AddAgent(TestData.Definition()),
            configureServices: services =>
            {
                services.UseScheduling(static options => options.PollInterval = TimeSpan.FromMilliseconds(20));
                services.AddSingleton<ITraconDrainState>(drainState);
            });

        var jobStore = host.Services.GetRequiredService<IJobStore>();
        var runStore = host.Services.GetRequiredService<IRunStore>();
        var tenantContext = host.Services.GetRequiredService<ITenantContext>();

        var runId = TraconId.NewId();
        var now = DateTimeOffset.UtcNow;

        await runStore.StartRunAsync(new RunStartInfo
        {
            RunId = runId,
            AgentName = "kod-agent",
            Status = RunStatus.Queued,
            StartedAt = now,
            TenantId = tenantContext.TenantId,
        });

        drainState.IsDraining = true;

        var job = await jobStore.EnqueueAsync(
            new JobRecord
            {
                Id = runId,
                TenantId = tenantContext.TenantId,
                HandlerKey = JobHandlerKeys.AgentRun,
                TargetName = "kod-agent",
                Status = JobStatus.Pending,
                Payload = JsonSerializer.SerializeToElement(new { runId = runId.ToString(), message = "hello" }),
                ScheduledFor = now,
                CreatedAt = now,
            },
            []);

        // Give the worker several ticks to (wrongly) pick this up. Phase 184:
        // this used to be a fixed 200 ms, which under load could pass without
        // a single tick - a green test that proved nothing. Every tick reads
        // the drain state first, so counting reads counts refused ticks.
        var readsBefore = drainState.Reads;
        await WaitUntil.TrueAsync(
            () => drainState.Reads >= readsBefore + 3,
            "the worker to tick three times while draining");

        (await jobStore.GetAsync(tenantContext.TenantId, job.Id)).ShouldNotBeNull().Status.ShouldBe(JobStatus.Pending);

        drainState.IsDraining = false;

        var settled = await WaitUntil.ValueAsync(
            async () => (await jobStore.GetAsync(tenantContext.TenantId, job.Id)).ShouldNotBeNull().Status,
            static status => status is not (JobStatus.Pending or JobStatus.Leased or JobStatus.Running),
            "the job to settle once the drain ended");

        settled.ShouldBe(JobStatus.Completed);
    }

    private sealed class FixedDrainState(bool isDraining) : ITraconDrainState
    {
        public bool IsDraining => isDraining;
    }

    private sealed class MutableDrainState : ITraconDrainState
    {
        private volatile bool _isDraining;
        private int _reads;

        /// <summary>How many times anything asked whether the process is draining.</summary>
        public int Reads => Volatile.Read(ref _reads);

        public bool IsDraining
        {
            get
            {
                Interlocked.Increment(ref _reads);

                return _isDraining;
            }

            set => _isDraining = value;
        }
    }
}
