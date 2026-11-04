using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;
using Tracon.TwoNode.IntegrationTests.Infrastructure;

namespace Tracon.TwoNode.IntegrationTests;

/// <summary>
/// An operation on a resource with the longest name the API accepts leaves its audit entry, on a real database server.
/// </summary>
/// <remarks>
/// <para>
/// The entity of an audit entry is a prefix and the name the caller chose: deleting the session a client opened under a
/// 200-character id writes <c>session:</c> and those 200 characters. SQL Server holds an entity in 200 characters and
/// refused the write; the recorder logs a failed write and carries on, because observability must not break the
/// operation, so the session was deleted and the trail said nothing about it. The longest name the API accepts is the
/// case that exposed it; the store now shortens a label it cannot hold instead of refusing the entry.
/// </para>
/// </remarks>
public abstract class AuditLabelLimitAcceptanceContract
{
    private const string Action = "session.delete";

    private static readonly string LongSessionId = new('s', 200);

    private static Uri Api(string path) => new($"/tracon/api/{path}", UriKind.Relative);

    /// <summary>Gets a schema no other test uses, on the class's server.</summary>
    /// <returns>The database the host connects to.</returns>
    protected abstract TwoNodeDatabase NewDatabase();

    /// <summary>The entry is there, the whole entity finds it, the chain verifies, and the write did not fail in the log.</summary>
    /// <returns>A task that completes when the scenario is done.</returns>
    [Fact]
    public async Task Deleting_a_session_with_the_longest_accepted_id_leaves_an_audit_entry()
    {
        await using var host = await TraconTestHost.StartAsync(configureTracon: NewDatabase().Use);

        using (var created = await host.Client.PostAsJsonAsync(
                   Api("agents"),
                   new AgentDefinitionRequest
                   {
                       Name = "long-session",
                       Instructions = "Answer.",
                       Model = new ModelBinding { Provider = "echo", Model = "echo-1" },
                   }))
        {
            created.EnsureSuccessStatusCode();
        }

        using (var run = await host.Client.PostAsJsonAsync(
                   Api("agents/long-session/run"),
                   new AgentRunRequest { Message = "hello", SessionId = LongSessionId }))
        {
            run.StatusCode.ShouldBe(HttpStatusCode.OK, await run.Content.ReadAsStringAsync());
        }

        using (var deleted = await host.Client.DeleteAsync(Api($"sessions/{LongSessionId}")))
        {
            deleted.StatusCode.ShouldBe(HttpStatusCode.NoContent, await deleted.Content.ReadAsStringAsync());
        }

        var entries = await ListAsync(host, $"audit?action={Action}");

        var entry = entries.ShouldHaveSingleItem("the delete must leave its audit entry");
        entry.GetProperty("entity").GetString().ShouldStartWith("session:sss");

        // The operator who knows the session id asks for it by its entity and gets the entry.
        (await ListAsync(host, $"audit?entity={Uri.EscapeDataString("session:" + LongSessionId)}")).ShouldHaveSingleItem();

        using var verified = await host.Client.GetAsync(Api("audit/verify"));
        var verification = await TraconTestHost.ReadJsonAsync(verified);
        verification.GetProperty("status").GetString().ShouldBe("Valid", verification.GetRawText());

        host.Logs.AllText.ShouldNotContain("Could not write action");
    }

    private static async Task<List<JsonElement>> ListAsync(TraconTestHost host, string path)
    {
        using var response = await host.Client.GetAsync(Api(path));
        var json = await TraconTestHost.ReadJsonAsync(response);

        return [.. json.EnumerateArray()];
    }
}
