using System.Diagnostics.Metrics;
using Tracon.Workflows.UnitTests.Fakes;

namespace Tracon.Workflows.UnitTests;

/// <summary>
/// The workflow runner is one of only two places in production that construct a
/// <c>RunEventWriter</c>. This holds it to the same bargain the agent path keeps:
/// a store that cannot record the run does not stop the run, and the loss is counted.
/// </summary>
public sealed class WorkflowRecordingFailureTests
{
    [Fact]
    public async Task A_workflow_whose_run_record_cannot_be_written_still_runs_and_counts_the_loss()
    {
        var host = new WorkflowTestHost("writer", "editor");

        await host.SaveAsync(new WorkflowDefinition
        {
            Name = "chain",
            Kind = WorkflowKind.Sequential,
            AgentNames = ["writer", "editor"],
        });

        using var probe = new WorkflowMetricProbe();

        // 🚨 The runner's own store fails; the agents underneath keep the host's
        // healthy store, so what is measured here is the runner's writer and not a
        // child agent's. The compiler forces the runner to pass SOMETHING for the
        // writer's metrics parameter — only this test forces it to pass the metrics
        // it actually holds.
        var runner = host.CreateRunner(new UnwritableRunStore(), probe.Metrics);

        var events = new List<RunEvent>();

        await foreach (var runEvent in runner.RunStreamingAsync(new WorkflowRunRequest
        {
            WorkflowName = "chain",
            Message = "hello",
        }))
        {
            events.Add(runEvent);
        }

        // The run is not interrupted: observability never breaks functionality.
        events.ShouldNotBeEmpty();

        var tags = probe.TagsFor(TraconDiagnostics.RunRecordingFailureCounterName).ShouldHaveSingleItem();

        tags[TraconDiagnostics.Tags.RecordingStage].ShouldBe("start");
        tags[TraconDiagnostics.Tags.TenantId].ShouldBe(host.TenantContext.TenantId);
    }

    /// <summary>Refuses to open a run; every later write is skipped by the writer itself.</summary>
    private sealed class UnwritableRunStore : IRunStore
    {
        public ValueTask<RunRecord> StartRunAsync(RunStartInfo info, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("store unavailable");

        public ValueTask AppendEventAsync(RunEvent runEvent, CancellationToken cancellationToken = default)
            => ValueTask.CompletedTask;

        public ValueTask<long?> GetLastEventSequenceAsync(Guid runId, CancellationToken cancellationToken = default)
            => ValueTask.FromResult<long?>(null);

        public ValueTask CompleteRunAsync(RunCompletion completion, CancellationToken cancellationToken = default)
            => ValueTask.CompletedTask;

        public ValueTask RecordToolInvocationAsync(ToolInvocationRecord invocation, CancellationToken cancellationToken = default)
            => ValueTask.CompletedTask;

        public ValueTask<RunRecord?> GetRunAsync(Guid runId, CancellationToken cancellationToken = default)
            => ValueTask.FromResult<RunRecord?>(null);

        public ValueTask<IReadOnlyList<RunRecord>> QueryRunsAsync(RunQuery query, CancellationToken cancellationToken = default)
            => ValueTask.FromResult<IReadOnlyList<RunRecord>>([]);

        public ValueTask<RunStatistics> GetStatisticsAsync(RunStatisticsQuery query, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public IAsyncEnumerable<RunEvent> ReadEventsAsync(Guid runId, long fromSequence = 0, CancellationToken cancellationToken = default)
            => AsyncEnumerable.Empty<RunEvent>();

        public ValueTask<IReadOnlyList<ToolInvocationRecord>> ListToolInvocationsAsync(Guid runId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public ValueTask<IReadOnlyList<ToolUsage>> GetToolUsageAsync(ToolUsageQuery query, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public ValueTask<IReadOnlyList<ExperimentVariantResult>> GetExperimentResultsAsync(ExperimentResultsQuery query, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public ValueTask<IReadOnlyList<TimeSeriesPoint>> GetTimeSeriesAsync(RunTimeSeriesQuery query, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public ValueTask UpdateRunCostAsync(Guid runId, RunCost? cost, string? tenantId = null, CancellationToken cancellationToken = default)
            => ValueTask.CompletedTask;

        public ValueTask TouchHeartbeatAsync(IReadOnlyCollection<Guid> runIds, DateTimeOffset at, CancellationToken cancellationToken = default)
            => ValueTask.CompletedTask;

        public ValueTask<IReadOnlyList<RunRecord>> ClaimOrphanedRunsAsync(DateTimeOffset staleBefore, int max, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    /// <summary>Owns a metric set over a private meter and collects what is published on it.</summary>
    /// <remarks>
    /// The listener matches its meter by REFERENCE. Tracon's meter name is shared by
    /// every instrument in the process, so a name match would also collect a
    /// neighbouring test's measurements and make "counted once" a race.
    /// </remarks>
    private sealed class WorkflowMetricProbe : IDisposable
    {
        private readonly Meter _meter = new(TraconDiagnostics.MeterName);
        private readonly MeterListener _listener = new();
        private readonly List<(string Name, Dictionary<string, object?> Tags)> _measurements = [];
        private readonly Lock _gate = new();

        public WorkflowMetricProbe()
        {
            Metrics = new TraconMetrics(new SingleMeterFactory(_meter));

            _listener.InstrumentPublished = (instrument, listener) =>
            {
                if (ReferenceEquals(instrument.Meter, _meter))
                {
                    listener.EnableMeasurementEvents(instrument);
                }
            };

            _listener.SetMeasurementEventCallback<long>((instrument, _, tags, _) =>
            {
                // The TagList is a ref struct over the caller's stack; reading it
                // after the callback returns is undefined, so it is copied here.
                var copied = new Dictionary<string, object?>(tags.Length, StringComparer.Ordinal);

                foreach (var tag in tags)
                {
                    copied[tag.Key] = tag.Value;
                }

                lock (_gate)
                {
                    _measurements.Add((instrument.Name, copied));
                }
            });

            _listener.Start();
        }

        public TraconMetrics Metrics { get; }

        public List<IReadOnlyDictionary<string, object?>> TagsFor(string name)
        {
            lock (_gate)
            {
                return
                [
                    .. _measurements
                        .Where(measurement => string.Equals(measurement.Name, name, StringComparison.Ordinal))
                        .Select(measurement => (IReadOnlyDictionary<string, object?>)measurement.Tags),
                ];
            }
        }

        public void Dispose()
        {
            _listener.Dispose();
            Metrics.Dispose();
            _meter.Dispose();
        }

        private sealed class SingleMeterFactory(Meter meter) : IMeterFactory
        {
            public Meter Create(MeterOptions options) => meter;

            public void Dispose()
            {
                // The meter is owned by the probe, which disposes it.
            }
        }
    }
}
