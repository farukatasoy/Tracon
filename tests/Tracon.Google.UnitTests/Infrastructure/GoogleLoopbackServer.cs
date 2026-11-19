using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Tracon.Google.UnitTests.Infrastructure;

/// <summary>
/// A local HTTP/1.1 server that answers every request on a loopback port and keeps the address, the
/// <c>x-goog-api-key</c> header and the body each one carried.
/// </summary>
/// <remarks>
/// <para>
/// A provider that follows a configuration key builds its own Google client, so a test cannot hand it a stub handler:
/// the only seam is the network. Unlike <c>StubHttpServer</c> this server stays up for as many requests as a test makes,
/// so one test can call before and after a rotation. A request is answered by <c>responder</c>; the default answers a
/// model list for the health check and a chat sentence for everything else.
/// </para>
/// <para>
/// Each request is answered with <c>Connection: close</c>, so no later request rides on an earlier connection and the
/// header a request carried is always the one the client sent for that request.
/// </para>
/// </remarks>
internal sealed class GoogleLoopbackServer : IAsyncDisposable
{
    /// <summary>The sentence the default answer carries.</summary>
    public const string Sentence = "hello from the loopback";

    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _stop = new();
    private readonly ConcurrentQueue<GoogleRecordedRequest> _requests = new();
    private readonly Func<GoogleRecordedRequest, string> _responder;
    private readonly Task _accept;

    /// <summary>Starts the server.</summary>
    /// <param name="responder">Builds the JSON body of the answer; <see langword="null"/> answers the default.</param>
    public GoogleLoopbackServer(Func<GoogleRecordedRequest, string>? responder = null)
    {
        _responder = responder ?? DefaultAnswer;
        _listener.Start();
        BaseAddress = new Uri($"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}/");
        _accept = AcceptAsync();
    }

    /// <summary>Gets the server's base address, with a trailing slash.</summary>
    public Uri BaseAddress { get; }

    /// <summary>Gets every request the server received, in arrival order.</summary>
    public IReadOnlyList<GoogleRecordedRequest> Requests => [.. _requests];

    /// <summary>Gets the key of every request, in arrival order.</summary>
    public IReadOnlyList<string?> Keys => [.. _requests.Select(static request => request.ApiKey)];

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync();
        _listener.Stop();

        try
        {
            await _accept;
        }
        catch (Exception exception) when (exception is OperationCanceledException or SocketException or ObjectDisposedException or IOException)
        {
            // The listener was stopped while the server waited; that is the normal end.
        }

        _stop.Dispose();
    }

    private static string DefaultAnswer(GoogleRecordedRequest request)
        => request.Uri.AbsolutePath.EndsWith("/models", StringComparison.Ordinal)
            ? """{"models":[{"name":"models/gemini-test"}]}"""
            : $$"""
                {"candidates":[{"content":{"role":"model","parts":[{"text":"{{Sentence}}"}]},"finishReason":"STOP","index":0}],
                 "usageMetadata":{"promptTokenCount":5,"candidatesTokenCount":3,"totalTokenCount":8},"modelVersion":"gemini-test"}
                """;

    private async Task AcceptAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            var client = await _listener.AcceptTcpClientAsync(_stop.Token);

            _ = ServeAsync(client);
        }
    }

    private async Task ServeAsync(TcpClient client)
    {
        using (client)
        {
            try
            {
                await using var stream = client.GetStream();

                var (head, body) = await ReadRequestAsync(stream, _stop.Token);
                var lines = head.Split("\r\n");
                var target = lines[0].Split(' ')[1];
                var key = lines
                    .Skip(1)
                    .Select(static line => line.Split(':', 2))
                    .Where(static pair => pair.Length == 2 && pair[0].Equals("x-goog-api-key", StringComparison.OrdinalIgnoreCase))
                    .Select(static pair => pair[1].Trim())
                    .FirstOrDefault();

                var request = new GoogleRecordedRequest(new Uri(BaseAddress, target.TrimStart('/')), key, body);

                _requests.Enqueue(request);

                var payload = Encoding.UTF8.GetBytes(_responder(request));
                var header = Encoding.ASCII.GetBytes(
                    "HTTP/1.1 200 OK\r\n" +
                    "Content-Type: application/json\r\n" +
                    $"Content-Length: {payload.Length}\r\n" +
                    "Connection: close\r\n\r\n");

                await stream.WriteAsync(header, _stop.Token);
                await stream.WriteAsync(payload, _stop.Token);
                await stream.FlushAsync(_stop.Token);
            }
            catch (Exception exception) when (exception is OperationCanceledException or SocketException or ObjectDisposedException or IOException)
            {
                // The client went away or the server stopped; nothing is left to answer.
            }
        }
    }

    private static async Task<(string Head, string Body)> ReadRequestAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        var buffer = new byte[8192];
        using var received = new MemoryStream();
        var headEnd = -1;

        while (headEnd < 0)
        {
            var read = await stream.ReadAsync(buffer, cancellationToken);

            if (read == 0)
            {
                return (Encoding.ASCII.GetString(received.ToArray()), string.Empty);
            }

            received.Write(buffer, 0, read);
            headEnd = IndexOfHeadEnd(received.GetBuffer(), (int)received.Length);
        }

        var all = received.ToArray();
        var head = Encoding.ASCII.GetString(all, 0, headEnd + 4);
        var contentLength = ContentLength(head);

        using var body = new MemoryStream();
        body.Write(all, headEnd + 4, all.Length - headEnd - 4);

        while (body.Length < contentLength)
        {
            var read = await stream.ReadAsync(buffer, cancellationToken);

            if (read == 0)
            {
                break;
            }

            body.Write(buffer, 0, read);
        }

        return (head, Encoding.UTF8.GetString(body.ToArray()));
    }

    private static int IndexOfHeadEnd(byte[] bytes, int length)
    {
        for (var index = 0; index + 3 < length; index++)
        {
            if (bytes[index] == '\r' && bytes[index + 1] == '\n' && bytes[index + 2] == '\r' && bytes[index + 3] == '\n')
            {
                return index;
            }
        }

        return -1;
    }

    private static int ContentLength(string head)
    {
        foreach (var line in head.Split("\r\n"))
        {
            if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase)
                && int.TryParse(line["Content-Length:".Length..].Trim(), System.Globalization.CultureInfo.InvariantCulture, out var length))
            {
                return length;
            }
        }

        return 0;
    }
}
