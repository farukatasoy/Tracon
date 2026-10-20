using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;
using TraconTestHost = Tracon.AspNetCore.FunctionalTests.Infrastructure.TraconTestHost;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// F-603 — the support table (<c>HostedToolSupportTests</c>) and the content shapes the recorder reads, against
/// the wire: the request body the real SDK adapter of each API surface sends for a hosted tool, and what the same
/// adapter builds from the provider's answer.
/// </summary>
/// <remarks>
/// <para>
/// 🚨 A table is a copy of the provider constants until the wire says otherwise (F-598 found a cell that was
/// wrong, and the tests mirrored the constants). Each row below is read from the request the adapter wrote, so a
/// kind that the table says a surface runs but is not on the wire fails here, and a kind the table refuses but the
/// adapter sends fails too.
/// </para>
/// <para>
/// The answers are the providers' own item shapes, parsed by the real SDK. The file search call has no content
/// type of its own: the OpenAI adapter keeps the provider's item as the raw representation of a base
/// <see cref="ToolCallContent"/>, and the run record finds it by that raw item's type name.
/// </para>
/// <para>
/// Google has no local fake server here. Its rows rest on the real calls of 2026-10-20 (<c>gemini-3.6-flash</c>),
/// repeated by the manual acceptance cases.
/// </para>
/// </remarks>
public sealed class HostedToolWireTests
{
    private const string AgentName = "wire-agent";
    private const string TestKey = "functional-test-key";

    // --- OpenAI ---

    [Fact]
    public async Task OpenAI_responses_sends_each_kind_as_the_tool_of_its_name()
    {
        await using var server = await FakeOpenAiCompatibleServer.StartAsync();
        await using var host = await StartOpenAIAsync(server, ["search", "python", "docs"], "wire-openai-responses");

        await RunAsync(host);

        var tools = server.ResponsesBodies.ShouldHaveSingleItem()["tools"]!.AsArray();

        tools.Select(static tool => tool!["type"]!.GetValue<string>()).ShouldBe(["web_search", "code_interpreter", "file_search"]);
        tools[2]!["vector_store_ids"]!.ToJsonString().ShouldBe("""["vs_wire"]""");
    }

    [Fact]
    public async Task OpenAI_chat_completions_maps_only_the_web_search_and_drops_the_others_without_an_error()
    {
        // The table refuses all three for this surface (the validation report says so). The adapter is called
        // directly, past that refusal, to see WHY: it drops the code interpreter and the file search in silence,
        // and it maps the web search to a field only a search model accepts (K-892).
        await using var server = await FakeOpenAiCompatibleServer.StartAsync();
        await using var host = await StartOpenAIAsync(server, [], "wire-openai");

        var client = host.Services.GetRequiredService<IModelProviderRegistry>()
            .CreateChatClient(new ModelBinding { Provider = "wire-openai", Model = "fake-local-model" });

        await client.GetResponseAsync(
            [new ChatMessage(ChatRole.User, "hi")],
            new ChatOptions
            {
                Tools = [new HostedCodeInterpreterTool(), new HostedFileSearchTool { Inputs = [new HostedVectorStoreContent("vs_wire")] }],
            },
            TestContext.Current.CancellationToken);

        var code = server.ChatCompletionBodies.ShouldHaveSingleItem();

        code.ContainsKey("tools").ShouldBeFalse("the code interpreter and the file search have no Chat Completions field");
        code.ContainsKey("web_search_options").ShouldBeFalse();

        await client.GetResponseAsync(
            [new ChatMessage(ChatRole.User, "hi")],
            new ChatOptions { Tools = [new HostedWebSearchTool()] },
            TestContext.Current.CancellationToken);

        server.ChatCompletionBodies[1].ContainsKey("web_search_options").ShouldBeTrue("the web search is the one hosted tool this surface maps");
    }

    [Fact]
    public async Task OpenAI_responses_items_become_the_registered_tools_calls_and_are_billed()
    {
        await using var server = await FakeOpenAiCompatibleServer.StartAsync();
        server.ResponsesOutputBeforeMessage = JsonNode.Parse(
            """
            [
              {"type":"web_search_call","id":"ws_1","status":"completed","action":{"type":"search","query":"private query text"}},
              {"type":"code_interpreter_call","id":"ci_1","status":"completed","code":"print(1)","container_id":"cntr_1","outputs":null},
              {"type":"file_search_call","id":"fs_1","status":"completed","queries":["private query text"],"results":null}
            ]
            """)!.AsArray();

        await using var host = await StartOpenAIAsync(server, ["search", "python", "docs"], "wire-openai-responses", priced: true);

        await RunAsync(host);

        var run = await OutputTruncationTests.SingleRunAsync(host);
        var events = await OutputTruncationTests.EventsAsync(host, run.Id);

        events.Where(static e => e.Type is RunEventType.ToolInvoking or RunEventType.ToolInvoked)
            .Select(static e => (e.Type, e.ToolName, e.ToolCallId))
            .ShouldBe(
            [
                (RunEventType.ToolInvoking, "search", "ws_1"),
                (RunEventType.ToolInvoked, "search", "ws_1"),
                (RunEventType.ToolInvoking, "python", "ci_1"),
                (RunEventType.ToolInvoked, "python", "ci_1"),
                (RunEventType.ToolInvoking, "docs", "fs_1"),
                (RunEventType.ToolInvoked, "docs", "fs_1"),
            ]);
        events.ShouldNotContain(static e => string.Equals(e.CustomType, RunEventCustomTypes.Content, StringComparison.Ordinal));

        run.NonTokenCost.ShouldNotBeNull().Amount.ShouldBe(0.01m + 0.03m + 0.0025m);
        (await host.Services.GetRequiredService<IRunStore>().ListToolInvocationsAsync(run.Id)).Count.ShouldBe(3);
    }

    // --- Anthropic ---

    [Fact]
    public async Task Anthropic_sends_the_web_search_and_the_code_execution_tools()
    {
        await using var anthropic = await FakeAnthropicServer.StartAsync();
        await using var host = await StartAnthropicAsync(anthropic, ["search", "python"]);

        await RunAsync(host);

        var tools = anthropic.Bodies.ShouldHaveSingleItem()["tools"]!.AsArray();

        tools.Select(static tool => tool!["type"]!.GetValue<string>()).ShouldBe(["web_search_20250305", "code_execution_20250825"]);
    }

    [Fact]
    public async Task Anthropic_web_search_blocks_become_one_tool_call_and_the_providers_own_count_is_read()
    {
        await using var anthropic = await FakeAnthropicServer.StartAsync();
        anthropic.Respond(
            """
            {"id":"msg_1","type":"message","role":"assistant","model":"claude-test",
             "content":[
               {"type":"server_tool_use","id":"srvtoolu_1","name":"web_search","input":{"query":"private query text"}},
               {"type":"web_search_tool_result","tool_use_id":"srvtoolu_1","content":[]},
               {"type":"text","text":"the answer"}],
             "stop_reason":"end_turn","stop_sequence":null,
             "usage":{"input_tokens":12,"output_tokens":6,"server_tool_use":{"web_search_requests":3,"web_fetch_requests":0}}}
            """);

        await using var host = await StartAnthropicAsync(anthropic, ["search"], priced: true);

        await RunAsync(host);

        var run = await OutputTruncationTests.SingleRunAsync(host);
        var events = await OutputTruncationTests.EventsAsync(host, run.Id);

        events.Where(static e => e.Type is RunEventType.ToolInvoking or RunEventType.ToolInvoked)
            .Select(static e => (e.Type, e.ToolName, e.ToolCallId))
            .ShouldBe([(RunEventType.ToolInvoking, "search", "srvtoolu_1"), (RunEventType.ToolInvoked, "search", "srvtoolu_1")]);
        events.ShouldNotContain(static e => string.Equals(e.CustomType, RunEventCustomTypes.Content, StringComparison.Ordinal));

        // Anthropic billed three searches and the response shows one: the provider's count is the bill.
        run.NonTokenCost.ShouldNotBeNull().Amount.ShouldBe(0.03m);
    }

    // --- Hosts ---

    private static Task<TraconTestHost> StartOpenAIAsync(
        FakeOpenAiCompatibleServer server,
        string[] toolNames,
        string provider,
        bool priced = false)
        => TraconTestHost.StartAsync(
            builder => builder
                .UseOpenAICompatible("wire-openai", options =>
                {
                    options.Endpoint = server.BaseAddress;
                    options.EnableResponsesSurface = true;
                })
                .AddHostedTool("search", HostedToolKind.WebSearch, static _ => new HostedWebSearchTool())
                .AddHostedTool("python", HostedToolKind.CodeInterpreter, static _ => new HostedCodeInterpreterTool())
                .AddHostedTool(
                    "docs",
                    HostedToolKind.FileSearch,
                    static _ => new HostedFileSearchTool { Inputs = [new HostedVectorStoreContent("vs_wire")] })
                .AddAgent(Agent(provider, "fake-local-model", toolNames)),
            configureServices: services => services.Configure<TraconOptions>(options =>
            {
                options.Pricing.Currency = "USD";

                if (priced)
                {
                    options.Pricing.HostedTools[provider] = Prices();
                }
            }));

    private static Task<TraconTestHost> StartAnthropicAsync(FakeAnthropicServer server, string[] toolNames, bool priced = false)
        => TraconTestHost.StartAsync(
            builder => builder
                .UseAnthropic(TestKey, options =>
                {
                    options.Endpoint = server.BaseAddress;
                    options.MaxRetries = 0;
                    options.Models.Add(new ModelDescriptor { Name = "claude-test" });
                })
                .AddHostedTool("search", HostedToolKind.WebSearch, static _ => new HostedWebSearchTool())
                .AddHostedTool("python", HostedToolKind.CodeInterpreter, static _ => new HostedCodeInterpreterTool())
                .AddAgent(Agent(AnthropicProviderNames.Anthropic, "claude-test", toolNames)),
            configureServices: services => services.Configure<TraconOptions>(options =>
            {
                options.Pricing.Currency = "USD";

                if (priced)
                {
                    options.Pricing.HostedTools[AnthropicProviderNames.Anthropic] = Prices();
                }
            }));

    private static Dictionary<string, HostedToolPriceOverride> Prices() => new(StringComparer.OrdinalIgnoreCase)
    {
        ["WebSearch"] = new HostedToolPriceOverride { PerCall = 0.01m },
        ["CodeInterpreter"] = new HostedToolPriceOverride { PerCall = 0.03m },
        ["FileSearch"] = new HostedToolPriceOverride { PerCall = 0.0025m },
    };

    private static AgentDefinition Agent(string provider, string model, string[] toolNames) => new()
    {
        Name = AgentName,
        Instructions = "Answer briefly.",
        Model = new ModelBinding { Provider = provider, Model = model },
        ToolNames = toolNames,
        Origin = AgentDefinitionOrigin.Code,
    };

    private static async Task RunAsync(TraconTestHost host)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri($"/tracon/api/agents/{AgentName}/run", UriKind.Relative))
        {
            Content = JsonContent.Create(new AgentRunRequest { Message = "hi" }),
        };
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));

        using var response = await host.Client.SendAsync(request, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }
}
