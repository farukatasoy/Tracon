using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;
using Tracon.Tests.Common;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// An event stream behind a reverse proxy that buffers response bodies in blocks still reaches the reader frame by frame,
/// because the response says not to buffer it.
/// </summary>
/// <remarks>
/// <para>
/// The tests run over real sockets (Kestrel on loopback, twice): the in-memory test server hands every write to the
/// reader at once and cannot show buffering. The proxy is a small test-only forwarder that copies the response and
/// models what nginx does with <c>proxy_buffering on</c>: it holds the body back until a block of
/// <see cref="BlockSize"/> bytes has arrived, unless the upstream answered <c>X-Accel-Buffering: no</c>, in which case
/// every read is passed on and flushed at once.
/// </para>
/// <para>
/// The proxy is a model, not a measurement of nginx. What the cases prove is narrow and real: with the headers the
/// server sends, a block-buffering forwarder that honors the documented opt-out lets the first event through while the
/// run is still going, and the same forwarder without the opt-out does not (so the case can go red).
/// </para>
/// </remarks>
public sealed class SseProxyBufferingTests
{
    private const int BlockSize = 4096;
    private const string Agent = "kod-agent";

    [Fact]
    public async Task The_first_event_passes_a_block_buffering_proxy_while_the_run_is_still_going()
    {
        var gated = new GatedModelProvider("proxy-gated", "proxy-1");
        await using var origin = await OriginHost.StartAsync(gated);
        await using var proxy = await ProxyHost.StartAsync(origin.Address, honorAccelBuffering: true);

        var runId = await origin.StartRunAsync(gated);
        using var release = new GateRelease(gated);

        using var response = await OpenEventsAsync(proxy.Address, runId);

        var firstFrame = await ReadFirstFrameAsync(response, TimeSpan.FromSeconds(10));

        firstFrame.ShouldNotBeNull("the run is held by its model, and the stream must still deliver run.started");
        firstFrame.Event.ShouldBe("run.started");
        gated.Open();
        await origin.Run;
    }

    [Fact]
    public async Task The_same_proxy_without_the_opt_out_holds_the_first_event_back_so_the_case_can_fail()
    {
        var gated = new GatedModelProvider("proxy-gated-control", "proxy-1");
        await using var origin = await OriginHost.StartAsync(gated);
        await using var proxy = await ProxyHost.StartAsync(origin.Address, honorAccelBuffering: false);

        var runId = await origin.StartRunAsync(gated);
        using var release = new GateRelease(gated);

        using var response = await OpenEventsAsync(proxy.Address, runId);

        var firstFrame = await ReadFirstFrameAsync(response, TimeSpan.FromSeconds(2));

        firstFrame.ShouldBeNull("a proxy that buffers in blocks holds a small frame back until the block fills or the stream ends");

        // Letting the run finish ends the stream, which flushes the held block: the events were never lost, only late.
        gated.Open();
        await origin.Run;
    }

    [Fact]
    public async Task The_event_stream_response_carries_the_headers_that_keep_proxies_from_buffering_or_caching_it()
    {
        var gated = new GatedModelProvider("proxy-gated-headers", "proxy-1");
        await using var origin = await OriginHost.StartAsync(gated);

        var runId = await origin.StartRunAsync(gated);
        using var release = new GateRelease(gated);

        using var response = await OpenEventsAsync(origin.Address, runId);

        response.Content.Headers.ContentType?.MediaType.ShouldBe("text/event-stream");
        response.Headers.TryGetValues("X-Accel-Buffering", out var accel).ShouldBeTrue();
        accel.ShouldNotBeNull().Any(static value => string.Equals(value, "no", StringComparison.OrdinalIgnoreCase)).ShouldBeTrue();

        var cacheControl = string.Join(",", response.Headers.GetValues("Cache-Control"));

        cacheControl.ShouldContain("no-cache", Case.Insensitive);
        cacheControl.ShouldContain("no-transform", Case.Insensitive, "an intermediary must not re-encode a stream it has to forward frame by frame");
        response.Content.Headers.ContentEncoding.ShouldNotContain("gzip", StringComparer.OrdinalIgnoreCase);
        response.Headers.TransferEncodingChunked.ShouldBe(true, "the body is a stream of unknown length");

        gated.Open();
        await origin.Run;
    }

    private static async Task<HttpResponseMessage> OpenEventsAsync(Uri address, Guid runId)
    {
        var client = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(30));

        return await client.GetAsync(
            new Uri(address, $"/tracon/api/runs/{runId}/events"),
            HttpCompletionOption.ResponseHeadersRead,
            deadline.Token);
    }

    private static async Task<SseFrame?> ReadFirstFrameAsync(HttpResponseMessage response, TimeSpan within)
    {
        var read = Task.Run(
            async () =>
            {
                await using var stream = await response.Content.ReadAsStreamAsync(TestContext.Current.CancellationToken);

                await foreach (var frame in SseReader.ReadAsync(stream, TestContext.Current.CancellationToken))
                {
                    return frame;
                }

                return null;
            },
            TestContext.Current.CancellationToken);

        try
        {
            return await read.WaitAsync(within, TestContext.Current.CancellationToken);
        }
        catch (TimeoutException)
        {
            // A read blocked on the socket does not see a token; closing the response ends it.
            response.Dispose();

            try
            {
                await read;
            }
            catch (Exception exception) when (exception is IOException or ObjectDisposedException or OperationCanceledException or HttpRequestException)
            {
                // The read was abandoned on purpose.
            }

            return null;
        }
    }

    /// <summary>Opens the model's gate when disposed, so a case that fails early still lets its run finish.</summary>
    private sealed class GateRelease(GatedModelProvider gated) : IDisposable
    {
        public void Dispose() => gated.Open();
    }

    /// <summary>A Tracon host on a real loopback socket, with a model that answers only when the test lets it.</summary>
    private sealed class OriginHost : IAsyncDisposable
    {
        private readonly WebApplication _app;
        private readonly GatedModelProvider _gated;
        private readonly HttpClient _client = new();

        private OriginHost(WebApplication app, Uri address, GatedModelProvider gated)
        {
            _app = app;
            Address = address;
            _gated = gated;
        }

        public Uri Address { get; }

        public Task Run { get; private set; } = Task.CompletedTask;

        public static async Task<OriginHost> StartAsync(GatedModelProvider gated)
        {
            var builder = WebApplication.CreateSlimBuilder();
            builder.WebHost.ConfigureKestrel(static options => options.Listen(IPAddress.Loopback, 0));
            builder.Logging.ClearProviders();

            var tracon = builder.Services.AddTracon();
            tracon.AddModelProvider(gated);
            tracon.AddAgent(TestData.Definition() with { Model = new ModelBinding { Provider = gated.Name, Model = gated.Models[0].Name } });

            var app = builder.Build();
            app.MapTracon("/tracon");
            await app.StartAsync();

            return new OriginHost(app, new Uri(app.Urls.First()), gated);
        }

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

        public async ValueTask DisposeAsync()
        {
            // The gated model ignores cancellation, so a case that failed before it opened the gate would otherwise
            // wait for a run that nobody can finish when the host stops.
            _gated.Open();
            _client.Dispose();
            await _app.StopAsync();
            await _app.DisposeAsync();
        }
    }

    /// <summary>A test-only forwarder that buffers the response body in blocks unless the upstream opts out.</summary>
    private sealed class ProxyHost : IAsyncDisposable
    {
        private readonly WebApplication _app;
        private readonly HttpClient _upstream = new() { Timeout = Timeout.InfiniteTimeSpan };

        private ProxyHost(WebApplication app, Uri address)
        {
            _app = app;
            Address = address;
        }

        public Uri Address { get; }

        public static async Task<ProxyHost> StartAsync(Uri origin, bool honorAccelBuffering)
        {
            var builder = WebApplication.CreateSlimBuilder();
            builder.WebHost.ConfigureKestrel(static options => options.Listen(IPAddress.Loopback, 0));
            builder.Logging.ClearProviders();

            var app = builder.Build();
            ProxyHost? self = null;

            app.Run(context => self!.ForwardAsync(context, origin, honorAccelBuffering));
            await app.StartAsync();

            self = new ProxyHost(app, new Uri(app.Urls.First()));

            return self;
        }

        public async ValueTask DisposeAsync()
        {
            _upstream.Dispose();
            await _app.StopAsync();
            await _app.DisposeAsync();
        }

        private async Task ForwardAsync(HttpContext context, Uri origin, bool honorAccelBuffering)
        {
            using var request = new HttpRequestMessage(
                new HttpMethod(context.Request.Method),
                new Uri(origin, context.Request.Path + context.Request.QueryString));

            using var response = await _upstream.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, context.RequestAborted);

            context.Response.StatusCode = (int)response.StatusCode;

            foreach (var header in response.Headers.Concat(response.Content.Headers))
            {
                if (!string.Equals(header.Key, "Transfer-Encoding", StringComparison.OrdinalIgnoreCase))
                {
                    context.Response.Headers[header.Key] = header.Value.ToArray();
                }
            }

            var passThrough = honorAccelBuffering
                && response.Headers.TryGetValues("X-Accel-Buffering", out var accel)
                && accel.Any(static value => string.Equals(value, "no", StringComparison.OrdinalIgnoreCase));

            // Headers go out at once, as a proxy that buffers the body still forwards them; only the body is held.
            await context.Response.StartAsync(context.RequestAborted);
            await context.Response.Body.FlushAsync(context.RequestAborted);

            await using var body = await response.Content.ReadAsStreamAsync(context.RequestAborted);
            var buffer = new byte[BlockSize * 4];
            var held = new MemoryStream();

            int read;

            while ((read = await body.ReadAsync(buffer, context.RequestAborted)) > 0)
            {
                if (passThrough)
                {
                    await context.Response.Body.WriteAsync(buffer.AsMemory(0, read), context.RequestAborted);
                    await context.Response.Body.FlushAsync(context.RequestAborted);

                    continue;
                }

                held.Write(buffer, 0, read);

                if (held.Length >= BlockSize)
                {
                    await context.Response.Body.WriteAsync(held.ToArray(), context.RequestAborted);
                    await context.Response.Body.FlushAsync(context.RequestAborted);
                    held.SetLength(0);
                }
            }

            if (held.Length > 0)
            {
                await context.Response.Body.WriteAsync(held.ToArray(), context.RequestAborted);
                await context.Response.Body.FlushAsync(context.RequestAborted);
            }
        }
    }
}
