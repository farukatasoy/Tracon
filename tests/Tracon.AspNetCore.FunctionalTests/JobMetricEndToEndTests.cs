using System.Diagnostics.Metrics;
using System.Net.Http.Json;
using System.Text.Json;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// Phase 133 — <c>tracon.job.executions</c> and <c>tracon.job.duration</c>
/// are verified end to end through a real DI registration, a real HTTP request
/// and the real background worker.
/// </summary>
/// <remarks>
/// <para>
/// The unit tests in <c>Tracon.Core.UnitTests</c> hand
/// <c>JobWorkerBackgroundService</c> its <see cref="TraconMetrics"/> by
/// construction. That CANNOT prove the wiring: the parameter is optional, so a
/// container that never supplies it leaves it <see langword="null"/> and every
/// job metric is silently dropped while those tests stay green.
/// </para>
/// <para>
/// This test builds the chain FOR REAL with <c>TestHost</c> — schedule saved
/// over HTTP, job enqueued over HTTP, worker leasing and executing it — and,
/// because <see cref="MeterListener"/> runs in the SAME process, sees what
/// <see cref="TraconMetrics"/> actually published.
/// </para>
/// </remarks>
public sealed class JobMetricEndToEndTests
{
    [Fact]
    public async Task A_job_run_by_the_real_worker_publishes_the_counter_and_the_histogram()
    {
        using var meterFactory = new TestMeterFactory();
        using var collector = new MeterInstanceCollector(meterFactory.Meter);

        await using var host = await TraconTestHost.StartAsync(
            configureServices: services =>
            {
                services.AddSingleton<IMeterFactory>(meterFactory);
                services.UseScheduling(static options =>
                {
                    options.RunWorker = true;
                    options.PollInterval = TimeSpan.FromMilliseconds(50);
                });
            });

        await host.Client.PutAsJsonAsync(
            new Uri("/tracon/api/schedules/metric-probe", UriKind.Relative),
            new JobScheduleSaveRequest
            {
                HandlerKey = JobHandlerKeys.AgentBatch,
                TargetName = "no-such-agent",
                Cron = "0 3 * * *",
                TimeZone = "UTC",
                Lane = "media",
                Payload = JsonDocument.Parse("""["a"]""").RootElement,
                Enabled = true,
            });

        using var triggered = await host.Client.PostAsJsonAsync(
            new Uri("/tracon/api/schedules/metric-probe/trigger", UriKind.Relative),
            new { });

        triggered.EnsureSuccessStatusCode();

        // No tag predicate is needed: the collector is bound to THIS host's own
        // Meter instance, so nothing another test publishes can reach it. The
        // previous version discriminated by Lane == "media" and reasoned in prose
        // about which other tests could collide - MeterListenerIsolationTests now
        // enforces the stronger property instead.
        var counter = await collector.WaitForAsync(TraconDiagnostics.JobCounterName);

        counter.Value.ShouldBe(1);
        counter.Tags[TraconDiagnostics.Tags.Lane].ShouldBe("media");
        counter.Tags[TraconDiagnostics.Tags.JobHandlerKey].ShouldBe(JobHandlerKeys.AgentBatch);

        // The target agent does not exist, so every item fails and the job's
        // terminal status is Failed. What matters here is that a TERMINAL
        // status reached the counter through the real chain.
        counter.Tags.ShouldContainKey(TraconDiagnostics.Tags.JobStatus);
        counter.Tags.ShouldContainKey(TraconDiagnostics.Tags.TenantId);

        var duration = await collector.WaitForAsync(TraconDiagnostics.JobDurationName);

        duration.Value.ShouldBeGreaterThanOrEqualTo(0);
        duration.Tags[TraconDiagnostics.Tags.Lane].ShouldBe("media");
        duration.Tags.ShouldNotContainKey(TraconDiagnostics.Tags.TenantId);
    }

    [Fact]
    public async Task The_queue_depth_gauge_publishes_nothing_in_a_default_installation()
    {
        using var meterFactory = new TestMeterFactory();
        using var collector = new MeterInstanceCollector(meterFactory.Meter);

        await using var host = await TraconTestHost.StartAsync(
            configureServices: services =>
            {
                services.AddSingleton<IMeterFactory>(meterFactory);
                services.UseScheduling(static options => options.RunWorker = false);
            });

        await host.Client.PutAsJsonAsync(
            new Uri("/tracon/api/schedules/depth-probe", UriKind.Relative),
            new JobScheduleSaveRequest
            {
                HandlerKey = JobHandlerKeys.AgentBatch,
                TargetName = "no-such-agent",
                Cron = "0 3 * * *",
                TimeZone = "UTC",
                Payload = JsonDocument.Parse("""["a"]""").RootElement,
                Enabled = true,
            });

        await host.Client.PostAsJsonAsync(
            new Uri("/tracon/api/schedules/depth-probe/trigger", UriKind.Relative),
            new { });

        // The job is sitting in the queue, yet the gauge stays silent: it is
        // opt-in, and a default installation issues no depth query at all.
        collector.Scrape(TraconDiagnostics.JobQueueDepthGaugeName).ShouldBeEmpty();
    }

}
