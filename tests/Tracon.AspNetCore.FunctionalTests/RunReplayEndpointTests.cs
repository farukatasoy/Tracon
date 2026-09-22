using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;
// 🚨 `using Tracon.Testing;` is NOT WRITTEN: the package's TraconTestHost
// and this project's own (TestServer-based) TraconTestHost have the SAME
// name, and CS0104 fires when both are visible (K-269). Only the type is
// aliased.
using FakeModelProvider = Tracon.Testing.FakeModelProvider;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// Tests for the replay, input, and compare endpoints (Phase 47).
/// </summary>
public sealed class RunReplayEndpointTests
{
    private const string TenantHeader = "X-Tracon-Tenant";
    private const string AgentName = "replayable";
    private const string ModelId = "echo-1";

    [Fact]
    public async Task Recorded_input_is_read_back_with_its_polymorphic_content()
    {
        await using var host = await TraconTestHost.StartAsync();
        var runId = await SeedAsync(
            host,
            [
                new ChatMessage(
                    ChatRole.User,
                    [
                        new TextContent("explain this image"),
                        new UriContent("https://example/image.png", "image/png"),
                    ]),
            ]);

        using var response = await host.Client.GetAsync(InputUri(runId));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var body = await TraconTestHost.ReadJsonAsync(response);
        var contents = body.GetProperty("messages")[0].GetProperty("contents");

        contents.GetArrayLength().ShouldBe(2);
        contents[0].GetProperty("text").GetString().ShouldBe("explain this image");
        contents[1].GetProperty("mediaType").GetString().ShouldBe("image/png");
    }

    [Fact]
    public async Task A_run_with_no_input_record_cannot_be_replayed_returns_404()
    {
        await using var host = await TraconTestHost.StartAsync();
        var runs = host.Services.GetRequiredService<IRunStore>();
        var runId = TraconId.NewId();

        // A run started WITHOUT writing input: the counterpart of a row that
        // started while RecordRunInput was disabled, or that was deleted by
        // a retention policy.
        await runs.StartRunAsync(new RunStartInfo
        {
            RunId = runId,
            AgentName = AgentName,
            StartedAt = DateTimeOffset.UtcNow,
        });

        using var input = await host.Client.GetAsync(InputUri(runId));
        using var replay = await host.Client.PostAsJsonAsync(ReplayUri(runId), new { toolMode = "NoTools" });

        input.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        replay.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Run_started_while_input_recording_is_OFF_has_a_row_but_no_input()
    {
        // 🚨 The sibling test above writes the run row BY HAND, which is a
        // different code path: it never asks RunRecordingAgent whether to record
        // the input. Only turning the real option off proves that the switch
        // consumers actually flip reaches the input store — and that turning it
        // off costs the input alone, not the run row.
        await using var host = await TraconTestHost.StartAsync(
            ConfigureAgent,
            configureServices: static services => services.Configure<TraconOptions>(
                static o => o.RunRecording.RecordRunInput = false));

        await SeedDefinitionsAsync(host);

        using (var run = await host.Client.PostAsJsonAsync(
            new Uri($"/tracon/api/agents/{AgentName}/run", UriKind.Relative),
            new AgentRunRequest { Message = "hello" }))
        {
            run.EnsureSuccessStatusCode();
            await SseReader.ReadAllAsync(await run.Content.ReadAsStreamAsync());
        }

        var runs = host.Services.GetRequiredService<IRunStore>();
        var runId = (await runs.QueryRunsAsync(new RunQuery())).ShouldHaveSingleItem().Id;

        using var record = await host.Client.GetAsync(
            new Uri($"/tracon/api/runs/{runId}", UriKind.Relative));
        using var input = await host.Client.GetAsync(InputUri(runId));

        record.StatusCode.ShouldBe(HttpStatusCode.OK);
        input.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Replay_opens_a_new_run_and_carries_lineage()
    {
        await using var host = await TraconTestHost.StartAsync(ConfigureAgent);
        var runId = await SeedAsync(host, [new ChatMessage(ChatRole.User, "hello")]);

        using var response = await host.Client.PostAsJsonAsync(ReplayUri(runId), new { toolMode = "NoTools" });

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());

        var body = await TraconTestHost.ReadJsonAsync(response);
        var replayId = body.GetProperty("runId").GetGuid();

        body.GetProperty("sourceRunId").GetGuid().ShouldBe(runId);

        var runs = host.Services.GetRequiredService<IRunStore>();
        var replayed = await runs.GetRunAsync(replayId);
        var source = await runs.GetRunAsync(runId);

        replayed.ShouldNotBeNull();
        replayed!.ReplayOfRunId.ShouldBe(runId);

        // 🚨 The source run DOES NOT CHANGE: lineage is one-directional.
        source!.ReplayOfRunId.ShouldBeNull();
    }

    [Fact]
    public async Task NO_tool_actually_runs_in_ReplayTools_mode()
    {
        ReplayProbeTools.Reset();

        await using var host = await TraconTestHost.StartAsync(ConfigureToolAgent);
        var runId = await SeedAsync(host, [new ChatMessage(ChatRole.User, "where is ORD-7")]);

        await RecordToolCallAsync(host, runId, "get_order_status", "orderId=ORD-7", "in transit");

        using var response = await host.Client.PostAsJsonAsync(ReplayUri(runId), new { toolMode = "ReplayTools" });

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        // The tool body did NOT run, but the model saw the tool and got the recorded result.
        ReplayProbeTools.Calls.ShouldBe(0);

        var body = await TraconTestHost.ReadJsonAsync(response);

        body.GetProperty("output").GetString().ShouldNotBeNull().ShouldContain("in transit");
    }

    [Fact]
    public async Task A_mismatched_tool_call_returns_422_and_writes_the_tool_name()
    {
        ReplayProbeTools.Reset();

        await using var host = await TraconTestHost.StartAsync(ConfigureToolAgent);
        var runId = await SeedAsync(host, [new ChatMessage(ChatRole.User, "where is ORD-7")]);

        // The recorded result belongs to a DIFFERENT argument; the model will call with ORD-7.
        await RecordToolCallAsync(host, runId, "get_order_status", "orderId=ORD-9", "delivered");

        using var response = await host.Client.PostAsJsonAsync(ReplayUri(runId), new { toolMode = "ReplayTools" });

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);

        var body = await TraconTestHost.ReadJsonAsync(response);

        body.GetProperty("toolName").GetString().ShouldBe("get_order_status");
        body.GetProperty("arguments").GetString().ShouldBe("orderId=ORD-7");

        // 🚨 Not silently skipped, and not run live.
        ReplayProbeTools.Calls.ShouldBe(0);
    }

    [Fact]
    public async Task A_tool_requiring_approval_returns_409_with_LiveTools()
    {
        await using var host = await TraconTestHost.StartAsync(ConfigureApprovalAgent);
        var runId = await SeedAsync(host, [new ChatMessage(ChatRole.User, "cancel the order")]);

        using var live = await host.Client.PostAsJsonAsync(ReplayUri(runId), new { toolMode = "LiveTools" });
        using var replayed = await host.Client.PostAsJsonAsync(ReplayUri(runId), new { toolMode = "NoTools" });

        live.StatusCode.ShouldBe(HttpStatusCode.Conflict);

        // The same agent must remain replayable in a side-effect-free mode.
        replayed.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_tool_requiring_approval_on_a_code_sourced_agent_also_returns_409_with_LiveTools()
    {
        // HATA-S4-014: the approval-tool guard only ran on the `definition is
        // not null` branch (DB-sourced agent, test above).
        // `PrepareFromCatalogAsync` (the code-sourced agent path) did NOT
        // apply the SAME guard — the request would open "successfully" with
        // 200 and fall into a silent run where the model never called the
        // tool, with no signal to the user about WHY nothing happened.
        await using var host = await TraconTestHost.StartAsync(builder =>
        {
            builder.AddToolsFrom(typeof(ReplayProbeTools));
            builder.AddAgent(TestData.Definition("code-approval-agent") with { ToolNames = ["cancel_order"] });
        });

        var runId = await SeedAsync(
            host, [new ChatMessage(ChatRole.User, "cancel the order")], agentName: "code-approval-agent");

        using var live = await host.Client.PostAsJsonAsync(ReplayUri(runId), new { toolMode = "LiveTools" });

        live.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task An_agent_with_no_persistent_definition_cannot_be_replayed_with_overrides_returns_400()
    {
        // A code agent has no AgentDefinition counterpart; model overriding
        // and tool modes require recompiling, and silently falling back to
        // LiveTools would violate K1.
        await using var host = await TraconTestHost.StartAsync(
            static builder => builder.AddAgent(TestData.Definition("kod-agent")));

        var runId = await SeedAsync(host, [new ChatMessage(ChatRole.User, "hello")], agentName: "kod-agent");

        using var response = await host.Client.PostAsJsonAsync(ReplayUri(runId), new { toolMode = "ReplayTools" });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Another_tenants_run_cannot_be_replayed_returns_the_SAME_404()
    {
        await using var host = await TraconTestHost.StartAsync(builder =>
        {
            ConfigureAgent(builder);
            builder.UseTenancy(static options =>
            {
                options.Enabled = true;
                options.AllowHeaderResolution = true;
            });
        });

        var runId = await SeedAsync(host, [new ChatMessage(ChatRole.User, "hello")], tenantId: "tenant-a");

        using var missing = await SendAsTenant(host, ReplayUri(TraconId.NewId()), "tenant-b");
        using var wrongTenant = await SendAsTenant(host, ReplayUri(runId), "tenant-b");

        missing.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        wrongTenant.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var missingBody = await TraconTestHost.ReadJsonAsync(missing);
        var wrongBody = await TraconTestHost.ReadJsonAsync(wrongTenant);

        missingBody.GetProperty("title").GetString().ShouldBe(wrongBody.GetProperty("title").GetString());
    }

    [Fact]
    public async Task Two_runs_are_compared_side_by_side()
    {
        await using var host = await TraconTestHost.StartAsync(ConfigureAgent);
        var runId = await SeedAsync(host, [new ChatMessage(ChatRole.User, "hello")]);

        using var replayResponse = await host.Client.PostAsJsonAsync(ReplayUri(runId), new { toolMode = "NoTools" });
        var replayId = (await TraconTestHost.ReadJsonAsync(replayResponse)).GetProperty("runId").GetGuid();

        using var compare = await host.Client.GetAsync(
            new Uri($"/tracon/api/runs/{runId}/compare/{replayId}", UriKind.Relative));

        compare.StatusCode.ShouldBe(HttpStatusCode.OK);

        var body = await TraconTestHost.ReadJsonAsync(compare);

        body.GetProperty("left").GetProperty("runId").GetGuid().ShouldBe(runId);
        body.GetProperty("right").GetProperty("runId").GetGuid().ShouldBe(replayId);
        body.GetProperty("right").GetProperty("replayOfRunId").GetGuid().ShouldBe(runId);
        body.GetProperty("right").GetProperty("output").GetString().ShouldNotBeNullOrEmpty();
    }

    private static void ConfigureAgent(ITraconBuilder builder)
        => SaveDefinition(builder, new AgentDefinition
        {
            Name = AgentName,
            Instructions = "Give a short answer.",
            Model = TestData.Model(),
        });

    private static void ConfigureToolAgent(ITraconBuilder builder)
    {
        builder
            .AddModelProvider(new FakeModelProvider("tool-echo")
                .CallsTool("get_order_status", new { orderId = "ORD-7" })
                .EchoesLastToolResult())
            .AddToolsFrom(typeof(ReplayProbeTools));

        SaveDefinition(builder, new AgentDefinition
        {
            Name = AgentName,
            Instructions = "Give a short answer.",
            Model = new ModelBinding { Provider = "tool-echo", Model = ModelId },
            ToolNames = ["get_order_status"],
        });
    }

    private static void ConfigureApprovalAgent(ITraconBuilder builder)
    {
        builder.AddToolsFrom(typeof(ReplayProbeTools));

        SaveDefinition(builder, new AgentDefinition
        {
            Name = AgentName,
            Instructions = "Give a short answer.",
            Model = TestData.Model(),
            ToolNames = ["cancel_order"],
        });
    }

    /// <summary>
    /// Saves the definition as if it were <em>database</em>-sourced.
    /// </summary>
    /// <remarks>
    /// Replay recompiles the definition; a code agent added with <c>AddAgent</c>
    /// has its definition in the catalog but it does NOT exist in
    /// <see cref="IAgentDefinitionStore"/>, so overriding cannot be applied.
    /// </remarks>
    private static void SaveDefinition(ITraconBuilder builder, AgentDefinition definition)
        => builder.Services.AddSingleton<IStartupSeed>(new StartupSeed(definition));

    private static async ValueTask<Guid> SeedAsync(
        TraconTestHost host,
        IReadOnlyList<ChatMessage> messages,
        string agentName = AgentName,
        string? tenantId = null)
    {
        await SeedDefinitionsAsync(host);

        var runs = host.Services.GetRequiredService<IRunStore>();
        var inputs = host.Services.GetRequiredService<IRunInputStore>();
        var tenants = host.Services.GetRequiredService<ITenantContext>();
        var tenant = tenantId ?? tenants.TenantId;
        var runId = TraconId.NewId();

        await runs.StartRunAsync(new RunStartInfo
        {
            RunId = runId,
            AgentName = agentName,
            StartedAt = DateTimeOffset.UtcNow,
            TenantId = tenant,
            ModelId = ModelId,
        });

        await runs.CompleteRunAsync(new RunCompletion
        {
            RunId = runId,
            Status = RunStatus.Completed,
            CompletedAt = DateTimeOffset.UtcNow,
        });

        await inputs.SaveAsync(new RunInputRecord
        {
            RunId = runId,
            TenantId = tenant,
            Messages = messages,
            CreatedAt = DateTimeOffset.UtcNow,
        });

        return runId;
    }

    private static async ValueTask SeedDefinitionsAsync(TraconTestHost host)
    {
        var store = host.Services.GetRequiredService<IAgentDefinitionStore>();

        foreach (var seed in host.Services.GetServices<IStartupSeed>())
        {
            await store.SaveAsync(seed.Definition);
        }
    }

    private static async ValueTask RecordToolCallAsync(
        TraconTestHost host,
        Guid runId,
        string toolName,
        string arguments,
        string result)
    {
        var runs = host.Services.GetRequiredService<IRunStore>();

        await runs.RecordToolInvocationAsync(new ToolInvocationRecord
        {
            Id = TraconId.NewId(),
            RunId = runId,
            ToolName = toolName,
            Arguments = arguments,
            Result = result,
            CreatedAt = DateTimeOffset.UtcNow,
        });
    }

    private static async Task<HttpResponseMessage> SendAsTenant(
        TraconTestHost host,
        Uri uri,
        string tenantId)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, uri)
        {
            Content = JsonContent.Create(new { toolMode = "NoTools" }),
        };

        request.Headers.Add(TenantHeader, tenantId);

        return await host.Client.SendAsync(request);
    }

    private static Uri ReplayUri(Guid runId) => new($"/tracon/api/runs/{runId}/replay", UriKind.Relative);

    private static Uri InputUri(Guid runId) => new($"/tracon/api/runs/{runId}/input", UriKind.Relative);

    /// <summary>Marker interface carrying definitions to be saved during startup.</summary>
    private interface IStartupSeed
    {
        AgentDefinition Definition { get; }
    }

    private sealed record StartupSeed(AgentDefinition Definition) : IStartupSeed;
}

/// <summary>
/// The tools for the replay tests. Measures whether the body ACTUALLY ran
/// by counting calls.
/// </summary>
internal static class ReplayProbeTools
{
    private static int _calls;

    /// <summary>How many times the tool body ran.</summary>
    public static int Calls => Volatile.Read(ref _calls);

    /// <summary>Resets the counter.</summary>
    public static void Reset() => Volatile.Write(ref _calls, 0);

    /// <summary>Returns an order's status.</summary>
    /// <param name="orderId">The order id.</param>
    /// <returns>The status text.</returns>
    [TraconTool("get_order_status", "Returns an order's shipping status.")]
    public static string GetOrderStatus(string orderId)
    {
        Interlocked.Increment(ref _calls);

        return $"{orderId}: RAN LIVE";
    }

    /// <summary>Cancels an order. Requires approval.</summary>
    /// <param name="orderId">The order id.</param>
    /// <returns>The result text.</returns>
    [TraconTool("cancel_order", "Cancels an order.", RequiresApproval = true)]
    public static string CancelOrder(string orderId)
    {
        Interlocked.Increment(ref _calls);

        return $"{orderId} canceled.";
    }
}
