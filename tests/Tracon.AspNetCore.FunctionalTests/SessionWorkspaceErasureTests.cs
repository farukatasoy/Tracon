using System.Net;
using Microsoft.Agents.AI;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;
// Same rationale as AgentExtensionRunTests.cs (K-269): the package's Tracon.Testing.TraconTestHost and
// this project's own TraconTestHost share a name.
using FakeModelProvider = Tracon.Testing.FakeModelProvider;

// MAAI001: AgentFileStore is marked "evaluation purposes only" - the same rationale as AgentDefinitionCompiler.Memory.cs.
#pragma warning disable MAAI001

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// Phase 234 (F-628): over a SQL host, the files an agent wrote in a session go when the session goes, and only
/// those.
/// </summary>
/// <remarks>
/// The SQL stores take the content erasers from the container (<c>ActivatorUtilities</c>); a test that builds them by
/// hand cannot see a constructor that would leave them out. The data subject erasure and the retention target over SQLite
/// are driven through the store interfaces in <c>Tracon.Sqlite.IntegrationTests</c>, because the HTTP erasure ends in an audit
/// write that hangs over SQLite for a reason of its own (F-644).
/// </remarks>
public sealed class SessionWorkspaceErasureTests
{
    private const string ProviderName = "erasure-files-model";

    [Fact]
    public async Task Deleting_a_session_over_SQLite_deletes_the_files_the_agent_wrote_in_it_after_the_approval_and_leaves_another_session_alone()
    {
        using var database = new TempSqliteDatabase("workspace-erasure");
        var provider = new FakeModelProvider(ProviderName)
            .CallsTool("file_access_write", new { fileName = "notes.md", content = "- first" })
            .RespondsWith("Saved.")
            .CallsTool("file_access_write", new { fileName = "notes.md", content = "- other session" })
            .RespondsWith("Saved.")
            .EchoesUserMessage();

        await using var host = await TraconTestHost.StartAsync(
            builder =>
            {
                builder.UseSqlite(database.ConnectionString);
                builder.AddModelProvider(provider);
                builder.AddAgent(new AgentDefinition
                {
                    Name = "drafter",
                    Instructions = "Work with files.",
                    Model = new ModelBinding { Provider = ProviderName, Model = "files-1" },
                    Workspace = new WorkspaceSettings { Enabled = true },
                    Origin = AgentDefinitionOrigin.Code,
                });
            },
            configureServices: static services => services.UseScheduling(static options => options.PollInterval = TimeSpan.FromMilliseconds(20)));
        var tree = (IAgentFileTree)host.Services.GetRequiredService<AgentFileStore>();

        foreach (var session in new[] { "session-1", "session-2" })
        {
            var queued = await SessionWorkspaceApprovalTests.QueueAsync(host, session);
            await host.WaitForRunStatusAsync(queued, "AwaitingApproval");
            await SessionWorkspaceApprovalTests.DecideAsync(host, await SessionWorkspaceApprovalTests.PendingAsync(host), approved: true);
            await SessionWorkspaceApprovalTests.WaitForResumedRunAsync(host, session, queued);
        }

        (await tree.MeasureAsync("default", SessionWorkspacePaths.SessionRoot("default", "session-1"), CancellationToken.None)).Files.ShouldBe(1);
        (await tree.MeasureAsync("default", SessionWorkspacePaths.SessionRoot("default", "session-2"), CancellationToken.None)).Files.ShouldBe(1);

        using var deleted = await host.Client.DeleteAsync(new Uri("/tracon/api/sessions/session-1", UriKind.Relative));
        deleted.StatusCode.ShouldBe(HttpStatusCode.NoContent, await deleted.Content.ReadAsStringAsync());

        (await tree.MeasureAsync("default", SessionWorkspacePaths.SessionRoot("default", "session-1"), CancellationToken.None)).Files.ShouldBe(0);
        (await tree.MeasureAsync("default", SessionWorkspacePaths.SessionRoot("default", "session-2"), CancellationToken.None)).Files.ShouldBe(1, "another session keeps its files");
    }

    [Fact]
    public async Task Deleting_a_session_that_does_not_exist_deletes_no_files()
    {
        using var database = new TempSqliteDatabase("workspace-erasure-missing");

        await using var host = await TraconTestHost.StartAsync(
            builder =>
            {
                builder.UseSqlite(database.ConnectionString);
                builder.AddModelProvider(new FakeModelProvider(ProviderName).EchoesUserMessage());
            });
        var store = host.Services.GetRequiredService<AgentFileStore>();
        var tree = (IAgentFileTree)store;

        // Files under a session key that no session row owns (a store the host filled by hand).
        using var scope = new TestRunScope("drafter");
        await store.WriteAsync(SessionWorkspacePaths.AgentRoot("default", "no-such-session", "drafter") + "/notes.md", "kept");

        using var response = await host.Client.DeleteAsync(new Uri("/tracon/api/sessions/no-such-session", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await tree.MeasureAsync("default", SessionWorkspacePaths.SessionRoot("default", "no-such-session"), CancellationToken.None)).Files.ShouldBe(1);
    }

    /// <summary>Sets the scope of a run of one agent, as the run does, and clears it.</summary>
    private sealed class TestRunScope : IDisposable
    {
        public TestRunScope(string agent)
            => TraconRunContext.SetCurrent(new AgentRunScope { RunId = Guid.NewGuid(), RootRunId = Guid.NewGuid(), AgentName = agent });

        public void Dispose() => TraconRunContext.SetCurrent(null);
    }
}
