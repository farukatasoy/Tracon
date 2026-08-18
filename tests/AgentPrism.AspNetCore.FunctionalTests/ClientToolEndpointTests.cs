using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AgentPrism.AspNetCore.FunctionalTests.Infrastructure;
using FakeModelProvider = AgentPrism.Testing.FakeModelProvider;

namespace AgentPrism.AspNetCore.FunctionalTests;

/// <summary>
/// Tests for client-side tools (<c>AddClientTool</c>) and the
/// <c>toolResults</c> run request field (Phase 61).
/// </summary>
public sealed class ClientToolEndpointTests
{
    private const string AgentName = "client-tool-agent";
    private const string ToolName = "read_page_title";

    private static readonly Uri Run = new($"/agentprism/api/agents/{AgentName}/run", UriKind.Relative);
    private static readonly JsonElement EmptyObjectSchema =
        JsonSerializer.Deserialize<JsonElement>("""{"type":"object","properties":{}}""");

    private static void ConfigureAgent(IAgentPrismBuilder builder)
    {
        builder
            .AddModelProvider(new FakeModelProvider("client-tool-model")
                .CallsTool(ToolName)
                .EchoesLastToolResult("Title: "))
            .AddClientTool(ToolName, "Reads the current browser page title.", EmptyObjectSchema)
            .AddAgent(new AgentDefinition
            {
                Name = AgentName,
                Instructions = "Use the tool when asked about the page title.",
                Model = new ModelBinding { Provider = "client-tool-model", Model = "client-tool-1" },
                ToolNames = [ToolName],
            });
    }

    private static async Task<HttpResponseMessage> PostBufferedAsync(AgentPrismTestHost host, AgentRunRequest body)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, Run) { Content = JsonContent.Create(body) };
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));

        return await host.Client.SendAsync(request, TestContext.Current.CancellationToken).ConfigureAwait(false);
    }

    private static async Task<HttpResponseMessage> PostBufferedAsync(AgentPrismTestHost host, AgentRunRequest body, string tenant)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, Run) { Content = JsonContent.Create(body) };
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));
        request.Headers.Add("X-AgentPrism-Tenant", tenant);

        return await host.Client.SendAsync(request, TestContext.Current.CancellationToken).ConfigureAwait(false);
    }

    private static string ExtractCallId(JsonElement runResult)
    {
        foreach (var message in runResult.GetProperty("response").GetProperty("messages").EnumerateArray())
        {
            foreach (var content in message.GetProperty("contents").EnumerateArray())
            {
                if (string.Equals(content.GetProperty("$type").GetString(), "functionCall", StringComparison.Ordinal))
                {
                    return content.GetProperty("callId").GetString()!;
                }
            }
        }

        throw new InvalidOperationException("The response carried no functionCall content.");
    }

    private static string ExtractFinalText(JsonElement runResult)
    {
        string? text = null;

        foreach (var message in runResult.GetProperty("response").GetProperty("messages").EnumerateArray())
        {
            foreach (var content in message.GetProperty("contents").EnumerateArray())
            {
                if (string.Equals(content.GetProperty("$type").GetString(), "text", StringComparison.Ordinal))
                {
                    text = content.GetProperty("text").GetString();
                }
            }
        }

        return text ?? throw new InvalidOperationException("The response carried no text content.");
    }

    [Fact]
    public async Task Client_tool_call_is_returned_to_the_caller_without_running_on_the_server()
    {
        await using var host = await AgentPrismTestHost.StartAsync(ConfigureAgent);

        using var response = await PostBufferedAsync(
            host,
            new AgentRunRequest { SessionId = "s1", Message = "what is the page title?" });

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var body = await AgentPrismTestHost.ReadJsonAsync(response);
        var messages = body.GetProperty("response").GetProperty("messages").EnumerateArray().ToList();

        var contents = messages.SelectMany(static m => m.GetProperty("contents").EnumerateArray()).ToList();

        contents.ShouldContain(c => string.Equals(c.GetProperty("$type").GetString(), "functionCall", StringComparison.Ordinal));
        contents.ShouldNotContain(c => string.Equals(c.GetProperty("$type").GetString(), "functionResult", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Tool_result_completes_the_run()
    {
        await using var host = await AgentPrismTestHost.StartAsync(ConfigureAgent);

        using var first = await PostBufferedAsync(
            host,
            new AgentRunRequest { SessionId = "s1", Message = "what is the page title?" });

        var callId = ExtractCallId(await AgentPrismTestHost.ReadJsonAsync(first));

        using var second = await PostBufferedAsync(
            host,
            new AgentRunRequest
            {
                SessionId = "s1",
                ToolResults = [new ClientToolResult { CallId = callId, Result = "Shopping cart" }],
            });

        second.StatusCode.ShouldBe(HttpStatusCode.OK);

        var body = await AgentPrismTestHost.ReadJsonAsync(second);
        ExtractFinalText(body).ShouldBe("Title: Shopping cart");
    }

    [Fact]
    public async Task Error_message_reaches_the_model_instead_of_the_result()
    {
        await using var host = await AgentPrismTestHost.StartAsync(ConfigureAgent);

        using var first = await PostBufferedAsync(
            host,
            new AgentRunRequest { SessionId = "s1", Message = "what is the page title?" });

        var callId = ExtractCallId(await AgentPrismTestHost.ReadJsonAsync(first));

        using var second = await PostBufferedAsync(
            host,
            new AgentRunRequest
            {
                SessionId = "s1",
                ToolResults = [new ClientToolResult { CallId = callId, ErrorMessage = "permission denied" }],
            });

        second.StatusCode.ShouldBe(HttpStatusCode.OK);

        var body = await AgentPrismTestHost.ReadJsonAsync(second);
        ExtractFinalText(body).ShouldBe("Title: Error: permission denied");
    }

    [Fact]
    public async Task Unknown_call_id_returns_400()
    {
        await using var host = await AgentPrismTestHost.StartAsync(ConfigureAgent);

        using var first = await PostBufferedAsync(
            host,
            new AgentRunRequest { SessionId = "s1", Message = "what is the page title?" });

        await AgentPrismTestHost.ReadJsonAsync(first);

        using var response = await PostBufferedAsync(
            host,
            new AgentRunRequest
            {
                SessionId = "s1",
                ToolResults = [new ClientToolResult { CallId = "does-not-exist", Result = "x" }],
            });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Answering_the_same_call_twice_returns_409()
    {
        await using var host = await AgentPrismTestHost.StartAsync(ConfigureAgent);

        using var first = await PostBufferedAsync(
            host,
            new AgentRunRequest { SessionId = "s1", Message = "what is the page title?" });

        var callId = ExtractCallId(await AgentPrismTestHost.ReadJsonAsync(first));

        using (var second = await PostBufferedAsync(
            host,
            new AgentRunRequest { SessionId = "s1", ToolResults = [new ClientToolResult { CallId = callId, Result = "Shopping cart" }] }))
        {
            second.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        using var third = await PostBufferedAsync(
            host,
            new AgentRunRequest { SessionId = "s1", ToolResults = [new ClientToolResult { CallId = callId, Result = "Shopping cart" }] });

        third.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Missing_session_id_returns_400()
    {
        await using var host = await AgentPrismTestHost.StartAsync(ConfigureAgent);

        using var response = await PostBufferedAsync(
            host,
            new AgentRunRequest { ToolResults = [new ClientToolResult { CallId = "x", Result = "y" }] });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Queued_run_with_tool_results_returns_400()
    {
        await using var host = await AgentPrismTestHost.StartAsync(
            ConfigureAgent,
            configureServices: static services => services.UseScheduling(o => o.RunWorker = false));

        using var request = new HttpRequestMessage(HttpMethod.Post, Run)
        {
            Content = JsonContent.Create(
                new AgentRunRequest { SessionId = "s1", ToolResults = [new ClientToolResult { CallId = "x", Result = "y" }] }),
        };

        request.Headers.Add("Prefer", "respond-async");

        using var response = await host.Client.SendAsync(request, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Oversized_result_returns_400()
    {
        await using var host = await AgentPrismTestHost.StartAsync(ConfigureAgent);

        using var first = await PostBufferedAsync(
            host,
            new AgentRunRequest { SessionId = "s1", Message = "what is the page title?" });

        var callId = ExtractCallId(await AgentPrismTestHost.ReadJsonAsync(first));

        using var response = await PostBufferedAsync(
            host,
            new AgentRunRequest
            {
                SessionId = "s1",
                ToolResults = [new ClientToolResult { CallId = callId, Result = new string('x', 65_537) }],
            });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Streaming_path_client_tool_call_and_result_round_trip()
    {
        // Every other test in this file forces the buffered branch
        // (Idempotency-Key) for simple JSON assertions. K-439's entire
        // rationale is a constraint SPECIFIC to the default streaming
        // branch (SSE headers sent before BuildMessagesAsync runs) — this
        // test proves that branch actually works, not just the one every
        // other test happens to exercise.
        await using var host = await AgentPrismTestHost.StartAsync(ConfigureAgent);

        using var first = await host.Client.PostAsJsonAsync(Run, new AgentRunRequest { SessionId = "s1", Message = "what is the page title?" });
        first.StatusCode.ShouldBe(HttpStatusCode.OK);
        first.Content.Headers.ContentType?.MediaType.ShouldBe("text/event-stream");

        var firstFrames = await SseReader.ReadAllAsync(await first.Content.ReadAsStreamAsync());
        var callId = ExtractCallIdFromFrames(firstFrames);

        using var second = await host.Client.PostAsJsonAsync(
            Run,
            new AgentRunRequest { SessionId = "s1", ToolResults = [new ClientToolResult { CallId = callId, Result = "Shopping cart" }] });

        second.StatusCode.ShouldBe(HttpStatusCode.OK);

        var secondFrames = await SseReader.ReadAllAsync(await second.Content.ReadAsStreamAsync());
        secondFrames.Select(static frame => frame.Data).ShouldContain(static data => data.Contains("Title: Shopping cart", StringComparison.Ordinal));
    }

    private static string ExtractCallIdFromFrames(IReadOnlyList<SseFrame> frames)
    {
        foreach (var frame in frames)
        {
            if (!string.Equals(frame.Event, "update", StringComparison.Ordinal))
            {
                continue;
            }

            using var doc = JsonDocument.Parse(frame.Data);

            if (!doc.RootElement.TryGetProperty("contents", out var contents))
            {
                continue;
            }

            foreach (var content in contents.EnumerateArray())
            {
                if (string.Equals(content.GetProperty("$type").GetString(), "functionCall", StringComparison.Ordinal))
                {
                    return content.GetProperty("callId").GetString()!;
                }
            }
        }

        throw new InvalidOperationException("No 'update' frame carried a functionCall content.");
    }

    [Fact]
    public async Task Another_tenants_pending_call_cannot_be_answered()
    {
        await using var host = await AgentPrismTestHost.StartAsync(builder =>
        {
            ConfigureAgent(builder);
            builder.UseTenancy(static options =>
            {
                options.Enabled = true;
                options.AllowHeaderResolution = true;
            });
        });

        using var first = await PostBufferedAsync(
            host,
            new AgentRunRequest { SessionId = "s1", Message = "what is the page title?" },
            "tenant-a");

        var callId = ExtractCallId(await AgentPrismTestHost.ReadJsonAsync(first));

        // Same session id, different tenant: the session lookup is
        // tenant-scoped, so tenant-b sees no session at all and the call id
        // is unknown to it — never tenant-a's actual pending call.
        using var response = await PostBufferedAsync(
            host,
            new AgentRunRequest { SessionId = "s1", ToolResults = [new ClientToolResult { CallId = callId, Result = "hijacked" }] },
            "tenant-b");

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }
}
