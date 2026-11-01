using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Tracon.Core.UnitTests.Fakes;
using Tracon.Tests.Common;

namespace Tracon.Core.UnitTests.Recording;

/// <summary>
/// A sink that never returns does not hold the run. The writer calls a sink while it holds the append gate of the run,
/// one event at a time, so without a limit one sink that hangs stops every later event of that run and the run with them.
/// </summary>
public sealed class RunEventSinkTimeoutTests
{
    private static readonly TimeSpan Timeout10 = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task A_sink_that_never_returns_is_dropped_for_the_run_when_its_time_is_up()
    {
        using var factory = new TestMeterFactory();
        using var collector = new MetricCollector(factory.Meter);
        using var metrics = new TraconMetrics(factory, TenantTaggedMetricsOptions.Create());

        var clock = new ManualClock();
        var hanging = new HangingSink();
        var healthy = new SpySink();
        var writer = NewWriter(new TraconRunRecordingOptions { SinkTimeout = Timeout10 }, clock, metrics, hanging, healthy);

        var starting = writer.StartAsync(Start(writer), "hello").AsTask();

        await WaitUntil.TrueAsync(() => hanging.Calls == 1, "the writer to hand the first event to the sink");
        starting.IsCompleted.ShouldBeFalse("the sink holds the gate until its time is up");

        clock.Advance(Timeout10);
        await starting.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);

        // The run goes on: the next events do not wait for the dropped sink, and the healthy sink sees every one of them.
        await writer.AppendAsync(new RunEventDraft(RunEventType.MessageDelta) { Text = "delta" });

        hanging.Calls.ShouldBe(1, "a dropped sink is not asked again in this run");
        healthy.Events.Select(static e => e.Type).ShouldBe([RunEventType.RunStarted, RunEventType.MessageDelta]);

        // Counted as the loss of a sink, once, and never as a store failure.
        collector.LongTags(TraconDiagnostics.RunRecordingFailureCounterName)
            .ShouldHaveSingleItem()[TraconDiagnostics.Tags.RecordingStage]
            .ShouldBe(RunRecordingStages.Sink);
        writer.IsDisabled.ShouldBeFalse();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task A_sink_is_waited_for_without_end_when_the_limit_is_off(int seconds)
    {
        var clock = new ManualClock();
        var hanging = new HangingSink();
        var timeout = seconds < 0 ? System.Threading.Timeout.InfiniteTimeSpan : TimeSpan.FromSeconds(seconds);
        var writer = NewWriter(new TraconRunRecordingOptions { SinkTimeout = timeout }, clock, metrics: null, hanging);

        var starting = writer.StartAsync(Start(writer), "hello").AsTask();

        await WaitUntil.TrueAsync(() => hanging.Calls == 1, "the writer to hand the first event to the sink");

        clock.Advance(TimeSpan.FromDays(1));

        starting.IsCompleted.ShouldBeFalse("with no limit the sink may take as long as it likes");

        hanging.Release();
        await starting.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task A_sink_that_fails_after_it_was_dropped_leaves_no_unobserved_exception()
    {
        var hanging = new HangingSink();

        await DropSinkAsync(hanging);

        // The abandoned call fails later. Nobody awaits it, so the writer has to have looked at it: an exception nobody
        // observes is reported by the test host as a failure of the whole run. The sink lets go of its task as it fails it,
        // or the task could not be collected, and a task that is not collected reports nothing either way.
        hanging.Fail(new InvalidOperationException("the sink broke after it was dropped"));

        UnobservedTaskGuard.ForceCollection();

        UnobservedTaskGuard.Take(static e => e.Message.Contains("broke after it was dropped", StringComparison.Ordinal))
            .ShouldBeEmpty("the abandoned call of the sink failed and nobody looked at it");
    }

    /// <summary>Runs a writer until it has dropped the sink, in a frame of its own so that nothing of it stays reachable.</summary>
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static async Task DropSinkAsync(HangingSink hanging)
    {
        var clock = new ManualClock();
        var writer = NewWriter(new TraconRunRecordingOptions { SinkTimeout = Timeout10 }, clock, metrics: null, hanging);

        var starting = writer.StartAsync(Start(writer), "hello").AsTask();

        await WaitUntil.TrueAsync(() => hanging.Calls == 1, "the writer to hand the first event to the sink");
        clock.Advance(Timeout10);
        await starting.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task A_sink_dropped_in_one_run_is_asked_again_by_the_next_run()
    {
        // The sink is registered once and shared by every concurrent run; being dropped is a fact about one run. The other
        // side of it: every run that meets a sink that hangs pays the whole time on its first event.
        var clock = new ManualClock();
        var hanging = new HangingSink();

        var first = NewWriter(new TraconRunRecordingOptions { SinkTimeout = Timeout10 }, clock, metrics: null, hanging);
        var firstStart = first.StartAsync(Start(first), "hello").AsTask();

        await WaitUntil.TrueAsync(() => hanging.Calls == 1, "the first run to hand its first event to the sink");
        clock.Advance(Timeout10);
        await firstStart.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);

        var second = NewWriter(new TraconRunRecordingOptions { SinkTimeout = Timeout10 }, clock, metrics: null, hanging);
        var secondStart = second.StartAsync(Start(second), "hello").AsTask();

        await WaitUntil.TrueAsync(() => hanging.Calls == 2, "the second run to hand its first event to the sink");
        secondStart.IsCompleted.ShouldBeFalse("the second run waits its own time before it gives the sink up");

        clock.Advance(Timeout10);
        await secondStart.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
    }

    [Fact]
    public void The_default_wait_is_thirty_seconds_and_the_option_binds_from_configuration()
    {
        new TraconRunRecordingOptions().SinkTimeout.ShouldBe(TimeSpan.FromSeconds(30));

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection([new KeyValuePair<string, string?>("Tracon:RunRecording:SinkTimeout", "00:00:07")])
            .Build();
        var services = new ServiceCollection();
        services.AddTracon(configuration.GetSection(TraconOptions.SectionName));

        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IOptions<TraconOptions>>().Value.RunRecording.SinkTimeout.ShouldBe(TimeSpan.FromSeconds(7));
    }

    [Fact]
    public async Task A_caller_that_cancels_is_not_a_sink_failure()
    {
        using var factory = new TestMeterFactory();
        using var collector = new MetricCollector(factory.Meter);
        using var metrics = new TraconMetrics(factory, TenantTaggedMetricsOptions.Create());

        var hanging = new HangingSink();
        var writer = NewWriter(new TraconRunRecordingOptions { SinkTimeout = Timeout10 }, new ManualClock(), metrics, hanging);

        using var caller = new CancellationTokenSource();
        var starting = writer.StartAsync(Start(writer), "hello", caller.Token).AsTask();

        await WaitUntil.TrueAsync(() => hanging.Calls == 1, "the writer to hand the first event to the sink");
        await caller.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(() => starting);
        collector.LongValues(TraconDiagnostics.RunRecordingFailureCounterName).ShouldBeEmpty();
    }

    private static RunEventWriter NewWriter(
        TraconRunRecordingOptions options,
        TimeProvider clock,
        TraconMetrics? metrics,
        params IRunEventSink[] sinks)
        => new(new InMemoryRunStore(), options, NullLogger.Instance, TraconId.NewId(), metrics, sinks, clock);

    private static RunStartInfo Start(RunEventWriter writer) => new()
    {
        RunId = writer.RunId,
        AgentName = "test-agent",
        StartedAt = DateTimeOffset.UtcNow,
    };

    /// <summary>Takes the first event and does not give it back until it is released or fails.</summary>
    private sealed class HangingSink : IRunEventSink
    {
        private TaskCompletionSource _pending = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _calls;

        public int Calls => Volatile.Read(ref _calls);

        public ValueTask OnEventAsync(RunEvent runEvent, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _calls);

            return new ValueTask(_pending.Task);
        }

        public void Release() => _pending.TrySetResult();

        /// <summary>Fails the call that is pending and lets go of its task.</summary>
        public void Fail(Exception exception)
        {
            var pending = _pending;
            _pending = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            pending.TrySetException(exception);
        }
    }

    private sealed class SpySink : IRunEventSink
    {
        private readonly Lock _gate = new();
        private readonly List<RunEvent> _events = [];

        public IReadOnlyList<RunEvent> Events
        {
            get
            {
                lock (_gate)
                {
                    return [.. _events];
                }
            }
        }

        public ValueTask OnEventAsync(RunEvent runEvent, CancellationToken cancellationToken = default)
        {
            lock (_gate)
            {
                _events.Add(runEvent);
            }

            return ValueTask.CompletedTask;
        }
    }
}
