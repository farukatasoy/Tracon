using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.AI;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;
// Same rationale as ToolGovernanceEndpointTests.cs (K-269): the package's
// Tracon.Testing.TraconTestHost and this project's own TraconTestHost
// share the same name; a blanket `using Tracon.Testing;` would produce CS0104.
using FakeModelProvider = Tracon.Testing.FakeModelProvider;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// F-598 — the model's request fields and the tool-call loop come from the definition: a definition saved
/// over HTTP carries each field to the provider's request, and the definition reads back as it was saved.
/// </summary>
/// <remarks>
/// <para>
/// Measured 2026-10-20 before this phase: <c>Seed</c>, <c>TopK</c>, the stop sequences, the penalties, the
/// tool mode and <c>AllowMultipleToolCalls</c> reached the provider only when an agent extension set them,
/// where no definition version, experiment variant or replay could see them, and <c>Reasoning.Output</c>
/// reached the provider by no route at all.
/// </para>
/// <para>
/// The test goes through the whole host: the JSON a client sends, the store, the catalog, the compiler, the
/// model pipeline, and the request the (fake) provider receives. A field added to the contract whose body
/// forgets to hand it on is the failure it is built to see.
/// </para>
/// </remarks>
public sealed class ModelSamplingFieldsTests
{
    private const string ProviderName = "sampling-model";
    private const string ModelName = "sampling-1";
    private const string AgentName = "sampling-agent";

    private const string FullDefinition = """
        {
          "name": "sampling-agent",
          "instructions": "Answer briefly.",
          "toolNames": ["lookup"],
          "model": {
            "provider": "sampling-model",
            "model": "sampling-1",
            "seed": 42,
            "topK": 40,
            "stopSequences": ["END", "STOP"],
            "frequencyPenalty": 0.25,
            "presencePenalty": 0.5,
            "toolMode": "required:lookup",
            "allowMultipleToolCalls": false,
            "reasoningEffort": "Low",
            "reasoningOutput": "Summary",
            "toolLoop": { "allowConcurrentToolCalls": true, "maximumConsecutiveErrors": 1, "terminateOnUnknownCalls": true }
          }
        }
        """;

    [Fact]
    public async Task Every_field_a_definition_saves_reaches_the_providers_request()
    {
        var provider = Provider();
        await using var host = await StartAsync(provider);

        using var created = await PostDefinitionAsync(host, FullDefinition);

        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());

        using var run = await RunAsync(host);

        run.StatusCode.ShouldBe(HttpStatusCode.OK, await run.Content.ReadAsStringAsync());

        var options = provider.Requests.ShouldHaveSingleItem().Options.ShouldNotBeNull();

        options.Seed.ShouldBe(42);
        options.TopK.ShouldBe(40);
        options.StopSequences.ShouldBe(["END", "STOP"]);
        options.FrequencyPenalty.ShouldBe(0.25f);
        options.PresencePenalty.ShouldBe(0.5f);
        options.ToolMode.ShouldBeOfType<RequiredChatToolMode>().RequiredFunctionName.ShouldBe("lookup");
        options.AllowMultipleToolCalls.ShouldBe(false);
        options.Reasoning.ShouldNotBeNull().Effort.ShouldBe(ReasoningEffort.Low);
        options.Reasoning.Output.ShouldBe(ReasoningOutput.Summary);
    }

    [Fact]
    public async Task The_definition_reads_back_with_every_field_it_was_saved_with()
    {
        await using var host = await StartAsync(Provider());

        using var created = await PostDefinitionAsync(host, FullDefinition);

        created.EnsureSuccessStatusCode();

        var saved = await host.Client.GetFromJsonAsync<JsonElement>(new Uri($"/tracon/api/agents/{AgentName}", UriKind.Relative));
        var model = saved.GetProperty("definition").GetProperty("model");

        model.GetProperty("seed").GetInt64().ShouldBe(42);
        model.GetProperty("topK").GetInt32().ShouldBe(40);
        model.GetProperty("stopSequences").EnumerateArray().Select(static item => item.GetString()).ShouldBe(["END", "STOP"]);
        model.GetProperty("frequencyPenalty").GetSingle().ShouldBe(0.25f);
        model.GetProperty("presencePenalty").GetSingle().ShouldBe(0.5f);
        model.GetProperty("toolMode").GetString().ShouldBe("required:lookup");
        model.GetProperty("allowMultipleToolCalls").GetBoolean().ShouldBeFalse();
        model.GetProperty("reasoningOutput").GetString().ShouldBe("Summary");

        var loop = model.GetProperty("toolLoop");

        loop.GetProperty("allowConcurrentToolCalls").GetBoolean().ShouldBeTrue();
        loop.GetProperty("maximumConsecutiveErrors").GetInt32().ShouldBe(1);
        loop.GetProperty("terminateOnUnknownCalls").GetBoolean().ShouldBeTrue();

        // The old place is gone from the wire, not kept beside the new one.
        model.TryGetProperty("allowConcurrentToolCalls", out _).ShouldBeFalse();
    }

    [Fact]
    public async Task A_definition_that_sets_none_sends_none_of_them()
    {
        var provider = Provider();
        await using var host = await StartAsync(provider);

        using var created = await PostDefinitionAsync(host, """
            {"name":"sampling-agent","instructions":"Answer briefly.","model":{"provider":"sampling-model","model":"sampling-1"}}
            """);

        created.EnsureSuccessStatusCode();

        using var run = await RunAsync(host);

        run.EnsureSuccessStatusCode();

        var options = provider.Requests.ShouldHaveSingleItem().Options.ShouldNotBeNull();

        options.Seed.ShouldBeNull();
        options.TopK.ShouldBeNull();
        options.StopSequences.ShouldBeNull();
        options.FrequencyPenalty.ShouldBeNull();
        options.PresencePenalty.ShouldBeNull();
        options.ToolMode.ShouldBeNull();
        options.AllowMultipleToolCalls.ShouldBeNull();
        options.Reasoning.ShouldBeNull();
    }

    /// <summary>
    /// The generated client sends a member it was not given as <c>null</c>. A <c>toolLoop</c> of <c>null</c> means
    /// the default settings, so the save is not refused and the definition reads back with the object.
    /// </summary>
    [Fact]
    public async Task A_null_tool_loop_is_accepted_and_reads_back_as_the_default_settings()
    {
        await using var host = await StartAsync(Provider());

        using var created = await PostDefinitionAsync(host, """
            {"name":"sampling-agent","instructions":"Answer briefly.","model":{"provider":"sampling-model","model":"sampling-1","toolLoop":null}}
            """);

        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());

        var saved = await host.Client.GetFromJsonAsync<JsonElement>(new Uri($"/tracon/api/agents/{AgentName}", UriKind.Relative));
        var loop = saved.GetProperty("definition").GetProperty("model").GetProperty("toolLoop");

        loop.GetProperty("allowConcurrentToolCalls").GetBoolean().ShouldBeFalse();
        loop.GetProperty("maximumConsecutiveErrors").ValueKind.ShouldBe(JsonValueKind.Null);
        loop.GetProperty("terminateOnUnknownCalls").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task A_new_version_changes_the_field_the_next_run_sends()
    {
        // The definition is the one place a model setting lives: a new version of it, not code, changes the next run.
        var provider = Provider();
        await using var host = await StartAsync(provider);

        using var first = await PostDefinitionAsync(host, FullDefinition);

        first.EnsureSuccessStatusCode();

        using var firstRun = await RunAsync(host);

        firstRun.EnsureSuccessStatusCode();

        using var update = await host.Client.PutAsync(
            new Uri($"/tracon/api/agents/{AgentName}", UriKind.Relative),
            new StringContent(FullDefinition.Replace("\"seed\": 42", "\"seed\": 99", StringComparison.Ordinal), System.Text.Encoding.UTF8, "application/json"));

        update.EnsureSuccessStatusCode();

        using var secondRun = await RunAsync(host);

        secondRun.EnsureSuccessStatusCode();

        provider.Requests.Select(static request => request.Options!.Seed).ShouldBe([42L, 99L]);
    }

    private static FakeModelProvider Provider()
        => new FakeModelProvider(ProviderName)
            .WithModel(new ModelDescriptor { Name = ModelName })
            .EchoesUserMessage();

    private static Task<TraconTestHost> StartAsync(FakeModelProvider provider)
        => TraconTestHost.StartAsync(builder => builder
            .AddModelProvider(provider)
            .AddTool(() => "found", name: "lookup", description: "Looks something up."));

    private static Task<HttpResponseMessage> PostDefinitionAsync(TraconTestHost host, string json)
        => host.Client.PostAsync(
            new Uri("/tracon/api/agents", UriKind.Relative),
            new StringContent(json, System.Text.Encoding.UTF8, "application/json"));

    private static async Task<HttpResponseMessage> RunAsync(TraconTestHost host)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri($"/tracon/api/agents/{AgentName}/run", UriKind.Relative))
        {
            Content = JsonContent.Create(new AgentRunRequest { Message = "hi" }),
        };

        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));

        return await host.Client.SendAsync(request);
    }
}
