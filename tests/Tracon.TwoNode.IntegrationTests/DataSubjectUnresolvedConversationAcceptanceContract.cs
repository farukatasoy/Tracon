using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;
using Tracon.TwoNode.IntegrationTests.Infrastructure;

namespace Tracon.TwoNode.IntegrationTests;

/// <summary>
/// A data subject export and erasure that meets a session whose agent is no longer registered finds the session's
/// conversation in the stored state, and counts the conversation it could not look up when the state does not allow that,
/// on a real database server.
/// </summary>
/// <remarks>
/// <para>
/// The first host has the agent and runs it, so the session and its conversation are rows of the server. The second
/// host is the same database without the agent. The erasure reads the conversation id from the stored state and deletes
/// the conversation rows with the session; a state of another shape leaves them, and the answer and the audit entry say so.
/// </para>
/// <para>
/// The store code does not change with the count, which is made before the store is called. What a server can still
/// get wrong is the rows (the session goes and the conversation goes with it, or stays), the state a server hands back
/// to the reader, and the audit entry written inside the erasure's transaction, which holds the count.
/// </para>
/// </remarks>
public abstract class DataSubjectUnresolvedConversationAcceptanceContract
{
    private const string Ghost = "ghost-agent";
    private const string SessionId = "ghost-session";

    /// <summary>Gets a schema no other test uses, on the class's server.</summary>
    /// <returns>The database the hosts connect to.</returns>
    protected abstract TwoNodeDatabase NewDatabase();

    /// <summary>The conversation of a session whose agent is gone is found in the stored state: the export lists it, the preview counts it and the erasure deletes it with the session.</summary>
    /// <returns>A task that completes when the scenario is done.</returns>
    [Fact]
    public async Task The_conversation_of_a_session_whose_agent_is_gone_is_found_in_the_stored_state_and_goes_with_the_session()
    {
        var database = NewDatabase();
        await SeedAsync(database);

        await using var host = await TraconTestHost.StartAsync(
            configureTracon: database.Use,
            configureServices: static services => services.AddSingleton<IDataSubjectResolver>(new OneSessionResolver()));

        using var export = await host.Client.GetAsync(new Uri("/tracon/api/data-subjects/subject/export", UriKind.Relative));
        export.StatusCode.ShouldBe(HttpStatusCode.OK, await export.Content.ReadAsStringAsync());
        export.Headers.GetValues("X-Tracon-Unresolved-Conversations").ShouldBe(["0"]);

        var preview = await DeleteAsync(host, "data-subjects/subject");
        preview.GetProperty("dryRun").GetBoolean().ShouldBeTrue();
        preview.GetProperty("unresolvedConversations").GetInt32().ShouldBe(0);
        preview.GetProperty("rowsByTarget").GetProperty("conversations").GetInt32().ShouldBe(1);
        (await database.CountRowsAsync("sessions")).ShouldBe(1, "a preview deletes nothing");

        var erased = await DeleteAsync(host, "data-subjects/subject?dryRun=false");
        erased.GetProperty("dryRun").GetBoolean().ShouldBeFalse();
        erased.GetProperty("unresolvedConversations").GetInt32().ShouldBe(0);

        (await database.CountRowsAsync("sessions")).ShouldBe(0);
        (await database.CountRowsAsync("conversations")).ShouldBe(0, "the id is in the stored state, so the agent that wrote it is not needed");
        (await database.CountRowsAsync("conversation_items")).ShouldBe(0);

        using var audit = await host.Client.GetAsync(new Uri("/tracon/api/audit?entity=data-subject:subject", UriKind.Relative));
        var entry = (await TraconTestHost.ReadJsonAsync(audit)).EnumerateArray().ShouldHaveSingleItem();
        entry.GetProperty("action").GetString().ShouldBe("data_subject.erase");

        using var after = JsonDocument.Parse(entry.GetProperty("after").GetString()!);
        after.RootElement.GetProperty("unresolvedConversations").GetInt32().ShouldBe(0);

        using var verify = await host.Client.GetAsync(new Uri("/tracon/api/audit/verify", UriKind.Relative));
        (await TraconTestHost.ReadJsonAsync(verify)).GetProperty("status").GetString().ShouldBe("Valid");
    }

    /// <summary>A state of another shape leaves the conversation unknown: the session goes, its conversation stays, and the export, the preview, the answer and the audit entry carry the count.</summary>
    /// <returns>A task that completes when the scenario is done.</returns>
    [Fact]
    public async Task A_state_of_another_shape_leaves_the_conversation_and_the_count_is_in_the_answer_and_the_audit_entry()
    {
        var database = NewDatabase();
        await SeedAsync(database);

        await using var host = await TraconTestHost.StartAsync(
            configureTracon: database.Use,
            configureServices: static services => services.AddSingleton<IDataSubjectResolver>(new OneSessionResolver()));

        var sessions = host.Services.GetRequiredService<ISessionStore>();
        var stored = (await sessions.GetAsync(SessionId)).ShouldNotBeNull();
        await sessions.SaveAsync(stored with { State = JsonDocument.Parse("{}").RootElement.Clone() });

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
        (await database.CountRowsAsync("conversations")).ShouldBe(1, "nothing says which conversation was the session's, so none is guessed");
        (await database.CountRowsAsync("conversation_items")).ShouldBeGreaterThan(0);

        using var audit = await host.Client.GetAsync(new Uri("/tracon/api/audit?entity=data-subject:subject", UriKind.Relative));
        var entry = (await TraconTestHost.ReadJsonAsync(audit)).EnumerateArray().ShouldHaveSingleItem();
        entry.GetProperty("action").GetString().ShouldBe("data_subject.erase");

        using var after = JsonDocument.Parse(entry.GetProperty("after").GetString()!);
        after.RootElement.GetProperty("unresolvedConversations").GetInt32().ShouldBe(1);

        using var verify = await host.Client.GetAsync(new Uri("/tracon/api/audit/verify", UriKind.Relative));
        (await TraconTestHost.ReadJsonAsync(verify)).GetProperty("status").GetString().ShouldBe("Valid");
    }

    /// <summary>Runs the agent on a first host, so that a session and its conversation are rows of the server, and stops that host.</summary>
    private static async Task SeedAsync(TwoNodeDatabase database)
    {
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
