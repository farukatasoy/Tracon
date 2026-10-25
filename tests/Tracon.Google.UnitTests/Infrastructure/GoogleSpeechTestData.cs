using System.Buffers.Binary;
using System.Net;
using System.Text;
using System.Text.Json;
using Google.GenAI;
using Google.GenAI.Types;

namespace Tracon.Google.UnitTests.Infrastructure;

/// <summary>
/// A fake HTTP handler that records the requests the Google client sends and answers a canned response.
/// </summary>
/// <remarks>
/// No test reaches Google. The request shape (path, key header, body) is what the speech tests verify, and the
/// answers are the shapes measured against the live API on 2026-10-25.
/// </remarks>
internal sealed class GoogleStubHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

    public GoogleStubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
        => _responder = responder;

    /// <summary>Requests seen, in arrival order.</summary>
    public List<GoogleRecordedRequest> Requests { get; } = [];

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // The body is read before the request is disposed: afterwards the content is gone.
        var body = request.Content is null
            ? string.Empty
            : await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        Requests.Add(new GoogleRecordedRequest(
            request.RequestUri!,
            request.Headers.TryGetValues("x-goog-api-key", out var key) ? string.Join(',', key) : null,
            body));

        return _responder(request);
    }
}

/// <summary>A request the Google client sent.</summary>
internal sealed record GoogleRecordedRequest(Uri Uri, string? ApiKey, string Body)
{
    /// <summary>Parses the JSON body.</summary>
    public JsonElement Json => JsonDocument.Parse(Body).RootElement;
}

/// <summary>Builders for the speech tests.</summary>
internal static class GoogleSpeechTestData
{
    public const string SetupKey = "setup-key-0000000000000";
    public const string TenantKey = "tenant-key-111111111111";
    public const string Model = "gemini-test-tts";

    /// <summary>Builds a Google client whose HTTP traffic goes to <paramref name="handler"/>.</summary>
    public static Client CreateClient(GoogleStubHandler handler, string apiKey = SetupKey)
        => new(
            apiKey: apiKey,
            httpOptions: new HttpOptions { BaseUrl = "https://google.example.test/" },
            clientOptions: new ClientOptions { HttpClientFactory = () => new HttpClient(handler, disposeHandler: false) });

    /// <summary>Builds the synthesizer; a tenant credential gets a client of its own over the same handler.</summary>
    public static GoogleSpeechSynthesizer CreateSynthesizer(
        GoogleStubHandler handler,
        Action<GoogleSpeechOptions>? configure = null,
        List<ModelProviderCredential>? tenantCredentials = null)
    {
        var options = new GoogleSpeechOptions { Model = Model };
        configure?.Invoke(options);

        return new GoogleSpeechSynthesizer(
            options,
            CreateClient(handler),
            credential =>
            {
                tenantCredentials?.Add(credential);
                return CreateClient(handler, credential.ApiKey);
            });
    }

    /// <summary>One second of a 440 Hz-ish ramp as 16-bit mono PCM, so a test can tell the samples apart.</summary>
    public static byte[] Pcm(int samples = 24_000)
    {
        var bytes = new byte[samples * 2];

        for (var index = 0; index < samples; index++)
        {
            BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(index * 2, 2), (short)(index % 1000));
        }

        return bytes;
    }

    /// <summary>A WAV file the way <c>gemini-3.8-flash-tts</c> answers it: the samples, then a <c>C2PA</c> chunk.</summary>
    public static byte[] WavWithProvenance(byte[] pcm, int sampleRate = 24_000, int channels = 1)
    {
        var provenance = Encoding.ASCII.GetBytes("jumb-manifest-bytes");
        using var file = new MemoryStream();
        using var writer = new BinaryWriter(file, Encoding.ASCII, leaveOpen: true);

        writer.Write("RIFF"u8);
        writer.Write(0);
        writer.Write("WAVE"u8);
        writer.Write("fmt "u8);
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)channels);
        writer.Write(sampleRate);
        writer.Write(sampleRate * channels * 2);
        writer.Write((short)(channels * 2));
        writer.Write((short)16);
        writer.Write("data"u8);
        writer.Write(pcm.Length);
        writer.Write(pcm);
        writer.Write("C2PA"u8);
        writer.Write(provenance.Length);
        writer.Write(provenance);

        if ((provenance.Length & 1) == 1)
        {
            writer.Write((byte)0);
        }

        writer.Flush();

        var bytes = file.ToArray();
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(4, 4), bytes.Length - 8);

        return bytes;
    }

    /// <summary>A <c>generateContent</c> answer with one audio part.</summary>
    public static HttpResponseMessage Answer(
        byte[] audio,
        string mimeType,
        string? modelVersion = Model,
        string? responseId = "response-1")
        => Json(AnswerJson(new[] { (mimeType, audio) }, modelVersion, responseId));

    /// <summary>The JSON of a <c>generateContent</c> answer with the given audio parts.</summary>
    public static string AnswerJson(IEnumerable<(string MimeType, byte[] Audio)> parts, string? modelVersion = Model, string? responseId = "response-1")
        => JsonSerializer.Serialize(new
        {
            candidates = new[]
            {
                new
                {
                    content = new
                    {
                        parts = parts.Select(static part => new
                        {
                            inlineData = new { mimeType = part.MimeType, data = Convert.ToBase64String(part.Audio) },
                        }).ToArray(),
                        role = "model",
                    },
                    finishReason = "STOP",
                    index = 0,
                },
            },
            usageMetadata = new { promptTokenCount = 9, candidatesTokenCount = 65, totalTokenCount = 74 },
            modelVersion,
            responseId,
        });

    /// <summary>A server-sent-events answer: one <c>data:</c> event per audio chunk.</summary>
    public static HttpResponseMessage Stream(string mimeType, params byte[][] chunks)
    {
        var body = new StringBuilder();

        foreach (var chunk in chunks)
        {
            body.Append("data: ")
                .Append(AnswerJson(new[] { (mimeType, chunk) }, Model, "stream-1").ReplaceLineEndings(string.Empty))
                .Append("\r\n\r\n");
        }

        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(body.ToString(), Encoding.UTF8, "text/event-stream"),
        };
    }

    /// <summary>
    /// A server-sent-events answer that sends one audio chunk and then either breaks the connection or never sends
    /// again, so a test can stop or fail a stream in the middle.
    /// </summary>
    public static HttpResponseMessage StreamThatStopsAfterOneChunk(byte[] chunk, bool breakConnection)
    {
        var first = Encoding.UTF8.GetBytes(
            "data: " + AnswerJson(new[] { ("audio/l16; rate=24000; channels=1", chunk) }, Model, "stream-1").ReplaceLineEndings(string.Empty) + "\r\n\r\n");

        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(new StallingStream(first, breakConnection))
            {
                Headers = { ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("text/event-stream") },
            },
        };
    }

    /// <summary>Returns its bytes, then throws <see cref="IOException"/> or waits until the read is cancelled.</summary>
    private sealed class StallingStream(byte[] data, bool breakConnection) : Stream
    {
        private int _position;

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (_position < data.Length)
            {
                var count = Math.Min(buffer.Length, data.Length - _position);
                data.AsMemory(_position, count).CopyTo(buffer);
                _position += count;

                return count;
            }

            if (breakConnection)
            {
                throw new IOException("The connection was reset.");
            }

            await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false); // delay: simulated

            return 0;
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_position < data.Length)
            {
                var length = Math.Min(count, data.Length - _position);
                Array.Copy(data, _position, buffer, offset, length);
                _position += length;

                return length;
            }

            if (breakConnection)
            {
                throw new IOException("The connection was reset.");
            }

            // A read that nothing cancels: the test ends it by cancelling the token the caller holds.
            Thread.Sleep(TimeSpan.FromSeconds(30));

            return 0;
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    public static HttpResponseMessage Json(string body, HttpStatusCode status = HttpStatusCode.OK)
        => new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    /// <summary>A Google error answer.</summary>
    public static HttpResponseMessage Error(int code, string status, string message)
        => Json(JsonSerializer.Serialize(new { error = new { code, message, status } }), (HttpStatusCode)code);

    public static SpeechRequest Speak(string text = "Hello there.", string? voice = "Kore")
        => new() { Text = text, VoiceId = voice };

    /// <summary>A two-turn request between two voices.</summary>
    public static SpeechDialogueRequest TwoTurns()
        => new()
        {
            Turns =
            [
                new SpeechDialogueTurn { Text = "Welcome back.", VoiceId = "Kore" },
                new SpeechDialogueTurn { Text = "Glad to be here.", VoiceId = "Puck" },
            ],
        };

    /// <summary>A settings dictionary from key/value pairs.</summary>
    public static IReadOnlyDictionary<string, JsonElement> Settings(params (string Key, object? Value)[] pairs)
        => pairs.ToDictionary(
            static pair => pair.Key,
            static pair => JsonSerializer.SerializeToElement(pair.Value),
            StringComparer.Ordinal);
}
