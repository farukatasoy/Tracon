using System.Diagnostics.Metrics;
using System.Net.Http.Json;
using System.Text.Json;
using AgentPrism.AspNetCore.FunctionalTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace AgentPrism.AspNetCore.FunctionalTests;

/// <summary>
/// Phase 133 — <c>agentprism.job.executions</c> and <c>agentprism.job.duration</c>
/// are verified end to end through a real DI registration, a real HTTP request
/// and the real background worker.
/// </summary>
/// <remarks>
/// <para>
/// The unit tests in <c>AgentPrism.Core.UnitTests</c> hand
/// <c>JobWorkerBackgroundService</c> its <see cref="AgentPrismMetrics"/> by
/// construction. That CANNOT prove the wiring: the parameter is optional, so a
/// container that never supplies it leaves it <see langword="null"/> and every
/// job metric is silently dropped while those tests stay green.
/// </para>
/// <para>
/// This test builds the chain FOR REAL with <c>TestHost</c> — schedule saved
/// over HTTP, job enqueued over HTTP, worker leasing and executing it — and,
/// because <see cref="MeterListener"/> runs in the SAME process, sees what
/// <see cref="AgentPrismMetrics"/> actually published.
/// </para>
/// </remarks>
public sealed class JobMetricEndToEndTests
{
    [Fact]
    public async Task A_job_run_by_the_real_worker_publishes_the_counter_and_the_histogram()
    {
        using var collector = new JobMetricCollector(AgentPrismDiagnostics.MeterName);

        await using var host = await AgentPrismTestHost.StartAsync(
            configureServices: static services => services.UseScheduling(static options =>
            {
                options.RunWorker = true;
                options.PollInterval = TimeSpan.FromMilliseconds(50);
            }));

        await host.Client.PutAsJsonAsync(
            new Uri("/agentprism/api/schedules/metric-probe", UriKind.Relative),
            new JobScheduleSaveRequest
            {
                Kind = JobKind.AgentBatch,
                TargetName = "no-such-agent",
                Cron = "0 3 * * *",
                TimeZone = "UTC",
                Lane = "media",
                Payload = JsonDocument.Parse("""["a"]""").RootElement,
                Enabled = true,
            });

        using var triggered = await host.Client.PostAsJsonAsync(
            new Uri("/agentprism/api/schedules/metric-probe/trigger", UriKind.Relative),
            new { });

        triggered.EnsureSuccessStatusCode();

        // "media" (this schedule's own lane, set above) discriminates this
        // test's own measurement from another, concurrently-running test's -
        // MeterListener is process-wide. A few other tests also use the
        // string "media" as a Lane value, but RecordJob (the only source of
        // this instrument) fires exclusively from JobWorkerBackgroundService,
        // and none of those other tests enables a real worker
        // (UseScheduling(o => o.RunWorker = true)) - so none of them can
        // actually publish a measurement under this name to collide with.
        static bool IsThisTestsLane(Dictionary<string, object?> tags)
            => Equals(tags.GetValueOrDefault(AgentPrismDiagnostics.Tags.Lane), "media");

        var counter = await collector.WaitForAsync(AgentPrismDiagnostics.JobCounterName, IsThisTestsLane);

        counter.Value.ShouldBe(1);
        counter.Tags[AgentPrismDiagnostics.Tags.Lane].ShouldBe("media");
        counter.Tags[AgentPrismDiagnostics.Tags.JobKind].ShouldBe(nameof(JobKind.AgentBatch));

        // The target agent does not exist, so every item fails and the job's
        // terminal status is Failed. What matters here is that a TERMINAL
        // status reached the counter through the real chain.
        counter.Tags.ShouldContainKey(AgentPrismDiagnostics.Tags.JobStatus);
        counter.Tags.ShouldContainKey(AgentPrismDiagnostics.Tags.TenantId);

        var duration = await collector.WaitForAsync(AgentPrismDiagnostics.JobDurationName, IsThisTestsLane);

        duration.Value.ShouldBeGreaterThanOrEqualTo(0);
        duration.Tags[AgentPrismDiagnostics.Tags.Lane].ShouldBe("media");
        duration.Tags.ShouldNotContainKey(AgentPrismDiagnostics.Tags.TenantId);
    }

    [Fact]
    public async Task The_queue_depth_gauge_publishes_nothing_in_a_default_installation()
    {
        using var collector = new JobMetricCollector(AgentPrismDiagnostics.MeterName);

        await using var host = await AgentPrismTestHost.StartAsync(
            configureServices: static services => services.UseScheduling(static options => options.RunWorker = false));

        await host.Client.PutAsJsonAsync(
            new Uri("/agentprism/api/schedules/depth-probe", UriKind.Relative),
            new JobScheduleSaveRequest
            {
                Kind = JobKind.AgentBatch,
                TargetName = "no-such-agent",
                Cron = "0 3 * * *",
                TimeZone = "UTC",
                Payload = JsonDocument.Parse("""["a"]""").RootElement,
                Enabled = true,
            });

        await host.Client.PostAsJsonAsync(
            new Uri("/agentprism/api/schedules/depth-probe/trigger", UriKind.Relative),
            new { });

        // The job is sitting in the queue, yet the gauge stays silent: it is
        // opt-in, and a default installation issues no depth query at all.
        collector.Scrape(AgentPrismDiagnostics.JobQueueDepthGaugeName).ShouldBeEmpty();
    }

    /// <summary>Collects <c>long</c> and <c>double</c> job measurements from a named meter.</summary>
    private sealed class JobMetricCollector : IDisposable
    {
        private readonly MeterListener _listener = new();
        private readonly List<(string Name, double Value, Dictionary<string, object?> Tags)> _measurements = [];
        private readonly Lock _gate = new();

        public JobMetricCollector(string meterName)
        {
            _listener.InstrumentPublished = (instrument, listener) =>
            {
                if (string.Equals(instrument.Meter.Name, meterName, StringComparison.Ordinal))
                {
                    listener.EnableMeasurementEvents(instrument);
                }
            };

            _listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) => Add(instrument.Name, value, tags));
            _listener.SetMeasurementEventCallback<double>((instrument, value, tags, _) => Add(instrument.Name, value, tags));

            _listener.Start();
        }

        /// <summary>
        /// Waits for the first measurement published under <paramref name="name"/>
        /// whose tags satisfy <paramref name="matches"/>.
        /// </summary>
        /// <param name="name">The instrument name.</param>
        /// <param name="matches">
        /// A predicate over the measurement's tags. Required: <see cref="MeterListener"/>
        /// is process-wide, so a job triggered by a DIFFERENT, concurrently-running
        /// test (any of them queues its own job under the SAME instrument name) can
        /// publish a measurement in between — a plain "first measurement with this
        /// name" wait is not this test's own. Defaults to "any" for callers that do
        /// not need discrimination.
        /// </param>
        /// <returns>The measurement and its tags.</returns>
        public async Task<(double Value, Dictionary<string, object?> Tags)> WaitForAsync(
            string name, Func<Dictionary<string, object?>, bool>? matches = null)
        {
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(30);

            while (DateTime.UtcNow < deadline)
            {
                var candidate = Named(name).FirstOrDefault(m => matches is null || matches(m.Tags));

                if (candidate.Tags is not null)
                {
                    return candidate;
                }

                await Task.Delay(25, TestContext.Current.CancellationToken);
            }

            throw new TimeoutException($"No matching '{name}' measurement was published in time.");
        }

        /// <summary>Polls the observable instruments and returns what <paramref name="name"/> reported.</summary>
        /// <param name="name">The instrument name.</param>
        /// <returns>The measurements.</returns>
        public List<(double Value, Dictionary<string, object?> Tags)> Scrape(string name)
        {
            lock (_gate)
            {
                _measurements.Clear();
            }

            _listener.RecordObservableInstruments();

            return Named(name);
        }

        public void Dispose() => _listener.Dispose();

        private List<(double Value, Dictionary<string, object?> Tags)> Named(string name)
        {
            lock (_gate)
            {
                return [.. _measurements
                    .Where(m => string.Equals(m.Name, name, StringComparison.Ordinal))
                    .Select(static m => (m.Value, m.Tags))];
            }
        }

        private void Add(string name, double value, ReadOnlySpan<KeyValuePair<string, object?>> tags)
        {
            var map = new Dictionary<string, object?>(StringComparer.Ordinal);

            foreach (var tag in tags)
            {
                map[tag.Key] = tag.Value;
            }

            lock (_gate)
            {
                _measurements.Add((name, value, map));
            }
        }
    }
}
