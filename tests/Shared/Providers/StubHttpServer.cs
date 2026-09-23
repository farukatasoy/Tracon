using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Tracon.ProviderCore.Tests;

/// <summary>
/// A one-request HTTP/1.1 server on a loopback port. It records the request
/// line and headers, then writes a canned response - or never answers, to
/// drive a timeout.
/// </summary>
/// <remarks>
/// Linked into the four provider test projects (phase 181). A real socket, not
/// a fake <see cref="HttpMessageHandler"/>: the provider health checks own
/// their <see cref="HttpClient"/>, so the only seam is the network.
/// </remarks>
internal sealed class StubHttpServer : IAsyncDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _serve;
    private readonly TaskCompletionSource<string> _request = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private StubHttpServer(int statusCode, string reason, string? body)
    {
        _listener.Start();
        BaseAddress = new Uri($"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}/");
        _serve = ServeAsync(statusCode, reason, body);
    }

    /// <summary>The server's base address, with a trailing slash.</summary>
    public Uri BaseAddress { get; }

    /// <summary>Starts a server that answers with <paramref name="statusCode"/> and a JSON body.</summary>
    public static StubHttpServer Respond(int statusCode, string reason, string body) => new(statusCode, reason, body);

    /// <summary>Starts a server that reads the request and never answers.</summary>
    public static StubHttpServer NeverRespond() => new(0, string.Empty, body: null);

    /// <summary>The raw request head (request line + headers) the server received.</summary>
    public Task<string> RequestHead => _request.Task;

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync();
        _listener.Stop();

        try
        {
            await _serve;
        }
        catch (Exception exception) when (exception is OperationCanceledException or SocketException or ObjectDisposedException or IOException)
        {
            // The listener was stopped while the server waited; that is the normal end.
        }

        _stop.Dispose();
    }

    private async Task ServeAsync(int statusCode, string reason, string? body)
    {
        using var client = await _listener.AcceptTcpClientAsync(_stop.Token);
        await using var stream = client.GetStream();

        var head = await ReadHeadAsync(stream, _stop.Token);
        _request.TrySetResult(head);

        if (body is null)
        {
            // Hold the connection open until the test ends.
            await Task.Delay(Timeout.Infinite, _stop.Token); // delay: simulated
            return;
        }

        var payload = Encoding.UTF8.GetBytes(body);
        var header = Encoding.ASCII.GetBytes(
            $"HTTP/1.1 {statusCode} {reason}\r\n" +
            "Content-Type: application/json\r\n" +
            $"Content-Length: {payload.Length}\r\n" +
            "Connection: close\r\n\r\n");

        await stream.WriteAsync(header, _stop.Token);
        await stream.WriteAsync(payload, _stop.Token);
        await stream.FlushAsync(_stop.Token);
    }

    private static async Task<string> ReadHeadAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        var buffer = new byte[8192];
        var received = new StringBuilder();

        while (!received.ToString().Contains("\r\n\r\n", StringComparison.Ordinal))
        {
            var read = await stream.ReadAsync(buffer, cancellationToken);

            if (read == 0)
            {
                break;
            }

            received.Append(Encoding.ASCII.GetString(buffer, 0, read));
        }

        return received.ToString();
    }
}
