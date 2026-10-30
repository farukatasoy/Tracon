using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Tracon.AspNetCore.FunctionalTests.Infrastructure;

/// <summary>
/// A local Gemini text-to-speech endpoint (<c>:generateContent</c> and <c>:streamGenerateContent</c>). It keeps what
/// arrived, with the key each request carried, and refuses the multi-speaker bodies the real API refuses.
/// </summary>
/// <remarks>
/// <para>
/// The adapter is only as honest as the wire. A test that asserts a fake synthesizer received a credential
/// proves nothing about the request the Google client sends, so these tests drive the real <c>Google.GenAI</c> client
/// against this server and read the JSON body and the <c>x-goog-api-key</c> header it received.
/// </para>
/// <para>
/// The refusals and the answers are the ones measured against the live API on 2026-10-25. The server pretends to be
/// one model generation at a time (<see cref="Format"/>): the gemini-3.8 models want the speaker on each text part
/// and answer a WAV file; the gemini-2.5 and gemini-3.1 preview models want a label in the text, refuse the part
/// metadata and answer raw PCM.
/// </para>
/// </remarks>
internal sealed class ScriptedGeminiSpeechServer : IAsyncDisposable
{
    /// <summary>The model generation the server answers like.</summary>
    public enum Generation
    {
        /// <summary>The speaker is on each text part; the answer is a WAV file with a provenance chunk.</summary>
        PartMetadata,

        /// <summary>The speaker is a label in the text; the answer is raw PCM.</summary>
        TextLabels,
    }

    /// <summary>The text the server puts in every error answer, with the key it received. A test must never see it echoed.</summary>
    public const string UpstreamText = "upstream-body-text-that-must-not-leak";

    private readonly WebApplication _app;
    private readonly ConcurrentQueue<int> _failures = new();
    private int _requests;

    private ScriptedGeminiSpeechServer(WebApplication app, Uri baseAddress)
    {
        _app = app;
        BaseAddress = baseAddress;
    }

    /// <summary>The server's base address (<c>http://127.0.0.1:{port}/</c>).</summary>
    public Uri BaseAddress { get; }

    /// <summary>Gets or sets the model generation the server answers like.</summary>
    public Generation Format { get; set; } = Generation.PartMetadata;

    /// <summary>Gets every request the server received, refused or not.</summary>
    public int Requests => Volatile.Read(ref _requests);

    /// <summary>Gets each request, in arrival order.</summary>
    public ConcurrentQueue<GeminiCall> Calls { get; } = new();

    /// <summary>The samples every answer carries: one second of 24 kHz mono 16-bit PCM.</summary>
    public static byte[] Pcm { get; } = BuildPcm();

    /// <summary>Makes the next requests answer with an error status.</summary>
    /// <param name="status">The HTTP status; a <c>5xx</c> is retried by the client, so prefer <c>4xx</c>.</param>
    /// <param name="times">How many requests fail.</param>
    /// <returns>This server.</returns>
    public ScriptedGeminiSpeechServer FailNext(int status, int times = 1)
    {
        for (var attempt = 0; attempt < times; attempt++)
        {
            _failures.Enqueue(status);
        }

        return this;
    }

    /// <summary>Starts the server and binds it to a random loopback port.</summary>
    /// <returns>The running server.</returns>
    public static async Task<ScriptedGeminiSpeechServer> StartAsync()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();

        var app = builder.Build();
        ScriptedGeminiSpeechServer? server = null;

        app.MapPost("/{**path}", async (HttpContext context, string path) =>
        {
            Interlocked.Increment(ref server!._requests);

            var body = (await JsonNode.ParseAsync(context.Request.Body).ConfigureAwait(false))!.AsObject();
            var key = context.Request.Headers["x-goog-api-key"].ToString();
            var streaming = path.EndsWith(":streamGenerateContent", StringComparison.Ordinal);

            server.Calls.Enqueue(new GeminiCall(path, key, body, streaming));

            if (server._failures.TryDequeue(out var status))
            {
                return Error(status, $"{UpstreamText} key={key}");
            }

            if (Validate(body, server.Format) is { } rejection)
            {
                return Error(StatusCodes.Status400BadRequest, rejection);
            }

            return streaming
                ? Stream(Pcm)
                : server.Format == Generation.PartMetadata
                    ? Answer("audio/wav", WavWithProvenance(Pcm))
                    : Answer("audio/L16;codec=pcm;rate=24000", Pcm);
        });

        await app.StartAsync().ConfigureAwait(false);

        server = new ScriptedGeminiSpeechServer(app, new Uri(app.Urls.First() + "/"));

        return server;
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync().ConfigureAwait(false);
        await _app.DisposeAsync().ConfigureAwait(false);
    }

    private static string? Validate(JsonObject body, Generation format)
    {
        var generation = body["generationConfig"]?.AsObject();
        var modalities = generation?["responseModalities"]?.AsArray().Select(static m => m?.GetValue<string>());

        if (modalities is null || !modalities.Contains("AUDIO", StringComparer.Ordinal))
        {
            return "Model tried to generate text, but it should only be used for TTS.";
        }

        var speech = generation!["speechConfig"]?.AsObject();
        var parts = body["contents"]?.AsArray().SelectMany(static c => c!["parts"]!.AsArray()).ToList() ?? [];

        if (speech?["multiSpeakerVoiceConfig"] is not JsonObject multi)
        {
            return null;
        }

        if (multi["speakerVoiceConfigs"]?.AsArray().Count != 2)
        {
            return "speaker_voice_configs: the number of speaker_voice_configs must equal 2.";
        }

        var withMetadata = parts.Count(static part => part!["speechMetadata"] is not null);

        return format switch
        {
            Generation.PartMetadata when withMetadata != parts.Count
                => "Multi-speaker generation requests must specify speech_metadata.speaker for each text part in the contents.",
            Generation.TextLabels when withMetadata > 0
                => "Speech metadata is not supported for this model.",
            _ => null,
        };
    }

    private static IResult Error(int status, string message)
        => Results.Json(
            new { error = new { code = status, message, status = "INVALID_ARGUMENT" } },
            statusCode: status);

    private static IResult Answer(string mimeType, byte[] audio)
        => Results.Json(new
        {
            candidates = new[]
            {
                new
                {
                    content = new
                    {
                        parts = new[] { new { inlineData = new { mimeType, data = Convert.ToBase64String(audio) } } },
                        role = "model",
                    },
                    finishReason = "STOP",
                    index = 0,
                },
            },
            usageMetadata = new { promptTokenCount = 9, candidatesTokenCount = 65, totalTokenCount = 74 },
            modelVersion = "gemini-scripted-tts",
            responseId = "scripted-response",
        });

    /// <summary>Three server-sent events, each a slice of the samples, the way the gemini-3.8 models stream.</summary>
    private static IResult Stream(byte[] pcm)
    {
        var third = pcm.Length / 3;
        var slices = new[] { pcm[..third], pcm[third..(2 * third)], pcm[(2 * third)..] };
        var body = new StringBuilder();

        foreach (var slice in slices)
        {
            var json = new JsonObject
            {
                ["candidates"] = new JsonArray(new JsonObject
                {
                    ["content"] = new JsonObject
                    {
                        ["parts"] = new JsonArray(new JsonObject
                        {
                            ["inlineData"] = new JsonObject
                            {
                                ["mimeType"] = "audio/l16; rate=24000; channels=1",
                                ["data"] = Convert.ToBase64String(slice),
                            },
                        }),
                        ["role"] = "model",
                    },
                }),
                ["modelVersion"] = "gemini-scripted-tts",
            };

            body.Append("data: ").Append(json.ToJsonString()).Append("\r\n\r\n");
        }

        return Results.Text(body.ToString(), "text/event-stream");
    }

    private static byte[] BuildPcm()
    {
        var bytes = new byte[24_000 * 2];

        for (var index = 0; index < 24_000; index++)
        {
            BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(index * 2, 2), (short)(index % 1000));
        }

        return bytes;
    }

    /// <summary>A WAV file the way the gemini-3.8 models answer it: the samples, then a <c>C2PA</c> chunk.</summary>
    private static byte[] WavWithProvenance(byte[] pcm)
    {
        using var file = new MemoryStream();
        using var writer = new BinaryWriter(file, Encoding.ASCII, leaveOpen: true);

        writer.Write("RIFF"u8);
        writer.Write(0);
        writer.Write("WAVE"u8);
        writer.Write("fmt "u8);
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)1);
        writer.Write(24_000);
        writer.Write(48_000);
        writer.Write((short)2);
        writer.Write((short)16);
        writer.Write("data"u8);
        writer.Write(pcm.Length);
        writer.Write(pcm);
        writer.Write("C2PA"u8);
        writer.Write(8);
        writer.Write("manifest"u8);
        writer.Flush();

        var bytes = file.ToArray();
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(4, 4), bytes.Length - 8);

        return bytes;
    }

    /// <summary>One request as the server saw it.</summary>
    /// <param name="Path">The request path, after the host.</param>
    /// <param name="ApiKey">The <c>x-goog-api-key</c> header.</param>
    /// <param name="Body">The JSON body.</param>
    /// <param name="Streaming">Whether the request was <c>:streamGenerateContent</c>.</param>
    internal sealed record GeminiCall(string Path, string ApiKey, JsonObject Body, bool Streaming);
}
