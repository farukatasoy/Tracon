using System.Collections.Concurrent;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Tracon.AspNetCore.FunctionalTests.Infrastructure;

/// <summary>
/// A local OpenAI audio server: <c>/v1/audio/speech</c> and <c>/v1/audio/transcriptions</c>.
/// It keeps what arrived and refuses the bodies the real API refuses.
/// </summary>
/// <remarks>
/// <para>
/// The speech bridge is only as honest as the wire. A test that asserts a fake
/// <c>ITextToSpeechClient</c> received an option proves nothing about the request the OpenAI
/// client sends, so these tests drive the real <c>OpenAIClient</c> against this server and
/// assert the JSON body (<see cref="SpeechBodies"/>) and the transcription form
/// (<see cref="Transcriptions"/>).
/// </para>
/// <para>
/// Refused like the real API: a <c>response_format</c> outside the six containers
/// (<c>invalid_value</c>), and a missing <c>input</c> or <c>voice</c>. An unknown
/// <c>response_format</c> such as <c>mp3_44100_128</c> is the case the bridge must prevent.
/// </para>
/// </remarks>
internal sealed class ScriptedOpenAIAudioServer : IAsyncDisposable
{
    /// <summary>A valid MP3 first frame (ID3 tag + frame sync), so the attachment type check passes.</summary>
    public static readonly byte[] Mp3 =
        [0x49, 0x44, 0x33, 0x04, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0xFF, 0xFB, 0x90, 0x00];

    private static readonly string[] Containers = ["mp3", "opus", "aac", "flac", "wav", "pcm"];

    private readonly WebApplication _app;
    private readonly ConcurrentQueue<int> _failures = new();
    private int _requests;

    private ScriptedOpenAIAudioServer(WebApplication app, Uri baseAddress)
    {
        _app = app;
        BaseAddress = baseAddress;
    }

    /// <summary>The server's base address (<c>http://127.0.0.1:{port}/v1</c>).</summary>
    public Uri BaseAddress { get; }

    /// <summary>Gets every request the server received, speech and transcription, refused or not.</summary>
    public int Requests => Volatile.Read(ref _requests);

    /// <summary>Gets each JSON body received on <c>/v1/audio/speech</c>, in order.</summary>
    public List<JsonObject> SpeechBodies { get; } = [];

    /// <summary>Gets each form received on <c>/v1/audio/transcriptions</c>, in order.</summary>
    public List<TranscriptionForm> Transcriptions { get; } = [];

    /// <summary>How many bytes of <see cref="Mp3"/> the gated speech answer sends before it waits.</summary>
    public const int FirstChunkLength = 6;

    /// <summary>
    /// Gets or sets a gate: while it is set, a speech answer sends <see cref="FirstChunkLength"/> bytes,
    /// flushes, and sends the rest only when the gate is completed.
    /// </summary>
    public TaskCompletionSource? StreamGate { get; set; }

    /// <summary>Gets or sets the text the transcription endpoint answers with.</summary>
    public string TranscriptText { get; set; } = "where is my order";

    /// <summary>Makes the next requests answer with an error status. The body is a fixed text the tests must never see echoed.</summary>
    /// <param name="status">The HTTP status.</param>
    /// <param name="times">
    /// How many requests fail. The OpenAI client retries a <c>5xx</c> on its own (three times by
    /// default), so one failure is retried into a success and proves nothing.
    /// </param>
    /// <returns>This server.</returns>
    public ScriptedOpenAIAudioServer FailNext(int status, int times = 1)
    {
        for (var attempt = 0; attempt < times; attempt++)
        {
            _failures.Enqueue(status);
        }

        return this;
    }

    /// <summary>Starts the server and binds it to a random loopback port.</summary>
    /// <returns>The running server.</returns>
    public static async Task<ScriptedOpenAIAudioServer> StartAsync()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();

        var app = builder.Build();
        ScriptedOpenAIAudioServer? server = null;

        app.MapPost("/v1/audio/speech", async (HttpContext context) =>
        {
            Interlocked.Increment(ref server!._requests);

            var body = (await JsonNode.ParseAsync(context.Request.Body).ConfigureAwait(false))!.AsObject();

            lock (server.SpeechBodies)
            {
                server.SpeechBodies.Add(body);
            }

            if (server._failures.TryDequeue(out var status))
            {
                return Failure(status);
            }

            if (Validate(body) is { } rejection)
            {
                return Results.Json(
                    new { error = new { message = rejection.Message, type = "invalid_request_error", param = rejection.Param, code = rejection.Code } },
                    statusCode: StatusCodes.Status400BadRequest);
            }

            if (server.StreamGate is not { } gate)
            {
                return Results.Bytes(Mp3, "audio/mpeg");
            }

            // Chunked on purpose: the first half is flushed to the client and the second waits for
            // the gate, so a test can see the first chunk BEFORE the audio is complete.
            context.Response.ContentType = "audio/mpeg";
            await context.Response.Body.WriteAsync(Mp3.AsMemory(0, FirstChunkLength)).ConfigureAwait(false);
            await context.Response.Body.FlushAsync().ConfigureAwait(false);
            await gate.Task.ConfigureAwait(false);
            await context.Response.Body.WriteAsync(Mp3.AsMemory(FirstChunkLength)).ConfigureAwait(false);

            return Results.Empty;
        });

        app.MapPost("/v1/audio/transcriptions", async (HttpContext context) =>
        {
            Interlocked.Increment(ref server!._requests);

            var form = await context.Request.ReadFormAsync().ConfigureAwait(false);
            var file = form.Files.Count > 0 ? form.Files[0] : null;

            lock (server.Transcriptions)
            {
                server.Transcriptions.Add(new TranscriptionForm(
                    form["model"].ToString(),
                    form["language"].ToString(),
                    file?.FileName,
                    file?.Length ?? 0));
            }

            return server._failures.TryDequeue(out var status)
                ? Failure(status)
                : Results.Json(new { text = server.TranscriptText });
        });

        await app.StartAsync().ConfigureAwait(false);

        server = new ScriptedOpenAIAudioServer(app, new Uri($"{app.Urls.First()}/v1"));

        return server;
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync().ConfigureAwait(false);
        await _app.DisposeAsync().ConfigureAwait(false);
    }

    private static IResult Failure(int status)
        => Results.Json(
            new { error = new { message = "upstream-body-text-that-must-not-leak", type = "server_error", code = "model_not_found" } },
            statusCode: status);

    private static Rejection? Validate(JsonObject body)
    {
        if (body["input"] is null)
        {
            return new Rejection("Missing required parameter: 'input'.", "input", "missing_required_parameter");
        }

        if (body["voice"] is null)
        {
            return new Rejection("Missing required parameter: 'voice'.", "voice", "missing_required_parameter");
        }

        if (body["response_format"]?.GetValue<string>() is { } format && Array.IndexOf(Containers, format) < 0)
        {
            return new Rejection(
                $"Invalid value: '{format}'. Supported values are: 'mp3', 'opus', 'aac', 'flac', 'wav', and 'pcm'.",
                "response_format",
                "invalid_value");
        }

        return null;
    }

    /// <summary>One transcription request as the server saw it.</summary>
    /// <param name="Model">The <c>model</c> field.</param>
    /// <param name="Language">The <c>language</c> field, empty when absent.</param>
    /// <param name="FileName">The file part's name, or <see langword="null"/> when there was no file.</param>
    /// <param name="FileLength">The file part's size in bytes.</param>
    internal sealed record TranscriptionForm(string Model, string Language, string? FileName, long FileLength);

    private sealed record Rejection(string Message, string Param, string Code);
}
