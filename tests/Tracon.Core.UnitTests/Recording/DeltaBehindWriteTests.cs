using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Tracon.Core.UnitTests.Fakes;
using Tracon.Tests.Common;

namespace Tracon.Core.UnitTests.Recording;

/// <summary>
/// A streamed text chunk is numbered at once and written to the store behind the stream, in groups. These tests hold the
/// store busy with a group and look at what the writer does meanwhile, because a store that answers at once cannot show
/// whether the writer waited for it.
/// </summary>
public sealed class DeltaBehindWriteTests
{
    private static RunEventDraft Delta(string text = "chunk") => new(RunEventType.MessageDelta) { Text = text };

    [Fact]
    public async Task A_delta_returns_before_the_store_has_written_it()
    {
        var rig = await Rig.StartAsync();
        rig.Store.Hold();

        // The call returns while the store is stuck on the group, which is the whole point: the stream is not held up.
        var runEvent = await rig.Writer.AppendAsync(Delta("first"));

        await WaitUntil.TrueAsync(() => rig.Store.GroupsEntered == 1, "the drain to hand the group to the store");

        runEvent.Sequence.ShouldBe(1);
        (await rig.Store.ReadAsync(rig.Writer.RunId)).Select(static e => e.Type).ShouldBe([RunEventType.RunStarted]);

        rig.Store.Release();
        await rig.Writer.CompleteAsync(RunStatus.Completed);

        (await rig.Store.ReadAsync(rig.Writer.RunId)).Select(static e => e.Text).ShouldContain("first", StringComparer.Ordinal);
    }

    [Fact]
    public async Task Deltas_that_queue_behind_a_group_in_flight_go_out_together()
    {
        var rig = await Rig.StartAsync();
        rig.Store.Hold();

        await rig.Writer.AppendAsync(Delta("first"));
        await WaitUntil.TrueAsync(() => rig.Store.GroupsEntered == 1, "the drain to hand the first group to the store");

        for (var i = 0; i < 9; i++)
        {
            await rig.Writer.AppendAsync(Delta($"queued {i}"));
        }

        rig.Store.Release();
        await rig.Writer.CompleteAsync(RunStatus.Completed);

        // There is no timer and no batch size: the group is whatever waited for the one before it.
        rig.Store.Groups.Select(static group => group.Count).ShouldBe([1, 9]);
        rig.Store.Groups[1].ShouldBe(Enumerable.Range(2, 9).Select(static n => (long)n));
        rig.Store.Singles.ShouldBe([0, 11], "only the start and the closing event are written alone");
    }

    [Fact]
    public async Task An_event_that_is_not_a_delta_waits_for_the_deltas_queued_before_it()
    {
        var rig = await Rig.StartAsync();
        rig.Store.Hold();

        await rig.Writer.AppendAsync(Delta("one"));
        await WaitUntil.TrueAsync(() => rig.Store.GroupsEntered == 1, "the drain to hand the first group to the store");
        await rig.Writer.AppendAsync(Delta("two"));
        await rig.Writer.AppendAsync(Delta("three"));

        var tool = rig.Writer.AppendAsync(new RunEventDraft(RunEventType.ToolInvoking) { ToolName = "lookup", ToolCallId = "c1" }).AsTask();

        tool.IsCompleted.ShouldBeFalse("the tool event is written after the deltas, and the store is still busy with them");

        rig.Store.Release();

        var written = await tool.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
        await rig.Writer.CompleteAsync(RunStatus.Completed);

        // Numbered after the wait, so the stream has no gap, and in the store in the order of the numbers.
        written.Sequence.ShouldBe(4);
        rig.Store.WriteOrder.Take(5).ShouldBe([0L, 1, 2, 3, 4]);
        rig.Store.WriteOrder.ShouldBe(rig.Store.WriteOrder.Order());
    }

    [Fact]
    public async Task Other_events_are_written_before_their_call_returns()
    {
        var rig = await Rig.StartAsync();
        rig.Store.Hold();

        await rig.Writer.AppendAsync(new RunEventDraft(RunEventType.Custom) { CustomType = "contoso.note", Text = "now" });

        // No group was started and the event is in the store already: only streamed text is written behind.
        rig.Store.GroupsEntered.ShouldBe(0);
        (await rig.Store.ReadAsync(rig.Writer.RunId)).Select(static e => e.Type).ShouldBe([RunEventType.RunStarted, RunEventType.Custom]);

        rig.Store.Release();
    }

    [Fact]
    public async Task Reasoning_deltas_are_written_behind_like_message_deltas()
    {
        var rig = await Rig.StartAsync();
        rig.Store.Hold();

        await rig.Writer.AppendAsync(new RunEventDraft(RunEventType.ReasoningDelta) { Text = "thinking" });
        await WaitUntil.TrueAsync(() => rig.Store.GroupsEntered == 1, "the drain to hand the group to the store");

        (await rig.Store.ReadAsync(rig.Writer.RunId)).Select(static e => e.Type).ShouldBe([RunEventType.RunStarted]);

        rig.Store.Release();
        await rig.Writer.CompleteAsync(RunStatus.Completed);

        (await rig.Store.ReadAsync(rig.Writer.RunId)).Select(static e => e.Type).ShouldContain(RunEventType.ReasoningDelta);
    }

    [Fact]
    public async Task With_the_option_off_every_delta_is_written_before_its_call_returns()
    {
        var rig = await Rig.StartAsync(new TraconRunRecordingOptions { WriteDeltasBehind = false });
        rig.Store.Hold();

        await rig.Writer.AppendAsync(Delta("in line"));

        rig.Store.GroupsEntered.ShouldBe(0);
        rig.Store.Singles.ShouldBe([0, 1]);
        (await rig.Store.ReadAsync(rig.Writer.RunId)).Select(static e => e.Text).ShouldContain("in line", StringComparer.Ordinal);
    }

    [Theory]
    [InlineData(RunStatus.Completed)]
    [InlineData(RunStatus.Failed)]
    [InlineData(RunStatus.Canceled)]
    public async Task The_end_of_a_run_writes_what_is_queued_whichever_way_the_run_ends(RunStatus status)
    {
        var rig = await Rig.StartAsync();
        rig.Store.Hold();

        for (var i = 0; i < 20; i++)
        {
            await rig.Writer.AppendAsync(Delta($"chunk {i} "));
        }

        var closing = rig.Writer.CompleteAsync(status, error: status == RunStatus.Failed ? new RunError { Type = "Test", Message = "failed" } : null).AsTask();

        await WaitUntil.TrueAsync(() => rig.Store.GroupsEntered >= 1, "the drain to hand a group to the store");
        closing.IsCompleted.ShouldBeFalse("the end of the run waits for the text of the reply");

        rig.Store.Release();
        await closing.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);

        var events = await rig.Store.ReadAsync(rig.Writer.RunId);
        string.Concat(events.Where(static e => e.Type == RunEventType.MessageDelta).Select(static e => e.Text))
            .ShouldBe(string.Concat(Enumerable.Range(0, 20).Select(static i => $"chunk {i} ")));
        events[^1].Type.ShouldBeOneOf(RunEventType.RunCompleted, RunEventType.RunFailed);
        events.Select(static e => e.Sequence).ShouldBe(Enumerable.Range(0, events.Count).Select(static n => (long)n));
    }

    [Fact]
    public async Task The_end_of_a_run_writes_what_is_queued_even_when_the_callers_token_is_cancelled()
    {
        var rig = await Rig.StartAsync();

        for (var i = 0; i < 5; i++)
        {
            await rig.Writer.AppendAsync(Delta($"chunk {i}"));
        }

        using var gone = new CancellationTokenSource();
        await gone.CancelAsync();

        // The caller is gone, so its own closing event may be refused. The text the model had produced is not its
        // to cancel: it is the only copy of the reply.
        await Should.ThrowAsync<OperationCanceledException>(() => rig.Writer.CompleteAsync(RunStatus.Canceled, cancellationToken: gone.Token).AsTask());

        (await rig.Store.ReadAsync(rig.Writer.RunId)).Count(static e => e.Type == RunEventType.MessageDelta).ShouldBe(5);
    }

    [Fact]
    public async Task Cancelling_the_token_of_an_append_does_not_cancel_its_write()
    {
        var rig = await Rig.StartAsync();
        rig.Store.Hold();

        using var caller = new CancellationTokenSource();
        await rig.Writer.AppendAsync(Delta("kept"), caller.Token);
        await WaitUntil.TrueAsync(() => rig.Store.GroupsEntered == 1, "the drain to hand the group to the store");

        await caller.CancelAsync();
        rig.Store.Release();
        await rig.Writer.CompleteAsync(RunStatus.Completed);

        (await rig.Store.ReadAsync(rig.Writer.RunId)).Select(static e => e.Text).ShouldContain("kept", StringComparer.Ordinal);
    }

    [Fact]
    public async Task The_store_write_runs_in_no_context_of_the_caller()
    {
        var caller = new AsyncLocal<string?> { Value = "the caller's span" };
        var rig = await Rig.StartAsync();
        rig.Store.Probe = () => caller.Value;

        for (var i = 0; i < 20; i++)
        {
            await rig.Writer.AppendAsync(Delta($"chunk {i}"));
        }

        await rig.Writer.CompleteAsync(RunStatus.Completed);

        // The drain belongs to the run. Carrying the caller's context would tie it to a span that has ended, to an
        // ambient tenant that may not be the run's, and to whatever a store lent that caller.
        rig.Store.Observed.ShouldNotBeEmpty();
        rig.Store.Observed.ShouldAllBe(static seen => seen == null);
        caller.Value.ShouldBe("the caller's span");
    }

    [Fact]
    public async Task A_sink_sees_a_delta_while_the_store_still_holds_nothing_of_it()
    {
        var sink = new SpySink();
        var rig = await Rig.StartAsync(sinks: [sink]);
        rig.Store.Hold();

        await rig.Writer.AppendAsync(Delta("seen first"));

        sink.Events.Select(static e => e.Text).ShouldContain("seen first", StringComparer.Ordinal);
        (await rig.Store.ReadAsync(rig.Writer.RunId)).Select(static e => e.Text).ShouldNotContain("seen first", StringComparer.Ordinal);

        rig.Store.Release();
        await rig.Writer.CompleteAsync(RunStatus.Completed);

        // Every event reaches the sink exactly once and in the order of its number, whichever way it was written.
        sink.Events.Select(static e => e.Sequence).ShouldBe(Enumerable.Range(0, sink.Events.Count).Select(static n => (long)n));
    }

    [Fact]
    public async Task Sequence_numbers_have_no_gap_and_no_repeat_and_the_store_sees_them_in_order_under_concurrent_writers()
    {
        const int Writers = 8;
        const int Deltas = 200;

        var rig = await Rig.StartAsync();

        await Task.WhenAll(Enumerable.Range(0, Writers).Select(writer => Task.Run(async () =>
        {
            for (var i = 0; i < Deltas; i++)
            {
                await rig.Writer.AppendAsync(Delta($"w{writer}-{i}"));

                if (i % 25 == 0)
                {
                    await rig.Writer.AppendAsync(new RunEventDraft(RunEventType.ToolInvoking) { ToolName = "lookup", ToolCallId = $"c{writer}-{i}" });
                }
            }
        })));

        await rig.Writer.CompleteAsync(RunStatus.Completed);

        var events = await rig.Store.ReadAsync(rig.Writer.RunId);

        events.Count.ShouldBe(1 + Writers * (Deltas + (Deltas / 25)) + 1);
        events.Select(static e => e.Sequence).ShouldBe(Enumerable.Range(0, events.Count).Select(static n => (long)n));

        // The numbers are given in order and the store receives them in order: no event overtakes an earlier one, so a
        // process that died at any moment would have left a prefix.
        rig.Store.WriteOrder.ShouldBe(rig.Store.WriteOrder.Order());
        rig.Store.WriteOrder.Count.ShouldBe(events.Count);
    }

    [Fact]
    public async Task A_group_the_store_refuses_is_lost_whole_and_counted_once()
    {
        using var factory = new TestMeterFactory();
        using var collector = new MetricCollector(factory.Meter);
        using var metrics = new TraconMetrics(factory, TenantTaggedMetricsOptions.Create());

        var clock = new ManualClock();
        var rig = await Rig.StartAsync(metrics: metrics, clock: clock);
        rig.Store.FailGroups = true;

        await rig.Writer.AppendAsync(Delta("lost"));
        await WaitUntil.TrueAsync(() => rig.Writer.IsDisabled, "the writer to give up on the store");

        // The run goes on. Nothing more is offered to the store, and the stream is not interrupted.
        await rig.Writer.AppendAsync(Delta("never sent"));
        await rig.Writer.AppendAsync(new RunEventDraft(RunEventType.ToolInvoking) { ToolName = "lookup" });

        var closing = rig.Writer.CompleteAsync(RunStatus.Completed).AsTask();
        await WaitUntil.TrueAsync(() => clock.Pending.Any(static due => due <= RunEventWriter.CompletionRetryPause), "the writer to wait before the closing write");
        clock.Advance(RunEventWriter.CompletionRetryPause);
        await closing.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);

        rig.Store.GroupsEntered.ShouldBe(1);
        (await rig.Store.ReadAsync(rig.Writer.RunId)).Select(static e => e.Type).ShouldBe([RunEventType.RunStarted]);

        // The closing write is still made: the run does not stay Running because its text was lost.
        (await rig.Store.GetRunAsync(rig.Writer.RunId))!.Status.ShouldBe(RunStatus.Completed);

        collector.LongValues(TraconDiagnostics.RunRecordingFailureCounterName).Count.ShouldBe(1);
        collector.LongTags(TraconDiagnostics.RunRecordingFailureCounterName)
            .ShouldHaveSingleItem()[TraconDiagnostics.Tags.RecordingStage]
            .ShouldBe(RunRecordingStages.Event);
    }

    [Fact]
    public async Task A_store_that_never_answers_is_given_up_at_the_end_of_the_run_within_the_budget()
    {
        using var factory = new TestMeterFactory();
        using var collector = new MetricCollector(factory.Meter);
        using var metrics = new TraconMetrics(factory, TenantTaggedMetricsOptions.Create());

        var clock = new ManualClock();
        var rig = await Rig.StartAsync(metrics: metrics, clock: clock);
        rig.Store.Hold();

        await rig.Writer.AppendAsync(Delta("stuck"));
        await WaitUntil.TrueAsync(() => rig.Store.GroupsEntered == 1, "the drain to hand the group to the store");

        var closing = rig.Writer.CompleteAsync(RunStatus.Completed).AsTask();

        await WaitUntil.TrueAsync(() => clock.Pending.Any(static due => due <= RunEventWriter.CompletionBudget), "the writer to start its budget");
        closing.IsCompleted.ShouldBeFalse();

        clock.Advance(RunEventWriter.CompletionBudget);
        await WaitUntil.TrueAsync(() => clock.Pending.Any(static due => due <= RunEventWriter.CompletionRetryPause), "the writer to wait before the closing write");
        clock.Advance(RunEventWriter.CompletionRetryPause);
        await closing.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);

        rig.Writer.IsDisabled.ShouldBeTrue();
        collector.LongTags(TraconDiagnostics.RunRecordingFailureCounterName)
            .ShouldHaveSingleItem()[TraconDiagnostics.Tags.RecordingStage]
            .ShouldBe(RunRecordingStages.Event);
        (await rig.Store.GetRunAsync(rig.Writer.RunId))!.Status.ShouldBe(RunStatus.Completed);

        rig.Store.Release();
    }

    [Fact]
    public async Task The_end_of_a_run_leaves_no_timer_armed_on_the_clock_once_the_queue_is_written()
    {
        var clock = new ManualClock();
        var rig = await Rig.StartAsync(clock: clock);
        rig.Store.Hold();

        await rig.Writer.AppendAsync(Delta("queued"));
        await WaitUntil.TrueAsync(() => rig.Store.GroupsEntered == 1, "the drain to hand the group to the store");

        var closing = rig.Writer.CompleteAsync(RunStatus.Completed).AsTask();

        await WaitUntil.TrueAsync(() => clock.Pending.Any(static due => due <= RunEventWriter.CompletionBudget), "the end of the run to start its budget");

        rig.Store.Release();
        await closing.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);

        // A run that is over holds no timer: the budget of the wait was the end of the run's own, and it went with the wait.
        clock.Pending.ShouldBeEmpty();
    }

    [Fact]
    public async Task The_events_not_yet_in_the_store_are_bounded_and_a_full_queue_makes_the_stream_wait_for_the_store()
    {
        var rig = await Rig.StartAsync();
        rig.Store.Hold();

        await rig.Writer.AppendAsync(Delta("in flight"));
        await WaitUntil.TrueAsync(() => rig.Store.GroupsEntered == 1, "the drain to take the first group");

        // The group in flight counts with the queue: both are lost to a process that dies, and both are memory.
        for (var i = 1; i < RunEventWriter.MaxQueuedEvents; i++)
        {
            await rig.Writer.AppendAsync(Delta("."));
        }

        var blocked = rig.Writer.AppendAsync(Delta("one too many")).AsTask();

        blocked.IsCompleted.ShouldBeFalse("the queue is full: the stream waits, as it did for every chunk before the queue");

        rig.Store.Release();
        await blocked.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
        await rig.Writer.CompleteAsync(RunStatus.Completed);

        (await rig.Store.ReadAsync(rig.Writer.RunId)).Count(static e => e.Type == RunEventType.MessageDelta)
            .ShouldBe(RunEventWriter.MaxQueuedEvents + 1);
    }

    [Fact]
    public async Task A_big_group_in_flight_leaves_no_room_for_a_second_queue_of_the_same_size()
    {
        var rig = await Rig.StartAsync();
        rig.Store.Hold();

        // However the first appends divide between the group the drain takes and the queue behind it, the sum is what is
        // bounded: after MaxQueuedEvents appends the next one waits.
        for (var i = 0; i < RunEventWriter.MaxQueuedEvents; i++)
        {
            await rig.Writer.AppendAsync(Delta("."));
        }

        await WaitUntil.TrueAsync(() => rig.Store.GroupsEntered == 1, "the drain to take a group");

        var blocked = rig.Writer.AppendAsync(Delta("one too many")).AsTask();

        blocked.IsCompleted.ShouldBeFalse();

        rig.Store.Release();
        await blocked.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
        await rig.Writer.CompleteAsync(RunStatus.Completed);
    }

    [Fact]
    public async Task The_queue_is_bounded_by_the_text_it_holds_too_and_still_takes_one_event_of_any_size()
    {
        var rig = await Rig.StartAsync(new TraconRunRecordingOptions { MaxPayloadLength = 0 });
        rig.Store.Hold();

        await rig.Writer.AppendAsync(Delta("in flight"));
        await WaitUntil.TrueAsync(() => rig.Store.GroupsEntered == 1, "the drain to take the first group");

        // One event larger than the whole bound is taken: refusing it would stop a run whose model sends big chunks.
        await rig.Writer.AppendAsync(Delta(new string('x', RunEventWriter.MaxQueuedCharacters + 1)));

        var blocked = rig.Writer.AppendAsync(Delta("next")).AsTask();

        blocked.IsCompleted.ShouldBeFalse("the queue holds more text than its bound");

        rig.Store.Release();
        await blocked.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
        await rig.Writer.CompleteAsync(RunStatus.Completed);

        (await rig.Store.ReadAsync(rig.Writer.RunId)).Count(static e => e.Type == RunEventType.MessageDelta).ShouldBe(3);
    }

    [Fact]
    public async Task A_caller_that_gives_up_waiting_for_the_queue_leaves_no_gap_and_loses_nothing()
    {
        var rig = await Rig.StartAsync();
        rig.Store.Hold();

        await rig.Writer.AppendAsync(Delta("one"));
        await WaitUntil.TrueAsync(() => rig.Store.GroupsEntered == 1, "the drain to take the first group");
        await rig.Writer.AppendAsync(Delta("two"));

        // An event that is not a delta waits for the queue; its caller gives up. The event was never numbered, so the
        // numbers that follow are the ones it would have taken.
        using var gone = new CancellationTokenSource();
        var tool = rig.Writer.AppendAsync(new RunEventDraft(RunEventType.ToolInvoking) { ToolName = "lookup" }, gone.Token).AsTask();

        tool.IsCompleted.ShouldBeFalse();
        await gone.CancelAsync();
        await Should.ThrowAsync<OperationCanceledException>(() => tool);

        rig.Store.Release();
        var next = await rig.Writer.AppendAsync(Delta("three"));
        await rig.Writer.CompleteAsync(RunStatus.Completed);

        next.Sequence.ShouldBe(3, "the cancelled event took no number");
        var events = await rig.Store.ReadAsync(rig.Writer.RunId);
        events.Select(static e => e.Sequence).ShouldBe(Enumerable.Range(0, events.Count).Select(static n => (long)n));
        events.Where(static e => e.Type == RunEventType.MessageDelta).Select(static e => e.Text).ShouldBe(["one", "two", "three"], StringComparer.Ordinal);
    }

    [Fact]
    public async Task A_caller_that_gives_up_waiting_on_a_full_queue_leaves_no_gap_and_loses_nothing()
    {
        var rig = await Rig.StartAsync();
        rig.Store.Hold();

        for (var i = 0; i < RunEventWriter.MaxQueuedEvents; i++)
        {
            await rig.Writer.AppendAsync(Delta("."));
        }

        await WaitUntil.TrueAsync(() => rig.Store.GroupsEntered == 1, "the drain to take a group");

        using var gone = new CancellationTokenSource();
        var waiting = rig.Writer.AppendAsync(Delta("never numbered"), gone.Token).AsTask();

        waiting.IsCompleted.ShouldBeFalse();
        await gone.CancelAsync();
        await Should.ThrowAsync<OperationCanceledException>(() => waiting);

        rig.Store.Release();
        await rig.Writer.CompleteAsync(RunStatus.Completed);

        var events = await rig.Store.ReadAsync(rig.Writer.RunId);
        events.Select(static e => e.Sequence).ShouldBe(Enumerable.Range(0, events.Count).Select(static n => (long)n));
        events.Count(static e => e.Type == RunEventType.MessageDelta).ShouldBe(RunEventWriter.MaxQueuedEvents);
    }

    [Fact]
    public async Task A_logger_that_throws_while_a_lost_group_is_reported_does_not_leave_the_writer_waiting_for_ever()
    {
        var rig = await Rig.StartAsync(logger: new ThrowingLogger());
        rig.Store.FailGroups = true;

        await rig.Writer.AppendAsync(Delta("lost"));
        await WaitUntil.TrueAsync(() => rig.Writer.IsDisabled, "the writer to give up on the store");

        // The drain died on the logger. Whoever waits for the queue is let go, not held by a flag nobody clears.
        await rig.Writer.AppendAsync(new RunEventDraft(RunEventType.ToolInvoking) { ToolName = "lookup" })
            .AsTask()
            .WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);

        var closing = rig.Writer.CompleteAsync(RunStatus.Completed).AsTask();
        await WaitUntil.TrueAsync(() => rig.Clock.Pending.Any(static due => due <= RunEventWriter.CompletionRetryPause), "the writer to wait before the closing write");
        rig.Clock.Advance(RunEventWriter.CompletionRetryPause);
        await closing.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task A_disabled_writer_queues_nothing()
    {
        var rig = await Rig.StartAsync();
        rig.Store.FailGroups = true;

        await rig.Writer.AppendAsync(Delta("lost"));
        await WaitUntil.TrueAsync(() => rig.Writer.IsDisabled, "the writer to give up on the store");

        var entered = rig.Store.GroupsEntered;

        for (var i = 0; i < 50; i++)
        {
            var runEvent = await rig.Writer.AppendAsync(Delta($"after {i}"));
            runEvent.Sequence.ShouldBe(2 + i, "the event is still produced and numbered; it is only not recorded");
        }

        rig.Store.GroupsEntered.ShouldBe(entered);
    }

    private sealed class Rig(GroupSpyRunStore store, RunEventWriter writer, ManualClock clock)
    {
        public GroupSpyRunStore Store { get; } = store;

        public RunEventWriter Writer { get; } = writer;

        public ManualClock Clock { get; } = clock;

        public static async Task<Rig> StartAsync(
            TraconRunRecordingOptions? options = null,
            IReadOnlyList<IRunEventSink>? sinks = null,
            TraconMetrics? metrics = null,
            ManualClock? clock = null,
            ILogger? logger = null)
        {
            clock ??= new ManualClock();
            var store = new GroupSpyRunStore(new InMemoryRunStore());
            var writer = new RunEventWriter(store, options ?? new TraconRunRecordingOptions(), logger ?? NullLogger.Instance, TraconId.NewId(), metrics, sinks, clock);

            await writer.StartAsync(
                new RunStartInfo { RunId = writer.RunId, AgentName = "test-agent", StartedAt = DateTimeOffset.UtcNow },
                "hello");

            return new Rig(store, writer, clock);
        }
    }

    /// <summary>A logger whose every entry throws, as a provider that has failed does.</summary>
    private sealed class ThrowingLogger : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
            => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => throw new InvalidOperationException("the log provider failed");
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
