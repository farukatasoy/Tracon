using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// The HTTP surface of the call graph: validation at save time and the run tree endpoints.
/// </summary>
public sealed class AgentCallGraphTests
{
    private static readonly Uri Agents = new("/tracon/api/agents", UriKind.Relative);

    [Fact]
    public async Task Self_calling_definition_is_rejected()
    {
        await using var host = await TraconTestHost.StartAsync();

        using var response = await host.Client.PostAsJsonAsync(
            Agents,
            TestData.Request() with { CallableAgentNames = ["db-agent"] });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var problem = await TraconTestHost.ReadJsonAsync(response);
        Detail(problem).ShouldContain("cannot call itself", Case.Sensitive);
    }

    [Fact]
    public async Task Unknown_agent_name_is_rejected()
    {
        await using var host = await TraconTestHost.StartAsync();

        using var response = await host.Client.PostAsJsonAsync(
            Agents,
            TestData.Request() with { CallableAgentNames = ["does-not-exist"] });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var problem = await TraconTestHost.ReadJsonAsync(response);
        Detail(problem).ShouldContain("no such agent exists in the catalog", Case.Sensitive);
    }

    [Fact]
    public async Task Indirect_cycle_is_rejected()
    {
        await using var host = await TraconTestHost.StartAsync();

        await CreateAsync(host, "a");
        await CreateAsync(host, "b");
        await UpdateAsync(host, "b", ["a"]);

        // The chain b -> a is ready. Adding a -> b closes the cycle.
        using var response = await host.Client.PutAsJsonAsync(
            new Uri("/tracon/api/agents/a", UriKind.Relative),
            TestData.Request(name: "a") with { CallableAgentNames = ["b"] });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var problem = await TraconTestHost.ReadJsonAsync(response);
        Detail(problem).ShouldContain("cycle", Case.Sensitive);
    }

    [Fact]
    public async Task Valid_graph_is_saved_and_appears_in_the_catalog()
    {
        await using var host = await TraconTestHost.StartAsync();

        await CreateAsync(host, "arastirmaci");
        await CreateAsync(host, "yonlendirici");
        await UpdateAsync(host, "yonlendirici", ["arastirmaci"]);

        using var list = await host.Client.GetAsync(Agents);
        var json = await TraconTestHost.ReadJsonAsync(list);

        var router = json.EnumerateArray()
            .Single(static agent => string.Equals(agent.GetProperty("name").GetString(), "yonlendirici", StringComparison.Ordinal));

        router.GetProperty("callableAgentNames")
            .EnumerateArray()
            .Select(static name => name.GetString() ?? string.Empty)
            .ShouldBe(["arastirmaci"]);
    }

    [Fact]
    public async Task Runs_list_returns_only_roots_by_default()
    {
        await using var host = await TraconTestHost.StartAsync();

        var (rootId, childId) = await RecordTreeAsync(host);

        using var roots = await host.Client.GetAsync(new Uri("/tracon/api/runs", UriKind.Relative));
        var rootIds = await IdsAsync(roots);

        rootIds.ShouldContain(id => string.Equals(id, rootId.ToString(), StringComparison.Ordinal));
        rootIds.ShouldNotContain(id => string.Equals(id, childId.ToString(), StringComparison.Ordinal));

        using var all = await host.Client.GetAsync(
            new Uri("/tracon/api/runs?includeChildren=true", UriKind.Relative));

        var allIds = await IdsAsync(all);
        allIds.ShouldContain(id => string.Equals(id, rootId.ToString(), StringComparison.Ordinal));
        allIds.ShouldContain(id => string.Equals(id, childId.ToString(), StringComparison.Ordinal));
    }

    [Fact]
    public async Task Tree_endpoint_returns_the_whole_tree_even_from_a_child_run()
    {
        await using var host = await TraconTestHost.StartAsync();

        var (rootId, childId) = await RecordTreeAsync(host);

        // A request coming from a child run's detail also returns the whole tree:
        // the user cannot tell where they are in the tree without seeing sibling branches.
        using var response = await host.Client.GetAsync(
            new Uri($"/tracon/api/runs/{childId}/tree", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var ids = await IdsAsync(response);
        ids.ShouldBe([rootId.ToString(), childId.ToString()], ignoreOrder: true);
    }

    [Fact]
    public async Task Session_filter_with_includeChildren_also_returns_child_runs()
    {
        // HATA-S2-001 / MT-API-060: SessionId is set only on the ROOT run
        // (K-217). A direct equality filter combined with "includeChildren=true"
        // used to never match a child run.
        await using var host = await TraconTestHost.StartAsync();

        var runs = host.Services.GetRequiredService<IRunStore>();
        var rootId = TraconId.NewId();
        var childId = TraconId.NewId();

        await runs.StartRunAsync(new RunStartInfo
        {
            RunId = rootId,
            AgentName = "yonlendirici",
            StartedAt = DateTimeOffset.UtcNow,
            SessionId = "api-tree-01",
        });

        await runs.StartRunAsync(new RunStartInfo
        {
            RunId = childId,
            AgentName = "arastirmaci",
            StartedAt = DateTimeOffset.UtcNow,
            ParentRunId = rootId,
            RootRunId = rootId,
            Depth = 1,
        });

        using var rootOnly = await host.Client.GetAsync(
            new Uri("/tracon/api/runs?sessionId=api-tree-01", UriKind.Relative));

        (await IdsAsync(rootOnly)).ShouldBe([rootId.ToString()]);

        using var withChildren = await host.Client.GetAsync(
            new Uri("/tracon/api/runs?sessionId=api-tree-01&includeChildren=true", UriKind.Relative));

        (await IdsAsync(withChildren)).ShouldBe(
            [rootId.ToString(), childId.ToString()],
            ignoreOrder: true);
    }

    [Fact]
    public async Task ErrorType_filter_binds_and_filters()
    {
        // HATA-S3-007 / MT-GUARD-064: RunEndpoints.MapGet("/api/runs", ...)
        // used to not bind a parameter named "errorType"; ASP.NET Core
        // silently ignores an unknown query parameter, so the result ALWAYS
        // came back as if unfiltered.
        await using var host = await TraconTestHost.StartAsync();

        var runs = host.Services.GetRequiredService<IRunStore>();
        var blockedId = TraconId.NewId();
        var otherId = TraconId.NewId();

        await runs.StartRunAsync(new RunStartInfo { RunId = blockedId, AgentName = "a", StartedAt = DateTimeOffset.UtcNow });
        await runs.CompleteRunAsync(new RunCompletion
        {
            RunId = blockedId,
            Status = RunStatus.Failed,
            CompletedAt = DateTimeOffset.UtcNow,
            Error = new RunError { Type = "content_blocked", Message = "blocked" },
        });

        await runs.StartRunAsync(new RunStartInfo { RunId = otherId, AgentName = "a", StartedAt = DateTimeOffset.UtcNow });
        await runs.CompleteRunAsync(new RunCompletion
        {
            RunId = otherId,
            Status = RunStatus.Failed,
            CompletedAt = DateTimeOffset.UtcNow,
            Error = new RunError { Type = "upstream_error", Message = "provider error" },
        });

        using var filtered = await host.Client.GetAsync(
            new Uri("/tracon/api/runs?errorType=content_blocked", UriKind.Relative));

        (await IdsAsync(filtered)).ShouldBe([blockedId.ToString()]);
    }

    [Fact]
    public async Task Tree_of_a_nonexistent_run_returns_404()
    {
        await using var host = await TraconTestHost.StartAsync();

        using var response = await host.Client.GetAsync(
            new Uri($"/tracon/api/runs/{Guid.Empty}/tree", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Root_record_carries_child_run_count_and_tree_totals()
    {
        await using var host = await TraconTestHost.StartAsync();

        var (rootId, _) = await RecordTreeAsync(host);

        using var response = await host.Client.GetAsync(
            new Uri($"/tracon/api/runs/{rootId}", UriKind.Relative));

        var json = await TraconTestHost.ReadJsonAsync(response);

        json.GetProperty("childRunCount").GetInt32().ShouldBe(1);
        json.GetProperty("depth").GetInt32().ShouldBe(0);

        // The tree total also includes the root's own usage; the two are not added together.
        json.GetProperty("usage").GetProperty("totalTokens").GetInt64().ShouldBe(10);
        json.GetProperty("treeUsage").GetProperty("totalTokens").GetInt64().ShouldBe(30);
    }

    /// <summary>Writes a two-row run tree directly to the store.</summary>
    /// <remarks>
    /// No actual model call is made: what these tests verify is the HTTP
    /// surface, not the child agent call itself. The call path is tested
    /// in <c>ChildAgentInvokerTests</c>.
    /// </remarks>
    private static async Task<(Guid RootId, Guid ChildId)> RecordTreeAsync(TraconTestHost host)
    {
        var runs = host.Services.GetRequiredService<IRunStore>();
        var rootId = TraconId.NewId();
        var childId = TraconId.NewId();

        await runs.StartRunAsync(new RunStartInfo
        {
            RunId = rootId,
            AgentName = "yonlendirici",
            StartedAt = DateTimeOffset.UtcNow,
        });

        await runs.StartRunAsync(new RunStartInfo
        {
            RunId = childId,
            AgentName = "arastirmaci",
            StartedAt = DateTimeOffset.UtcNow,
            ParentRunId = rootId,
            RootRunId = rootId,
            Depth = 1,
        });

        await CompleteAsync(runs, rootId, 10);
        await CompleteAsync(runs, childId, 20);

        return (rootId, childId);
    }

    private static ValueTask CompleteAsync(IRunStore runs, Guid runId, long tokens)
        => runs.CompleteRunAsync(new RunCompletion
        {
            RunId = runId,
            Status = RunStatus.Completed,
            CompletedAt = DateTimeOffset.UtcNow,
            Usage = new RunUsage { InputTokens = tokens, OutputTokens = 0, TotalTokens = tokens },
        });

    private static async Task<List<string>> IdsAsync(HttpResponseMessage response)
    {
        var json = await TraconTestHost.ReadJsonAsync(response);

        return [.. json.EnumerateArray().Select(static run => run.GetProperty("id").GetString() ?? string.Empty)];
    }

    private static string Detail(System.Text.Json.JsonElement problem)
        => problem.GetProperty("detail").GetString() ?? string.Empty;

    private static async Task CreateAsync(TraconTestHost host, string name)
    {
        using var response = await host.Client.PostAsJsonAsync(Agents, TestData.Request(name: name));

        response.EnsureSuccessStatusCode();
    }

    private static async Task UpdateAsync(TraconTestHost host, string name, string[] callable)
    {
        using var response = await host.Client.PutAsJsonAsync(
            new Uri($"/tracon/api/agents/{name}", UriKind.Relative),
            TestData.Request(name: name) with { CallableAgentNames = callable });

        response.EnsureSuccessStatusCode();
    }
}
