using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Tracon.Testing;

namespace Tracon.AspNetCore.FunctionalTests.Infrastructure;

/// <summary>
/// Boots the Tracon endpoints in an in-process test host.
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
internal sealed class TraconTestHost : IAsyncDisposable
{
    /// <summary>The header a test uses to set the remote IP address.</summary>
    public const string RemoteIpHeader = "X-Test-Remote-Ip";

    /// <summary>The most log lines a failed test's note holds.</summary>
    internal const int NoteLineLimit = 200;

    /// <summary>The most characters a failed test's note holds; the last line may pass it by one line width.</summary>
    internal const int NoteByteLimit = 32 * 1024;

    /// <summary>The longest log line a note keeps whole.</summary>
    internal const int NoteLineWidth = 2000;

    private readonly WebApplication _app;

    private TraconTestHost(WebApplication app, HttpClient client, RecordingLoggerProvider logs)
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
    /// <param name="configureTracon">Changes the Tracon chain.</param>
    /// <param name="configureEndpoints">Changes the endpoint settings.</param>
    /// <param name="configureServices">Registers additional services.</param>
    /// <param name="prefix">The path prefix.</param>
    /// <param name="withOpenApi">
    /// Turns on OpenAPI document generation. This is what a consumer does in
    /// their own app; Tracon.AspNetCore does NOT depend on the OpenAPI package.
    /// </param>
    /// <param name="configureApp">
    /// Wires up an additional route after <c>app.Build()</c> and before the
    /// <c>MapTracon</c> call (example: <c>app.MapHealthChecks("/health")</c>).
    /// </param>
    /// <param name="configureAfterMap">
    /// Wires up an additional route AFTER the <c>MapTracon</c> call
    /// (example: <c>app.MapTraconMcpServer()</c> — it must be called AFTER
    /// <c>MapTracon</c> to inherit the shared access settings from it).
    /// </param>
    /// <param name="environment">
    /// The host environment name. <see langword="null"/> keeps whatever the test
    /// process provides; a test that asserts environment-dependent behavior
    /// (the non-persistent storage warning) sets it explicitly.
    /// </param>
    /// <param name="validateServiceProvider">
    /// Turns on scope and build validation of the service provider, as a Development host built with the full
    /// builder has them on.
    /// </param>
    /// <returns>The running host.</returns>
    public static async Task<TraconTestHost> StartAsync(
        Action<ITraconBuilder>? configureTracon = null,
        Action<TraconEndpointOptions>? configureEndpoints = null,
        Action<IServiceCollection>? configureServices = null,
        string prefix = "/tracon",
        bool withOpenApi = false,
        Action<WebApplication>? configureApp = null,
        Action<WebApplication>? configureAfterMap = null,
        string? environment = null,
        bool validateServiceProvider = false)
    {
        var logs = new RecordingLoggerProvider();

        var builder = environment is null
            ? WebApplication.CreateSlimBuilder()
            : WebApplication.CreateSlimBuilder(new WebApplicationOptions { EnvironmentName = environment });
        builder.WebHost.UseTestServer();

        if (validateServiceProvider)
        {
            // What the framework turns on by itself in a Development host built with the full builder; the slim
            // builder used here does not, so a test that wants the consumer's first run asks for it.
            builder.Host.UseDefaultServiceProvider(static options =>
            {
                options.ValidateScopes = true;
                options.ValidateOnBuild = true;
            });
        }

        builder.Logging.ClearProviders();
        builder.Logging.AddProvider(logs);
        builder.Logging.SetMinimumLevel(LogLevel.Trace);

        if (withOpenApi)
        {
            // 🚨 The bare AddOpenApi() call is REQUIRED, and the product metadata is
            // registered separately below. .NET 10 emits an interceptor for the XML
            // documentation transformer, and it only matches this exact invocation:
            // passing the configuration delegate here instead
            // (`AddOpenApi(ProductOpenApiDocument.Configure)`) silently drops the
            // `description` of EVERY schema property. Measured: 166 of 226 schemas
            // lost their documentation, and OpenApiSnapshotTests did not catch it
            // because a refreshed snapshot only proves the file matches the host.
            builder.Services.AddOpenApi();
            builder.Services.Configure<OpenApiOptions>(
                ProductOpenApiDocument.DocumentName,
                ProductOpenApiDocument.Configure);
        }

        configureServices?.Invoke(builder.Services);

        var tracon = builder.Services.AddTracon();
        // The name "echo" matches the fixed ModelBinding.Provider in TestData.Definition().
        tracon.AddModelProvider(new FakeModelProvider("echo").EchoesUserMessage());
        configureTracon?.Invoke(tracon);

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

        var endpointSetups = app.Services.GetServices<TestEndpointOptionsSetup>().ToArray();

        app.MapTracon(prefix, options =>
        {
            configureEndpoints?.Invoke(options);

            foreach (var setup in endpointSetups)
            {
                setup.Apply(options);
            }
        });
        configureAfterMap?.Invoke(app);

        if (withOpenApi)
        {
            app.MapOpenApi();
        }

        await app.StartAsync();

        var client = app.GetTestClient();

        // The host's log lives in this object, which the test disposes before anybody reads the result.
        TestEvidence.Register("host log", failure => FailureNote(logs.Entries, failure.Text));

        return new TraconTestHost(app, client, logs);
    }

    /// <summary>
    /// The part of a host's log a failed test needs: every line at <c>Warning</c> or above, and the lines that carry a
    /// reference id the failure text names.
    /// </summary>
    /// <param name="entries">The log lines the host wrote, each starting with its level.</param>
    /// <param name="failureText">What the test failed with; a <c>(ref: id)</c> in it selects the lines that carry that id.</param>
    /// <returns>
    /// The selected lines, or <see langword="null"/> when there are none. At most <see cref="NoteLineLimit"/> lines and
    /// <see cref="NoteByteLimit"/> characters; the note says how many lines it left out.
    /// </returns>
    /// <remarks>
    /// A job that fails with <c>(ref: 234bf944)</c> points at the line the host logged for it. That line is an
    /// <c>Information</c> or <c>Error</c> line the failure text alone cannot show.
    /// </remarks>
    internal static string? FailureNote(IEnumerable<string> entries, string failureText)
    {
        var references = TestEvidence.References(failureText);
        var selected = entries
            .Where(entry => IsSerious(entry) || references.Any(reference => entry.Contains(reference, StringComparison.Ordinal)))
            .ToArray();

        if (selected.Length == 0)
        {
            return null;
        }

        var builder = new System.Text.StringBuilder();
        var written = 0;

        foreach (var entry in selected)
        {
            if (written >= NoteLineLimit || builder.Length >= NoteByteLimit)
            {
                break;
            }

            builder.AppendLine(entry.Length <= NoteLineWidth ? entry : entry[..NoteLineWidth] + "...");
            written++;
        }

        if (written < selected.Length)
        {
            builder.Append('(').Append(selected.Length - written).Append(" more lines left out)");
        }

        return builder.ToString();
    }

    private static bool IsSerious(string entry)
        => entry.StartsWith("Warning ", StringComparison.Ordinal)
           || entry.StartsWith("Error ", StringComparison.Ordinal)
           || entry.StartsWith("Critical ", StringComparison.Ordinal);

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

    /// <summary>Polls <c>GET /runs/{id}</c> until the run reports <paramref name="expected"/>.</summary>
    /// <param name="runId">The run to watch.</param>
    /// <param name="expected">The status to wait for.</param>
    /// <param name="tenant">The <c>X-Tracon-Tenant</c> header to send, when the run belongs to one.</param>
    /// <returns><paramref name="expected"/>.</returns>
    /// <remarks>
    /// A background run settles on its own schedule. Seven test classes carried a
    /// private copy of this loop. The generous bound is measured:
    /// 5 seconds failed once in a parallel run of 16 test projects, while a healthy
    /// run leaves the wait in milliseconds (<see cref="WaitUntil"/>).
    /// </remarks>
    public async Task<string> WaitForRunStatusAsync(Guid runId, string expected, string? tenant = null)
    {
        var uri = new Uri($"/tracon/api/runs/{runId}", UriKind.Relative);

        await WaitUntil.ValueAsync(
            async () =>
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, uri);

                if (tenant is not null)
                {
                    request.Headers.Add("X-Tracon-Tenant", tenant);
                }

                using var poll = await Client.SendAsync(request);

                return (await ReadJsonAsync(poll)).GetProperty("status").GetString();
            },
            status => string.Equals(status, expected, StringComparison.Ordinal),
            $"run {runId} to reach status '{expected}'");

        return expected;
    }

    /// <summary>Polls <c>GET /runs/{id}</c> until the run reaches a terminal status.</summary>
    /// <param name="runId">The run to watch.</param>
    /// <returns>The run as it was when it settled.</returns>
    public async Task<RunRecord> WaitForTerminalRunAsync(Guid runId)
    {
        var uri = new Uri($"/tracon/api/runs/{runId}", UriKind.Relative);

        var run = await WaitUntil.ValueAsync(
            () => Client.GetFromJsonAsync<RunRecord>(uri),
            static run => run is { Status: RunStatus.Completed or RunStatus.Failed or RunStatus.Canceled },
            $"run {runId} to reach a terminal status");

        return run!;
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await _app.StopAsync();
        await _app.DisposeAsync();
        Logs.Dispose();
    }
}

/// <summary>
/// Changes the endpoint options a test host maps with, from a place that only has the service collection.
/// </summary>
/// <remarks>
/// The endpoint options are built by <c>MapTracon</c> and are not in the container, so a helper that answers a decision
/// through <c>ITraconBuilder</c> (the production profile tests) cannot reach them. It registers one of these and
/// <see cref="TraconTestHost"/> applies every registered one when it maps the endpoints. It lives in this file because
/// another test project links this file alone.
/// </remarks>
/// <param name="apply">What to change.</param>
internal sealed class TestEndpointOptionsSetup(Action<TraconEndpointOptions> apply)
{
    /// <summary>Applies the change.</summary>
    /// <param name="options">The options <c>MapTracon</c> is about to use.</param>
    public void Apply(TraconEndpointOptions options) => apply(options);
}
