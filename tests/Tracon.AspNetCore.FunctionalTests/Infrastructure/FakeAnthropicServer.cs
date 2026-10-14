using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Tracon.AspNetCore.FunctionalTests.Infrastructure;

/// <summary>
/// A local Anthropic Messages API (<c>POST /v1/messages</c>) that keeps every
/// request body and answers with one canned, non-streaming message, or with the
/// answers a test queued (<see cref="Respond"/>, <see cref="Stream"/>).
/// </summary>
/// <remarks>
/// F-346: what an <c>anthropic.*</c> setting puts on the wire is only proven by
/// the body the real SDK sends through the whole host — compiled agent, MAF,
/// the model pipeline and the Anthropic adapter, which merges fields of its own
/// into ours.
/// </remarks>
internal sealed class FakeAnthropicServer : IAsyncDisposable
{
    private const string Response = """
        {"id":"msg_test","type":"message","role":"assistant","model":"claude-test",
         "content":[{"type":"text","text":"{\"answer\":42}"}],
         "stop_reason":"end_turn","stop_sequence":null,
         "usage":{"input_tokens":12,"output_tokens":6}}
        """;

    private readonly WebApplication _app;
    private readonly Queue<(string Body, bool Streamed)> _answers = new();

    private FakeAnthropicServer(WebApplication app, Uri baseAddress)
    {
        _app = app;
        BaseAddress = baseAddress;
    }

    /// <summary>The server's base address (<c>http://127.0.0.1:{port}/</c>).</summary>
    public Uri BaseAddress { get; }

    /// <summary>Every request body received, in order.</summary>
    public List<JsonObject> Bodies { get; } = [];

    /// <summary>Queues a non-streaming answer: a Messages API response body.</summary>
    /// <param name="message">The response JSON.</param>
    /// <returns>This server, for chaining.</returns>
    public FakeAnthropicServer Respond(string message)
    {
        lock (_answers)
        {
            _answers.Enqueue((message, Streamed: false));
        }

        return this;
    }

    /// <summary>Queues a streaming answer: the server-sent events of one message.</summary>
    /// <param name="events">The <c>event:</c>/<c>data:</c> lines, ending with <c>message_stop</c>.</param>
    /// <returns>This server, for chaining.</returns>
    public FakeAnthropicServer Stream(string events)
    {
        lock (_answers)
        {
            _answers.Enqueue((events, Streamed: true));
        }

        return this;
    }

    /// <summary>Starts the server on a random loopback port.</summary>
    /// <returns>The running server.</returns>
    public static async Task<FakeAnthropicServer> StartAsync()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();

        var app = builder.Build();
        FakeAnthropicServer? server = null;

        app.MapPost("/v1/messages", async (HttpContext context) =>
        {
            var body = (await JsonNode.ParseAsync(context.Request.Body).ConfigureAwait(false))!.AsObject();

            lock (server!.Bodies)
            {
                server.Bodies.Add(body);
            }

            (string Body, bool Streamed) answer;

            lock (server._answers)
            {
                answer = server._answers.Count > 0 ? server._answers.Dequeue() : (Response, Streamed: false);
            }

            return answer.Streamed
                ? Results.Text(answer.Body, "text/event-stream")
                : Results.Text(answer.Body, "application/json");
        });

        await app.StartAsync().ConfigureAwait(false);

        server = new FakeAnthropicServer(app, new Uri($"{app.Urls.First()}/"));

        return server;
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync().ConfigureAwait(false);
        await _app.DisposeAsync().ConfigureAwait(false);
    }
}
