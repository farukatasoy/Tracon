using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace AgentPrism.AspNetCore.FunctionalTests.Infrastructure;

/// <summary>
/// Gercek bir yerel HTTP sunucusu (<c>127.0.0.1</c>, rastgele port) uzerinden
/// minimal bir OpenAI uyumlu API taklit eder.
/// </summary>
/// <remarks>
/// Ollama bu makinede kurulu degil; F-05'in "yerel, anahtarsiz sunucu" mekanizmasini
/// (baglanti + kimliksiz istek) gercek bir soket uzerinden dogrulamak icin bu
/// yerine gecen sunucu kullanilir. Bkz. <c>docs/08-SAGLAYICI-GENISLEMESI.md</c>,
/// bolum 8.2 ve DoD notu.
/// </remarks>
internal sealed class FakeOpenAiCompatibleServer : IAsyncDisposable
{
    private readonly WebApplication _app;

    private FakeOpenAiCompatibleServer(WebApplication app, Uri baseAddress)
    {
        _app = app;
        BaseAddress = baseAddress;
    }

    /// <summary>Sunucunun taban adresi (<c>http://127.0.0.1:{port}/v1</c>).</summary>
    public Uri BaseAddress { get; }

    /// <summary><c>/v1/chat/completions</c> ucuna gelen isteklerdeki Authorization baslik degerleri.</summary>
    public List<string?> ReceivedAuthorizationHeaders { get; } = [];

    /// <summary><c>/v1/chat/completions</c> ucunun kac kez cagrildigi.</summary>
    public int ChatCompletionCallCount { get; private set; }

    /// <summary><c>/v1/models</c> ucunun kac kez cagrildigi.</summary>
    public int ModelsCallCount { get; private set; }

    /// <summary>
    /// <c>/v1/chat/completions</c> cagrisinda donulecek HTTP durum kodu.
    /// Varsayilan 200; hata senaryolarini denemek icin degistirilebilir.
    /// </summary>
    public int ChatCompletionStatusCode { get; set; } = StatusCodes.Status200OK;

    /// <summary>
    /// <c>/v1/models</c> cagrisinda donulecek HTTP durum kodu. Varsayilan 200.
    /// </summary>
    public int ModelsStatusCode { get; set; } = StatusCodes.Status200OK;

    /// <summary>Sunucuyu baslatir ve rastgele bir loopback portuna baglar.</summary>
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
                        message = new { role = "assistant", content = "merhaba yerelden" },
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
