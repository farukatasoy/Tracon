using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;
using Tracon.TwoNode.IntegrationTests.Infrastructure;

namespace Tracon.TwoNode.IntegrationTests;

/// <summary>
/// A data subject export and erasure that meets a session whose agent is no longer registered counts the conversation it
/// could not look up, on a real database server.
/// </summary>
/// <remarks>
/// <para>
/// The first host has the agent and runs it, so the session and its conversation are rows of the server. The second
/// host is the same database without the agent. The erasure deletes the session row and, because the conversation is
/// found through the session's agent, leaves the conversation rows; the answer and the audit entry say so.
/// </para>
/// <para>
/// The store code does not change with the count, which is made before the store is called. What a server can still
/// get wrong is the rows: the session goes, the conversation stays, and the audit entry written inside the erasure's
/// transaction holds the count.
/// </para>
/// </remarks>
public abstract class DataSubjectUnresolvedConversationAcceptanceContract
{
    private const string Ghost = "ghost-agent";
    private const string SessionId = "ghost-session";

    /// <summary>Gets a schema no other test uses, on the class's server.</summary>
    /// <returns>The database the hosts connect to.</returns>
    protected abstract TwoNodeDatabase NewDatabase();

    /// <summary>The session is erased, its conversation stays, and the export, the preview, the answer and the audit entry carry the count.</summary>
    /// <returns>A task that completes when the scenario is done.</returns>
    [Fact]
    public async Task The_session_goes_the_conversation_stays_and_the_count_is_in_the_answer_and_the_audit_entry()
    {
        var database = NewDatabase();

        await using (var first = await TraconTestHost.StartAsync(
            configureTracon: builder =>
            {
                database.Use(builder);
                builder.AddAgent(new AgentDefinition
                {
                    Name = Ghost,
                    Instructions = "Answer briefly.",
                    Model = new ModelBinding { Provider = "echo", Model = "echo-1" },
                    Origin = AgentDefinitionOrigin.Code,
                });
            }))
        {
            using var run = await first.Client.PostAsJsonAsync(
                new Uri($"/tracon/api/agents/{Ghost}/run", UriKind.Relative),
                new { message = "where is my order", sessionId = SessionId });

            run.StatusCode.ShouldBe(HttpStatusCode.OK, await run.Content.ReadAsStringAsync());
        }

        (await database.CountRowsAsync("sessions")).ShouldBe(1);
        (await database.CountRowsAsync("conversations")).ShouldBe(1);
        (await database.CountRowsAsync("conversation_items")).ShouldBeGreaterThan(0, "the run has to have written chat history");

        await using var host = await TraconTestHost.StartAsync(
            configureTracon: database.Use,
            configureServices: static services => services.AddSingleton<IDataSubjectResolver>(new OneSessionResolver()));

        using var export = await host.Client.GetAsync(new Uri("/tracon/api/data-subjects/subject/export", UriKind.Relative));
        export.StatusCode.ShouldBe(HttpStatusCode.OK, await export.Content.ReadAsStringAsync());
        export.Headers.GetValues("X-Tracon-Unresolved-Conversations").ShouldBe(["1"]);

        var preview = await DeleteAsync(host, "data-subjects/subject");
        preview.GetProperty("dryRun").GetBoolean().ShouldBeTrue();
        preview.GetProperty("unresolvedConversations").GetInt32().ShouldBe(1);
        (await database.CountRowsAsync("sessions")).ShouldBe(1, "a preview deletes nothing");

        var erased = await DeleteAsync(host, "data-subjects/subject?dryRun=false");
        erased.GetProperty("dryRun").GetBoolean().ShouldBeFalse();
        erased.GetProperty("unresolvedConversations").GetInt32().ShouldBe(1);

        (await database.CountRowsAsync("sessions")).ShouldBe(0);
        (await database.CountRowsAsync("conversations")).ShouldBe(1, "the agent is gone, so the conversation cannot be found");
        (await database.CountRowsAsync("conversation_items")).ShouldBeGreaterThan(0);

        using var audit = await host.Client.GetAsync(new Uri("/tracon/api/audit?entity=data-subject:subject", UriKind.Relative));
        var entry = (await TraconTestHost.ReadJsonAsync(audit)).EnumerateArray().ShouldHaveSingleItem();
        entry.GetProperty("action").GetString().ShouldBe("data_subject.erase");

        using var after = JsonDocument.Parse(entry.GetProperty("after").GetString()!);
        after.RootElement.GetProperty("unresolvedConversations").GetInt32().ShouldBe(1);

        using var verify = await host.Client.GetAsync(new Uri("/tracon/api/audit/verify", UriKind.Relative));
        (await TraconTestHost.ReadJsonAsync(verify)).GetProperty("status").GetString().ShouldBe("Valid");
    }

    private static async Task<JsonElement> DeleteAsync(TraconTestHost host, string path)
    {
        using var response = await host.Client.DeleteAsync(new Uri($"/tracon/api/{path}", UriKind.Relative));
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());

        return await TraconTestHost.ReadJsonAsync(response);
    }

    private sealed class OneSessionResolver : IDataSubjectResolver
    {
        public ValueTask<DataSubjectScope> ResolveAsync(string subjectId, string tenantId, CancellationToken cancellationToken = default)
            => new(new DataSubjectScope { SessionIds = [SessionId], RunIds = [], ConversationIds = [] });
    }
}
