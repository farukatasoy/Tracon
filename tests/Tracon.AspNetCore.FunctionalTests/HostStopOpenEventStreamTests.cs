using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;
using Tracon.Tests.Common;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// Stopping a host on a real socket while a run is live and a client holds its event stream
/// (<c>GET /api/runs/{id}/events</c>) open: that the stop and the dispose return, with and without draining, with the
/// event client still connected and gone, and against a control with no event stream.
/// </summary>
/// <remarks>
/// <para>
/// The model of the run ignores its token, so nothing but the host's own timeout can end the wait. What the host owes is
/// a bounded stop: the event stream polls the store and a run that never finishes keeps it going, so a stream that
/// ignored the stop signal would hold the host until the shutdown timeout cut it.
/// </para>
/// <para>
/// The host runs on a <see cref="ManualClock"/> and the test measures nothing. The drain wait is an hour on that clock, and
/// the clock is moved only in steps of the stream's poll interval while the host stops, so the drain wait cannot end: a stop
/// that waited for it, or for any wait but the host's own timeout, never returns and fails the bound on the stop. The host's
/// shutdown timeout is the generic host's and runs on the system clock; it is short so that the cases do not wait for it.
/// </para>
/// </remarks>
public sealed class HostStopOpenEventStreamTests
{
    // The generic host's own timer, on the system clock: it cuts the wait for a run whose model ignores its token.
    private static readonly TimeSpan ShutdownTimeout = TimeSpan.FromMilliseconds(250);

    // A failure bound, not a measurement: a stop that does not return within it is a hang.
    private static readonly TimeSpan StopBound = ShutdownTimeout + TimeSpan.FromSeconds(20);

    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(100);

    // Far past anything the clock reaches while the host stops, so that only the host's own timeout can end the stop.
    private static readonly TimeSpan DrainWait = TimeSpan.FromHours(1);

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
        var clock = new ManualClock();
        var origin = await LoopbackOriginHost.StartAsync(
            gated,
            pollInterval: PollInterval,
            configureServices: services =>
            {
                services.AddSingleton<TimeProvider>(clock);
                services.Configure<HostOptions>(options => options.ShutdownTimeout = ShutdownTimeout);
                services.Configure<TraconDrainOptions>(options =>
                {
                    options.Enabled = drain;
                    options.Timeout = DrainWait;
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
                origin.EventStreamsInFlight.ShouldBe(1);

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

            // The stream polls and the drain waits on the clock, which moves by one poll interval at a time while the host
            // stops. The drain wait is far beyond the interval, so it is left armed: the host's own timeout ends the stop.
            var stopping = origin.StopAsync();

            await clock.AdvanceUntilAsync(stopping, within: PollInterval, timeout: StopBound);

            // The run's own request may outlive the stop (its model ignores the token); a connection to the event stream
            // must not: its handler ends when the host aborts the connection.
            await WaitUntil.TrueAsync(
                () => Task.FromResult(origin.EventStreamsInFlight == 0),
                "the event stream's request to end after the host stopped",
                TimeSpan.FromSeconds(10));

            var inFlightAfterStop = origin.InFlight;

            await origin.DisposeAsync().AsTask().WaitAsync(StopBound, TestContext.Current.CancellationToken);
            stoppedClean = true;

            TestContext.Current.SendDiagnosticMessage(
                $"host stop: drain={drain} events={events} polls={clock.Steps.Count} inFlightAfterStop={inFlightAfterStop}");

            // What the host owes ended above, on its own side of the socket. When the client's read learns of the reset is
            // the runtime's and the operating system's: on a loaded machine the connection was gone from the TCP table and
            // the read still had not returned 23 seconds later. The test ends its own read instead of waiting for it.
            await eventsGone.CancelAsync();
            await reading.WaitAsync(StopBound, TestContext.Current.CancellationToken);
        }
        finally
        {
            if (!stoppedClean)
            {
                // The failure is already on its way out; a stop that does not return must not hide it by holding the test.
                try
                {
                    await origin.DisposeAsync().AsTask().WaitAsync(StopBound, TestContext.Current.CancellationToken);
                }
                catch (TimeoutException)
                {
                    // Reported by the failure above.
                }
            }
        }
    }
}
