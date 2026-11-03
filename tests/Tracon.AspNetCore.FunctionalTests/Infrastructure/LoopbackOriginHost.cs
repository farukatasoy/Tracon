using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Tracon.Tests.Common;

namespace Tracon.AspNetCore.FunctionalTests.Infrastructure;

/// <summary>A Tracon host on a real loopback socket, with a model that answers only when the test lets it.</summary>
/// <remarks>
/// The in-memory test server hands every write to the reader at once and ends a request when the client stops reading,
/// so it cannot show buffering, an abrupt close or a handler that does not notice one. Real sockets can. The host counts
/// the requests it is still serving, which is how a test sees that a closed connection released its handler.
/// </remarks>
internal sealed class LoopbackOriginHost : IAsyncDisposable
{
    /// <summary>The agent the host registers; it runs on the gated model.</summary>
    public const string Agent = "kod-agent";

    private readonly WebApplication _app;
    private readonly GatedModelProvider _gated;
    private readonly InFlightCounter _inFlight;
    private readonly HttpClient _client = new();

    private LoopbackOriginHost(WebApplication app, Uri address, GatedModelProvider gated, InFlightCounter inFlight)
    {
        _app = app;
        Address = address;
        _gated = gated;
        _inFlight = inFlight;
    }

    /// <summary>The address the host listens on.</summary>
    public Uri Address { get; }

    /// <summary>The run request started by <see cref="StartRunAsync"/>; completes when the run's answer has been read.</summary>
    public Task Run { get; private set; } = Task.CompletedTask;

    /// <summary>The number of requests the host is serving right now.</summary>
    public int InFlight => _inFlight.Count;

    /// <summary>The services of the host.</summary>
    public IServiceProvider Services => _app.Services;

    /// <summary>Starts the host.</summary>
    /// <param name="gated">The model the agent runs on.</param>
    /// <param name="pollInterval">How often a live event stream polls for new events; the default of the endpoints when omitted.</param>
    /// <returns>The running host.</returns>
    public static async Task<LoopbackOriginHost> StartAsync(GatedModelProvider gated, TimeSpan? pollInterval = null)
    {
        var inFlight = new InFlightCounter();
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.ConfigureKestrel(static options => options.Listen(IPAddress.Loopback, 0));
        builder.Logging.ClearProviders();

        var tracon = builder.Services.AddTracon();
        tracon.AddModelProvider(gated);
        tracon.AddAgent(TestData.Definition() with { Model = new ModelBinding { Provider = gated.Name, Model = gated.Models[0].Name } });

        var app = builder.Build();

        app.Use(
            async (context, next) =>
            {
                inFlight.Enter();

                try
                {
                    await next(context);
                }
                finally
                {
                    inFlight.Leave();
                }
            });

        app.MapTracon(
            "/tracon",
            options =>
            {
                if (pollInterval is { } interval)
                {
                    options.RunEventPollInterval = interval;
                }
            });

        await app.StartAsync();

        return new LoopbackOriginHost(app, new Uri(app.Urls.First()), gated, inFlight);
    }

    /// <summary>Starts a run that the model holds, and returns its id once it is recorded.</summary>
    /// <param name="gated">The model the host was started with.</param>
    /// <returns>The run id.</returns>
    public async Task<Guid> StartRunAsync(GatedModelProvider gated)
    {
        Run = Task.Run(
            async () =>
            {
                using var response = await _client.PostAsJsonAsync(
                    new Uri(Address, $"/tracon/api/agents/{Agent}/run"),
                    new { message = "stream me" },
                    TestContext.Current.CancellationToken);

                response.EnsureSuccessStatusCode();
            },
            TestContext.Current.CancellationToken);

        await gated.Started;

        var runs = _app.Services.GetRequiredService<IRunStore>();
        var recorded = await WaitUntil.ValueAsync(
            async () => await runs.QueryRunsAsync(new RunQuery(), TestContext.Current.CancellationToken),
            static found => found.Count > 0,
            "the run to be recorded");

        return recorded[0].Id;
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        // The gated model ignores cancellation, so a case that failed before it opened the gate would otherwise
        // wait for a run that nobody can finish when the host stops.
        _gated.Open();
        _client.Dispose();
        await _app.StopAsync();
        await _app.DisposeAsync();
    }

    private sealed class InFlightCounter
    {
        private int _count;

        public int Count => Volatile.Read(ref _count);

        public void Enter() => Interlocked.Increment(ref _count);

        public void Leave() => Interlocked.Decrement(ref _count);
    }
}
