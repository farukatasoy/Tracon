using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Tracon.Samples.ProviderAotSmoke;

/// <summary>
/// A loopback HTTP server that answers the request shapes the provider
/// packages send with one canned reply each. It stands in for OpenAI (chat and
/// audio), Azure OpenAI and ElevenLabs so the smoke needs no network and no key.
/// </summary>
/// <remarks>
/// Built on <see cref="TcpListener"/> on purpose: a Kestrel host would pull the
/// ASP.NET Core graph, which does not make the Native AOT promise, into a sample
/// whose point is to publish only the provider packages.
/// </remarks>
internal sealed class FakeProviderServer : IDisposable
{
    public const string Answer = "pong";

    public const string Transcript = "where is my order";

    private static readonly byte[] AudioBytes = "ID3-fake-audio"u8.ToArray();

    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _loop;

    public FakeProviderServer()
    {
        _listener.Start();
        BaseAddress = new Uri($"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}/");
        _loop = Task.Run(AcceptLoopAsync);
    }

    public Uri BaseAddress { get; }

    /// <summary>The paths the server saw, in order. The smoke checks each provider reached its own.</summary>
    public List<string> Requests { get; } = [];

    public static ReadOnlySpan<byte> ExpectedAudio => AudioBytes;

    public void Dispose()
    {
        _stop.Cancel();
        _listener.Stop();

        try
        {
            _loop.GetAwaiter().GetResult();
        }
        catch (OperationCanceledException)
        {
            // Stopping the listener ends the accept loop.
        }
        catch (ObjectDisposedException)
        {
        }

        _stop.Dispose();
    }

    private async Task AcceptLoopAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            TcpClient client;

            try
            {
                client = await _listener.AcceptTcpClientAsync(_stop.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (SocketException)
            {
                return;
            }

            _ = Task.Run(() => ServeAsync(client));
        }
    }

    private async Task ServeAsync(TcpClient client)
    {
        using (client)
        {
            try
            {
                var stream = client.GetStream();
                var (target, body) = await ReadRequestAsync(stream).ConfigureAwait(false);

                lock (Requests)
                {
                    Requests.Add(target);
                }

                var (contentType, payload) = Route(target, body);
                var head = $"HTTP/1.1 200 OK\r\nContent-Type: {contentType}\r\nContent-Length: {payload.Length}\r\nConnection: close\r\n\r\n";

                await stream.WriteAsync(Encoding.ASCII.GetBytes(head)).ConfigureAwait(false);
                await stream.WriteAsync(payload).ConfigureAwait(false);
                await stream.FlushAsync().ConfigureAwait(false);
            }
            catch (IOException)
            {
                // The client hung up; nothing to answer.
            }
        }
    }

    private static async Task<(string Target, string Body)> ReadRequestAsync(NetworkStream stream)
    {
        var buffer = new List<byte>();
        var one = new byte[1];

        // Read to the blank line that ends the headers.
        while (!EndsWithBlankLine(buffer))
        {
            if (await stream.ReadAsync(one).ConfigureAwait(false) == 0)
            {
                break;
            }

            buffer.Add(one[0]);
        }

        var head = Encoding.ASCII.GetString([.. buffer]);
        var target = head.Split(' ', 3)[1];
        var length = 0;

        foreach (var line in head.Split("\r\n"))
        {
            if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
            {
                length = int.Parse(line["Content-Length:".Length..].Trim(), System.Globalization.CultureInfo.InvariantCulture);
            }
        }

        var body = new byte[length];
        var read = 0;

        while (read < length)
        {
            var count = await stream.ReadAsync(body.AsMemory(read)).ConfigureAwait(false);

            if (count == 0)
            {
                break;
            }

            read += count;
        }

        return (target, Encoding.UTF8.GetString(body, 0, read));
    }

    private static bool EndsWithBlankLine(List<byte> bytes)
        => bytes.Count >= 4
            && bytes[^4] == '\r' && bytes[^3] == '\n' && bytes[^2] == '\r' && bytes[^1] == '\n';

    private static (string ContentType, byte[] Payload) Route(string target, string body)
    {
        if (target.Contains("chat/completions", StringComparison.Ordinal))
        {
            return body.Contains("\"stream\":true", StringComparison.Ordinal)
                ? ("text/event-stream", Encoding.UTF8.GetBytes(ChatCompletionStream()))
                : ("application/json", Encoding.UTF8.GetBytes(ChatCompletion()));
        }

        if (target.StartsWith("/v2/voices", StringComparison.Ordinal))
        {
            return ("application/json", Encoding.UTF8.GetBytes(
                """{"voices":[{"voice_id":"voice-1","name":"Rachel","category":"premade"}],"has_more":false}"""));
        }

        if (target.StartsWith("/v1/text-to-speech/", StringComparison.Ordinal))
        {
            return ("audio/mpeg", AudioBytes);
        }

        // OpenAI audio: the speech endpoint answers the audio bytes, the transcription
        // endpoint answers {"text": ...}. The bridge's two paths reach these.
        if (target.StartsWith("/v1/audio/speech", StringComparison.Ordinal))
        {
            return ("audio/mpeg", AudioBytes);
        }

        if (target.StartsWith("/v1/audio/transcriptions", StringComparison.Ordinal))
        {
            return ("application/json", Encoding.UTF8.GetBytes($"{{\"text\":\"{Transcript}\"}}"));
        }

        return ("application/json", "{}"u8.ToArray());
    }

    private static string ChatCompletion() =>
        "{\"id\":\"chatcmpl-1\",\"object\":\"chat.completion\",\"created\":1,\"model\":\"smoke-model\"," +
        $"\"choices\":[{{\"index\":0,\"message\":{{\"role\":\"assistant\",\"content\":\"{Answer}\"}},\"finish_reason\":\"stop\"}}]," +
        "\"usage\":{\"prompt_tokens\":3,\"completion_tokens\":1,\"total_tokens\":4}}";

    private static string ChatCompletionStream() =>
        "data: {\"id\":\"c1\",\"object\":\"chat.completion.chunk\",\"created\":1,\"model\":\"smoke-model\"," +
        "\"choices\":[{\"index\":0,\"delta\":{\"role\":\"assistant\",\"content\":\"po\"},\"finish_reason\":null}]}\n\n" +
        "data: {\"id\":\"c1\",\"object\":\"chat.completion.chunk\",\"created\":1,\"model\":\"smoke-model\"," +
        "\"choices\":[{\"index\":0,\"delta\":{\"content\":\"ng\"},\"finish_reason\":\"stop\"}]}\n\n" +
        "data: [DONE]\n\n";
}
