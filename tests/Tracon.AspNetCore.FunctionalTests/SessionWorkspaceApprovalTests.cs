using System.Net;
using System.Net.Http.Json;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;
// Same rationale as AgentExtensionRunTests.cs: the package's Tracon.Testing.TraconTestHost and
// this project's own TraconTestHost share a name.
using FakeModelProvider = Tracon.Testing.FakeModelProvider;

// MAAI001: AgentFileStore and FileAccessProvider are marked "evaluation purposes only" - the same
// rationale as AgentDefinitionCompiler.Memory.cs.
#pragma warning disable MAAI001

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// The approval of the file tools of a session workspace. A tool that changes a file always
/// waits for a person; a tool that reads waits unless the definition turns that off.
/// </summary>
/// <remarks>
/// The approval travels the whole path a run takes: the definition, the compiled agent, the model request, the
/// pending approval row, the decision, and the run that carries it out in the same session. The file is the
/// witness: it is in the store only after the decision.
/// </remarks>
public sealed class SessionWorkspaceApprovalTests
{
    private const string ProviderName = "approval-files-model";

    [Fact]
    public async Task A_write_waits_for_approval_and_the_file_exists_only_after_the_decision()
    {
        var provider = new FakeModelProvider(ProviderName)
            .CallsTool("file_access_write", new { fileName = "notes.md", content = "- first\n- second\n- third" })
            .RespondsWith("Saved.")
            .EchoesUserMessage();

        await using var host = await StartAsync(provider, new WorkspaceSettings { Enabled = true, DisableReadApproval = true });
        var store = host.Services.GetRequiredService<AgentFileStore>();
        var path = SessionWorkspacePaths.AgentRoot("default", "session-1", "drafter") + "/notes.md";

        var originalRunId = await QueueAsync(host, "session-1");
        (await host.WaitForRunStatusAsync(originalRunId, "AwaitingApproval")).ShouldBe("AwaitingApproval");

        var pending = await PendingAsync(host);
        pending.GetProperty("toolName").GetString().ShouldBe("file_access_write");
        (await store.ReadAsync(path)).ShouldBeNull("the write has not run: it waits for a person");

        await DecideAsync(host, pending, approved: true);
        await WaitForResumedRunAsync(host, "session-1", originalRunId);

        (await store.ReadAsync(path)).ShouldBe("- first\n- second\n- third");
    }

    [Fact]
    public async Task A_declined_write_leaves_no_file()
    {
        var provider = new FakeModelProvider(ProviderName)
            .CallsTool("file_access_write", new { fileName = "notes.md", content = "- first" })
            .RespondsWith("Understood.")
            .EchoesUserMessage();

        await using var host = await StartAsync(provider, new WorkspaceSettings { Enabled = true, DisableReadApproval = true });
        var store = host.Services.GetRequiredService<AgentFileStore>();

        var originalRunId = await QueueAsync(host, "session-1");
        await host.WaitForRunStatusAsync(originalRunId, "AwaitingApproval");

        await DecideAsync(host, await PendingAsync(host), approved: false);
        await WaitForResumedRunAsync(host, "session-1", originalRunId);

        (await store.ReadAsync(SessionWorkspacePaths.AgentRoot("default", "session-1", "drafter") + "/notes.md")).ShouldBeNull();
    }

    [Fact]
    public async Task A_delete_waits_for_approval_even_when_the_definition_turns_the_read_approval_off()
    {
        var provider = new FakeModelProvider(ProviderName)
            .CallsTool("file_access_delete", new { fileName = "notes.md" })
            .RespondsWith("Deleted.")
            .EchoesUserMessage();

        await using var host = await StartAsync(provider, new WorkspaceSettings { Enabled = true, DisableReadApproval = true });
        var store = host.Services.GetRequiredService<AgentFileStore>();
        var path = SessionWorkspacePaths.AgentRoot("default", "session-1", "drafter") + "/notes.md";
        await store.WriteAsync(path, "kept");

        var originalRunId = await QueueAsync(host, "session-1");
        await host.WaitForRunStatusAsync(originalRunId, "AwaitingApproval");

        (await PendingAsync(host)).GetProperty("toolName").GetString().ShouldBe("file_access_delete");
        (await store.ReadAsync(path)).ShouldBe("kept");

        await DecideAsync(host, await PendingAsync(host), approved: true);
        await WaitForResumedRunAsync(host, "session-1", originalRunId);

        (await store.ReadAsync(path)).ShouldBeNull();
    }

    [Fact]
    public async Task A_read_waits_for_approval_by_default_and_reads_after_it()
    {
        var provider = new FakeModelProvider(ProviderName)
            .CallsTool("file_access_read", new { fileName = "notes.md" })
            .EchoesLastToolResult("Read: ");

        await using var host = await StartAsync(provider, new WorkspaceSettings { Enabled = true });
        await host.Services.GetRequiredService<AgentFileStore>()
            .WriteAsync(SessionWorkspacePaths.AgentRoot("default", "session-1", "drafter") + "/notes.md", "kept in the session");

        var originalRunId = await QueueAsync(host, "session-1");
        await host.WaitForRunStatusAsync(originalRunId, "AwaitingApproval");

        (await PendingAsync(host)).GetProperty("toolName").GetString().ShouldBe("file_access_read");

        await DecideAsync(host, await PendingAsync(host), approved: true);
        await WaitForResumedRunAsync(host, "session-1", originalRunId);

        // The model reads the file as the result of the call that was approved.
        string.Join(
            "\n",
            provider.Requests[^1].Messages
                .SelectMany(static message => message.Contents)
                .OfType<FunctionResultContent>()
                .Select(static result => result.Result?.ToString())).ShouldContain("kept in the session");
    }

    [Fact]
    public async Task The_definition_turns_the_read_approval_off_and_a_read_runs_at_once()
    {
        var provider = new FakeModelProvider(ProviderName)
            .CallsTool("file_access_read", new { fileName = "notes.md" })
            .EchoesLastToolResult("Read: ");

        await using var host = await StartAsync(provider, new WorkspaceSettings { Enabled = true, DisableReadApproval = true });
        await host.Services.GetRequiredService<AgentFileStore>()
            .WriteAsync(SessionWorkspacePaths.AgentRoot("default", "session-1", "drafter") + "/notes.md", "kept in the session");

        var runId = await QueueAsync(host, "session-1");

        (await host.WaitForRunStatusAsync(runId, "Completed")).ShouldBe("Completed");
    }

    [Fact]
    public async Task The_model_is_offered_the_write_tools_with_approval_and_the_read_tools_with_approval_only_when_the_definition_asks()
    {
        var asking = new FakeModelProvider(ProviderName).EchoesUserMessage();
        var open = new FakeModelProvider(ProviderName).EchoesUserMessage();

        await using (var host = await StartAsync(asking, new WorkspaceSettings { Enabled = true }))
        {
            await RunAsync(host, "session-1");

            Approvals(asking).ShouldBe(
            [
                ("file_access_delete", true),
                ("file_access_grep", true),
                ("file_access_ls", true),
                ("file_access_read", true),
                ("file_access_read_lines", true),
                ("file_access_replace", true),
                ("file_access_replace_lines", true),
                ("file_access_write", true),
            ]);
        }

        await using (var host = await StartAsync(open, new WorkspaceSettings { Enabled = true, DisableReadApproval = true }))
        {
            await RunAsync(host, "session-1");

            Approvals(open).ShouldBe(
            [
                ("file_access_delete", true),
                ("file_access_grep", false),
                ("file_access_ls", false),
                ("file_access_read", false),
                ("file_access_read_lines", false),
                ("file_access_replace", true),
                ("file_access_replace_lines", true),
                ("file_access_write", true),
            ]);
        }
    }

    private static List<(string Name, bool AsksForApproval)> Approvals(FakeModelProvider provider)
        => (provider.Requests[0].Options?.Tools ?? [])
            .Where(static tool => tool.Name.StartsWith("file_access_", StringComparison.Ordinal))
            .Select(static tool => (tool.Name, tool.GetService<ApprovalRequiredAIFunction>() is not null))
            .OrderBy(static tool => tool.Name, StringComparer.Ordinal)
            .ToList();

    private static Task<TraconTestHost> StartAsync(FakeModelProvider provider, WorkspaceSettings workspace)
        => TraconTestHost.StartAsync(
            builder =>
            {
                builder
                .AddModelProvider(provider)
                .AddAgent(new AgentDefinition
                {
                    Name = "drafter",
                    Instructions = "Work with files.",
                    Model = new ModelBinding { Provider = ProviderName, Model = "files-1" },
                    Workspace = workspace,
                    Origin = AgentDefinitionOrigin.Code,
                });
                builder.UseScheduling(static options => options.PollInterval = TimeSpan.FromMilliseconds(20));
            });

    private static async Task RunAsync(TraconTestHost host, string sessionId)
    {
        using var response = await host.Client.PostAsJsonAsync(
            new Uri("/tracon/api/agents/drafter/run", UriKind.Relative), new AgentRunRequest { Message = "work", SessionId = sessionId });
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    internal static async Task<Guid> QueueAsync(TraconTestHost host, string sessionId)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri("/tracon/api/agents/drafter/run", UriKind.Relative))
        {
            Content = JsonContent.Create(new AgentRunRequest { Message = "work", SessionId = sessionId }),
        };
        request.Headers.Add("Prefer", "respond-async");

        using var accepted = await host.Client.SendAsync(request);
        accepted.StatusCode.ShouldBe(HttpStatusCode.Accepted);

        return (await TraconTestHost.ReadJsonAsync(accepted)).GetProperty("runId").GetGuid();
    }

    internal static async Task<System.Text.Json.JsonElement> PendingAsync(TraconTestHost host)
    {
        using var response = await host.Client.GetAsync(new Uri("/tracon/api/approvals/pending", UriKind.Relative));

        return (await TraconTestHost.ReadJsonAsync(response)).EnumerateArray().ShouldHaveSingleItem().Clone();
    }

    internal static async Task DecideAsync(TraconTestHost host, System.Text.Json.JsonElement pending, bool approved)
    {
        using var response = await host.Client.PostAsJsonAsync(
            new Uri($"/tracon/api/approvals/{pending.GetProperty("id").GetGuid()}/decide", UriKind.Relative),
            new ApprovalDecisionRequest { Approved = approved });
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    /// <summary>The run that carries a decision out is a new run of the same session; waits until it ends.</summary>
    internal static async Task<Guid> WaitForResumedRunAsync(TraconTestHost host, string sessionId, Guid originalRunId)
    {
        using var runs = await host.Client.GetAsync(new Uri($"/tracon/api/runs?sessionId={sessionId}", UriKind.Relative));
        var resumed = (await TraconTestHost.ReadJsonAsync(runs)).EnumerateArray()
            .Select(static run => run.GetProperty("id").GetGuid())
            .Single(id => id != originalRunId);

        (await host.WaitForRunStatusAsync(resumed, "Completed")).ShouldBe("Completed");

        return resumed;
    }
}
