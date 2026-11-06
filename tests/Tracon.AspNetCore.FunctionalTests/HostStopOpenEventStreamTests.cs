using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;
using Tracon.Tests.Common;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// Stopping a host on a real socket while a run is live and a client holds its event stream
/// (<c>GET /api/runs/{id}/events</c>) open: how long the stop and the dispose take, with and without draining, with the
/// event client still connected and gone, and against a control with no event stream.
/// </summary>
/// <remarks>
/// <para>
/// The model of the run ignores its token, so nothing but the host's own timeout can end the wait. What the host owes is
/// a bounded stop: the event stream polls the store and a run that never finishes keeps it going, so a stream that
/// ignored the stop signal would hold the host until the shutdown timeout cut it. The bound is the shutdown timeout
/// plus a margin for a loaded machine, as in the stop test of a run without an event stream. The drain wait is set
/// longer than the shutdown timeout, so the host's timeout is the one that decides.
/// </para>
/// <para>
/// Each case reports its measured times through the test's diagnostic messages.
/// </para>
/// </remarks>
public sealed class HostStopOpenEventStreamTests
{
    private static readonly TimeSpan ShutdownTimeout = TimeSpan.FromSeconds(3);

    // The stop is cut at ShutdownTimeout; the margin is what a loaded machine may add.
    private static readonly TimeSpan StopBound = ShutdownTimeout + TimeSpan.FromSeconds(20);

    // The host's own timeout is what ends the wait, not the drain wait (ten seconds here): a stop that took as long as the
    // drain would be linked to it. Measured: four seconds, a second of it Kestrel's.
    private static readonly TimeSpan TightStopBound = ShutdownTimeout + TimeSpan.FromSeconds(5);

    public enum EventClient
    {
        /// <summary>No event stream is open: the control.</summary>
        None,

        /// <summary>The client holds the event stream open and reads it.</summary>
        Connected,

        /// <summary>The client opened the event stream and then left before the host stopped.</summary>
        Gone,
    }

    [Theory]
    [InlineData(false, EventClient.None)]
    [InlineData(false, EventClient.Connected)]
    [InlineData(false, EventClient.Gone)]
    [InlineData(true, EventClient.None)]
    [InlineData(true, EventClient.Connected)]
    [InlineData(true, EventClient.Gone)]
    public async Task A_host_stopped_with_a_live_run_and_an_open_event_stream_stops_in_bounded_time(bool drain, EventClient events)
    {
        var gated = new GatedModelProvider("gated", "gated-1");
        var origin = await LoopbackOriginHost.StartAsync(
            gated,
            pollInterval: TimeSpan.FromMilliseconds(100),
            configureServices: services =>
            {
                services.Configure<HostOptions>(options => options.ShutdownTimeout = ShutdownTimeout);
                services.Configure<TraconDrainOptions>(options =>
                {
                    options.Enabled = drain;
                    options.Timeout = TimeSpan.FromSeconds(10);
                });
            });
        var stoppedClean = false;

        using var client = new HttpClient();
        using var eventsGone = new CancellationTokenSource();

        try
        {
            var runId = await origin.StartRunAsync(gated);

            // The run's own request ends with a reset when the host stops; nobody awaits it.
            _ = origin.Run.ContinueWith(static task => _ = task.Exception, TaskScheduler.Default);

            Task reading = Task.CompletedTask;

            if (events is not EventClient.None)
            {
                var response = await client.GetAsync(
                    new Uri(origin.Address, $"/tracon/api/runs/{runId}/events"),
                    HttpCompletionOption.ResponseHeadersRead,
                    TestContext.Current.CancellationToken);

                response.EnsureSuccessStatusCode();

                var stream = await response.Content.ReadAsStreamAsync(TestContext.Current.CancellationToken);
                var first = new byte[1];

                // The stream is live: at least one byte of the run's history has arrived.
                (await stream.ReadAsync(first, TestContext.Current.CancellationToken)).ShouldBeGreaterThan(0);

                reading = Task.Run(
                    async () =>
                    {
                        var buffer = new byte[4096];

                        try
                        {
                            while (await stream.ReadAsync(buffer, eventsGone.Token) > 0)
                            {
                            }
                        }
                        catch (Exception exception) when (exception is IOException or OperationCanceledException or HttpRequestException)
                        {
                            // The host cut the connection, or the client left: either ends the read.
                        }
                    },
                    TestContext.Current.CancellationToken);

                if (events is EventClient.Gone)
                {
                    await eventsGone.CancelAsync();
                    response.Dispose();
                    await reading;
                }
            }

            var stop = Stopwatch.StartNew();
            await origin.StopAsync().WaitAsync(StopBound, TestContext.Current.CancellationToken);
            var stopTime = stop.Elapsed;

            // The run's own request may outlive the stop (its model ignores the token); a connection to the event stream
            // must not: its handler ends when the host aborts the connection.
            await WaitUntil.TrueAsync(
                () => Task.FromResult(origin.InFlight <= 1),
                "the event stream's request to end after the host stopped",
                TimeSpan.FromSeconds(10));

            var inFlightAfterStop = origin.InFlight;

            stop.Restart();
            await origin.DisposeAsync().AsTask().WaitAsync(StopBound, TestContext.Current.CancellationToken);
            var disposeTime = stop.Elapsed;
            stoppedClean = true;

            TestContext.Current.SendDiagnosticMessage(
                $"host stop: drain={drain} events={events} stop={stopTime.TotalSeconds:F2}s dispose={disposeTime.TotalSeconds:F2}s inFlightAfterStop={inFlightAfterStop}");

            stopTime.ShouldBeLessThan(StopBound);
            stopTime.ShouldBeLessThan(TightStopBound, "the stop took as long as the drain wait, so it is tied to it");
            disposeTime.ShouldBeLessThan(StopBound);

            await reading.WaitAsync(StopBound, TestContext.Current.CancellationToken);
        }
        finally
        {
            if (!stoppedClean)
            {
                await origin.DisposeAsync();
            }
        }
    }
}
