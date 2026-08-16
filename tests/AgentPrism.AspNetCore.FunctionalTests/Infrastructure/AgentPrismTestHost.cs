using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AgentPrism.Testing;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AgentPrism.AspNetCore.FunctionalTests.Infrastructure;

/// <summary>
/// Boots the AgentPrism endpoints in an in-process test host.
/// </summary>
/// <remarks>
/// <para>
/// Each test builds its own host; there is no state shared across tests.
/// In-memory stores are used, so setup is fast and no database is required.
/// </para>
/// <para>
/// The <c>X-Test-Remote-Ip</c> header sets the connection's remote address. The
/// loopback restriction looks at a real socket address; on the test host that
/// address is normally <see langword="null"/>, so there is no other way to
/// simulate a remote request. This seam exists <strong>only in tests</strong>,
/// not in the library.
/// </para>
/// </remarks>
internal sealed class AgentPrismTestHost : IAsyncDisposable
{
    /// <summary>The header a test uses to set the remote IP address.</summary>
    public const string RemoteIpHeader = "X-Test-Remote-Ip";

    private readonly WebApplication _app;

    private AgentPrismTestHost(WebApplication app, HttpClient client, RecordingLoggerProvider logs)
    {
        _app = app;
        Client = client;
        Logs = logs;
    }

    /// <summary>The client connected to the host.</summary>
    public HttpClient Client { get; }

    /// <summary>All log lines the host has written.</summary>
    public RecordingLoggerProvider Logs { get; }

    /// <summary>The application's service provider.</summary>
    public IServiceProvider Services => _app.Services;

    /// <summary>Builds and starts a host.</summary>
    /// <param name="configureAgentPrism">Changes the AgentPrism chain.</param>
    /// <param name="configureEndpoints">Changes the endpoint settings.</param>
    /// <param name="configureServices">Registers additional services.</param>
    /// <param name="prefix">The path prefix.</param>
    /// <param name="withOpenApi">
    /// Turns on OpenAPI document generation. This is what a consumer does in
    /// their own app; AgentPrism.AspNetCore does NOT depend on the OpenAPI package.
    /// </param>
    /// <param name="configureApp">
    /// Wires up an additional route after <c>app.Build()</c> and before the
    /// <c>MapAgentPrism</c> call (example: <c>app.MapHealthChecks("/health")</c>).
    /// </param>
    /// <param name="configureAfterMap">
    /// Wires up an additional route AFTER the <c>MapAgentPrism</c> call
    /// (example: <c>app.MapAgentPrismMcpServer()</c> — it must be called AFTER
    /// <c>MapAgentPrism</c> to inherit the shared access settings from it).
    /// </param>
    /// <returns>The running host.</returns>
    public static async Task<AgentPrismTestHost> StartAsync(
        Action<IAgentPrismBuilder>? configureAgentPrism = null,
        Action<AgentPrismEndpointOptions>? configureEndpoints = null,
        Action<IServiceCollection>? configureServices = null,
        string prefix = "/agentprism",
        bool withOpenApi = false,
        Action<WebApplication>? configureApp = null,
        Action<WebApplication>? configureAfterMap = null)
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
        // The name "echo" matches the fixed ModelBinding.Provider in TestData.Definition().
        agentPrism.AddModelProvider(new FakeModelProvider("echo").EchoesUserMessage());
        configureAgentPrism?.Invoke(agentPrism);

        var app = builder.Build();

        // Test seam that simulates the remote address. Runs before the endpoint filter.
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
        configureAfterMap?.Invoke(app);

        if (withOpenApi)
        {
            app.MapOpenApi();
        }

        await app.StartAsync();

        var client = app.GetTestClient();

        return new AgentPrismTestHost(app, client, logs);
    }

    /// <summary>
    /// Builds a client that connects to the host over WebSocket.
    /// </summary>
    /// <returns>The client.</returns>
    /// <remarks>
    /// <c>TestServer</c> does not open a real socket, but it simulates the
    /// WebSocket upgrade; the conversation endpoint can therefore be verified
    /// in-process.
    /// </remarks>
    public WebSocketClient CreateWebSocketClient() => _app.GetTestServer().CreateWebSocketClient();

    /// <summary>Parses the response as a JSON document.</summary>
    /// <param name="response">The HTTP response.</param>
    /// <returns>The parsed root element.</returns>
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
