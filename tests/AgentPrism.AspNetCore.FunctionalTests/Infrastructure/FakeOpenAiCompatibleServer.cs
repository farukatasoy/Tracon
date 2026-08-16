using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace AgentPrism.AspNetCore.FunctionalTests.Infrastructure;

/// <summary>
/// Fakes a minimal OpenAI-compatible API over a real local HTTP server
/// (<c>127.0.0.1</c>, random port).
/// </summary>
/// <remarks>
/// Ollama is not installed on this machine; this stand-in server verifies
/// F-05's "local, keyless server" mechanism (connect + unauthenticated request)
/// over a real socket. See <c>docs/08-SAGLAYICI-GENISLEMESI.md</c>, section 8.2
/// and its DoD note.
/// </remarks>
internal sealed class FakeOpenAiCompatibleServer : IAsyncDisposable
{
    private readonly WebApplication _app;

    private FakeOpenAiCompatibleServer(WebApplication app, Uri baseAddress)
    {
        _app = app;
        BaseAddress = baseAddress;
    }

    /// <summary>The server's base address (<c>http://127.0.0.1:{port}/v1</c>).</summary>
    public Uri BaseAddress { get; }

    /// <summary>The Authorization header values received on requests to <c>/v1/chat/completions</c>.</summary>
    public List<string?> ReceivedAuthorizationHeaders { get; } = [];

    /// <summary>How many times <c>/v1/chat/completions</c> was called.</summary>
    public int ChatCompletionCallCount { get; private set; }

    /// <summary>How many times <c>/v1/models</c> was called.</summary>
    public int ModelsCallCount { get; private set; }

    /// <summary>
    /// The HTTP status code to return from <c>/v1/chat/completions</c>.
    /// Defaults to 200; can be changed to exercise error scenarios.
    /// </summary>
    public int ChatCompletionStatusCode { get; set; } = StatusCodes.Status200OK;

    /// <summary>
    /// The HTTP status code to return from <c>/v1/models</c>. Defaults to 200.
    /// </summary>
    public int ModelsStatusCode { get; set; } = StatusCodes.Status200OK;

    /// <summary>Starts the server and binds it to a random loopback port.</summary>
    public static async Task<FakeOpenAiCompatibleServer> StartAsync()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();

        var app = builder.Build();
        FakeOpenAiCompatibleServer? server = null;

        app.MapGet("/v1/models", () =>
        {
            server!.ModelsCallCount++;

            return server.ModelsStatusCode == StatusCodes.Status200OK
                ? Results.Json(new { data = new[] { new { id = "fake-local-model", @object = "model" } } })
                : Results.StatusCode(server.ModelsStatusCode);
        });

        app.MapPost("/v1/chat/completions", (HttpContext context) =>
        {
            server!.ChatCompletionCallCount++;
            server.ReceivedAuthorizationHeaders.Add(
                context.Request.Headers.Authorization is { Count: > 0 } values ? values.ToString() : null);

            if (server.ChatCompletionStatusCode != StatusCodes.Status200OK)
            {
                return Results.StatusCode(server.ChatCompletionStatusCode);
            }

            return Results.Json(new
            {
                id = "chatcmpl-fake",
                @object = "chat.completion",
                created = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                model = "fake-local-model",
                choices = new[]
                {
                    new
                    {
                        index = 0,
                        message = new { role = "assistant", content = "hello from local" },
                        finish_reason = "stop",
                    },
                },
                usage = new { prompt_tokens = 5, completion_tokens = 3, total_tokens = 8 },
            });
        });

        await app.StartAsync().ConfigureAwait(false);

        var address = app.Urls.First();
        server = new FakeOpenAiCompatibleServer(app, new Uri($"{address}/v1"));

        return server;
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync().ConfigureAwait(false);
        await _app.DisposeAsync().ConfigureAwait(false);
    }
}
