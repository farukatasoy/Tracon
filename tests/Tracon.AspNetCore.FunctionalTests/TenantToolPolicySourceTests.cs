using System.Net;
using System.Net.Http.Json;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;
using FakeModelProvider = Tracon.Testing.FakeModelProvider;

// MAAI001: AgentFileStore is marked "evaluation purposes only" - the same rationale as ToolBudgetGateTests.
#pragma warning disable MAAI001

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// The tenant tool policy binds every source of tools, not only the ones in the registry: a tool of an MCP server,
/// the file tools of a session workspace and the knowledge search each reach the policy through the one tool chain.
/// </summary>
/// <remarks>
/// <para>
/// The tool chain is composed in three places (the registry, the MCP tenant tool set and the governance of the tools
/// Tracon adds to a run itself). A policy that only one of them passed would leave the other sources open for every
/// tenant, and nothing but a call through that source shows it. Each scenario saves a policy that lists some OTHER
/// tool, so the tool under test is refused by the policy alone, then lists the tool and runs it again: the control
/// that stops a source which can never be called from passing as "refused".
/// </para>
/// <para>
/// The registry's own tools are in <c>TenantToolPolicyTests</c>.
/// </para>
/// </remarks>
public sealed class TenantToolPolicySourceTests
{
    private const string Provider = "source-provider";
    private const string Model = "source-model";
    private const string Other = "some_other_tool";

    private const string McpServerName = "policy-srv";
    private const string McpTool = McpServerName + "_lookup";
    private const string McpAgent = "mcp-agent";
    private const string WorkspaceTool = "file_access_read";
    private const string WorkspaceAgent = "drafter";
    private const string KnowledgeTool = "search_knowledge";
    private const string KnowledgeAgent = "knowledge-agent";

    // --- An MCP tool ---

    [Fact]
    public async Task An_mcp_tool_the_policy_does_not_list_never_reaches_its_server()
    {
        await using var server = await LookupMcpServer.StartAsync("from-mcp");
        await using var host = await StartMcpAsync(server);
        var store = host.Services.GetRequiredService<ITenantToolPolicyStore>();

        await store.UpsertAsync("default", [Other], [], TestContext.Current.CancellationToken);

        var refused = await RunOverHttpAsync(host, McpAgent, "mcp-refused");

        refused.Status.ShouldBe("Completed", refused.Stream);
        server.Calls.ShouldBe(0, "the refused MCP tool reached its server");

        // The control: the same tool, listed by its name.
        await store.UpsertAsync("default", [McpTool], [], TestContext.Current.CancellationToken);

        var allowed = await RunOverHttpAsync(host, McpAgent, "mcp-allowed");

        allowed.Status.ShouldBe("Completed", allowed.Stream);
        server.Calls.ShouldBe(1);
    }

    // --- A session workspace tool ---

    [Fact]
    public async Task A_workspace_tool_the_policy_does_not_list_never_touches_the_store()
    {
        var files = new ReadCountingStore();
        await using var host = await StartWorkspaceAsync(files);
        var policies = host.Services.GetRequiredService<ITenantToolPolicyStore>();

        await policies.UpsertAsync("default", [Other], [], TestContext.Current.CancellationToken);

        var refused = await SessionWorkspaceApprovalTests.QueueAsync(host, "workspace-refused");

        (await host.WaitForRunStatusAsync(refused, "Completed")).ShouldBe("Completed");
        files.Reads.ShouldBe(0, "the refused workspace tool read the store");

        await policies.UpsertAsync("default", [WorkspaceTool], [], TestContext.Current.CancellationToken);

        var allowed = await SessionWorkspaceApprovalTests.QueueAsync(host, "workspace-allowed");

        (await host.WaitForRunStatusAsync(allowed, "Completed")).ShouldBe("Completed");
        files.Reads.ShouldBeGreaterThan(0);
    }

    // --- The knowledge search ---

    [Fact]
    public async Task A_knowledge_search_the_policy_does_not_list_neither_embeds_the_query_nor_reads_the_store()
    {
        // The search sends the text of the query to the embedding provider: a refusal that comes after the
        // embedding is text that left the process for a tool the tenant may not run.
        var embeddings = new CountingEmbeddings();
        var vectors = new CountingKnowledgeStore();
        await using var host = await StartKnowledgeAsync(embeddings, vectors);
        var policies = host.Services.GetRequiredService<ITenantToolPolicyStore>();

        await policies.UpsertAsync("default", [Other], [], TestContext.Current.CancellationToken);

        var refused = await RunOverHttpAsync(host, KnowledgeAgent, "knowledge-refused");

        refused.Status.ShouldBe("Completed", refused.Stream);
        embeddings.Calls.ShouldBe(0, "the refused search embedded its query");
        vectors.Searches.ShouldBe(0, "the refused search read the store");

        await policies.UpsertAsync("default", [KnowledgeTool], [], TestContext.Current.CancellationToken);

        var allowed = await RunOverHttpAsync(host, KnowledgeAgent, "knowledge-allowed");

        allowed.Status.ShouldBe("Completed", allowed.Stream);
        embeddings.Calls.ShouldBe(1);
        vectors.Searches.ShouldBe(1);
    }

    // --- Hosts ---

    private static FakeModelProvider Script(string tool, object? arguments, int runs)
        => new FakeModelProvider(Provider)
            .WithModel(new ModelDescriptor { Name = Model })
            .ForModel(Model, cfg =>
            {
                for (var run = 0; run < runs; run++)
                {
                    cfg.CallsTools((tool, arguments)).RespondsWith("Done");
                }
            });

    /// <summary>A host with one real MCP server whose tool the agent calls.</summary>
    private static async Task<TraconTestHost> StartMcpAsync(LookupMcpServer server)
    {
        var host = await TraconTestHost.StartAsync(
            builder =>
            {
                builder.UseMcp();
                builder.AddModelProvider(Script(McpTool, arguments: null, runs: 2));
            },
            configureServices: services => services.Configure<TraconEgressOptions>(
                static options => options.AllowPrivateNetworkTargets = true));

        try
        {
            using (var saved = await host.Client.PutAsJsonAsync(
                       new Uri($"/tracon/api/mcp-servers/{McpServerName}", UriKind.Relative),
                       new { endpoint = server.Endpoint.ToString(), requiresApproval = false }))
            {
                saved.StatusCode.ShouldBe(HttpStatusCode.OK, await saved.Content.ReadAsStringAsync());
            }

            using (var refreshed = await host.Client.PostAsync(new Uri("/tracon/api/mcp-servers/refresh", UriKind.Relative), content: null))
            {
                refreshed.StatusCode.ShouldBe(HttpStatusCode.OK, await refreshed.Content.ReadAsStringAsync());
            }

            using var agent = await host.Client.PostAsJsonAsync(
                new Uri("/tracon/api/agents", UriKind.Relative),
                new AgentDefinitionRequest
                {
                    Name = McpAgent,
                    Instructions = "Look things up.",
                    Model = new ModelBinding { Provider = Provider, Model = Model },
                    ToolNames = [McpTool],
                });

            agent.StatusCode.ShouldBe(HttpStatusCode.Created, await agent.Content.ReadAsStringAsync());

            return host;
        }
        catch
        {
            await host.DisposeAsync();

            throw;
        }
    }

    /// <summary>A host with a code agent that has a session workspace and reads a file.</summary>
    private static Task<TraconTestHost> StartWorkspaceAsync(AgentFileStore store)
        => TraconTestHost.StartAsync(
            builder =>
            {
                builder.AddModelProvider(Script(WorkspaceTool, new { fileName = "notes.md" }, runs: 2));
                builder.AddAgent(new AgentDefinition
                {
                    Name = WorkspaceAgent,
                    Instructions = "Work with files.",
                    Model = new ModelBinding { Provider = Provider, Model = Model },
                    Workspace = new WorkspaceSettings { Enabled = true, DisableReadApproval = true },
                    Origin = AgentDefinitionOrigin.Code,
                });
                builder.UseScheduling(static options => options.PollInterval = TimeSpan.FromMilliseconds(20));
            },
            configureServices: services => services.AddSingleton(store));

    /// <summary>A host with a code agent that searches the knowledge base.</summary>
    private static Task<TraconTestHost> StartKnowledgeAsync(CountingEmbeddings embeddings, CountingKnowledgeStore vectors)
        => TraconTestHost.StartAsync(
            builder =>
            {
                builder.AddModelProvider(Script(KnowledgeTool, new { query = "refund policy" }, runs: 2));
                builder.AddAgent(new AgentDefinition
                {
                    Name = KnowledgeAgent,
                    Instructions = "Answer from the knowledge base.",
                    Model = new ModelBinding { Provider = Provider, Model = Model },
                    Memory = new MemorySettings { EnableVectorSearch = true, VectorCollection = "docs" },
                    Origin = AgentDefinitionOrigin.Code,
                });
            },
            configureServices: services =>
            {
                services.Configure<TraconKnowledgeOptions>(static options => options.Dimensions = 3);
                services.AddSingleton<IVectorSearchStore>(vectors);
                services.AddSingleton<IEmbeddingGenerator<string, Embedding<float>>>(embeddings);
            });

    /// <summary>Runs a stored agent over HTTP and reads the run's status.</summary>
    private static async Task<(string Status, string Stream)> RunOverHttpAsync(TraconTestHost host, string agent, string session)
    {
        using var response = await host.Client.PostAsJsonAsync(
            new Uri($"/tracon/api/agents/{agent}/run", UriKind.Relative),
            new AgentRunRequest { Message = "go", SessionId = session });

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());

        var stream = await response.Content.ReadAsStringAsync();

        using var runs = await host.Client.GetAsync(new Uri($"/tracon/api/runs?sessionId={session}", UriKind.Relative));
        var status = (await TraconTestHost.ReadJsonAsync(runs)).EnumerateArray().ShouldHaveSingleItem()
            .GetProperty("status").GetString().ShouldNotBeNull();

        return (status, stream);
    }

    /// <summary>A file store that counts its reads and holds nothing.</summary>
    private sealed class ReadCountingStore : AgentFileStore
    {
        private int _reads;

        public int Reads => Volatile.Read(ref _reads);

        public override Task WriteAsync(string path, string content, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public override Task<string?> ReadAsync(string path, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _reads);

            return Task.FromResult<string?>(null);
        }

        public override Task<bool> DeleteAsync(string path, CancellationToken cancellationToken = default) => Task.FromResult(false);

        public override Task<bool> FileExistsAsync(string path, CancellationToken cancellationToken = default) => Task.FromResult(false);

        public override Task CreateDirectoryAsync(string path, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public override Task<IReadOnlyList<FileStoreEntry>> ListChildrenAsync(string directory, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<FileStoreEntry>>([]);

        public override Task<IReadOnlyList<FileSearchResult>> SearchAsync(
            string directory,
            string regexPattern,
            string? globPattern = null,
            bool recursive = false,
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<FileSearchResult>>([]);
    }
}
