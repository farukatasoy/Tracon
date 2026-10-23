using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Tracon.Tests.Shared;

/// <summary>
/// A real MCP server on loopback whose tools ask the user for input in the middle of a call
/// (elicitation), the way the 2026-07-28 protocol lets a server do it: the tool returns an
/// <c>input_required</c> result and runs again with the answer.
/// </summary>
/// <remarks>
/// <c>ask_name</c> asks one question ("What is your name?") and answers <c>hello {name}</c>;
/// <c>ask_twice</c> asks a second, different question after the first was answered;
/// <c>ask_direct</c> asks the way the older protocol does, with a request of its own sent while the call
/// runs (it is meant for a server started with <c>olderProtocol</c>); <c>ask_forever</c> asks the same question
/// on every call, whatever was answered; <c>ask_changing</c>
/// asks one question the first time it is called and another every time after. A declined or
/// cancelled request is answered <c>declined</c> or <c>cancelled</c> with no field in it, so a test
/// can tell what the server was told. <see cref="Seen"/> lists what each call received, in order.
/// </remarks>
internal sealed class ElicitingMcpServer : IAsyncDisposable
{
    /// <summary>The question every tool of this server asks first.</summary>
    public const string Question = "What is your name?";

    private readonly WebApplication _app;
    private readonly List<string> _seen;
    private readonly StrongBox<int> _calls;
    private int _disposed;

    private ElicitingMcpServer(WebApplication app, Uri endpoint, List<string> seen, StrongBox<int> calls)
    {
        _app = app;
        Endpoint = endpoint;
        _seen = seen;
        _calls = calls;
    }

    /// <summary>The MCP endpoint address.</summary>
    public Uri Endpoint { get; }

    /// <summary>How many tool calls reached this server, the repeated ones included.</summary>
    public int Calls => Volatile.Read(ref _calls.Value);

    /// <summary>What each call received: <c>asked</c> when it had no answer, else the answer's action and value.</summary>
    public IReadOnlyList<string> Seen
    {
        get
        {
            lock (_seen)
            {
                return [.. _seen];
            }
        }
    }

    /// <summary>Starts a server.</summary>
    /// <param name="olderProtocol">
    /// Whether the server speaks only the <c>initialize</c> protocol (2025-11-25). A client then falls back to
    /// it, and a server's request for input is a direct <c>elicitation/create</c> request instead of a retry.
    /// </param>
    /// <returns>The running server.</returns>
    public static async Task<ElicitingMcpServer> StartAsync(bool olderProtocol = false)
    {
        var seen = new List<string>();
        var calls = new StrongBox<int>();

        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();

        builder.Services.AddMcpServer(options =>
            {
                if (olderProtocol)
                {
                    options.ProtocolVersion = "2025-11-25";
                }
            })
            .WithHttpTransport(options => options.Stateless = !olderProtocol)
            .WithTools(
            [
                McpServerTool.Create(
                    [Description("Greets the user by the name they give when asked.")] (RequestContext<CallToolRequestParams> context) =>
                        Greet(context.Params, calls, seen, firstKey: "name", question: Question, followUp: null),
                    new McpServerToolCreateOptions { Name = "ask_name" }),
                McpServerTool.Create(
                    [Description("Asks for a name, then for a city.")] (RequestContext<CallToolRequestParams> context) =>
                        Greet(context.Params, calls, seen, firstKey: "name", question: Question, followUp: "Which city?"),
                    new McpServerToolCreateOptions { Name = "ask_twice" }),
                McpServerTool.Create(
                    [Description("Asks with a request of its own, sent while the call runs.")] async (McpServer server, CancellationToken cancellationToken) =>
                    {
                        Interlocked.Increment(ref calls.Value);
                        Record(seen, "asked");

                        ElicitResult answer;

                        try
                        {
                            answer = await server.ElicitAsync(
                                new ElicitRequestParams
                                {
                                    Message = Question,
                                    RequestedSchema = new ElicitRequestParams.RequestSchema
                                    {
                                        Properties = { ["name"] = new ElicitRequestParams.StringSchema { Title = "name" } },
                                    },
                                },
                                cancellationToken);
                        }
                        catch (Exception ex)
                        {
                            Record(seen, $"error:{ex.GetType().Name}:{ex.Message}");
                            throw;
                        }

                        Record(seen, answer.IsAccepted ? $"accept:{answer.Content?["name"].GetString()}" : answer.Action);

                        return answer.IsAccepted ? $"hello {answer.Content?["name"].GetString()}" : "declined";
                    },
                    new McpServerToolCreateOptions { Name = "ask_direct" }),
                McpServerTool.Create(
                    [Description("Asks for a name on every call.")] (RequestContext<CallToolRequestParams> context) =>
                    {
                        Interlocked.Increment(ref calls.Value);
                        Record(seen, "asked");

                        throw Ask("name", Question);
                    },
                    new McpServerToolCreateOptions { Name = "ask_forever" }),
                McpServerTool.Create(
                    [Description("Asks a different question every time it is called afresh.")] (RequestContext<CallToolRequestParams> context) =>
                    {
                        var call = Interlocked.Increment(ref calls.Value);

                        if (Answer(context.Params, "name") is not { } answer)
                        {
                            Record(seen, $"asked:{call}");

                            throw Ask("name", $"{Question} (ask {call})");
                        }

                        Record(seen, answer.IsAccepted ? $"accept:{answer.Content?["name"].GetString()}" : answer.Action);

                        return answer.IsAccepted ? "hello" : "declined";
                    },
                    new McpServerToolCreateOptions { Name = "ask_changing" }),
            ]);

        var app = builder.Build();
        app.Urls.Add("http://127.0.0.1:0");
        app.MapMcp("/mcp");
        await app.StartAsync();

        var address = app.Urls.First().TrimEnd('/');

        return new ElicitingMcpServer(app, new Uri($"{address}/mcp"), seen, calls);
    }

    /// <summary>Stops the server. A test may stop it early; the second call does nothing.</summary>
    /// <returns>A task that completes when the server is stopped.</returns>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        await _app.StopAsync();
        await _app.DisposeAsync();
    }

    private static string Greet(
        CallToolRequestParams? request,
        StrongBox<int> calls,
        List<string> seen,
        string firstKey,
        string question,
        string? followUp)
    {
        Interlocked.Increment(ref calls.Value);

        var first = Answer(request, firstKey);

        if (first is null)
        {
            Record(seen, "asked");

            throw Ask(firstKey, question);
        }

        if (!first.IsAccepted)
        {
            Record(seen, first.Action);

            return string.Equals(first.Action, "decline", StringComparison.Ordinal) ? "declined" : "cancelled";
        }

        var name = first.Content?[firstKey].GetString() ?? string.Empty;

        if (followUp is null)
        {
            Record(seen, $"accept:{name}");

            return $"hello {name}";
        }

        var second = Answer(request, "city");

        if (second is null)
        {
            Record(seen, $"accept:{name}+asked");

            throw Ask("city", followUp);
        }

        var city = second.Content?["city"].GetString() ?? string.Empty;
        Record(seen, $"accept:{name}+{city}");

        return $"hello {name} from {city}";
    }

    private static ElicitResult? Answer(CallToolRequestParams? request, string key)
        => request?.InputResponses is { } responses && responses.TryGetValue(key, out var response)
            ? response.Deserialize(InputResponse.ElicitResultJsonTypeInfo)
            : null;

    private static InputRequiredException Ask(string field, string message)
        => new(
            new Dictionary<string, InputRequest>(StringComparer.Ordinal)
            {
                [field] = InputRequest.ForElicitation(new ElicitRequestParams
                {
                    Message = message,
                    RequestedSchema = new ElicitRequestParams.RequestSchema
                    {
                        Properties = { [field] = new ElicitRequestParams.StringSchema { Title = field, MinLength = 1 } },
                        Required = [field],
                    },
                }),
            },
            requestState: "state");

    private static void Record(List<string> seen, string entry)
    {
        lock (seen)
        {
            seen.Add(entry);
        }
    }
}
