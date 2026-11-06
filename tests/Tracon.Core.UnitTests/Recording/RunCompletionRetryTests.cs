using Microsoft.Extensions.Logging.Abstractions;
using Tracon.Core.UnitTests.Fakes;
using Tracon.Tests.Common;

namespace Tracon.Core.UnitTests.Recording;

/// <summary>
/// The closing write is the one write of a run that is tried again. Run recording gives up on its first store
/// failure, and one dropped connection is enough to cost the rest of a run's events. If the closing write were
/// dropped with them, the record would stay <c>Running</c> after the run ended and the caller would still get
/// <c>200</c>.
/// </summary>
public sealed class RunCompletionRetryTests
{
    private const string Counter = TraconDiagnostics.RunRecordingFailureCounterName;

    [Fact]
    public async Task A_run_that_lost_an_event_still_gets_its_closing_status()
    {
        using var factory = new TestMeterFactory();
        using var collector = new MetricCollector(factory.Meter);
        using var metrics = new TraconMetrics(factory, TenantTaggedMetricsOptions.Create());

        var inner = new InMemoryRunStore();
        var store = new FaultyRunStore(inner);
        var clock = new ManualClock();
        var writer = NewWriter(store, metrics, clock);

        // One blip: the first event is refused, the store is back for everything after it.
        store.FailEventAppends = true;
        await writer.StartAsync(Start(writer), "hello");
        store.FailEventAppends = false;

        writer.IsDisabled.ShouldBeTrue();

        var pauses = await CloseAsync(writer, clock);

        (await inner.GetRunAsync(writer.RunId))!.Status.ShouldBe(RunStatus.Completed);

        // A failure earlier in the run is usually the blip that is still going on: the writer waits once before it asks.
        pauses.ShouldBe(1);
        store.CompleteAttempts.ShouldBe(1);

        // The lost event is counted once, at its own stage; the closing write that went through adds nothing.
        collector.LongTags(Counter)
            .ShouldHaveSingleItem()[TraconDiagnostics.Tags.RecordingStage]
            .ShouldBe(RunRecordingStages.Event);
    }

    [Fact]
    public async Task A_closing_write_that_fails_once_is_tried_again_and_nothing_is_lost()
    {
        using var factory = new TestMeterFactory();
        using var collector = new MetricCollector(factory.Meter);
        using var metrics = new TraconMetrics(factory, TenantTaggedMetricsOptions.Create());

        var inner = new InMemoryRunStore();
        var store = new FaultyRunStore(inner) { FailNextCompletes = 1 };
        var clock = new ManualClock();
        var writer = NewWriter(store, metrics, clock);

        await writer.StartAsync(Start(writer), "hello");

        var pauses = await CloseAsync(writer, clock);

        (await inner.GetRunAsync(writer.RunId))!.Status.ShouldBe(RunStatus.Completed);
        store.CompleteAttempts.ShouldBe(2);
        pauses.ShouldBe(1);

        // The record is whole, so there is no loss to count and the writer is not disabled.
        collector.LongValues(Counter).ShouldBeEmpty();
        writer.IsDisabled.ShouldBeFalse();
    }

    [Fact]
    public async Task A_closing_write_that_keeps_failing_is_tried_twice_and_counted_once()
    {
        using var factory = new TestMeterFactory();
        using var collector = new MetricCollector(factory.Meter);
        using var metrics = new TraconMetrics(factory, TenantTaggedMetricsOptions.Create());

        var inner = new InMemoryRunStore();
        var store = new FaultyRunStore(inner) { FailCompletes = true };
        var clock = new ManualClock();
        var writer = NewWriter(store, metrics, clock);

        await writer.StartAsync(Start(writer), "hello");

        // The run still answers: a store that stays down costs the record, never the run.
        await CloseAsync(writer, clock);

        store.CompleteAttempts.ShouldBe(2, "one more try, not a loop: a store that is down must not hold a finished run");
        writer.IsDisabled.ShouldBeTrue();

        collector.LongTags(Counter)
            .ShouldHaveSingleItem()[TraconDiagnostics.Tags.RecordingStage]
            .ShouldBe(RunRecordingStages.Completion);
    }

    [Fact]
    public async Task A_lost_event_and_a_lost_closing_write_are_one_lost_record()
    {
        using var factory = new TestMeterFactory();
        using var collector = new MetricCollector(factory.Meter);
        using var metrics = new TraconMetrics(factory, TenantTaggedMetricsOptions.Create());

        var store = new FaultyRunStore(new InMemoryRunStore()) { FailEventAppends = true, FailCompletes = true };
        var clock = new ManualClock();
        var writer = NewWriter(store, metrics, clock);

        await writer.StartAsync(Start(writer), "hello");
        var pauses = await CloseAsync(writer, clock);

        // The counter measures runs that lost their record, not writes: the first loss names the stage.
        store.CompleteAttempts.ShouldBe(2);
        pauses.ShouldBe(2);

        collector.LongTags(Counter)
            .ShouldHaveSingleItem()[TraconDiagnostics.Tags.RecordingStage]
            .ShouldBe(RunRecordingStages.Event);
    }

    [Fact]
    public async Task A_refusal_the_store_means_is_not_tried_again()
    {
        var store = new FaultyRunStore(new InMemoryRunStore()) { RefuseCompletes = true };
        var clock = new ManualClock();
        var writer = NewWriter(store, metrics: null, clock);

        await writer.StartAsync(Start(writer), "hello");

        // The store answers with its own verdict (a run of another tenant, a run it does not know). A second try of the
        // closing write asks the same question.
        var pauses = await CloseAsync(writer, clock);

        store.CompleteAttempts.ShouldBe(1);

        // Nothing failed earlier in this run, so the first try is not preceded by a wait, and a verdict is not followed by one.
        pauses.ShouldBe(0);
        writer.IsDisabled.ShouldBeTrue();
    }

    [Fact]
    public async Task A_run_whose_row_was_never_sent_closes_without_asking_the_store()
    {
        // The row was never written, so there is nothing to close: the closing event reached the store as an append for a
        // run it did not know, the store said so, the writer disabled itself, waited a pause and asked a second time.
        var store = new FaultyRunStore(new InMemoryRunStore());
        var clock = new ManualClock();
        var sink = new RecordingSink();
        var writer = NewWriter(store, metrics: null, clock, sink);

        var pauses = await CloseAsync(writer, clock, RunStatus.Canceled);

        store.CompleteAttempts.ShouldBe(0);
        store.StartAttempts.ShouldBe(0);
        pauses.ShouldBe(0, "a closing write that is not made does not wait");
        clock.Pending.Count.ShouldBe(0);
        writer.IsDisabled.ShouldBeFalse("nothing failed: there was no row to write");

        // The sinks still get the terminal event, as they do when the store fails.
        sink.Types.ShouldBe([RunEventType.RunFailed]);
    }

    [Fact]
    public async Task A_start_with_a_token_that_is_already_cancelled_sends_no_row_and_the_run_closes_without_the_store()
    {
        var store = new FaultyRunStore(new InMemoryRunStore());
        var clock = new ManualClock();
        var writer = NewWriter(store, metrics: null, clock);

        using var stopped = new CancellationTokenSource();
        await stopped.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(
            () => writer.StartAsync(Start(writer), "hello", stopped.Token).AsTask());

        var pauses = await CloseAsync(writer, clock, RunStatus.Canceled);

        store.StartAttempts.ShouldBe(0, "a token that is already cancelled sends nothing");
        store.CompleteAttempts.ShouldBe(0);
        pauses.ShouldBe(0);
        writer.IsDisabled.ShouldBeFalse();
    }

    [Fact]
    public async Task A_row_that_was_sent_and_then_cancelled_still_gets_its_closing_write()
    {
        // A stop can arrive after the store took the row and before the call returned (the caller leaves, an operator
        // cancels). The writer cannot tell that from a row that was never written, so it asks: skipping the closing
        // write would leave the row Running.
        var inner = new InMemoryRunStore();
        var store = new FaultyRunStore(inner);
        var clock = new ManualClock();
        var writer = NewWriter(store, metrics: null, clock);

        using var stopped = new CancellationTokenSource();
        store.AfterStart = () => stopped.Cancel();

        await Should.ThrowAsync<OperationCanceledException>(
            () => writer.StartAsync(Start(writer), "hello", stopped.Token).AsTask());

        store.StartAttempts.ShouldBe(1);

        await CloseAsync(writer, clock, RunStatus.Canceled);

        store.CompleteAttempts.ShouldBe(1);
        (await inner.GetRunAsync(writer.RunId, TestContext.Current.CancellationToken))!.Status.ShouldBe(RunStatus.Canceled);
    }

    [Fact]
    public async Task The_events_after_a_lost_event_are_still_not_written()
    {
        var inner = new InMemoryRunStore();
        var store = new FaultyRunStore(inner);
        var writer = NewWriter(store, metrics: null, new ManualClock());

        store.FailEventAppends = true;
        await writer.StartAsync(Start(writer), "hello");
        store.FailEventAppends = false;

        await writer.AppendAsync(new RunEventDraft(RunEventType.MessageDelta) { Text = "delta" });

        // Only the closing write is tried again. An event written after a gap would be a stream with a hole in it.
        var events = new List<RunEvent>();

        await foreach (var runEvent in inner.ReadEventsAsync(writer.RunId, 0, TestContext.Current.CancellationToken))
        {
            events.Add(runEvent);
        }

        events.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_caller_that_cancels_while_the_writer_waits_ends_the_closing_write()
    {
        var store = new FaultyRunStore(new InMemoryRunStore());
        var clock = new ManualClock();
        var writer = NewWriter(store, metrics: null, clock);

        store.FailEventAppends = true;
        await writer.StartAsync(Start(writer), "hello");
        store.FailEventAppends = false;

        using var caller = new CancellationTokenSource();
        var closing = writer.CompleteAsync(RunStatus.Completed, cancellationToken: caller.Token).AsTask();

        await WaitUntil.TrueAsync(() => HasPause(clock), "the writer to wait before the closing write");
        await caller.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(() => closing);

        store.CompleteAttempts.ShouldBe(0);
    }

    /// <summary>
    /// A closing write against a store that never answers is given up when the budget is spent. Without the budget a
    /// run would wait for the store's own connect and command timeouts, twice, to record a state its caller does not read.
    /// </summary>
    [Fact]
    public async Task A_closing_write_against_a_store_that_never_answers_is_given_up_when_the_budget_is_spent()
    {
        using var factory = new TestMeterFactory();
        using var collector = new MetricCollector(factory.Meter);
        using var metrics = new TraconMetrics(factory, TenantTaggedMetricsOptions.Create());

        var store = new FaultyRunStore(new InMemoryRunStore()) { HangCompletes = true };
        var clock = new ManualClock();
        var writer = NewWriter(store, metrics, clock);

        // The store failed earlier in this run, so it is the unreachable kind of failing and the closing write is bounded
        // from its first try.
        store.FailEventAppends = true;
        await writer.StartAsync(Start(writer), "hello");
        store.FailEventAppends = false;

        var closing = writer.CompleteAsync(RunStatus.Completed).AsTask();

        await WaitUntil.TrueAsync(() => HasPause(clock), "the writer to wait before the closing write");
        clock.Advance(RunEventWriter.CompletionRetryPause);

        await WaitUntil.TrueAsync(() => store.CompleteAttempts == 1, "the closing write to reach the store");
        closing.IsCompleted.ShouldBeFalse("the store has not answered");

        clock.Advance(RunEventWriter.CompletionBudget);
        await closing.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);

        store.CompleteAttempts.ShouldBe(1, "a closing write that spent the budget is not tried again");
        writer.IsDisabled.ShouldBeTrue();

        collector.LongTags(Counter)
            .ShouldHaveSingleItem()[TraconDiagnostics.Tags.RecordingStage]
            .ShouldBe(RunRecordingStages.Event);
    }

    private static bool HasPause(ManualClock clock) => clock.Pending.Any(static due => due <= RunEventWriter.CompletionRetryPause);

    /// <summary>
    /// Closes the run and answers every pause of the writer, so the test never sleeps. Returns how many pauses there
    /// were. The budget timer is armed too, but it is far longer than a pause and is left alone.
    /// </summary>
    private static async Task<int> CloseAsync(RunEventWriter writer, ManualClock clock, RunStatus status = RunStatus.Completed)
    {
        var closing = writer.CompleteAsync(status).AsTask();
        var pauses = 0;

        while (true)
        {
            await WaitUntil.TrueAsync(() => closing.IsCompleted || HasPause(clock), "the writer to finish or to pause");

            if (closing.IsCompleted)
            {
                break;
            }

            pauses++;
            clock.Advance(RunEventWriter.CompletionRetryPause);
        }

        await closing;

        return pauses;
    }

    private static RunEventWriter NewWriter(IRunStore store, TraconMetrics? metrics, TimeProvider clock, IRunEventSink? sink = null)
        => new(store, new TraconRunRecordingOptions(), NullLogger.Instance, TraconId.NewId(), metrics, sink is null ? null : [sink], clock);

    private sealed class RecordingSink : IRunEventSink
    {
        private readonly List<RunEventType> _types = [];

        public IReadOnlyList<RunEventType> Types
        {
            get
            {
                lock (_types)
                {
                    return [.. _types];
                }
            }
        }

        public ValueTask OnEventAsync(RunEvent runEvent, CancellationToken cancellationToken = default)
        {
            lock (_types)
            {
                _types.Add(runEvent.Type);
            }

            return ValueTask.CompletedTask;
        }
    }

    private static RunStartInfo Start(RunEventWriter writer) => new()
    {
        RunId = writer.RunId,
        AgentName = "test-agent",
        StartedAt = DateTimeOffset.UtcNow,
    };
}
