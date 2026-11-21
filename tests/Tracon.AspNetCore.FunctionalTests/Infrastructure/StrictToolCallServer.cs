using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Tracon.AspNetCore.FunctionalTests.Infrastructure;

/// <summary>
/// A local OpenAI Chat Completions server that answers from a script (several tool calls in one reply are possible) and
/// refuses the two request shapes the real API refuses: a <c>tool</c> message that answers no earlier call, and an
/// assistant tool call that no <c>tool</c> message answers.
/// </summary>
internal sealed class StrictToolCallServer : IAsyncDisposable
{
    private readonly WebApplication _app;
    private readonly Queue<Reply> _script = new();

    private StrictToolCallServer(WebApplication app, Uri baseAddress)
    {
        _app = app;
        BaseAddress = baseAddress;
    }

    public Uri BaseAddress { get; }

    public List<JsonObject> Bodies { get; } = [];

    public List<string> Transcripts { get; } = [];

    public List<string> Rejections { get; } = [];

    public StrictToolCallServer ThenCalls(params (string Id, string Name, string Args)[] calls)
    {
        _script.Enqueue(new Reply(null, calls));

        return this;
    }

    public StrictToolCallServer ThenAnswers(string text)
    {
        _script.Enqueue(new Reply(text, []));

        return this;
    }

    public static async Task<StrictToolCallServer> StartAsync()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();

        var app = builder.Build();
        StrictToolCallServer? server = null;

        app.MapPost("/v1/chat/completions", async (HttpContext context) =>
        {
            var body = (await JsonNode.ParseAsync(context.Request.Body).ConfigureAwait(false))!.AsObject();

            lock (server!.Bodies)
            {
                server.Bodies.Add(body);
                server.Transcripts.Add(Transcribe(body));
            }

            if (Validate(body) is { } rejection)
            {
                lock (server.Rejections)
                {
                    server.Rejections.Add(rejection);
                }

                return Results.Json(
                    new { error = new { message = rejection, type = "invalid_request_error", param = "messages", code = (string?)null } },
                    statusCode: StatusCodes.Status400BadRequest);
            }

            Reply reply;

            lock (server._script)
            {
                reply = server._script.TryDequeue(out var next) ? next : new Reply("done", []);
            }

            return body["stream"]?.GetValue<bool>() == true
                ? Results.Text(StreamOf(reply), "text/event-stream")
                : Results.Json(CompletionOf(reply));
        });

        await app.StartAsync().ConfigureAwait(false);

        server = new StrictToolCallServer(app, new Uri($"{app.Urls.First()}/v1"));

        return server;
    }

    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync().ConfigureAwait(false);
        await _app.DisposeAsync().ConfigureAwait(false);
    }

    /// <summary>The request as one line: role, then ids and names of calls and results.</summary>
    private static string Transcribe(JsonObject body)
    {
        var parts = new List<string>();

        foreach (var node in body["messages"]?.AsArray() ?? [])
        {
            var message = node!.AsObject();
            var role = message["role"]?.GetValue<string>() ?? "?";

            switch (role)
            {
                case "assistant" when message["tool_calls"] is JsonArray calls && calls.Count > 0:
                    parts.Add($"assistant[{string.Join(",", calls.Select(static call => $"{call!["id"]}:{call["function"]!["name"]}"))}]");

                    break;

                case "tool":
                    parts.Add($"tool({message["tool_call_id"]})=\"{Clip(ContentOf(message))}\"");

                    break;

                case "system":
                    parts.Add("system");

                    break;

                default:
                    parts.Add($"{role}:\"{Clip(ContentOf(message))}\"");

                    break;
            }
        }

        return string.Join(" | ", parts);
    }

    private static string ContentOf(JsonObject message)
        => message["content"] switch
        {
            JsonValue value => value.ToString(),
            JsonArray array => string.Concat(array.Select(static part => part?["text"]?.ToString())),
            _ => string.Empty,
        };

    private static string Clip(string text) => text.Length <= 48 ? text : text[..48] + "...";

    private static string? Validate(JsonObject body)
    {
        var open = new List<string>();
        var messages = body["messages"]?.AsArray() ?? [];

        for (var index = 0; index < messages.Count; index++)
        {
            var message = messages[index]!.AsObject();
            var role = message["role"]?.GetValue<string>();

            if (string.Equals(role, "tool", StringComparison.Ordinal))
            {
                if (!open.Remove(message["tool_call_id"]?.GetValue<string>() ?? string.Empty))
                {
                    return $"Invalid parameter: messages with role 'tool' must be a response to a preceeding message with 'tool_calls'. (messages.[{index}].role)";
                }

                continue;
            }

            if (open.Count > 0)
            {
                return "An assistant message with 'tool_calls' must be followed by tool messages responding to each 'tool_call_id'. " +
                       $"The following tool_call_ids did not have response messages: {string.Join(", ", open)} (messages.[{index}].role)";
            }

            if (string.Equals(role, "assistant", StringComparison.Ordinal) && message["tool_calls"] is JsonArray calls)
            {
                open.AddRange(calls.Select(static call => call!["id"]!.GetValue<string>()));
            }
        }

        return open.Count > 0
            ? "An assistant message with 'tool_calls' must be followed by tool messages responding to each 'tool_call_id'. " +
              $"The following tool_call_ids did not have response messages: {string.Join(", ", open)} (end of messages)"
            : null;
    }

    private static object CompletionOf(Reply reply) => new
    {
        id = "chatcmpl-scripted",
        @object = "chat.completion",
        created = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
        model = "scripted-model",
        choices = new[]
        {
            new
            {
                index = 0,
                message = reply.Calls.Length == 0
                    ? (object)new { role = "assistant", content = reply.Text }
                    : new
                    {
                        role = "assistant",
                        content = (string?)null,
                        tool_calls = reply.Calls.Select(static call => new { id = call.Id, type = "function", function = new { name = call.Name, arguments = call.Args } }).ToArray(),
                    },
                finish_reason = reply.Calls.Length == 0 ? "stop" : "tool_calls",
            },
        },
        usage = new { prompt_tokens = 5, completion_tokens = 3, total_tokens = 8 },
    };

    private static string StreamOf(Reply reply)
    {
        var created = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        object delta = reply.Calls.Length == 0
            ? new { role = "assistant", content = reply.Text }
            : new
            {
                role = "assistant",
                tool_calls = reply.Calls
                    .Select(static (call, index) => new { index, id = call.Id, type = "function", function = new { name = call.Name, arguments = call.Args } })
                    .ToArray(),
            };

        var chunk = JsonSerializer.Serialize(new
        {
            id = "chatcmpl-scripted",
            @object = "chat.completion.chunk",
            created,
            model = "scripted-model",
            choices = new[] { new { index = 0, delta, finish_reason = reply.Calls.Length == 0 ? "stop" : "tool_calls" } },
        });

        var usage = JsonSerializer.Serialize(new
        {
            id = "chatcmpl-scripted",
            @object = "chat.completion.chunk",
            created,
            model = "scripted-model",
            choices = Array.Empty<object>(),
            usage = new { prompt_tokens = 5, completion_tokens = 3, total_tokens = 8 },
        });

        return $"data: {chunk}\n\ndata: {usage}\n\ndata: [DONE]\n\n";
    }

    private sealed record Reply(string? Text, (string Id, string Name, string Args)[] Calls);
}
