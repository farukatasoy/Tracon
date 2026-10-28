using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Tracon.Package.Tests.Infrastructure;

/// <summary>
/// A local OpenAI Chat Completions server for a generated host to talk to:
/// every call is answered with <see cref="Answer"/>, and the model name of every
/// request is kept.
/// </summary>
/// <remarks>
/// <para>
/// A real loopback socket, not a fake <see cref="HttpMessageHandler"/>: the
/// generated host is a separate process, so the network is the only seam. This
/// project cannot reach the ASP.NET Core test servers of
/// <c>Tracon.AspNetCore.FunctionalTests</c> (it references no framework), so the
/// server is a plain <see cref="HttpListener"/>.
/// </para>
/// <para>
/// It answers both wire shapes the host may ask for: a streamed reply
/// (<c>"stream": true</c>) as server-sent events and any other call as one JSON
/// completion. Nothing the host sends is validated beyond its JSON shape; what the
/// test proves is that the request REACHED the provider with the model the guide
/// chose.
/// </para>
/// </remarks>
internal sealed class FakeChatCompletionsServer : IAsyncDisposable
{
    /// <summary>The text every reply carries.</summary>
    public const string Answer = "Order 4182 ships tomorrow.";

    private const string ChatCompletionsPath = "/v1/chat/completions";

    private readonly HttpListener _listener = new();
    private readonly CancellationTokenSource _stop = new();
    private readonly ConcurrentQueue<string> _models = new();
    private readonly ConcurrentQueue<string> _unexpectedPaths = new();
    private readonly Task _serve;

    private FakeChatCompletionsServer(int port)
    {
        BaseAddress = new Uri($"http://127.0.0.1:{port}/v1");
        _listener.Prefixes.Add($"http://127.0.0.1:{port}/");
        _listener.Start();
        _serve = ServeAsync();
    }

    /// <summary>The base address the provider's <c>Endpoint</c> setting takes (<c>http://127.0.0.1:{port}/v1</c>).</summary>
    public Uri BaseAddress { get; }

    /// <summary>The <c>model</c> field of every chat completion request received, in order.</summary>
    public IReadOnlyCollection<string> RequestedModels => _models;

    /// <summary>Every request path other than <c>/v1/chat/completions</c>.</summary>
    public IReadOnlyCollection<string> UnexpectedPaths => _unexpectedPaths;

    /// <summary>Starts the server on a free loopback port.</summary>
    /// <returns>The running server.</returns>
    public static FakeChatCompletionsServer Start()
    {
        // The port is probed and released before the listener binds it: HttpListener
        // cannot report the port of a prefix that asked for port 0.
        using var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();

        return new FakeChatCompletionsServer(port);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync();
        _listener.Close();

        try
        {
            await _serve;
        }
        catch (Exception exception) when (exception is HttpListenerException or ObjectDisposedException or OperationCanceledException)
        {
            // The listener was closed while it waited for a request; that is the normal end.
        }

        _stop.Dispose();
    }

    private static string StreamBody()
    {
        var created = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        var chunk = JsonSerializer.Serialize(new
        {
            id = "chatcmpl-fake",
            @object = "chat.completion.chunk",
            created,
            model = "fake-model",
            choices = new[] { new { index = 0, delta = new { role = "assistant", content = Answer }, finish_reason = "stop" } },
        });

        var usage = JsonSerializer.Serialize(new
        {
            id = "chatcmpl-fake",
            @object = "chat.completion.chunk",
            created,
            model = "fake-model",
            choices = Array.Empty<object>(),
            usage = new { prompt_tokens = 5, completion_tokens = 3, total_tokens = 8 },
        });

        return $"data: {chunk}\n\ndata: {usage}\n\ndata: [DONE]\n\n";
    }

    private static string CompletionBody() => JsonSerializer.Serialize(new
    {
        id = "chatcmpl-fake",
        @object = "chat.completion",
        created = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
        model = "fake-model",
        choices = new[]
        {
            new
            {
                index = 0,
                message = new { role = "assistant", content = Answer },
                finish_reason = "stop",
            },
        },
        usage = new { prompt_tokens = 5, completion_tokens = 3, total_tokens = 8 },
    });

    private async Task ServeAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            var context = await _listener.GetContextAsync().WaitAsync(_stop.Token);

            // One request at a time is enough: the host sends one call per run.
            await HandleAsync(context);
        }
    }

    private async Task HandleAsync(HttpListenerContext context)
    {
        try
        {
            var path = context.Request.Url?.AbsolutePath ?? string.Empty;

            if (!string.Equals(path, ChatCompletionsPath, StringComparison.Ordinal))
            {
                _unexpectedPaths.Enqueue(path);
                context.Response.StatusCode = (int)HttpStatusCode.NotFound;

                return;
            }

            using var reader = new StreamReader(context.Request.InputStream, Encoding.UTF8);
            var body = JsonNode.Parse(await reader.ReadToEndAsync(_stop.Token))?.AsObject();

            _models.Enqueue(body?["model"]?.GetValue<string>() ?? string.Empty);

            var streamed = body?["stream"]?.GetValue<bool>() == true;
            var payload = Encoding.UTF8.GetBytes(streamed ? StreamBody() : CompletionBody());

            context.Response.StatusCode = (int)HttpStatusCode.OK;
            context.Response.ContentType = streamed ? "text/event-stream" : "application/json";
            context.Response.ContentLength64 = payload.Length;
            await context.Response.OutputStream.WriteAsync(payload, _stop.Token);
        }
        finally
        {
            context.Response.Close();
        }
    }
}
