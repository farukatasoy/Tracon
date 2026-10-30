using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// The support table (<c>ModelSamplingSupportTests</c>) against the wire: the request body the real
/// SDK adapter of each API surface sends for an agent that sets the request fields.
/// </summary>
/// <remarks>
/// <para>
/// The table says which fields a surface sends. Without this test the table is a copy of the provider constants:
/// it passes when the constants are wrong. Each row below is a body read from the request the adapter wrote, so
/// a cell that says "sends" but is not on the wire fails here, and a cell that says "drops" but is on the wire
/// fails too.
/// </para>
/// <para>
/// 🚨 Measured while writing this (Anthropic 12.58.0): the adapter reads <c>AllowMultipleToolCalls</c> only
/// while it builds <c>tool_choice</c> from the tool mode, so a definition that set the first and not the second
/// ran without it. <c>Tracon.Anthropic</c> now gives the flag a <c>tool_choice</c> to travel in; the second
/// test of the Anthropic group is the proof.
/// </para>
/// <para>
/// Google has no local fake server here; its row of the table rests on the decompiled adapter. The Anthropic
/// cells for temperature, top-p and top-k ("drops") cannot be proved through a definition, which refuses them;
/// <c>AnthropicRequestSettingsTests.Temperature_top_p_and_top_k_never_reach_the_request_body</c> hands the
/// adapter those options directly.
/// </para>
/// </remarks>
public sealed class ModelSamplingWireTests
{
    private const string AgentName = "wire-agent";
    private const string TestKey = "functional-test-key";

    [Fact]
    public async Task OpenAI_chat_completions_sends_the_fields_its_surface_declares_and_no_others()
    {
        await using var server = await FakeOpenAiCompatibleServer.StartAsync();
        await using var host = await StartOpenAIAsync(server);

        await SaveAndRunAsync(host, new
        {
            provider = "wire-openai",
            model = "fake-local-model",
            temperature = 0.25,
            topP = 0.75,
            seed = 7,
            stopSequences = new[] { "END" },
            frequencyPenalty = 0.25,
            presencePenalty = 0.5,
            toolMode = "required",
            allowMultipleToolCalls = false,
        });

        var body = server.ChatCompletionBodies.ShouldHaveSingleItem();

        body["temperature"]!.GetValue<double>().ShouldBe(0.25);
        body["top_p"]!.GetValue<double>().ShouldBe(0.75);
        body["seed"]!.GetValue<long>().ShouldBe(7);
        body["stop"]!.ToJsonString().ShouldBe("""["END"]""");
        body["frequency_penalty"]!.GetValue<double>().ShouldBe(0.25);
        body["presence_penalty"]!.GetValue<double>().ShouldBe(0.5);
        body["tool_choice"]!.GetValue<string>().ShouldBe("required");
        body["parallel_tool_calls"]!.GetValue<bool>().ShouldBeFalse();
    }

    [Fact]
    public async Task OpenAI_responses_sends_temperature_top_p_the_tool_fields_and_the_reasoning_summary_and_drops_the_other_sampling_fields()
    {
        await using var server = await FakeOpenAiCompatibleServer.StartAsync();
        await using var host = await StartOpenAIAsync(server);

        // Seed, stop sequences and the penalties are refused for this surface (ModelSamplingSupportTests), so the
        // body is read for the fields it does send, temperature and top-p included.
        await SaveAndRunAsync(host, new
        {
            provider = "wire-openai-responses",
            model = "fake-local-model",
            temperature = 0.25,
            topP = 0.75,
            toolMode = "required",
            allowMultipleToolCalls = false,
            reasoningOutput = "Summary",
        });

        var body = server.ResponsesBodies.ShouldHaveSingleItem();

        body["temperature"]!.GetValue<double>().ShouldBe(0.25);
        body["top_p"]!.GetValue<double>().ShouldBe(0.75);
        body["tool_choice"]!.GetValue<string>().ShouldBe("required");
        body["parallel_tool_calls"]!.GetValue<bool>().ShouldBeFalse();
        body["reasoning"]!["summary"]!.GetValue<string>().ShouldBe("concise");

        foreach (var dropped in new[] { "seed", "stop", "frequency_penalty", "presence_penalty", "top_k" })
        {
            body.ContainsKey(dropped).ShouldBeFalse($"'{dropped}' is not a Responses API field this adapter writes");
        }
    }

    [Fact]
    public async Task OpenAI_chat_completions_ignores_top_k_and_reasoning_output_which_the_table_refuses()
    {
        // The definition cannot set them (validation refuses), so the wire proof is by the adapter: it has no
        // member for either. Settings the table refuses never reach a body.
        await using var server = await FakeOpenAiCompatibleServer.StartAsync();
        await using var host = await StartOpenAIAsync(server);

        using var response = await host.Client.PostAsJsonAsync(
            new Uri("/tracon/api/agents/validate", UriKind.Relative),
            Definition(new { provider = "wire-openai", model = "fake-local-model", topK = 40, reasoningOutput = "Summary" }));

        var report = await TraconTestHost.ReadJsonAsync(response);

        report.GetProperty("valid").GetBoolean().ShouldBeFalse();
        report.GetProperty("messages").EnumerateArray().Select(static message => message.GetProperty("path").GetString())
            .ShouldBe(["model.topK", "model.reasoningOutput"]);
        server.ChatCompletionBodies.ShouldBeEmpty();
    }

    [Fact]
    public async Task Anthropic_sends_stop_sequences_and_the_tool_choice_and_no_sampling_field_SDK_13_removed()
    {
        // SDK 12.58 wrote top_k; SDK 13.3.0's MessageCreateParams has no member for temperature, top_p
        // or top_k. The definition cannot set them (the next test), so the proof here is that a request which sets
        // everything the surface does send carries none of the three.
        await using var anthropic = await FakeAnthropicServer.StartAsync();
        await using var host = await StartAnthropicAsync(anthropic);

        await SaveAndRunAsync(host, new
        {
            provider = AnthropicProviderNames.Anthropic,
            model = "claude-test",
            stopSequences = new[] { "END" },
            toolMode = "required",
            allowMultipleToolCalls = false,
        });

        var body = anthropic.Bodies.ShouldHaveSingleItem();

        foreach (var removed in new[] { "temperature", "top_p", "top_k" })
        {
            body.ContainsKey(removed).ShouldBeFalse($"'{removed}' is not a field the SDK 13 request has");
        }

        body["stop_sequences"]!.ToJsonString().ShouldBe("""["END"]""");
        body["tool_choice"]!["type"]!.GetValue<string>().ShouldBe("any");
        body["tool_choice"]!["disable_parallel_tool_use"]!.GetValue<bool>().ShouldBeTrue();
    }

    [Fact]
    public async Task Anthropic_sends_the_parallel_tool_flag_when_the_definition_names_no_tool_mode()
    {
        // The adapter writes disable_parallel_tool_use only inside the tool_choice it builds from the tool mode.
        // A definition that sets the flag and no mode must still get it on the wire.
        await using var anthropic = await FakeAnthropicServer.StartAsync();
        await using var host = await StartAnthropicAsync(anthropic);

        await SaveAndRunAsync(host, new
        {
            provider = AnthropicProviderNames.Anthropic,
            model = "claude-test",
            allowMultipleToolCalls = false,
        });

        var body = anthropic.Bodies.ShouldHaveSingleItem();

        body["tool_choice"]!["type"]!.GetValue<string>().ShouldBe("auto");
        body["tool_choice"]!["disable_parallel_tool_use"]!.GetValue<bool>().ShouldBeTrue();
    }

    [Fact]
    public async Task Anthropic_with_the_flag_on_sends_it_on_and_with_no_flag_sends_no_tool_choice()
    {
        await using var anthropic = await FakeAnthropicServer.StartAsync();
        await using var host = await StartAnthropicAsync(anthropic);

        await SaveAndRunAsync(host, new { provider = AnthropicProviderNames.Anthropic, model = "claude-test" }, agentName: "plain");
        await SaveAndRunAsync(host, new { provider = AnthropicProviderNames.Anthropic, model = "claude-test", allowMultipleToolCalls = true }, agentName: "parallel");

        anthropic.Bodies.Count.ShouldBe(2);
        anthropic.Bodies[0].ContainsKey("tool_choice").ShouldBeFalse("a definition that sets nothing sends today's request");
        anthropic.Bodies[1]["tool_choice"]!["disable_parallel_tool_use"]!.GetValue<bool>().ShouldBeFalse();
    }

    [Fact]
    public async Task Anthropic_refuses_the_fields_which_the_adapter_has_no_member_for()
    {
        await using var anthropic = await FakeAnthropicServer.StartAsync();
        await using var host = await StartAnthropicAsync(anthropic);

        foreach (var (field, value) in new (string, object)[]
        {
            ("seed", 7), ("frequencyPenalty", 0.5), ("presencePenalty", 0.5), ("temperature", 0.5), ("topP", 0.5), ("topK", 40),
        })
        {
            using var response = await host.Client.PostAsJsonAsync(
                new Uri("/tracon/api/agents/validate", UriKind.Relative),
                Definition(new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    ["provider"] = AnthropicProviderNames.Anthropic,
                    ["model"] = "claude-test",
                    [field] = value,
                }));

            var report = await TraconTestHost.ReadJsonAsync(response);

            report.GetProperty("valid").GetBoolean().ShouldBeFalse(field);
            report.GetProperty("messages").EnumerateArray().ShouldHaveSingleItem().GetProperty("path").GetString().ShouldBe($"model.{field}");
        }

        anthropic.Bodies.ShouldBeEmpty();
    }

    private static object Definition(object model) => new
    {
        name = AgentName,
        instructions = "Answer briefly.",
        toolNames = new[] { "lookup" },
        model,
    };

    private static async Task SaveAndRunAsync(TraconTestHost host, object model, string agentName = AgentName)
    {
        using var created = await host.Client.PostAsJsonAsync(
            new Uri("/tracon/api/agents", UriKind.Relative),
            new
            {
                name = agentName,
                instructions = "Answer briefly.",
                toolNames = new[] { "lookup" },
                model,
            });

        created.StatusCode.ShouldBe(System.Net.HttpStatusCode.Created, await created.Content.ReadAsStringAsync());

        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri($"/tracon/api/agents/{agentName}/run", UriKind.Relative))
        {
            Content = JsonContent.Create(new AgentRunRequest { Message = "hi" }),
        };
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));

        using var run = await host.Client.SendAsync(request);

        run.StatusCode.ShouldBe(System.Net.HttpStatusCode.OK, await run.Content.ReadAsStringAsync());
    }

    private static Task<TraconTestHost> StartOpenAIAsync(FakeOpenAiCompatibleServer server)
        => TraconTestHost.StartAsync(configureTracon: builder => builder
            .UseOpenAICompatible("wire-openai", options =>
            {
                options.Endpoint = server.BaseAddress;
                options.EnableResponsesSurface = true;
            })
            .AddTool(() => "found", name: "lookup", description: "Looks something up."));

    private static Task<TraconTestHost> StartAnthropicAsync(FakeAnthropicServer server)
        => TraconTestHost.StartAsync(configureTracon: builder => builder
            .UseAnthropic(TestKey, options =>
            {
                options.Endpoint = server.BaseAddress;
                options.MaxRetries = 0;
                options.Models.Add(new ModelDescriptor { Name = "claude-test" });
            })
            .AddTool(() => "found", name: "lookup", description: "Looks something up."));
}
