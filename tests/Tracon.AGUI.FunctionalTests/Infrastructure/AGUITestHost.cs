using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using AGUI.Abstractions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Tracon.Testing;

namespace Tracon.AGUI.FunctionalTests.Infrastructure;

/// <summary>
/// Boots the Tracon endpoints and the AG-UI endpoint in an in-process test host.
/// </summary>
/// <remarks>
/// Each test builds its own host; no state is shared across tests and the stores are in memory. A request is a real
/// AG-UI <c>RunAgentInput</c> serialized by the protocol SDK's own serializer, and the answer is parsed back into
/// <see cref="BaseEvent"/>s the same way, so a test reads the stream a front end reads.
/// </remarks>
internal sealed class AGUITestHost : IAsyncDisposable
{
    public const string Pattern = "/tracon/agui";

    private readonly WebApplication _app;

    private AGUITestHost(WebApplication app, HttpClient client, ListLoggerProvider logs)
    {
        _app = app;
        Client = client;
        Logs = logs;
    }

    public HttpClient Client { get; }

    public ListLoggerProvider Logs { get; }

    public IServiceProvider Services => _app.Services;

    public static async Task<AGUITestHost> StartAsync(
        Action<ITraconBuilder>? configureTracon = null,
        Action<IServiceCollection>? configureServices = null,
        Action<TraconEndpointOptions>? configureEndpoints = null,
        Action<WebApplication>? configureApp = null)
    {
        var logs = new ListLoggerProvider();

        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Logging.AddProvider(logs);
        builder.Logging.SetMinimumLevel(LogLevel.Information);

        configureServices?.Invoke(builder.Services);

        var tracon = builder.Services.AddTracon();
        tracon.AddModelProvider(new FakeModelProvider("echo").EchoesUserMessage());
        configureTracon?.Invoke(tracon);

        var app = builder.Build();

        configureApp?.Invoke(app);

        app.MapTracon("/tracon", configureEndpoints);
        app.MapTraconAGUI();

        await app.StartAsync();

        return new AGUITestHost(app, app.GetTestClient(), logs);
    }

    /// <summary>Posts a run request and reads the whole answer.</summary>
    public async Task<AGUIResponse> RunAsync(
        string agentName,
        RunAgentInput input,
        Action<HttpRequestMessage>? configure = null)
        => await PostAsync(agentName, JsonSerializer.Serialize(input, AGUIJsonSerializerContext.Default.RunAgentInput), configure);

    /// <summary>Posts a raw body, for the requests a typed input cannot express.</summary>
    public async Task<AGUIResponse> PostAsync(string agentName, string body, Action<HttpRequestMessage>? configure = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri($"{Pattern}/{agentName}", UriKind.Relative))
        {
            Content = new StringContent(body, Encoding.UTF8, new MediaTypeHeaderValue("application/json")),
        };

        configure?.Invoke(request);

        using var response = await Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, TestContext.Current.CancellationToken);

        return await AGUIResponse.ReadAsync(response);
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await _app.StopAsync();
        await _app.DisposeAsync();
        Logs.Dispose();
    }
}

/// <summary>What an AG-UI request was answered with.</summary>
internal sealed class AGUIResponse
{
    private AGUIResponse(
        System.Net.HttpStatusCode status,
        string contentType,
        string body,
        IReadOnlyList<string> frames,
        IReadOnlyList<BaseEvent> events)
    {
        Status = status;
        ContentType = contentType;
        Body = body;
        Frames = frames;
        Events = events;
    }

    public System.Net.HttpStatusCode Status { get; }

    public string ContentType { get; }

    /// <summary>The body as received.</summary>
    public string Body { get; }

    /// <summary>The raw frames of an event stream, without their blank-line terminator.</summary>
    public IReadOnlyList<string> Frames { get; }

    public IReadOnlyList<BaseEvent> Events { get; }

    public IReadOnlyList<string> Types => [.. Events.Select(static e => e.Type)];

    /// <summary>The text the assistant streamed.</summary>
    public string Text => string.Concat(Events.OfType<TextMessageContentEvent>().Select(static e => e.Delta));

    public RunFinishedEvent Finished => Events.OfType<RunFinishedEvent>().Single();

    public IReadOnlyList<AGUIInterrupt> Interrupts
        => Finished.Outcome is RunFinishedInterruptOutcome { Interrupts: { } interrupts } ? [.. interrupts] : [];

    public Guid TraconRunId
    {
        get
        {
            var custom = Events.OfType<CustomEvent>().Single(static e => string.Equals(e.Name, "tracon.run", StringComparison.Ordinal));

            return custom.Value!.Value.GetProperty("runId").GetGuid();
        }
    }

    public JsonElement Problem => JsonDocument.Parse(Body).RootElement;

    public static async Task<AGUIResponse> ReadAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var contentType = response.Content.Headers.ContentType?.MediaType ?? string.Empty;

        var frames = new List<string>();
        var events = new List<BaseEvent>();

        if (string.Equals(contentType, "text/event-stream", StringComparison.Ordinal))
        {
            foreach (var frame in body.Split("\n\n", StringSplitOptions.RemoveEmptyEntries))
            {
                frames.Add(frame);

                var data = string.Join(
                    '\n',
                    frame.Split('\n').Where(static line => line.StartsWith("data: ", StringComparison.Ordinal)).Select(static line => line[6..]));

                if (data.Length > 0)
                {
                    events.Add(JsonSerializer.Deserialize(data, AGUIJsonSerializerContext.Default.BaseEvent)!);
                }
            }
        }

        return new AGUIResponse(response.StatusCode, contentType, body, frames, events);
    }
}

/// <summary>Keeps every log line the host writes.</summary>
internal sealed class ListLoggerProvider : ILoggerProvider
{
    private readonly List<string> _lines = [];

    public IReadOnlyList<string> Lines
    {
        get
        {
            lock (_lines)
            {
                return [.. _lines];
            }
        }
    }

    public ILogger CreateLogger(string categoryName) => new ListLogger(this, categoryName);

    public void Dispose()
    {
    }

    private sealed class ListLogger(ListLoggerProvider owner, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
            => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            lock (owner._lines)
            {
                owner._lines.Add($"{logLevel} {category}: {formatter(state, exception)}{(exception is null ? string.Empty : " | " + exception.GetType().Name)}");
            }
        }
    }
}
