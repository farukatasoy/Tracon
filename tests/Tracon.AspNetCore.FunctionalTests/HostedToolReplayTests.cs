using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// F-603 — a replay that plays tools back or runs none never runs a hosted tool: the provider runs it inside the model
/// call, the replay transform cannot reach it, and a web search would run and bill again.
/// </summary>
/// <remarks>
/// A function tool is wrapped by the replay transform. A hosted tool is not an <c>AIFunction</c>, so the tool list of the
/// replayed definition is the only place the promise ("no tool actually runs") can hold.
/// </remarks>
public sealed class HostedToolReplayTests
{
    private const string AgentName = "hosted-replay-agent";
    private const string ProviderName = "hosted-replay";

    [Theory]
    [InlineData("ReplayTools", false)]
    [InlineData("NoTools", false)]
    [InlineData("LiveTools", true)]
    public async Task A_replay_offers_the_hosted_tool_only_in_live_mode(string toolMode, bool offered)
    {
        var provider = new ContentModelProvider([new TextContent("replayed")], ProviderName);

        await using var host = await TraconTestHost.StartAsync(builder => builder
            .AddModelProvider(provider)
            .AddTool((Func<string>)(static () => "42"), name: "lookup", description: "Looks something up.")
            .AddHostedTool("search", HostedToolKind.WebSearch, static _ => new HostedWebSearchTool()));

        var runId = await SeedAsync(host);

        using var response = await host.Client.PostAsJsonAsync(
            new Uri($"/tracon/api/runs/{runId}/replay", UriKind.Relative),
            new { toolMode },
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        var tools = provider.Options.ShouldHaveSingleItem()?.Tools ?? [];

        tools.Any(static tool => tool is HostedWebSearchTool).ShouldBe(offered);
    }

    [Theory]
    [InlineData("ReplayTools", false)]
    [InlineData("NoTools", false)]
    [InlineData("LiveTools", true)]
    public async Task A_replay_offers_the_harness_web_search_only_in_live_mode(string toolMode, bool offered)
    {
        // F-610: the harness turns the same hosted tool on without a name in the tool list, so the
        // name filter above never sees it.
        var provider = new ContentModelProvider([new TextContent("replayed")], ProviderName);

        await using var host = await TraconTestHost.StartAsync(builder => builder
            .AddModelProvider(provider)
            .AddTool((Func<string>)(static () => "42"), name: "lookup", description: "Looks something up."));

        var runId = await SeedAsync(host, harness: new HarnessSettings { EnableWebSearch = true }, toolNames: ["lookup"]);

        using var response = await host.Client.PostAsJsonAsync(
            new Uri($"/tracon/api/runs/{runId}/replay", UriKind.Relative),
            new { toolMode },
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        var tools = provider.Options.ShouldHaveSingleItem()?.Tools ?? [];

        tools.Any(static tool => tool is HostedWebSearchTool).ShouldBe(offered);
    }

    private static async ValueTask<Guid> SeedAsync(
        TraconTestHost host,
        HarnessSettings? harness = null,
        IReadOnlyList<string>? toolNames = null)
    {
        await host.Services.GetRequiredService<IAgentDefinitionStore>().SaveAsync(new AgentDefinition
        {
            Name = AgentName,
            Instructions = "Answer briefly.",
            Model = new ModelBinding { Provider = ProviderName, Model = $"{ProviderName}-1" },
            ToolNames = [.. toolNames ?? ["search", "lookup"]],
            Harness = harness,
        });

        var runs = host.Services.GetRequiredService<IRunStore>();
        var runId = TraconId.NewId();

        await runs.StartRunAsync(new RunStartInfo { RunId = runId, AgentName = AgentName, StartedAt = DateTimeOffset.UtcNow });
        await runs.CompleteRunAsync(new RunCompletion { RunId = runId, Status = RunStatus.Completed, CompletedAt = DateTimeOffset.UtcNow });
        await host.Services.GetRequiredService<IRunInputStore>().SaveAsync(new RunInputRecord
        {
            RunId = runId,
            TenantId = host.Services.GetRequiredService<ITenantContext>().TenantId,
            Messages = [new ChatMessage(ChatRole.User, "what happened")],
            CreatedAt = DateTimeOffset.UtcNow,
        });

        return runId;
    }
}
