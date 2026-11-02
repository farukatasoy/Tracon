using System.Net;
using System.Text;
using System.Text.Json.Nodes;

namespace Tracon.Mcp.UnitTests;

/// <summary>
/// A local MCP server over streamable HTTP that answers JSON-RPC from a script, so a test can make it say what a broken
/// one says: nothing, too much, or something that is not a schema.
/// </summary>
/// <remarks>
/// The handshake (<c>server/discover</c> or <c>initialize</c>, and the notification after it) is answered the way a working server answers it.
/// What the test scripts is each of the other methods: <c>tools/list</c>, <c>tools/call</c>.
/// </remarks>
internal sealed class ScriptedMcpServer : IAsyncDisposable
{
    private readonly HttpListener _listener = new();
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _serve;

    private ScriptedMcpServer(Func<string, JsonNode?, JsonNode?, CancellationToken, Task<string?>> script)
    {
        Script = script;

        // HttpListener cannot bind port 0; a probe finds a free one, as the fake servers of the other test projects do.
        using var probe = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();

        Endpoint = new Uri($"http://127.0.0.1:{port}/mcp");
        _listener.Prefixes.Add($"http://127.0.0.1:{port}/");
        _listener.Start();
        _serve = ServeAsync();
    }

    /// <summary>The address of the MCP endpoint.</summary>
    public Uri Endpoint { get; }

    /// <summary>
    /// Answers a method other than the handshake: the JSON-RPC <c>result</c> object as text, or <see langword="null"/> to
    /// say nothing at all (the request is left open until the server stops).
    /// </summary>
    public Func<string, JsonNode?, JsonNode?, CancellationToken, Task<string?>> Script { get; }

    /// <summary>The methods received, in order.</summary>
    public List<string> Methods { get; } = [];

    /// <summary>The HTTP headers each request came with, in order, next to its JSON-RPC method.</summary>
    public List<(string Method, IReadOnlyDictionary<string, string> Headers)> Requests { get; } = [];

    /// <summary>Starts a server.</summary>
    /// <param name="script">The answer to each non-handshake method.</param>
    /// <returns>The running server.</returns>
    public static ScriptedMcpServer Start(Func<string, JsonNode?, JsonNode?, CancellationToken, Task<string?>> script) => new(script);

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

    private async Task ServeAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            HttpListenerContext context;

            try
            {
                context = await _listener.GetContextAsync();
            }
            catch (Exception exception) when (exception is HttpListenerException or ObjectDisposedException)
            {
                return;
            }

            _ = Task.Run(() => HandleAsync(context), CancellationToken.None);
        }
    }

    private async Task HandleAsync(HttpListenerContext context)
    {
        try
        {
            if (!string.Equals(context.Request.HttpMethod, "POST", StringComparison.Ordinal))
            {
                // No server-to-client stream.
                context.Response.StatusCode = 405;
                context.Response.Close();

                return;
            }

            using var reader = new StreamReader(context.Request.InputStream, Encoding.UTF8);
            var message = JsonNode.Parse(await reader.ReadToEndAsync(_stop.Token));
            var method = message?["method"]?.GetValue<string>() ?? string.Empty;
            var id = message?["id"]?.DeepClone();

            lock (Methods)
            {
                Methods.Add(method);
            }

            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var name in context.Request.Headers.AllKeys)
            {
                if (name is not null)
                {
                    headers[name] = context.Request.Headers[name] ?? string.Empty;
                }
            }

            lock (Requests)
            {
                Requests.Add((method, headers));
            }

            if (id is null)
            {
                // A notification has no answer.
                context.Response.StatusCode = 202;
                context.Response.Close();

                return;
            }

            // The client in use opens with server/discover (the stateless handshake of the 2026 revision); an older one
            // with initialize. Both are answered the way a working server answers them.
            var result = string.Equals(method, "server/discover", StringComparison.Ordinal)
                ? "{\"supportedVersions\":[\"2026-07-28\"],\"capabilities\":{\"tools\":{}},\"serverInfo\":{\"name\":\"scripted\",\"version\":\"1\"}}"
                : string.Equals(method, "initialize", StringComparison.Ordinal)
                ? "{\"protocolVersion\":" + (message!["params"]?["protocolVersion"]?.ToJsonString() ?? "\"2025-06-18\"")
                    + ",\"capabilities\":{\"tools\":{}},\"serverInfo\":{\"name\":\"scripted\",\"version\":\"1\"}}"
                : await Script(method, message!["params"], id, _stop.Token);

            if (result is null)
            {
                await Task.Delay(Timeout.Infinite, _stop.Token); // delay: simulated

                return;
            }

            var body = Encoding.UTF8.GetBytes("{\"jsonrpc\":\"2.0\",\"id\":" + id.ToJsonString() + ",\"result\":" + result + "}");
            context.Response.ContentType = "application/json";
            await context.Response.OutputStream.WriteAsync(body, _stop.Token);
            context.Response.Close();
        }
        catch (Exception exception) when (exception is HttpListenerException or ObjectDisposedException or OperationCanceledException or IOException)
        {
            // The client went away, or the server stopped.
        }
    }
}
