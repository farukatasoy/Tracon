using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;
using Tracon.Tests.Common;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// Stopping a host while a run is in flight: that the stop and the dispose return, and what the run row says
/// afterwards, with a model that honors cancellation and with one that ignores it, on the in-process test server and
/// on a real Kestrel socket.
/// </summary>
/// <remarks>
/// <para>
/// A model call that ignores its token cannot be stopped from outside. What the host owes is a bounded stop: the host's
/// own shutdown timeout cuts the wait for the hosted services, and the run is left to the reconciler, which is the
/// documented way a row that no process owns any more is closed. The test pins that the stop returns, not the row: the row of a run
/// whose model call never returns may stay <c>Running</c> after the stop, and the test says so.
/// </para>
/// </remarks>
public sealed class HostStopMidRunTests
{
    // The generic host's own timer, on the system clock: it cuts the wait for a run whose model ignores its token, and the
    // socket's graceful wait for one that honors it (only the abort at the cut cancels that run). Short, so no case waits long.
    private static readonly TimeSpan ShutdownTimeout = TimeSpan.FromMilliseconds(250);

    // A failure bound, not a measurement: a stop that does not return within it is a hang.
    private static readonly TimeSpan StopBound = ShutdownTimeout + TimeSpan.FromSeconds(20);

    [Theory]
    [InlineData(false, true, false)]
    [InlineData(false, false, false)]
    [InlineData(true, true, false)]
    [InlineData(true, false, false)]
    [InlineData(false, true, true)]
    [InlineData(false, false, true)]
    [InlineData(true, true, true)]
    [InlineData(true, false, true)]
    public async Task A_host_stopped_with_a_run_in_flight_stops_and_disposes_in_bounded_time(
        bool kestrel,
        bool modelHonorsCancellation,
        bool clientLeavesFirst)
    {
        var hanging = new HangingModelProvider("hanging", modelHonorsCancellation);
        var (app, client) = await StartAsync(kestrel, hanging);
        var stoppedClean = false;

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, new Uri("/tracon/api/agents/kod-agent/run", UriKind.Relative))
            {
                Content = JsonContent.Create(new { message = "hello" }),
            };

            // The reply is a stream that never ends while the model hangs; the response is not awaited past its headers.
            using var clientGone = new CancellationTokenSource();
            var sending = client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, clientGone.Token);
            await hanging.Started.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);

            if (clientLeavesFirst)
            {
                await clientGone.CancelAsync();
                await sending.ContinueWith(static _ => { }, TaskScheduler.Default);
            }

            await app.StopAsync().WaitAsync(StopBound, TestContext.Current.CancellationToken);

            // A real socket is closed when the host stops; the in-process test server has none to close, so only the
            // real one tells whether a model that honors its token ends the run. Nothing cancels the run before the host
            // aborts the connection, at the end of its shutdown timeout, so the row is written after that cut and may
            // not be there yet when the stop returns: the test waits for it (a failure bound) instead of reading it once.
            if (kestrel && modelHonorsCancellation)
            {
                await WaitUntil.TrueAsync(
                    async () => !(await RunStatusesAsync(app)).Contains("Running", StringComparer.Ordinal),
                    "a model that honors its token to leave the run in a terminal state after the host stopped");
            }

            await app.DisposeAsync().AsTask().WaitAsync(StopBound, TestContext.Current.CancellationToken);
            stoppedClean = true;

            hanging.Release("late");
            _ = sending.ContinueWith(static task => _ = task.Exception, TaskScheduler.Default);
        }
        finally
        {
            client.Dispose();

            if (!stoppedClean)
            {
                hanging.Release("cleanup");
            }
        }
    }

    private static async Task<(WebApplication App, HttpClient Client)> StartAsync(bool kestrel, HangingModelProvider hanging)
    {
        var builder = WebApplication.CreateSlimBuilder();

        if (kestrel)
        {
            builder.WebHost.UseKestrel(static options => options.Listen(IPAddress.Loopback, 0));
        }
        else
        {
            builder.WebHost.UseTestServer();
        }

        builder.Services.Configure<HostOptions>(options => options.ShutdownTimeout = ShutdownTimeout);

        builder.Services.AddTracon()
            .AddModelProvider(hanging)
            .AddAgent(TestData.Definition() with { Model = new ModelBinding { Provider = hanging.Name, Model = "hanging-1" } });

        var app = builder.Build();
        app.MapTracon("/tracon");

        await app.StartAsync();

        if (!kestrel)
        {
            return (app, app.GetTestClient());
        }

        var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();

        return (app, new HttpClient { BaseAddress = new Uri(address) });
    }

    private static async Task<List<string>> RunStatusesAsync(WebApplication app)
    {
        var store = app.Services.GetRequiredService<IRunStore>();
        var runs = await store.QueryRunsAsync(new RunQuery());

        return [.. runs.Select(static run => run.Status.ToString())];
    }
}
