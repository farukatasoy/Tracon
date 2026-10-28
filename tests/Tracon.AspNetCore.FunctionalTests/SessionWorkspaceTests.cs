using System.Net;
using System.Net.Http.Json;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;
// Same rationale as AgentExtensionRunTests.cs (K-269): the package's Tracon.Testing.TraconTestHost and
// this project's own TraconTestHost share a name.
using FakeModelProvider = Tracon.Testing.FakeModelProvider;
using FakeModelRequest = Tracon.Testing.FakeModelRequest;

// MAAI001: AgentFileStore and FileAccessProvider are marked "evaluation purposes only" - the same
// rationale as AgentDefinitionCompiler.Memory.cs.
#pragma warning disable MAAI001

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// Phase 234 (F-628): the session workspace through a real host. The file tools the model gets read and
/// write one session's directory of the agent file store, for that agent, that session and that tenant only.
/// </summary>
/// <remarks>
/// Each question crosses the DI boundary (the file store is the host's registration), the HTTP boundary (the
/// tenant comes from the request), the tenant boundary, the stream (the run's scope is an <c>AsyncLocal</c> the
/// store reads on every call) or the store boundary; a store unit test answers none of them. The files are
/// seeded straight into the store where a test is about reading: the approval a write needs is the subject of
/// <see cref="SessionWorkspaceApprovalTests"/>.
/// </remarks>
public sealed class SessionWorkspaceTests
{
    private const string ProviderName = "files-model";
    private const string TenantHeader = "X-Tracon-Tenant";

    [Fact]
    public async Task A_file_in_one_session_is_listed_in_that_session_and_not_in_another_session_of_the_same_agent()
    {
        var provider = Script(
            Call("file_access_ls", new { }), "done-1",
            Call("file_access_ls", new { }), "done-2");

        await using var host = await StartAsync(provider);
        await SeedAsync(host, "default", "session-1", "drafter", "notes.md", "alpha");

        await RunAsync(host, "session-1");
        await RunAsync(host, "session-2");

        ToolResult(provider, 1).ShouldContain("notes.md");
        ToolResult(provider, 3).ShouldNotContain("notes.md", customMessage: "a second session starts with an empty workspace");
    }

    [Fact]
    public async Task A_file_of_one_tenant_is_not_seen_by_another_tenant_that_uses_the_same_session_id_and_agent()
    {
        var provider = Script(
            Call("file_access_ls", new { }), "done-1",
            Call("file_access_ls", new { }), "done-2");

        await using var host = await StartAsync(provider, tenancy: true);
        await SeedAsync(host, "tenant-a", "shared-session", "drafter", "secret.md", "alpha");

        await RunAsync(host, "shared-session", tenant: "tenant-b");
        await RunAsync(host, "shared-session", tenant: "tenant-a");

        ToolResult(provider, 1).ShouldNotContain("secret.md");
        ToolResult(provider, 3).ShouldContain("secret.md");
    }

    [Fact]
    public async Task Another_agent_in_the_same_session_has_a_workspace_of_its_own()
    {
        var provider = Script(
            Call("file_access_ls", new { }), "done-1");

        await using var host = await StartAsync(provider, extraAgent: "reviewer");
        await SeedAsync(host, "default", "session-1", "drafter", "notes.md", "alpha");

        await RunAsync(host, "session-1", agent: "reviewer");

        ToolResult(provider, 1).ShouldNotContain("notes.md");
    }

    [Fact]
    public async Task The_workspace_is_not_the_file_memory_of_the_agent_and_the_file_memory_is_not_the_workspace()
    {
        var provider = Script(
            Call("file_access_grep", new { regexPattern = "alpha|omega" }), "done-1");
        var agent = Agent("drafter", new WorkspaceSettings { Enabled = true, DisableReadApproval = true }, new MemorySettings { EnableFileMemory = true, EnableTextSearch = true });

        await using var host = await StartAsync(provider, agent: agent);
        var store = host.Services.GetRequiredService<AgentFileStore>();

        // The file memory of the agent is the directory "{tenant}/{agent}"; the workspace is not below it.
        await store.WriteAsync("default/drafter/memory.md", "omega kept by the agent");
        await SeedAsync(host, "default", "session-1", "drafter", "notes.md", "alpha kept in the session");

        await RunAsync(host, "session-1");

        var found = ToolResult(provider, 1);
        found.ShouldContain("notes.md");
        found.ShouldNotContain("memory.md", customMessage: "a search in the workspace does not reach the file memory");
        found.ShouldNotContain("default/", customMessage: "the model never sees the path in the store");
        found.ShouldNotContain("~workspace");

        // And the other way: the text search of the file memory finds the memory file, not the session file.
        var searched = await store.SearchAsync("default/drafter", "alpha|omega", globPattern: null, recursive: true);
        searched.Select(static match => match.FileName).ShouldBe(["memory.md"]);
    }

    [Fact]
    public async Task A_run_that_names_no_session_is_offered_no_file_tool()
    {
        var provider = Script();

        await using var host = await StartAsync(provider);

        using var response = await host.Client.PostAsJsonAsync(
            new Uri("/tracon/api/agents/drafter/run", UriKind.Relative), new AgentRunRequest { Message = "hello" });
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        ToolNames(provider.Requests[0]).ShouldNotContain(static name => name.StartsWith("file_access_", StringComparison.Ordinal));
        provider.Requests[0].Messages.Select(static message => message.Text).ShouldNotContain(static text => text.Contains("Session workspace", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_run_with_a_session_is_offered_the_eight_file_tools_and_their_instructions()
    {
        var provider = Script();

        await using var host = await StartAsync(provider);

        await RunAsync(host, "session-1");

        ToolNames(provider.Requests[0]).Where(static name => name.StartsWith("file_access_", StringComparison.Ordinal)).Order(StringComparer.Ordinal).ShouldBe(
        [
            "file_access_delete",
            "file_access_grep",
            "file_access_ls",
            "file_access_read",
            "file_access_read_lines",
            "file_access_replace",
            "file_access_replace_lines",
            "file_access_write",
        ]);
    }

    [Fact]
    public async Task A_read_only_workspace_offers_the_four_tools_that_read()
    {
        var provider = Script();
        var agent = Agent("drafter", new WorkspaceSettings { Enabled = true, ReadOnly = true });

        await using var host = await StartAsync(provider, agent: agent);

        await RunAsync(host, "session-1");

        ToolNames(provider.Requests[0]).Where(static name => name.StartsWith("file_access_", StringComparison.Ordinal)).Order(StringComparer.Ordinal).ShouldBe(
        [
            "file_access_grep",
            "file_access_ls",
            "file_access_read",
            "file_access_read_lines",
        ]);
    }

    [Fact]
    public async Task An_agent_that_has_not_turned_the_workspace_on_is_offered_nothing()
    {
        var provider = Script();
        var agent = Agent("drafter", workspace: null);

        await using var host = await StartAsync(provider, agent: agent);

        await RunAsync(host, "session-1");

        ToolNames(provider.Requests[0]).ShouldNotContain(static name => name.StartsWith("file_access_", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_harness_agent_gets_the_workspace_once_and_not_a_second_set_of_file_tools_from_the_harness()
    {
        var provider = Script();
        var agent = Agent("drafter", new WorkspaceSettings { Enabled = true }, harness: new HarnessSettings());

        await using var host = await StartAsync(provider, agent: agent);

        await RunAsync(host, "session-1");

        var names = ToolNames(provider.Requests[0]).Where(static name => name.StartsWith("file_access_", StringComparison.Ordinal)).ToList();
        names.Count.ShouldBe(8, "the harness builds its own file tools only when FileAccessStore is assigned, and Tracon never assigns it");
        names.Distinct(StringComparer.Ordinal).Count().ShouldBe(8);
    }

    [Fact]
    public async Task A_path_that_leaves_the_workspace_reads_nothing()
    {
        var provider = Script(
            Call("file_access_read", new { fileName = "../other-session/notes.md" }), "done-1",
            Call("file_access_read", new { fileName = "/default/drafter/memory.md" }), "done-2");
        var agent = Agent("drafter", new WorkspaceSettings { Enabled = true, DisableReadApproval = true });

        await using var host = await StartAsync(provider, agent: agent);
        var store = host.Services.GetRequiredService<AgentFileStore>();
        await store.WriteAsync("default/drafter/memory.md", "kept by the agent");
        await SeedAsync(host, "default", "other-session", "drafter", "notes.md", "someone else's notes");

        await RunAsync(host, "session-1");
        await RunAsync(host, "session-1");

        // The run goes on: the model gets an error as the result of the call, and no content.
        var results = string.Join("\n", ToolResult(provider, 1), ToolResult(provider, 3));
        results.ShouldNotContain("someone else's notes");
        results.ShouldNotContain("kept by the agent");
    }

    [Fact]
    public async Task A_file_tool_is_denied_by_the_authorization_handler_like_any_other_tool()
    {
        var provider = Script(
            Call("file_access_ls", new { }), "done-1");
        var agent = Agent("drafter", new WorkspaceSettings { Enabled = true, DisableReadApproval = true });
        var handler = new DenyingHandler();

        await using var host = await StartAsync(provider, agent: agent, configureServices: services => services.AddSingleton<IToolAuthorizationHandler>(handler));
        await SeedAsync(host, "default", "session-1", "drafter", "notes.md", "alpha");

        await RunAsync(host, "session-1");

        handler.Seen.ShouldBe([("file_access_ls", ToolEffect.Read)]);
        ToolResult(provider, 1).ShouldNotContain("notes.md");
    }

    [Fact]
    public async Task A_multi_tenant_host_that_keeps_the_files_in_memory_is_warned_once_the_agent_is_compiled()
    {
        var provider = Script();

        await using var host = await StartAsync(provider, tenancy: true);

        await RunAsync(host, "session-1", tenant: "tenant-a");

        host.Logs.Entries.Count(static entry => entry.Contains("in-memory one", StringComparison.Ordinal) && entry.Contains("drafter", StringComparison.Ordinal))
            .ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task A_single_tenant_host_that_keeps_the_files_in_memory_is_not_warned()
    {
        var provider = Script();

        await using var host = await StartAsync(provider);

        await RunAsync(host, "session-1");

        host.Logs.Entries.Any(static entry => entry.Contains("in-memory one", StringComparison.Ordinal)).ShouldBeFalse();
    }

    private static AgentDefinition Agent(string name, WorkspaceSettings? workspace, MemorySettings? memory = null, HarnessSettings? harness = null)
        => new()
        {
            Name = name,
            Instructions = "Work with files.",
            Model = new ModelBinding { Provider = ProviderName, Model = "files-1" },
            Workspace = workspace,
            Memory = memory,
            Harness = harness,
            Origin = AgentDefinitionOrigin.Code,
        };

    private static ToolCall Call(string tool, object arguments) => new(tool, arguments);

    /// <summary>
    /// A model that makes each tool call in turn and answers with the text that follows it; a call with no
    /// following text ends the script.
    /// </summary>
    private static FakeModelProvider Script(params object[] steps)
    {
        var provider = new FakeModelProvider(ProviderName);

        foreach (var step in steps)
        {
            switch (step)
            {
                case ToolCall call:
                    provider.CallsTool(call.Tool, call.Arguments);
                    break;
                case string text:
                    provider.RespondsWith(text);
                    break;
            }
        }

        return provider.EchoesUserMessage();
    }

    private static async Task<TraconTestHost> StartAsync(
        FakeModelProvider provider,
        AgentDefinition? agent = null,
        string? extraAgent = null,
        bool tenancy = false,
        Action<IServiceCollection>? configureServices = null)
        => await TraconTestHost.StartAsync(
            builder =>
            {
                if (tenancy)
                {
                    builder.UseTenancy(static options =>
                    {
                        options.Enabled = true;
                        options.AllowHeaderResolution = true;
                    });
                }

                builder.AddModelProvider(provider);
                builder.AddAgent(agent ?? Agent("drafter", new WorkspaceSettings { Enabled = true, DisableReadApproval = true }));

                if (extraAgent is not null)
                {
                    builder.AddAgent(Agent(extraAgent, new WorkspaceSettings { Enabled = true, DisableReadApproval = true }));
                }
                builder.UseScheduling(static options => options.PollInterval = TimeSpan.FromMilliseconds(20));
            },
            configureServices: services =>
            {
                configureServices?.Invoke(services);
            });

    private static async Task SeedAsync(TraconTestHost host, string tenant, string sessionId, string agent, string path, string content)
        => await host.Services.GetRequiredService<AgentFileStore>()
            .WriteAsync(SessionWorkspacePaths.AgentRoot(tenant, sessionId, agent) + "/" + path, content);

    private static async Task RunAsync(TraconTestHost host, string sessionId, string agent = "drafter", string? tenant = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri($"/tracon/api/agents/{agent}/run", UriKind.Relative))
        {
            Content = JsonContent.Create(new AgentRunRequest { Message = "work", SessionId = sessionId }),
        };

        if (tenant is not null)
        {
            request.Headers.Add(TenantHeader, tenant);
        }

        using var response = await host.Client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, body);
    }

    /// <summary>The names of the tools a request offered the model.</summary>
    private static IEnumerable<string> ToolNames(FakeModelRequest request)
        => (request.Options?.Tools ?? []).Select(static tool => tool.Name);

    /// <summary>The text of every tool result the model had read when the request at <paramref name="index"/> reached it.</summary>
    private static string ToolResult(FakeModelProvider provider, int index)
        => string.Join(
            "\n",
            provider.Requests[index].Messages
                .SelectMany(static message => message.Contents)
                .OfType<FunctionResultContent>()
                .Select(static result => result.Result?.ToString() ?? string.Empty));

    private sealed record ToolCall(string Tool, object Arguments);

    /// <summary>Denies every call and remembers what it was asked.</summary>
    private sealed class DenyingHandler : IToolAuthorizationHandler
    {
        public List<(string Tool, ToolEffect Effect)> Seen { get; } = [];

        public ValueTask<ToolAuthorizationResult> AuthorizeAsync(ToolAuthorizationRequest request, CancellationToken cancellationToken = default)
        {
            Seen.Add((request.ToolName, request.Effect));

            return new(ToolAuthorizationResult.Deny("no files today"));
        }
    }
}
