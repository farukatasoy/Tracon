using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AgentPrism.AspNetCore.FunctionalTests.Infrastructure;

/// <summary>
/// AgentPrism uclarini surec ici bir test barindiricisinda ayaga kaldirir.
/// </summary>
/// <remarks>
/// <para>
/// Her test kendi barindiricisini kurar; testler arasinda paylasilan durum yoktur.
/// Bellek ici depolar kullanildigi icin kurulum hizlidir ve veritabani gerektirmez.
/// </para>
/// <para>
/// <c>X-Test-Remote-Ip</c> basligi baglantinin uzak adresini belirler. Loopback
/// kisiti gercek bir soket adresine bakar; test barindiricisinda bu adres normalde
/// <see langword="null"/>'dur, dolayisiyla uzak istegi taklit etmenin baska yolu yoktur.
/// Bu seam <strong>yalnizca testlerdedir</strong>, kutuphanede degil.
/// </para>
/// </remarks>
internal sealed class AgentPrismTestHost : IAsyncDisposable
{
    /// <summary>Testin uzak IP adresini belirledigi baslik.</summary>
    public const string RemoteIpHeader = "X-Test-Remote-Ip";

    private readonly WebApplication _app;

    private AgentPrismTestHost(WebApplication app, HttpClient client, RecordingLoggerProvider logs)
    {
        _app = app;
        Client = client;
        Logs = logs;
    }

    /// <summary>Barindiriciya baglanmis istemci.</summary>
    public HttpClient Client { get; }

    /// <summary>Barindiricinin yazdigi tum gunluk satirlari.</summary>
    public RecordingLoggerProvider Logs { get; }

    /// <summary>Uygulamanin servis saglayicisi.</summary>
    public IServiceProvider Services => _app.Services;

    /// <summary>Bir barindirici kurar ve baslatir.</summary>
    /// <param name="configureAgentPrism">AgentPrism zincirini degistirir.</param>
    /// <param name="configureEndpoints">Uc ayarlarini degistirir.</param>
    /// <param name="configureServices">Ek servis kaydi yapar.</param>
    /// <param name="prefix">Yol oneki.</param>
    /// <param name="withOpenApi">
    /// OpenAPI belgesi uretimini acar. Tuketicinin kendi uygulamasinda yaptigi
    /// sey budur; AgentPrism.AspNetCore OpenAPI paketine bagimli DEGILDIR.
    /// </param>
    /// <param name="configureApp">
    /// <c>app.Build()</c> sonrasi, <c>MapAgentPrism</c> cagrisindan once ek yol
    /// baglamak icin (ornek: <c>app.MapHealthChecks("/health")</c>).
    /// </param>
    /// <returns>Calisan barindirici.</returns>
    public static async Task<AgentPrismTestHost> StartAsync(
        Action<IAgentPrismBuilder>? configureAgentPrism = null,
        Action<AgentPrismEndpointOptions>? configureEndpoints = null,
        Action<IServiceCollection>? configureServices = null,
        string prefix = "/agentprism",
        bool withOpenApi = false,
        Action<WebApplication>? configureApp = null)
    {
        var logs = new RecordingLoggerProvider();

        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Logging.AddProvider(logs);
        builder.Logging.SetMinimumLevel(LogLevel.Trace);

        if (withOpenApi)
        {
            builder.Services.AddOpenApi();
        }

        configureServices?.Invoke(builder.Services);

        var agentPrism = builder.Services.AddAgentPrism();
        agentPrism.AddModelProvider(new EchoModelProvider());
        configureAgentPrism?.Invoke(agentPrism);

        var app = builder.Build();

        // Uzak adresi taklit eden test seam'i. Uc filtresinden once calisir.
        app.Use(static (context, next) =>
        {
            if (context.Request.Headers.TryGetValue(RemoteIpHeader, out var raw) &&
                IPAddress.TryParse(raw.ToString(), out var address))
            {
                context.Connection.RemoteIpAddress = address;
            }

            return next(context);
        });

        configureApp?.Invoke(app);

        app.MapAgentPrism(prefix, configureEndpoints);

        if (withOpenApi)
        {
            app.MapOpenApi();
        }

        await app.StartAsync();

        var client = app.GetTestClient();

        return new AgentPrismTestHost(app, client, logs);
    }

    /// <summary>
    /// Barindiriciya WebSocket ile baglanan bir istemci kurar.
    /// </summary>
    /// <returns>Istemci.</returns>
    /// <remarks>
    /// <c>TestServer</c> gercek bir soket acmaz ama WebSocket yukseltmesini
    /// taklit eder; konusma ucu bu yuzden surec ici dogrulanabilir.
    /// </remarks>
    public WebSocketClient CreateWebSocketClient() => _app.GetTestServer().CreateWebSocketClient();

    /// <summary>Yaniti JSON belgesi olarak cozumler.</summary>
    /// <param name="response">HTTP yaniti.</param>
    /// <returns>Cozumlenmis kok eleman.</returns>
    public static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
        => await response.Content.ReadFromJsonAsync<JsonElement>();

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await _app.StopAsync();
        await _app.DisposeAsync();
        Logs.Dispose();
    }
}
