using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// Tests for replay's rejection of agents carrying a client-side tool
/// (<c>AddClientTool</c>, phase 112). A client-side tool has no server-side
/// body and no recorded result, so no replay mode can answer a call to it —
/// the run must be rejected at preparation time, before it starts.
/// </summary>
public sealed class ReplayClientToolEndpointTests
{
    private const string AgentName = "client-tool-replay-agent";
    private const string ClientToolName = "open_page";
    private const string ServerToolName = "get_order_status";

    private static readonly JsonElement EmptyObjectSchema =
        JsonSerializer.Deserialize<JsonElement>("""{"type":"object","properties":{}}""");

    [Theory]
    [InlineData("ReplayTools")]
    [InlineData("LiveTools")]
    [InlineData("NoTools")]
    public async Task An_agent_carrying_a_client_side_tool_cannot_be_replayed_in_any_tool_mode(string toolMode)
    {
        await using var host = await TraconTestHost.StartAsync(ConfigureClientToolAgent);
        var runId = await SeedAsync(host, [new ChatMessage(ChatRole.User, "open the page")]);

        using var response = await host.Client.PostAsJsonAsync(ReplayUri(runId), new { toolMode });

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);

        var body = await TraconTestHost.ReadJsonAsync(response);

        body.GetProperty("detail").GetString().ShouldNotBeNull().ShouldContain(ClientToolName);
    }

    [Fact]
    public async Task An_agent_with_only_server_side_tools_is_not_affected_no_regression()
    {
        await using var host = await TraconTestHost.StartAsync(ConfigureServerToolAgent);
        var runId = await SeedAsync(host, [new ChatMessage(ChatRole.User, "where is ORD-7")]);

        using var response = await host.Client.PostAsJsonAsync(ReplayUri(runId), new { toolMode = "ReplayTools" });

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task An_agent_with_no_tools_at_all_is_not_affected()
    {
        await using var host = await TraconTestHost.StartAsync(ConfigureNoToolAgent);
        var runId = await SeedAsync(host, [new ChatMessage(ChatRole.User, "hello")]);

        using var response = await host.Client.PostAsJsonAsync(ReplayUri(runId), new { toolMode = "NoTools" });

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_code_defined_agent_carrying_a_client_side_tool_is_also_rejected()
    {
        // The catalog path (PrepareFromCatalogAsync) is a SEPARATE code path
        // from the persistent-definition one above; phase 47's approval-tool
        // guard once missed this exact branch (HATA-S4-014).
        await using var host = await TraconTestHost.StartAsync(builder =>
        {
            builder
                .AddClientTool(ClientToolName, "Opens a page in the user's browser.", EmptyObjectSchema)
                .AddAgent(TestData.Definition("code-client-tool-agent") with { ToolNames = [ClientToolName] });
        });

        var runId = await SeedAsync(
            host, [new ChatMessage(ChatRole.User, "open the page")], agentName: "code-client-tool-agent");

        using var response = await host.Client.PostAsJsonAsync(ReplayUri(runId), new { toolMode = "LiveTools" });

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);

        var body = await TraconTestHost.ReadJsonAsync(response);

        body.GetProperty("detail").GetString().ShouldNotBeNull().ShouldContain(ClientToolName);
    }

    private static void ConfigureClientToolAgent(ITraconBuilder builder)
    {
        builder.AddClientTool(ClientToolName, "Opens a page in the user's browser.", EmptyObjectSchema);

        SaveDefinition(builder, new AgentDefinition
        {
            Name = AgentName,
            Instructions = "Use the tool when asked to open a page.",
            Model = TestData.Model(),
            ToolNames = [ClientToolName],
        });
    }

    private static void ConfigureServerToolAgent(ITraconBuilder builder)
    {
        builder.AddToolsFrom(typeof(ReplayProbeServerTools));

        SaveDefinition(builder, new AgentDefinition
        {
            Name = AgentName,
            Instructions = "Give a short answer.",
            Model = TestData.Model(),
            ToolNames = [ServerToolName],
        });
    }

    private static void ConfigureNoToolAgent(ITraconBuilder builder)
        => SaveDefinition(builder, new AgentDefinition
        {
            Name = AgentName,
            Instructions = "Give a short answer.",
            Model = TestData.Model(),
        });

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
        string agentName = AgentName)
    {
        var store = host.Services.GetRequiredService<IAgentDefinitionStore>();

        foreach (var seed in host.Services.GetServices<IStartupSeed>())
        {
            await store.SaveAsync(seed.Definition);
        }

        var runs = host.Services.GetRequiredService<IRunStore>();
        var inputs = host.Services.GetRequiredService<IRunInputStore>();
        var runId = TraconId.NewId();

        await runs.StartRunAsync(new RunStartInfo
        {
            RunId = runId,
            AgentName = agentName,
            StartedAt = DateTimeOffset.UtcNow,
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
            TenantId = host.Services.GetRequiredService<ITenantContext>().TenantId,
            Messages = messages,
            CreatedAt = DateTimeOffset.UtcNow,
        });

        return runId;
    }

    private static Uri ReplayUri(Guid runId) => new($"/tracon/api/runs/{runId}/replay", UriKind.Relative);

    /// <summary>Marker interface carrying definitions to be saved during startup.</summary>
    private interface IStartupSeed
    {
        AgentDefinition Definition { get; }
    }

    private sealed record StartupSeed(AgentDefinition Definition) : IStartupSeed;
}

/// <summary>A server-side tool used only to prove the no-regression path.</summary>
internal static class ReplayProbeServerTools
{
    /// <summary>Returns an order's status.</summary>
    /// <param name="orderId">The order id.</param>
    /// <returns>The status text.</returns>
    [TraconTool("get_order_status", "Returns an order's shipping status.")]
    public static string GetOrderStatus(string orderId) => $"{orderId}: in transit";
}
