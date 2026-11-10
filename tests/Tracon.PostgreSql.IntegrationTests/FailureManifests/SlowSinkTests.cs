using Microsoft.Extensions.Logging.Abstractions;
using Tracon.PostgreSql.IntegrationTests.Infrastructure;
using Tracon.Testing.Contracts.Storage;

namespace Tracon.PostgreSql.IntegrationTests.FailureManifests;

/// <summary>
/// Failure manifest: <strong>a run event sink is slow or broken</strong>
/// </summary>
/// <remarks>
/// <para>
/// The written expectation, verified below:
/// </para>
/// <list type="bullet">
///   <item>
///     <description>
///       A sink that <strong>throws</strong> is isolated. It is disabled for
///       that run after its first failure, the store keeps its own writes, and
///       the run finishes normally. The sink is not asked again.
///     </description>
///   </item>
///   <item>
///     <description>
///       🚨 A sink that is merely <strong>slow</strong> is NOT isolated. Sink
///       dispatch is awaited inline on the recording path, so its latency is
///       added to every event the run produces. This is back-pressure on the
///       run, not a background cost.
///     </description>
///   </item>
/// </list>
/// <para>
/// The second point is the one worth writing down, because "observability must
/// not break functionality" is easy to read as "observability cannot slow
/// anything down". It cannot BREAK a run; it can absolutely SLOW one. A sink
/// that talks to a network target must buffer internally and return
/// immediately - see the guidance in
/// <c>docs-site/src/content/docs/guides/observability.md</c>.
/// </para>
/// </remarks>
public sealed class SlowSinkTests(PostgresFixture fixture)
{
    private const int EventCount = 5;

    private static readonly TimeSpan SinkDelay = TimeSpan.FromMilliseconds(120);

    [Fact]
    public async Task A_failing_sink_is_disabled_for_the_run_and_the_store_keeps_writing()
    {
        var schemaName = PostgresTestContext.NewSchemaName();
        await using var context = PostgresTestContext.Create(fixture, schemaName);
        await context.Migrations.ApplyAsync();

        var sink = new CountingSink(throws: true);
        var runId = Guid.NewGuid();

        var writer = new RunEventWriter(
            context.Runs,
            new TraconRunRecordingOptions(),
            NullLogger.Instance,
            runId,
            metrics: null,
            [sink]);

        await writer.StartAsync(TestData.Run(runId), "the user's question");

        for (var i = 0; i < EventCount; i++)
        {
            await writer.AppendAsync(new RunEventDraft(RunEventType.MessageDelta) { Text = "delta" });
        }

        await writer.CompleteAsync(RunStatus.Completed);

        // Asked exactly once, then never again for this run.
        sink.Calls.ShouldBe(1);

        // The store is untouched by the sink's failure: the run is closed and
        // its events are all there.
        writer.IsDisabled.ShouldBeFalse();

        var stored = await context.Runs.GetRunAsync(runId);
        stored.ShouldNotBeNull();
        stored.Status.ShouldBe(RunStatus.Completed);

        (await CountDeltasAsync(context, runId)).ShouldBe(EventCount);
    }

    [Fact]
    public async Task A_slow_sink_delays_the_run_because_dispatch_is_inline()
    {
        var schemaName = PostgresTestContext.NewSchemaName();
        await using var context = PostgresTestContext.Create(fixture, schemaName);
        await context.Migrations.ApplyAsync();

        // The sink's latency is paid on the sink's own clock, which the test moves.
        var clock = new ManualClock();
        var sink = new CountingSink(throws: false, delay: SinkDelay, clock);
        var runId = Guid.NewGuid();

        var writer = new RunEventWriter(
            context.Runs,
            new TraconRunRecordingOptions(),
            NullLogger.Instance,
            runId,
            metrics: null,
            [sink]);

        var recording = Task.Run(async () =>
        {
            await writer.StartAsync(TestData.Run(runId), "the user's question");

            for (var i = 0; i < EventCount; i++)
            {
                await writer.AppendAsync(new RunEventDraft(RunEventType.MessageDelta) { Text = "delta" });
            }

            await writer.CompleteAsync(RunStatus.Completed);
        });

        await clock.AdvanceUntilAsync(recording, within: SinkDelay);

        // The measured claim: the sink's latency is paid per event, on the recording path. It is not absorbed anywhere.
        // Each call to the sink held the recording until its delay had passed on the clock, so the clock was stepped by
        // the delay once for every call. A sink that was dispatched off the recording path would leave the recording
        // free to finish with the delays still armed, and the steps would be fewer than the calls.
        sink.Calls.ShouldBeGreaterThanOrEqualTo(EventCount);
        clock.Steps.ShouldBe(Enumerable.Repeat(SinkDelay, sink.Calls));
        clock.Pending.ShouldBeEmpty();

        // Slow is not broken: every event still reached the sink and the store.
        (await CountDeltasAsync(context, runId)).ShouldBe(EventCount);
    }

    private static async Task<int> CountDeltasAsync(PostgresTestContext context, Guid runId)
    {
        var deltas = 0;

        await foreach (var stored in context.Runs.ReadEventsAsync(runId))
        {
            if (stored.Type == RunEventType.MessageDelta)
            {
                deltas++;
            }
        }

        return deltas;
    }

    private sealed class CountingSink(bool throws, TimeSpan delay = default, TimeProvider? clock = null) : IRunEventSink
    {
        private int _calls;

        public int Calls => Volatile.Read(ref _calls);

        public async ValueTask OnEventAsync(RunEvent runEvent, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _calls);

            if (throws)
            {
                throw new InvalidOperationException("The sink's target is unreachable.");
            }

            if (delay > TimeSpan.Zero && clock is not null)
            {
                // The claim of the test is that a sink's latency is paid on the recording path, per event: the test moves
                // the clock past it, so the run can only go on once it has.
                var passed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

                using var timer = clock.CreateTimer(static state => ((TaskCompletionSource)state!).TrySetResult(), passed, delay, Timeout.InfiniteTimeSpan);

                await passed.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
        }
    }
}
