using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Tracon.AspNetCore.FunctionalTests.Infrastructure;

/// <summary>
/// A local Gemini text endpoint (<c>:generateContent</c> and <c>:streamGenerateContent</c>) that answers every request
/// with one canned sentence and one canned usage block, or with a Google-shaped error body.
/// </summary>
/// <remarks>
/// <para>
/// The real <c>Google.GenAI</c> client is driven against it, so the exception a run meets is the one the SDK throws for
/// the answer a Gemini service gives: a JSON body <c>{"error":{"code":503,"message":"…","status":"UNAVAILABLE"}}</c>
/// puts the service's own sentence in the message, and that sentence carries no HTTP status.
/// </para>
/// <para>
/// The SDK does not retry on its own unless the options ask for it, so <see cref="Calls"/> counts what a caller sent.
/// </para>
/// </remarks>
internal sealed class FakeGeminiServer : IAsyncDisposable
{
    private readonly WebApplication _app;
    private int _calls;
    private volatile int _status = StatusCodes.Status200OK;
    private volatile string _errorName = "UNAVAILABLE";
    private volatile string _usage = """{"promptTokenCount":12,"candidatesTokenCount":6,"totalTokenCount":18}""";
    private volatile string _text = "hello from gemini";

    private FakeGeminiServer(WebApplication app, Uri baseAddress)
    {
        _app = app;
        BaseAddress = baseAddress;
    }

    /// <summary>Gets the server's base address (<c>http://127.0.0.1:{port}/</c>).</summary>
    public Uri BaseAddress { get; }

    /// <summary>Gets how many requests the server received.</summary>
    public int Calls => Volatile.Read(ref _calls);

    /// <summary>Gets or sets the HTTP status of the answer; anything but <c>200</c> answers a Google-shaped error body.</summary>
    public int Status
    {
        get => _status;
        set => _status = value;
    }

    /// <summary>Gets or sets the status name the error body carries (<c>UNAVAILABLE</c>, <c>RESOURCE_EXHAUSTED</c>).</summary>
    public string ErrorName
    {
        get => _errorName;
        set => _errorName = value;
    }

    /// <summary>Gets or sets the <c>usageMetadata</c> JSON object the answer carries.</summary>
    public string Usage
    {
        get => _usage;
        set => _usage = value;
    }

    /// <summary>Gets or sets the text the answer carries.</summary>
    public string Text
    {
        get => _text;
        set => _text = value;
    }

    /// <summary>Starts the server on a random loopback port.</summary>
    /// <returns>The running server.</returns>
    public static async Task<FakeGeminiServer> StartAsync()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();

        var app = builder.Build();
        FakeGeminiServer? server = null;

        app.Run(async context =>
        {
            // The body is read to the end so the client is not cut off while it writes.
            await context.Request.Body.CopyToAsync(Stream.Null, context.RequestAborted).ConfigureAwait(false);

            Interlocked.Increment(ref server!._calls);

            var status = server._status;

            if (status != StatusCodes.Status200OK)
            {
                context.Response.StatusCode = status;
                context.Response.ContentType = "application/json";

                await context.Response.WriteAsync(
                    $$$"""{"error":{"code":{{{status}}},"message":"The service said something specific.","status":"{{{server._errorName}}}"}}""",
                    Encoding.UTF8,
                    context.RequestAborted).ConfigureAwait(false);

                return;
            }

            var answer =
                $$$"""{"candidates":[{"content":{"role":"model","parts":[{"text":"{{{server._text}}}"}]},"finishReason":"STOP","index":0}],"usageMetadata":{{{server._usage}}},"modelVersion":"gemini-test"}""";

            if (context.Request.Path.Value?.Contains("streamGenerateContent", StringComparison.Ordinal) == true)
            {
                context.Response.ContentType = "text/event-stream";

                await context.Response.WriteAsync($"data: {answer}\r\n\r\n", Encoding.UTF8, context.RequestAborted).ConfigureAwait(false);

                return;
            }

            context.Response.ContentType = "application/json";

            await context.Response.WriteAsync(answer, Encoding.UTF8, context.RequestAborted).ConfigureAwait(false);
        });

        await app.StartAsync().ConfigureAwait(false);

        server = new FakeGeminiServer(app, new Uri($"{app.Urls.First()}/"));

        return server;
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync().ConfigureAwait(false);
        await _app.DisposeAsync().ConfigureAwait(false);
    }
}
