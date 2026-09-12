using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Tracon.AspNetCore.FunctionalTests.Infrastructure;

/// <summary>
/// Fakes OpenAI's live voice surface over a <strong>real</strong> local HTTP and
/// WebSocket server.
/// </summary>
/// <remarks>
/// <para>
/// 🚨 <c>TestServer</c> cannot stand in here. The live provider opens an
/// <em>outgoing</em> <c>ClientWebSocket</c>, and <c>TestServer</c> only fakes the
/// incoming side; the connection has to reach a real socket. This server therefore
/// binds <c>http://127.0.0.1:0</c> and speaks both halves of the protocol:
/// <c>POST /v1/live/sessions</c> and the <c>attach</c> socket.
/// </para>
/// <para>
/// The frames it sends are the ones measured against the real API on 2026-09-11,
/// down to the field names — <c>content</c> rather than <c>text</c> on an append, and
/// <c>start_ms</c>/<c>end_ms</c> on a transcript delta.
/// </para>
/// </remarks>
internal sealed class FakeGptLiveServer : IAsyncDisposable
{
    private readonly WebApplication _app;
    private readonly ConcurrentDictionary<string, TaskCompletionSource> _attached = new(StringComparer.Ordinal);
    private readonly ConcurrentQueue<string> _received = [];

    private WebSocket? _socket;

    private FakeGptLiveServer(WebApplication app, Uri baseAddress)
    {
        _app = app;
        BaseAddress = baseAddress;
    }

    /// <summary>The server's base address (<c>http://127.0.0.1:{port}/v1/</c>).</summary>
    public Uri BaseAddress { get; }

    /// <summary>The frames the sideband sent to this server, in order.</summary>
    public IReadOnlyCollection<string> Received => _received;

    /// <summary>The Authorization header values seen on the create call.</summary>
    public List<string?> CreateAuthorizationHeaders { get; } = [];

    /// <summary>The Authorization header values seen on the attach handshake.</summary>
    public List<string?> AttachAuthorizationHeaders { get; } = [];

    /// <summary>The raw create bodies received.</summary>
    public List<string> CreateBodies { get; } = [];

    /// <summary>How many times a session was created.</summary>
    public int CreateCallCount { get; private set; }

    /// <summary>The status code the create call answers with. Defaults to 201.</summary>
    public int CreateStatusCode { get; set; } = StatusCodes.Status201Created;

    /// <summary>Whether the attach socket refuses the handshake.</summary>
    public bool RefuseAttach { get; set; }

    /// <summary>Starts the server on a random loopback port.</summary>
    /// <returns>The running server.</returns>
    public static async Task<FakeGptLiveServer> StartAsync()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();

        var app = builder.Build();
        app.UseWebSockets();

        FakeGptLiveServer? server = null;

        app.MapPost("/v1/live/sessions", async (HttpContext context) =>
        {
            server!.CreateCallCount++;
            server.CreateAuthorizationHeaders.Add(
                context.Request.Headers.Authorization is { Count: > 0 } values ? values.ToString() : null);
            server.CreateBodies.Add(await new StreamReader(context.Request.Body).ReadToEndAsync());

            if (server.CreateStatusCode != StatusCodes.Status201Created)
            {
                return Results.StatusCode(server.CreateStatusCode);
            }

            var id = "live_fake_" + Guid.NewGuid().ToString("N")[..12];
            server._attached[id] = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            return Results.Json(new
            {
                session = new { id, model = "gpt-live-1", status = "active" },
                transport = new { type = "webrtc", sdp = "v=0\r\no=- 1 1 IN IP4 0.0.0.0\r\ns=-\r\nt=0 0\r\n" },
            });
        });

        app.Map("/v1/live/sessions/{sessionId}/attach", async (HttpContext context, string sessionId) =>
        {
            server!.AttachAuthorizationHeaders.Add(
                context.Request.Headers.Authorization is { Count: > 0 } values ? values.ToString() : null);

            if (server.RefuseAttach || !context.WebSockets.IsWebSocketRequest)
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;

                return;
            }

            using var socket = await context.WebSockets.AcceptWebSocketAsync();
            server._socket = socket;

            await server.SendAsync(new
            {
                event_id = "event_fake_started",
                type = "session.started",
                session = new { id = sessionId, model = "gpt-live-1", status = "active" },
            });

            if (server._attached.TryGetValue(sessionId, out var signal))
            {
                signal.TrySetResult();
            }

            var buffer = new byte[64 * 1024];
            var builderText = new StringBuilder();

            while (socket.State == WebSocketState.Open)
            {
                WebSocketReceiveResult result;

                try
                {
                    result = await socket.ReceiveAsync(buffer, context.RequestAborted);
                }
                catch (Exception)
                {
                    break;
                }

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    break;
                }

                builderText.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));

                if (!result.EndOfMessage)
                {
                    continue;
                }

                server._received.Enqueue(builderText.ToString());
                builderText.Clear();
            }
        });

        await app.StartAsync().ConfigureAwait(false);

        var address = app.Urls.First();
        server = new FakeGptLiveServer(app, new Uri($"{address}/v1/"));

        return server;
    }

    /// <summary>Waits until the sideband has attached to a session.</summary>
    /// <param name="timeout">How long to wait.</param>
    /// <returns>The completion task.</returns>
    public async Task WaitForAttachAsync(TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;

        while (DateTime.UtcNow < deadline)
        {
            foreach (var signal in _attached.Values)
            {
                if (signal.Task.IsCompleted)
                {
                    return;
                }
            }

            await Task.Delay(20).ConfigureAwait(false);
        }

        throw new TimeoutException("The sideband never attached.");
    }

    /// <summary>Sends a transcript delta from the user.</summary>
    /// <param name="text">The text.</param>
    /// <param name="startMs">When the segment begins.</param>
    /// <param name="endMs">When the segment ends.</param>
    /// <returns>The completion task.</returns>
    public Task SendInputTranscriptAsync(string text, int startMs, int endMs)
        => SendAsync(new
        {
            type = "session.input_transcript.delta",
            start_ms = startMs,
            end_ms = endMs,
            delta = text,
            event_id = "event_fake_" + Guid.NewGuid().ToString("N")[..8],
        });

    /// <summary>Sends a transcript delta from the model.</summary>
    /// <param name="text">The text.</param>
    /// <param name="startMs">When the segment begins.</param>
    /// <param name="endMs">When the segment ends.</param>
    /// <returns>The completion task.</returns>
    public Task SendOutputTranscriptAsync(string text, int startMs, int endMs)
        => SendAsync(new
        {
            type = "session.output_transcript.delta",
            start_ms = startMs,
            end_ms = endMs,
            delta = text,
            event_id = "event_fake_" + Guid.NewGuid().ToString("N")[..8],
        });

    /// <summary>Sends a delegation.</summary>
    /// <param name="delegationId">The delegation identifier.</param>
    /// <param name="offsetMs">Where in the conversation it was cut.</param>
    /// <returns>The completion task.</returns>
    public Task SendDelegationAsync(string delegationId, int offsetMs)
        => SendAsync(new
        {
            type = "session.delegation.created",
            offset_ms = offsetMs,
            delegation = new { id = delegationId, type = "delegation", target = "client" },
            event_id = "event_fake_" + Guid.NewGuid().ToString("N")[..8],
        });

    /// <summary>Sends a usage update.</summary>
    /// <param name="seconds">The billable duration so far.</param>
    /// <returns>The completion task.</returns>
    public Task SendUsageAsync(decimal seconds)
        => SendAsync(new
        {
            type = "session.usage.updated",
            usage = new { seconds },
            event_id = "event_fake_" + Guid.NewGuid().ToString("N")[..8],
        });

    /// <summary>Sends a close.</summary>
    /// <param name="reason">Why the provider closed the session.</param>
    /// <param name="seconds">The final billable duration.</param>
    /// <returns>The completion task.</returns>
    public Task SendClosedAsync(string reason, decimal seconds)
        => SendAsync(new
        {
            type = "session.closed",
            reason,
            usage = new { seconds },
            event_id = "event_fake_" + Guid.NewGuid().ToString("N")[..8],
        });

    /// <summary>Drops the sideband socket without a close handshake.</summary>
    /// <returns>The completion task.</returns>
    public async Task KillSidebandAsync()
    {
        if (_socket is { State: WebSocketState.Open } socket)
        {
            socket.Abort();
        }

        await Task.Delay(50).ConfigureAwait(false);
    }

    /// <summary>Reads the appends this server received.</summary>
    /// <param name="channel">The event type to filter on, e.g. <c>session.commentary.append</c>.</param>
    /// <returns>The <c>content</c> values, in order.</returns>
    public IReadOnlyList<string> AppendsOf(string channel)
    {
        var results = new List<string>();

        foreach (var raw in _received)
        {
            using var document = JsonDocument.Parse(raw);

            if (document.RootElement.TryGetProperty("type", out var type)
                && string.Equals(type.GetString(), channel, StringComparison.Ordinal)
                && document.RootElement.TryGetProperty("content", out var content))
            {
                results.Add(content.GetString() ?? string.Empty);
            }
        }

        return results;
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync().ConfigureAwait(false);
        await _app.DisposeAsync().ConfigureAwait(false);
    }

    private async Task SendAsync(object payload)
    {
        if (_socket is not { State: WebSocketState.Open } socket)
        {
            return;
        }

        var json = JsonSerializer.Serialize(payload);

        await socket
            .SendAsync(Encoding.UTF8.GetBytes(json), WebSocketMessageType.Text, endOfMessage: true, CancellationToken.None)
            .ConfigureAwait(false);
    }
}
