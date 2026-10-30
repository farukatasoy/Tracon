using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Tracon.AspNetCore.FunctionalTests.Infrastructure;

/// <summary>
/// Fakes a minimal OpenAI-compatible API over a real local HTTP server
/// (<c>127.0.0.1</c>, random port).
/// </summary>
/// <remarks>
/// Ollama is not installed on this machine; this stand-in server verifies
/// F-05's "local, keyless server" mechanism (connect + unauthenticated request)
/// over a real socket. See Phase 8, section 8.2
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

    /// <summary>The JSON body of every request to <c>/v1/chat/completions</c>, in order.</summary>
    /// <remarks>
    /// F-598: what a definition field puts on the wire is proven by the body the real SDK sends
    /// through the whole host, not by the options object handed to it.
    /// </remarks>
    public List<JsonObject> ChatCompletionBodies { get; } = [];

    /// <summary>The JSON body of every request to <c>/v1/responses</c>, in order.</summary>
    public List<JsonObject> ResponsesBodies { get; } = [];

    /// <summary>
    /// The output items every <c>/v1/responses</c> answer carries BEFORE its message (a web search call, a code
    /// interpreter call, a file search call), or <see langword="null"/> for the message alone.
    /// </summary>
    /// <remarks>
    /// F-603: what a hosted tool call becomes in the run record depends on the content types the real adapter
    /// builds from the provider's items, so the items are answered over the wire and parsed by the real SDK.
    /// </remarks>
    public JsonArray? ResponsesOutputBeforeMessage { get; set; }

    /// <summary>
    /// The annotations (<c>url_citation</c>, <c>file_citation</c>) of the answer's text, or <see langword="null"/>
    /// for none.
    /// </summary>
    /// <remarks>
    /// F-773: what a citation becomes in the run record depends on the annotation types the real adapter builds
    /// from the provider's items, and on how many times a stream shows them.
    /// </remarks>
    public JsonArray? ResponsesAnnotations { get; set; }

    /// <summary>
    /// Whether a <c>/v1/responses</c> request that asks for a stream gets one, as the events the API sends,
    /// instead of the buffered answer. Off by default: the buffered answer is what the earlier tests expect.
    /// </summary>
    public bool StreamResponses { get; set; }

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
    /// Whether an error from <c>/v1/chat/completions</c> carries an OpenAI-shaped
    /// JSON error body. Defaults to <see langword="false"/> (an empty body).
    /// </summary>
    /// <remarks>
    /// The body decides the exception message the OpenAI client writes, measured
    /// (2026-10-12, F-354): with an OpenAI-shaped body it is
    /// <c>HTTP 400 (invalid_request_error: ...)</c>; with an empty body it is
    /// System.ClientModel's default <c>Service request failed. Status: 400 (Bad Request)</c>.
    /// </remarks>
    public bool ChatCompletionErrorHasOpenAiBody { get; set; }

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

        app.MapPost("/v1/chat/completions", async (HttpContext context) =>
        {
            server!.ChatCompletionCallCount++;
            server.ChatCompletionBodies.Add(await ReadBodyAsync(context).ConfigureAwait(false));
            server.ReceivedAuthorizationHeaders.Add(
                context.Request.Headers.Authorization is { Count: > 0 } values ? values.ToString() : null);

            if (server.ChatCompletionStatusCode != StatusCodes.Status200OK)
            {
                return server.ChatCompletionErrorHasOpenAiBody
                    ? Results.Json(
                        new { error = new { message = "The request was rejected.", type = "invalid_request_error", param = (string?)null, code = "fake_error" } },
                        statusCode: server.ChatCompletionStatusCode)
                    : Results.StatusCode(server.ChatCompletionStatusCode);
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

        app.MapPost("/v1/responses", async (HttpContext context) =>
        {
            server!.ResponsesBodies.Add(await ReadBodyAsync(context).ConfigureAwait(false));

            var annotations = server.ResponsesAnnotations ?? [];
            var output = new JsonArray();

            foreach (var item in server.ResponsesOutputBeforeMessage ?? [])
            {
                output.Add(item!.DeepClone());
            }

            output.Add(new JsonObject
            {
                ["type"] = "message",
                ["id"] = "msg_fake",
                ["status"] = "completed",
                ["role"] = "assistant",
                ["content"] = new JsonArray(new JsonObject
                {
                    ["type"] = "output_text",
                    ["text"] = "hello from local",
                    ["annotations"] = annotations.DeepClone(),
                }),
            });

            if (server.StreamResponses && server.ResponsesBodies[^1]["stream"]?.GetValue<bool>() == true)
            {
                return Results.Text(StreamedResponse(output, annotations), "text/event-stream");
            }

            return Results.Json(new
            {
                id = "resp_fake",
                @object = "response",
                created_at = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                status = "completed",
                model = "fake-local-model",
                output,
                usage = new
                {
                    input_tokens = 5,
                    output_tokens = 3,
                    total_tokens = 8,
                    input_tokens_details = new { cached_tokens = 0 },
                    output_tokens_details = new { reasoning_tokens = 0 },
                },
            });
        });

        await app.StartAsync().ConfigureAwait(false);

        var address = app.Urls.First();
        server = new FakeOpenAiCompatibleServer(app, new Uri($"{address}/v1"));

        return server;
    }

    /// <summary>
    /// The events the Responses API sends for an answer with annotations: each annotation as it is added, and
    /// again whole in the events that close the content part and the item.
    /// </summary>
    private static string StreamedResponse(JsonArray output, JsonArray annotations)
    {
        const string Text = "hello from local";
        var sequence = 0;
        var frames = new System.Text.StringBuilder();

        void Send(string type, JsonObject data)
        {
            data["type"] = type;
            data["sequence_number"] = sequence++;
            frames.Append("event: ").Append(type).Append("\ndata: ").Append(data.ToJsonString()).Append("\n\n");
        }

        JsonObject Response(string status, JsonArray items) => new()
        {
            ["id"] = "resp_fake",
            ["object"] = "response",
            ["created_at"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            ["status"] = status,
            ["model"] = "fake-local-model",
            ["output"] = items,
            ["usage"] = string.Equals(status, "completed", StringComparison.Ordinal)
                ? new JsonObject
                {
                    ["input_tokens"] = 5,
                    ["output_tokens"] = 3,
                    ["total_tokens"] = 8,
                    ["input_tokens_details"] = new JsonObject { ["cached_tokens"] = 0 },
                    ["output_tokens_details"] = new JsonObject { ["reasoning_tokens"] = 0 },
                }
                : null,
        };

        var messageIndex = output.Count - 1;
        var part = new JsonObject { ["type"] = "output_text", ["text"] = Text, ["annotations"] = annotations.DeepClone() };

        Send("response.created", new JsonObject { ["response"] = Response("in_progress", []) });

        // Output items before the message (a hosted tool call) are shown whole.
        for (var i = 0; i < messageIndex; i++)
        {
            Send("response.output_item.added", new JsonObject { ["output_index"] = i, ["item"] = output[i]!.DeepClone() });
            Send("response.output_item.done", new JsonObject { ["output_index"] = i, ["item"] = output[i]!.DeepClone() });
        }

        Send("response.output_item.added", new JsonObject
        {
            ["output_index"] = messageIndex,
            ["item"] = new JsonObject
            {
                ["type"] = "message",
                ["id"] = "msg_fake",
                ["status"] = "in_progress",
                ["role"] = "assistant",
                ["content"] = new JsonArray(),
            },
        });
        Send("response.content_part.added", new JsonObject
        {
            ["item_id"] = "msg_fake",
            ["output_index"] = messageIndex,
            ["content_index"] = 0,
            ["part"] = new JsonObject { ["type"] = "output_text", ["text"] = string.Empty, ["annotations"] = new JsonArray() },
        });
        Send("response.output_text.delta", new JsonObject
        {
            ["item_id"] = "msg_fake",
            ["output_index"] = messageIndex,
            ["content_index"] = 0,
            ["delta"] = Text,
        });

        for (var i = 0; i < annotations.Count; i++)
        {
            Send("response.output_text.annotation.added", new JsonObject
            {
                ["item_id"] = "msg_fake",
                ["output_index"] = messageIndex,
                ["content_index"] = 0,
                ["annotation_index"] = i,
                ["annotation"] = annotations[i]!.DeepClone(),
            });
        }

        Send("response.output_text.done", new JsonObject
        {
            ["item_id"] = "msg_fake",
            ["output_index"] = messageIndex,
            ["content_index"] = 0,
            ["text"] = Text,
        });
        Send("response.content_part.done", new JsonObject
        {
            ["item_id"] = "msg_fake",
            ["output_index"] = messageIndex,
            ["content_index"] = 0,
            ["part"] = part.DeepClone(),
        });
        Send("response.output_item.done", new JsonObject
        {
            ["output_index"] = messageIndex,
            ["item"] = output[messageIndex]!.DeepClone(),
        });
        Send("response.completed", new JsonObject { ["response"] = Response("completed", (JsonArray)output.DeepClone()) });

        return frames.ToString();
    }

    private static async Task<JsonObject> ReadBodyAsync(HttpContext context)
    {
        using var reader = new StreamReader(context.Request.Body);

        return JsonNode.Parse(await reader.ReadToEndAsync().ConfigureAwait(false))!.AsObject();
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync().ConfigureAwait(false);
        await _app.DisposeAsync().ConfigureAwait(false);
    }
}
