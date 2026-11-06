using System.Net;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Logging;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;
using Tracon.Tests.Common;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// A run's event stream whose connection is closed from the server's side after the n-th event leaves nothing behind
/// on the server, loses no event, and resumes with <c>Last-Event-ID</c> into exactly the rest.
/// </summary>
/// <remarks>
/// <para>
/// The resume tests that already exist cut the stream from the reader's side over the in-memory test server. A real
/// stream is also closed by what sits between the two: a proxy that times a response out, a load balancer that recycles a
/// connection, a process that is stopped. The reader then sees the server end the body (a clean end that is not the end
/// of the run) or break it. Both are exercised here over real sockets, through a forwarder that closes its side of the
/// connection right after it has passed the n-th event, once as a normal end of the response and once as an abort.
/// </para>
/// <para>
/// Two things are required. The handler on the server must end: the host counts the requests it is still serving, and a
/// stream whose reader went away has to leave that count. And the run is not touched: the reader that comes back with the
/// id of the last event it saw reads the same sequence an uninterrupted reader of the finished run reads, from the next
/// event on.
/// </para>
/// </remarks>
public sealed class RunEventConnectionCutTests
{
    public enum Close
    {
        /// <summary>The response is ended normally, as a proxy does when a read timeout expires.</summary>
        EndOfResponse,

        /// <summary>The connection is aborted, as when the process behind the proxy goes away.</summary>
        Abort,
    }

    [Theory]
    [InlineData(Close.EndOfResponse)]
    [InlineData(Close.Abort)]
    public async Task A_live_stream_closed_after_the_first_event_releases_its_handler_and_resumes_into_exactly_the_rest(Close close)
    {
        var gated = new GatedModelProvider("cut-gated", "cut-1");
        await using var origin = await LoopbackOriginHost.StartAsync(gated, pollInterval: TimeSpan.FromMilliseconds(25));
        await using var forwarder = await CuttingForwarder.StartAsync(origin.Address, close);

        var runId = await origin.StartRunAsync(gated);

        // The run request itself is being served: the stream that is cut must bring the count back to this.
        var baseline = origin.InFlight;

        var firstPart = await ReadThroughAsync(forwarder.Address, runId, lastEventId: null, cutAfter: 1, close);

        // An abort resets the connection, and a reset discards what the reader has not read yet: it may hold the event or not.
        firstPart.Count.ShouldBeLessThanOrEqualTo(1, "the run is held by its model; run.started is the one event there is");
        (close == Close.Abort || firstPart.Count == 1).ShouldBeTrue("a response that ends normally delivers what was written before it");

        await WaitUntil.TrueAsync(
            () => origin.InFlight <= baseline,
            "the server to end the handler of the stream whose connection was closed");

        gated.Open();
        await origin.Run;

        var full = await ReadDirectAsync(origin.Address, runId, lastEventId: null);
        var rest = await ReadDirectAsync(origin.Address, runId, lastEventId: firstPart.LastOrDefault()?.Id);

        full.Count.ShouldBeGreaterThanOrEqualTo(3, "a start, an answer and a completion");
        full.Any(static frame => string.Equals(frame.Event, "run.completed", StringComparison.Ordinal)).ShouldBeTrue();
        firstPart.ShouldBe(full.Take(firstPart.Count).ToList(), "what the reader saw before the close is the start of the run's sequence");
        firstPart.Concat(rest).ToList().ShouldBe(full, "resuming from the id of the last event seen gives every later event, once");
    }

    [Theory]
    [InlineData(Close.EndOfResponse)]
    [InlineData(Close.Abort)]
    public async Task A_finished_runs_stream_closed_after_every_event_and_resumed_joins_into_the_full_sequence_exactly_once(Close close)
    {
        var gated = new GatedModelProvider("cut-gated-finished", "cut-1");
        await using var origin = await LoopbackOriginHost.StartAsync(gated);
        await using var forwarder = await CuttingForwarder.StartAsync(origin.Address, close);

        var runId = await origin.StartRunAsync(gated);

        gated.Open();
        await origin.Run;

        var full = await ReadDirectAsync(origin.Address, runId, lastEventId: null);

        full.Count.ShouldBeGreaterThanOrEqualTo(3);

        for (var cutAfter = 1; cutAfter < full.Count; cutAfter++)
        {
            var first = await ReadThroughAsync(forwarder.Address, runId, lastEventId: null, cutAfter, close);

            // A response that ends normally delivers every event the forwarder passed; an abort resets the connection and may
            // drop the last ones the reader had not read. Either way what arrived is the start of the sequence.
            first.ShouldBe(full.Take(first.Count).ToList(), $"the forwarder passed {cutAfter} event(s) before it closed");

            if (close == Close.EndOfResponse)
            {
                first.Count.ShouldBe(cutAfter);
            }

            var lastSeen = first.LastOrDefault()?.Id;
            var resumed = await ReadDirectAsync(origin.Address, runId, lastEventId: lastSeen);

            first.Concat(resumed).ToList().ShouldBe(full, $"cut after {cutAfter} event(s), resumed from Last-Event-ID {lastSeen ?? "(none)"}");
        }

        await WaitUntil.TrueAsync(() => origin.InFlight == 0, "every stream that was cut to leave the server");
    }

    [Fact]
    public async Task An_abort_that_reaches_the_reader_before_it_has_read_the_response_head_leaves_it_with_nothing()
    {
        // The forwarder aborts right after it wrote the n-th event. The head of the response and that event are in the
        // reader's socket buffer by then, and a reset that arrives before the reader's first read discards both: on some
        // systems the first read then fails with "connection reset by peer", and the failure comes out of the request itself,
        // not out of the body. The reader holds no event, which is the same prefix of the run (the empty one) that an abort
        // after the head is allowed to leave. Here the reset is delivered to the reader as that failure.
        using var reset = new ResettingHandler(new HttpRequestException(
            "An error occurred while sending the request.",
            new IOException("Unable to read data from the transport connection: Connection reset by peer.", new System.Net.Sockets.SocketException((int)System.Net.Sockets.SocketError.ConnectionReset))));

        var seen = await ReadThroughAsync(new Uri("http://127.0.0.1:1/"), Guid.NewGuid(), lastEventId: null, cutAfter: 1, Close.Abort, reset);

        seen.ShouldBeEmpty();
        reset.Requests.ShouldBe(1);
    }

    [Fact]
    public async Task A_reset_before_the_response_head_is_not_expected_when_the_response_is_ended_normally()
    {
        // A response that ends normally never resets the connection, so a reader that fails there found a defect.
        using var reset = new ResettingHandler(new HttpRequestException(
            "An error occurred while sending the request.",
            new IOException("Unable to read data from the transport connection: Connection reset by peer.", new System.Net.Sockets.SocketException((int)System.Net.Sockets.SocketError.ConnectionReset))));

        await Should.ThrowAsync<HttpRequestException>(
            async () => await ReadThroughAsync(new Uri("http://127.0.0.1:1/"), Guid.NewGuid(), lastEventId: null, cutAfter: 1, Close.EndOfResponse, reset));
    }

    [Fact]
    public async Task A_failure_that_is_not_a_reset_of_an_open_connection_still_fails_the_reader_after_an_abort()
    {
        // Only the loss of a connection that was open is what an abort does. A connection that was never made (the origin or
        // the forwarder is down) is a test that is not running what it says.
        using var refused = new ResettingHandler(new HttpRequestException(
            "Connection refused",
            new System.Net.Sockets.SocketException((int)System.Net.Sockets.SocketError.ConnectionRefused)));

        await Should.ThrowAsync<HttpRequestException>(
            async () => await ReadThroughAsync(new Uri("http://127.0.0.1:1/"), Guid.NewGuid(), lastEventId: null, cutAfter: 1, Close.Abort, refused));
    }

    private static async Task<List<SseFrame>> ReadDirectAsync(Uri address, Guid runId, string? lastEventId)
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(address, $"/tracon/api/runs/{runId}/events"));

        if (lastEventId is not null)
        {
            request.Headers.TryAddWithoutValidation("Last-Event-ID", lastEventId);
        }

        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        await using var body = await response.Content.ReadAsStreamAsync(TestContext.Current.CancellationToken);

        return await Collect(body);
    }

    /// <summary>
    /// Reads a stream through the forwarder, which closes the connection after <paramref name="cutAfter"/> events. After an
    /// abort the reader holds a prefix of those events, which may be empty: the reset can discard the head of the response too.
    /// </summary>
    private static async Task<List<SseFrame>> ReadThroughAsync(
        Uri forwarder,
        Guid runId,
        string? lastEventId,
        int cutAfter,
        Close close,
        HttpMessageHandler? handler = null)
    {
        using var client = handler is null
            ? new HttpClient { Timeout = TimeSpan.FromSeconds(30) }
            : new HttpClient(handler, disposeHandler: false) { Timeout = TimeSpan.FromSeconds(30) };
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(forwarder, $"/tracon/api/runs/{runId}/events?cut={cutAfter}"));

        if (lastEventId is not null)
        {
            request.Headers.TryAddWithoutValidation("Last-Event-ID", lastEventId);
        }

        try
        {
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, TestContext.Current.CancellationToken);

            await using var body = await response.Content.ReadAsStreamAsync(TestContext.Current.CancellationToken);

            return await Collect(body);
        }
        catch (HttpRequestException exception) when (close == Close.Abort && exception.InnerException is IOException)
        {
            // 🚨 The abort can reach the reader before it has read the head of the response: the head and the first event
            // sit in its socket buffer, the reset discards them, and the first read fails inside the request, outside the
            // body that Collect guards. The reader then holds no event, which is a prefix of the run like any other an
            // abort leaves. Only the loss of a connection that was open is taken for this (the inner exception is an
            // IOException): a connection that was never made, or a response that was ended normally, still fails the test.
            return [];
        }
    }

    /// <summary>Reads frames until the body ends or breaks; what was read before a break is what the reader has.</summary>
    private static async Task<List<SseFrame>> Collect(Stream body)
    {
        var frames = new List<SseFrame>();

        try
        {
            await foreach (var frame in SseReader.ReadAsync(body, TestContext.Current.CancellationToken))
            {
                frames.Add(frame);
            }
        }
        catch (Exception exception) when (exception is IOException or HttpRequestException)
        {
            // An aborted connection ends the body with an error; the frames read before it are the result.
        }

        return frames;
    }

    /// <summary>A handler whose every request fails with the given exception, as a request does when the connection is reset under it.</summary>
    private sealed class ResettingHandler(Exception failure) : HttpMessageHandler
    {
        private int _requests;

        public int Requests => Volatile.Read(ref _requests);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _requests);

            return Task.FromException<HttpResponseMessage>(failure);
        }
    }

    /// <summary>
    /// A test-only forwarder that passes a response frame by frame and closes its side of the connection after the
    /// n-th event (a frame that carries an <c>id</c>), given in the request as <c>?cut=n</c>.
    /// </summary>
    private sealed class CuttingForwarder : IAsyncDisposable
    {
        private readonly WebApplication _app;
        private readonly HttpClient _upstream = new() { Timeout = Timeout.InfiniteTimeSpan };
        private readonly Close _close;

        private CuttingForwarder(WebApplication app, Uri address, Close close)
        {
            _app = app;
            Address = address;
            _close = close;
        }

        public Uri Address { get; }

        public static async Task<CuttingForwarder> StartAsync(Uri origin, Close close)
        {
            var builder = WebApplication.CreateSlimBuilder();
            builder.WebHost.ConfigureKestrel(static options => options.Listen(IPAddress.Loopback, 0));
            builder.Logging.ClearProviders();

            var app = builder.Build();
            CuttingForwarder? self = null;

            app.Run(context => self!.ForwardAsync(context, origin));
            await app.StartAsync();

            self = new CuttingForwarder(app, new Uri(app.Urls.First()), close);

            return self;
        }

        public async ValueTask DisposeAsync()
        {
            _upstream.Dispose();
            await _app.StopAsync();
            await _app.DisposeAsync();
        }

        private async Task ForwardAsync(HttpContext context, Uri origin)
        {
            var cut = int.Parse(context.Request.Query["cut"].ToString(), System.Globalization.CultureInfo.InvariantCulture);
            using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(origin, context.Request.Path));

            foreach (var header in context.Request.Headers.Where(static header => string.Equals(header.Key, "Last-Event-ID", StringComparison.OrdinalIgnoreCase)))
            {
                request.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray());
            }

            using var response = await _upstream.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, context.RequestAborted);

            context.Response.StatusCode = (int)response.StatusCode;
            context.Response.ContentType = response.Content.Headers.ContentType?.ToString();
            await context.Response.StartAsync(context.RequestAborted);

            await using var body = await response.Content.ReadAsStreamAsync(context.RequestAborted);
            var pending = new StringBuilder();
            var buffer = new byte[1024];
            var events = 0;
            int read;

            while ((read = await body.ReadAsync(buffer, context.RequestAborted)) > 0)
            {
                pending.Append(Encoding.UTF8.GetString(buffer, 0, read));

                // Whole frames only: a frame ends at a blank line, and a cut inside one is not "after the n-th event".
                int end;

                while ((end = pending.ToString().IndexOf("\n\n", StringComparison.Ordinal)) >= 0)
                {
                    var frame = pending.ToString(0, end + 2);

                    pending.Remove(0, end + 2);

                    await context.Response.WriteAsync(frame, context.RequestAborted);
                    await context.Response.Body.FlushAsync(context.RequestAborted);

                    if (frame.Contains("\nid: ", StringComparison.Ordinal) || frame.StartsWith("id: ", StringComparison.Ordinal))
                    {
                        events++;
                    }

                    if (events >= cut)
                    {
                        if (_close == Close.Abort)
                        {
                            context.Abort();
                        }

                        // Leaving the handler ends the response; disposing the upstream response closes the connection to the origin.
                        return;
                    }
                }
            }
        }
    }
}
