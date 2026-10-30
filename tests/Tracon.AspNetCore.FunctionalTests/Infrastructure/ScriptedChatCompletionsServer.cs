using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Tracon.AspNetCore.FunctionalTests.Infrastructure;

/// <summary>
/// A local OpenAI Chat Completions server that answers from a script and
/// enforces two request rules the real API enforces.
/// </summary>
/// <remarks>
/// <para>
/// A harness agent on the <c>openai</c> provider failed with two 400s
/// that only the real API produced. <see cref="FakeOpenAiCompatibleServer"/>
/// accepts any body, so it could not show either one. This server rejects the
/// same two bodies, with the error shape OpenAI returns:
/// </para>
/// <list type="bullet">
/// <item><description><c>web_search_options</c> on a model that does not search (<c>unknown_parameter</c>).</description></item>
/// <item><description>A <c>tool</c> message that does not answer a <c>tool_calls</c> entry of an earlier assistant message (<c>messages.[N].role</c>).</description></item>
/// </list>
/// <para>
/// Every request body is kept in <see cref="Bodies"/>, so a test can show what
/// went over the wire, not only that a call failed.
/// </para>
/// </remarks>
internal sealed class ScriptedChatCompletionsServer : IAsyncDisposable
{
    private readonly WebApplication _app;
    private readonly ConcurrentQueue<ScriptedReply> _script = new();

    private ScriptedChatCompletionsServer(WebApplication app, Uri baseAddress)
    {
        _app = app;
        BaseAddress = baseAddress;
    }

    /// <summary>The server's base address (<c>http://127.0.0.1:{port}/v1</c>).</summary>
    public Uri BaseAddress { get; }

    /// <summary>Every request body received on <c>/v1/chat/completions</c>, in order.</summary>
    public List<JsonObject> Bodies { get; } = [];

    /// <summary>The error message of every request the server refused, in order.</summary>
    public List<string> Rejections { get; } = [];

    /// <summary>Queues a reply that calls one function.</summary>
    /// <param name="name">The function name.</param>
    /// <param name="arguments">The arguments, as a JSON object string.</param>
    /// <returns>This server.</returns>
    public ScriptedChatCompletionsServer ThenCalls(string name, string arguments = "{}")
    {
        _script.Enqueue(new ScriptedReply(Text: null, ToolName: name, ToolArguments: arguments));

        return this;
    }

    /// <summary>Queues a plain text reply.</summary>
    /// <param name="text">The assistant text.</param>
    /// <returns>This server.</returns>
    public ScriptedChatCompletionsServer ThenAnswers(string text)
    {
        _script.Enqueue(new ScriptedReply(Text: text, ToolName: null, ToolArguments: null));

        return this;
    }

    /// <summary>Starts the server and binds it to a random loopback port.</summary>
    /// <returns>The running server.</returns>
    public static async Task<ScriptedChatCompletionsServer> StartAsync()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();

        var app = builder.Build();
        ScriptedChatCompletionsServer? server = null;

        app.MapPost("/v1/chat/completions", async (HttpContext context) =>
        {
            var body = (await JsonNode.ParseAsync(context.Request.Body).ConfigureAwait(false))!.AsObject();

            lock (server!.Bodies)
            {
                server.Bodies.Add(body);
            }

            if (Validate(body) is { } rejection)
            {
                lock (server.Rejections)
                {
                    server.Rejections.Add(rejection.Message);
                }

                return Results.Json(
                    new { error = new { message = rejection.Message, type = "invalid_request_error", param = rejection.Param, code = rejection.Code } },
                    statusCode: StatusCodes.Status400BadRequest);
            }

            var reply = server._script.TryDequeue(out var next) ? next : new ScriptedReply("done", null, null);

            return body["stream"]?.GetValue<bool>() == true
                ? Results.Text(StreamOf(reply), "text/event-stream")
                : Results.Json(CompletionOf(reply));
        });

        await app.StartAsync().ConfigureAwait(false);

        server = new ScriptedChatCompletionsServer(app, new Uri($"{app.Urls.First()}/v1"));

        return server;
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync().ConfigureAwait(false);
        await _app.DisposeAsync().ConfigureAwait(false);
    }

    private static Rejection? Validate(JsonObject body)
    {
        if (body.ContainsKey("web_search_options"))
        {
            return new Rejection(
                "Unrecognized request argument supplied: web_search_options",
                "web_search_options",
                "unknown_parameter");
        }

        var open = new HashSet<string>(StringComparer.Ordinal);
        var messages = body["messages"]?.AsArray() ?? [];

        for (var index = 0; index < messages.Count; index++)
        {
            var message = messages[index]!.AsObject();
            var role = message["role"]?.GetValue<string>();

            if (string.Equals(role, "assistant", StringComparison.Ordinal) && message["tool_calls"] is JsonArray calls)
            {
                foreach (var call in calls)
                {
                    open.Add(call!["id"]!.GetValue<string>());
                }

                continue;
            }

            if (string.Equals(role, "tool", StringComparison.Ordinal) && !open.Remove(message["tool_call_id"]?.GetValue<string>() ?? string.Empty))
            {
                return new Rejection(
                    "Invalid parameter: messages with role 'tool' must be a response to a preceeding message with 'tool_calls'.",
                    $"messages.[{index}].role",
                    null);
            }
        }

        return null;
    }

    private static object CompletionOf(ScriptedReply reply) => new
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
                message = reply.ToolName is null
                    ? (object)new { role = "assistant", content = reply.Text }
                    : new
                    {
                        role = "assistant",
                        content = (string?)null,
                        tool_calls = new[]
                        {
                            new { id = CallId(reply), type = "function", function = new { name = reply.ToolName, arguments = reply.ToolArguments } },
                        },
                    },
                finish_reason = reply.ToolName is null ? "stop" : "tool_calls",
            },
        },
        usage = new { prompt_tokens = 5, completion_tokens = 3, total_tokens = 8 },
    };

    private static string StreamOf(ScriptedReply reply)
    {
        var created = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        object delta = reply.ToolName is null
            ? new { role = "assistant", content = reply.Text }
            : new
            {
                role = "assistant",
                tool_calls = new[]
                {
                    new { index = 0, id = CallId(reply), type = "function", function = new { name = reply.ToolName, arguments = reply.ToolArguments } },
                },
            };

        var chunk = JsonSerializer.Serialize(new
        {
            id = "chatcmpl-scripted",
            @object = "chat.completion.chunk",
            created,
            model = "scripted-model",
            choices = new[] { new { index = 0, delta, finish_reason = reply.ToolName is null ? "stop" : "tool_calls" } },
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

    private static string CallId(ScriptedReply reply) => "call_" + reply.ToolName;

    private sealed record ScriptedReply(string? Text, string? ToolName, string? ToolArguments);

    private sealed record Rejection(string Message, string Param, string? Code);
}
