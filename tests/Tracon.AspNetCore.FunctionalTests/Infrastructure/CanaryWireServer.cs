using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Tracon.AspNetCore.FunctionalTests.Infrastructure;

/// <summary>
/// One local HTTP server that speaks the chat wire of four model providers (Anthropic Messages, Azure and plain OpenAI
/// chat completions and Responses, Google Gemini) and keeps every credential it is sent, wherever the provider's SDK
/// puts it.
/// </summary>
/// <remarks>
/// <para>
/// A secret search that finds nothing proves nothing unless the run reached the provider, so the server records what it
/// received: the credential headers and the query string. The wire a request used is told by its path, which is how
/// the providers' own SDKs differ; one catch-all route answers all of them.
/// </para>
/// <para>
/// The server answers in one of three ways. It accepts and says a sentence, it refuses with <c>401</c> (and, when told
/// to, quotes the credential it was sent in the body and in a header, as a provider that names the key it refuses does),
/// or it holds the request open until the caller gives up.
/// </para>
/// </remarks>
internal sealed class CanaryWireServer : IAsyncDisposable
{
    /// <summary>The sentence an accepting provider says.</summary>
    public const string Sentence = "hello from the provider";

    private readonly WebApplication _app;
    private readonly List<string> _credentials = [];
    private readonly List<string> _bodies = [];
    private int _requests;

    private CanaryWireServer(WebApplication app, Uri baseAddress)
    {
        _app = app;
        BaseAddress = baseAddress;
    }

    /// <summary>How the server answers a model call.</summary>
    public enum Behavior
    {
        /// <summary>Says <see cref="Sentence"/>.</summary>
        Accept,

        /// <summary>Answers <c>401</c> without naming the credential.</summary>
        Refuse,

        /// <summary>Answers <c>401</c> and quotes the credential in the body and in a header.</summary>
        RefuseAndQuote,

        /// <summary>Never answers; the call ends when the caller gives up.</summary>
        Hold,
    }

    /// <summary>Gets the address a provider's endpoint option is set to.</summary>
    public Uri BaseAddress { get; }

    /// <summary>Gets or sets how the server answers.</summary>
    public Behavior Mode { get; set; }

    /// <summary>Gets the number of model calls the server received.</summary>
    public int Requests => Volatile.Read(ref _requests);

    /// <summary>
    /// Gets or sets the name of a tool the server asks for. While it is set, a chat completions call that carries no tool
    /// result is answered with a call of that tool (with no arguments); a call that carries one is answered as
    /// <see cref="Mode"/> says. It lets a test send a run through a tool and then refuse the model's next turn.
    /// </summary>
    public string? ToolCall { get; set; }

    /// <summary>Gets the body of every model call the server received, in order.</summary>
    public IReadOnlyList<string> Bodies
    {
        get
        {
            lock (_bodies)
            {
                return [.. _bodies];
            }
        }
    }

    /// <summary>Gets every credential the server was sent: header values and query values, one per request and place.</summary>
    public IReadOnlyList<string> Credentials
    {
        get
        {
            lock (_credentials)
            {
                return [.. _credentials];
            }
        }
    }

    /// <summary>Starts the server on a random loopback port.</summary>
    /// <returns>The running server.</returns>
    public static async Task<CanaryWireServer> StartAsync()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();

        var app = builder.Build();
        CanaryWireServer? server = null;

        app.Map("/{**path}", async (HttpContext context) => await server!.AnswerAsync(context));

        await app.StartAsync();

        server = new CanaryWireServer(app, new Uri($"{app.Urls.First()}/"));

        return server;
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync();
        await _app.DisposeAsync();
    }

    private static string Chunk(string delta, string? finish)
    {
        var finishJson = finish is null ? "null" : "\"" + finish + "\"";

        return "{\"id\":\"chatcmpl-canary\",\"object\":\"chat.completion.chunk\",\"created\":1,\"model\":\"canary-model\","
            + "\"choices\":[{\"index\":0,\"delta\":" + delta + ",\"finish_reason\":" + finishJson + "}],\"usage\":null}";
    }

    private async Task AnswerAsync(HttpContext context)
    {
        var request = context.Request;
        var credential = string.Join(
            " | ",
            new[] { "Authorization", "x-api-key", "api-key", "x-goog-api-key" }
                .Select(name => request.Headers[name].ToString())
                .Where(static value => value.Length > 0));

        lock (_credentials)
        {
            _credentials.Add(credential);

            if (request.Query["key"].ToString() is { Length: > 0 } inUrl)
            {
                _credentials.Add("query:" + inUrl);
            }
        }

        var path = request.Path.Value ?? string.Empty;
        var isModelCall = path.Contains("/messages", StringComparison.Ordinal)
            || path.Contains("chat/completions", StringComparison.Ordinal)
            || path.EndsWith("/responses", StringComparison.Ordinal)
            || path.Contains("generateContent", StringComparison.OrdinalIgnoreCase);

        if (!isModelCall)
        {
            // A health probe or a catalog read: answered, and its credential kept above.
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync("{}", context.RequestAborted);

            return;
        }

        Interlocked.Increment(ref _requests);

        using var reader = new StreamReader(request.Body);
        var body = await reader.ReadToEndAsync(context.RequestAborted);

        lock (_bodies)
        {
            _bodies.Add(body);
        }

        if (ToolCall is { } tool && path.Contains("chat/completions", StringComparison.Ordinal)
            && !body.Contains("\"role\":\"tool\"", StringComparison.Ordinal))
        {
            await AskForToolAsync(context, tool, body);

            return;
        }

        switch (Mode)
        {
            case Behavior.Hold:
                await Task.Delay(Timeout.Infinite, context.RequestAborted); // delay: simulated

                return;

            case Behavior.Refuse or Behavior.RefuseAndQuote:
                var quote = Mode is Behavior.RefuseAndQuote;
                var echoed = quote ? credential : string.Empty;

                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                context.Response.ContentType = "application/json";

                if (quote)
                {
                    context.Response.Headers["x-echoed-credential"] = Uri.EscapeDataString(credential);
                }

                await context.Response.WriteAsync(RefusalBody(path, quote ? $": {echoed}" : string.Empty), context.RequestAborted);

                return;

            default:
                await AcceptAsync(context, path, body);

                return;
        }
    }

    private static string RefusalBody(string path, string quoted)
    {
        if (path.Contains("/messages", StringComparison.Ordinal))
        {
            return JsonSerializer.Serialize(new { type = "error", error = new { type = "authentication_error", message = "invalid x-api-key" + quoted } });
        }

        if (path.Contains("generateContent", StringComparison.OrdinalIgnoreCase))
        {
            return JsonSerializer.Serialize(new { error = new { code = 401, message = "API key not valid" + quoted + ". Please pass a valid API key.", status = "UNAUTHENTICATED" } });
        }

        return JsonSerializer.Serialize(new
        {
            error = new { message = "Incorrect API key provided" + quoted + ". Find your key at the provider's console.", type = "invalid_request_error", code = "invalid_api_key" },
        });
    }

    private static async Task AskForToolAsync(HttpContext context, string tool, string body)
    {
        const string CallId = "call_canary_1";

        if (body.Contains("\"stream\":true", StringComparison.Ordinal))
        {
            context.Response.ContentType = "text/event-stream";
            await context.Response.WriteAsync(
                $"data: {Chunk($$$"""{"role":"assistant","tool_calls":[{"index":0,"id":"{{{CallId}}}","type":"function","function":{"name":"{{{tool}}}","arguments":"{}"}}]}""", null)}\n\n" +
                $"data: {Chunk("{}", "tool_calls")}\n\n" +
                "data: [DONE]\n\n",
                Encoding.UTF8,
                context.RequestAborted);

            return;
        }

        context.Response.ContentType = "application/json";
        await context.Response.WriteAsync(
            $$$"""
            {"id":"chatcmpl-canary","object":"chat.completion","created":1,"model":"canary-model",
             "choices":[{"index":0,"message":{"role":"assistant","content":null,
               "tool_calls":[{"id":"{{{CallId}}}","type":"function","function":{"name":"{{{tool}}}","arguments":"{}"}}]},
               "finish_reason":"tool_calls"}],
             "usage":{"prompt_tokens":5,"completion_tokens":3,"total_tokens":8} }
            """,
            context.RequestAborted);
    }

    private static async Task AcceptResponsesAsync(HttpContext context, bool streaming)
    {
        JsonObject Message() => JsonNode.Parse(
            $$"""{"type":"message","id":"msg_canary","status":"completed","role":"assistant","content":[{"type":"output_text","text":"{{Sentence}}","annotations":[]}]}""")!.AsObject();

        JsonObject Response(string status, JsonArray output) => new()
        {
            ["id"] = "resp_canary",
            ["object"] = "response",
            ["created_at"] = 1,
            ["status"] = status,
            ["model"] = "canary-model",
            ["output"] = output,
            ["usage"] = JsonNode.Parse(
                """{"input_tokens":5,"output_tokens":3,"total_tokens":8,"input_tokens_details":{"cached_tokens":0},"output_tokens_details":{"reasoning_tokens":0}}"""),
        };

        if (!streaming)
        {
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(Response("completed", [Message()]).ToJsonString(), context.RequestAborted);

            return;
        }

        var sequence = 0;
        var frames = new StringBuilder();

        void Send(string type, JsonObject data)
        {
            data["type"] = type;
            data["sequence_number"] = sequence++;
            frames.Append("event: ").Append(type).Append("\ndata: ").Append(data.ToJsonString()).Append("\n\n");
        }

        JsonObject Part(string text) => new() { ["type"] = "output_text", ["text"] = text, ["annotations"] = new JsonArray() };

        Send("response.created", new JsonObject { ["response"] = Response("in_progress", []) });
        Send("response.output_item.added", new JsonObject
        {
            ["output_index"] = 0,
            ["item"] = new JsonObject { ["type"] = "message", ["id"] = "msg_canary", ["status"] = "in_progress", ["role"] = "assistant", ["content"] = new JsonArray() },
        });
        Send("response.content_part.added", new JsonObject { ["item_id"] = "msg_canary", ["output_index"] = 0, ["content_index"] = 0, ["part"] = Part(string.Empty) });
        Send("response.output_text.delta", new JsonObject { ["item_id"] = "msg_canary", ["output_index"] = 0, ["content_index"] = 0, ["delta"] = Sentence });
        Send("response.output_text.done", new JsonObject { ["item_id"] = "msg_canary", ["output_index"] = 0, ["content_index"] = 0, ["text"] = Sentence });
        Send("response.content_part.done", new JsonObject { ["item_id"] = "msg_canary", ["output_index"] = 0, ["content_index"] = 0, ["part"] = Part(Sentence) });
        Send("response.output_item.done", new JsonObject { ["output_index"] = 0, ["item"] = Message() });
        Send("response.completed", new JsonObject { ["response"] = Response("completed", [Message()]) });

        context.Response.ContentType = "text/event-stream";
        await context.Response.WriteAsync(frames.ToString(), Encoding.UTF8, context.RequestAborted);
    }

    private static async Task AcceptAsync(HttpContext context, string path, string body)
    {
        var streaming = path.Contains("streamGenerateContent", StringComparison.OrdinalIgnoreCase)
            || body.Contains("\"stream\":true", StringComparison.Ordinal);

        if (path.EndsWith("/responses", StringComparison.Ordinal))
        {
            await AcceptResponsesAsync(context, streaming);

            return;
        }

        if (path.Contains("/messages", StringComparison.Ordinal))
        {
            if (!streaming)
            {
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsync(
                    $$"""
                    {"id":"msg_canary","type":"message","role":"assistant","model":"canary-model",
                     "content":[{"type":"text","text":"{{Sentence}}"}],"stop_reason":"end_turn","stop_sequence":null,
                     "usage":{"input_tokens":5,"output_tokens":3} }
                    """,
                    context.RequestAborted);

                return;
            }

            context.Response.ContentType = "text/event-stream";
            await context.Response.WriteAsync(
                "event: message_start\ndata: {\"type\":\"message_start\",\"message\":{\"id\":\"msg_canary\",\"type\":\"message\",\"role\":\"assistant\",\"model\":\"canary-model\",\"content\":[],\"stop_reason\":null,\"stop_sequence\":null,\"usage\":{\"input_tokens\":5,\"output_tokens\":1}}}\n\n"
                + "event: content_block_start\ndata: {\"type\":\"content_block_start\",\"index\":0,\"content_block\":{\"type\":\"text\",\"text\":\"\"}}\n\n"
                + $"event: content_block_delta\ndata: {{\"type\":\"content_block_delta\",\"index\":0,\"delta\":{{\"type\":\"text_delta\",\"text\":\"{Sentence}\"}}}}\n\n"
                + "event: content_block_stop\ndata: {\"type\":\"content_block_stop\",\"index\":0}\n\n"
                + "event: message_delta\ndata: {\"type\":\"message_delta\",\"delta\":{\"stop_reason\":\"end_turn\",\"stop_sequence\":null},\"usage\":{\"output_tokens\":3}}\n\n"
                + "event: message_stop\ndata: {\"type\":\"message_stop\"}\n\n",
                context.RequestAborted);

            return;
        }

        if (path.Contains("generateContent", StringComparison.OrdinalIgnoreCase))
        {
            var candidate = $$"""
                {"candidates":[{"content":{"role":"model","parts":[{"text":"{{Sentence}}"}]},"finishReason":"STOP","index":0}],
                 "usageMetadata":{"promptTokenCount":5,"candidatesTokenCount":3,"totalTokenCount":8},"modelVersion":"canary-model"}
                """;

            if (!streaming)
            {
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsync(candidate, context.RequestAborted);

                return;
            }

            context.Response.ContentType = "text/event-stream";
            await context.Response.WriteAsync("data: " + candidate.ReplaceLineEndings(" ") + "\r\n\r\n", context.RequestAborted);

            return;
        }

        if (streaming)
        {
            context.Response.ContentType = "text/event-stream";
            await context.Response.WriteAsync(
                $"data: {Chunk($$"""{"role":"assistant","content":"{{Sentence}}"}""", null)}\n\n" +
                $"data: {Chunk("{}", "stop")}\n\n" +
                "data: [DONE]\n\n",
                Encoding.UTF8,
                context.RequestAborted);

            return;
        }

        context.Response.ContentType = "application/json";
        await context.Response.WriteAsync(
            $$"""
            {"id":"chatcmpl-canary","object":"chat.completion","created":1,"model":"canary-model",
             "choices":[{"index":0,"message":{"role":"assistant","content":"{{Sentence}}"},"finish_reason":"stop"}],
             "usage":{"prompt_tokens":5,"completion_tokens":3,"total_tokens":8} }
            """,
            context.RequestAborted);
    }
}
