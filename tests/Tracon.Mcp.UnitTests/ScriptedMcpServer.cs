using System.Net;
using System.Text;
using System.Text.Json.Nodes;

namespace Tracon.Mcp.UnitTests;

/// <summary>
/// A local MCP server over streamable HTTP that answers JSON-RPC from a script, so a test can make it say what a broken
/// one says: nothing, too much, or something that is not a schema.
/// </summary>
/// <remarks>
/// <para>
/// The handshake (<c>server/discover</c> or <c>initialize</c>, and the notification after it) is answered the way a working server answers it.
/// What the test scripts is each of the other methods: <c>tools/list</c>, <c>tools/call</c>.
/// </para>
/// <para>
/// The server stops in a fixed order: it takes no new request, cancels the ones it holds, waits for every request it
/// accepted, and only then closes the listener. Closing it first broke a request whose handler had not read the body
/// yet. On Unix the listener's close resets the connection to an empty next request (HTTP/1.0 until a request line
/// says otherwise), and the late first read of a chunked body then marked that next response chunked, which throws
/// <see cref="ProtocolViolationException"/>. The wait also means a handler that fails makes the dispose fail, so its
/// owner sees it; a handler started and forgotten failed the whole test run after every test had passed.
/// </para>
/// </remarks>
internal sealed class ScriptedMcpServer : IAsyncDisposable
{
    private readonly HttpListener _listener = new();
    private readonly CancellationTokenSource _stop = new();
    private readonly Func<Func<Task>, Task> _dispatch;
    private readonly List<Task> _handlers = [];
    private readonly Task _serve;

    private readonly string _capabilities;
    private bool _stopping;

    private ScriptedMcpServer(
        Func<string, JsonNode?, JsonNode?, CancellationToken, Task<string?>> script,
        string capabilities,
        Func<Func<Task>, Task>? dispatch)
    {
        Script = script;
        _capabilities = capabilities;
        _dispatch = dispatch ?? (static handle => Task.Run(handle, CancellationToken.None));

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
    /// <param name="capabilities">The <c>capabilities</c> object the handshake declares; tools only by default.</param>
    /// <param name="dispatch">
    /// Starts the handling of one accepted request and returns its task; by default it runs on the thread pool. A test
    /// passes its own to hold a request between its arrival and its handling.
    /// </param>
    /// <returns>The running server.</returns>
    public static ScriptedMcpServer Start(
        Func<string, JsonNode?, JsonNode?, CancellationToken, Task<string?>> script,
        string capabilities = "{\"tools\":{}}",
        Func<Func<Task>, Task>? dispatch = null)
        => new(script, capabilities, dispatch);

    /// <inheritdoc />
    /// <remarks>Throws what a request handler failed with, when that is not the end of its connection or of the server.</remarks>
    public async ValueTask DisposeAsync()
    {
        Task[] handlers;

        lock (_handlers)
        {
            _stopping = true;
            handlers = [.. _handlers];
        }

        _stop.Cancel();

        try
        {
            await Task.WhenAll(handlers);
        }
        finally
        {
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
    }

    private async Task ServeAsync()
    {
        while (true)
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

            lock (_handlers)
            {
                // A request that arrives while the server stops is not handled; closing the listener ends its connection.
                if (!_stopping)
                {
                    _handlers.Add(_dispatch(() => HandleAsync(context)));
                }
            }
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
                ? "{\"supportedVersions\":[\"2026-07-28\"],\"capabilities\":" + _capabilities + ",\"serverInfo\":{\"name\":\"scripted\",\"version\":\"1\"}}"
                : string.Equals(method, "initialize", StringComparison.Ordinal)
                ? "{\"protocolVersion\":" + (message!["params"]?["protocolVersion"]?.ToJsonString() ?? "\"2025-06-18\"")
                    + ",\"capabilities\":" + _capabilities + ",\"serverInfo\":{\"name\":\"scripted\",\"version\":\"1\"}}"
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
