using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// Verifies the session endpoints: listing, reading chat history, and deletion.
/// </summary>
public sealed class SessionEndpointTests
{
    [Fact]
    public async Task Session_detail_returns_the_chat_history()
    {
        await using var host = await TraconTestHost.StartAsync(
            static builder => builder.AddAgent(TestData.Definition()));

        await RunAsync(host, "hello", "session-1");

        using var response = await host.Client.GetAsync(
            new Uri("/tracon/api/sessions/session-1", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var json = await TraconTestHost.ReadJsonAsync(response);

        json.GetProperty("id").GetString().ShouldBe("session-1");
        json.GetProperty("agentName").GetString().ShouldBe("kod-agent");

        var messages = json.GetProperty("messages");
        messages.ValueKind.ShouldBe(JsonValueKind.Array);
        messages.GetArrayLength().ShouldBeGreaterThanOrEqualTo(2);

        var text = messages.ToString();
        text.ShouldContain("hello");
        text.ShouldContain("Echo: hello");
    }

    [Fact]
    public async Task Session_detail_also_returns_the_opaque_state()
    {
        await using var host = await TraconTestHost.StartAsync(
            static builder => builder.AddAgent(TestData.Definition()));

        await RunAsync(host, "hello", "session-2");

        using var response = await host.Client.GetAsync(
            new Uri("/tracon/api/sessions/session-2", UriKind.Relative));

        var state = (await TraconTestHost.ReadJsonAsync(response)).GetProperty("state");

        state.ValueKind.ShouldBe(JsonValueKind.Object);

        // The session identity stamp lives inside the state and persists with the session.
        state.ToString().ShouldContain(AgentSessionIdentity.StateKey);
    }

    [Fact]
    public async Task Sessions_are_listed()
    {
        await using var host = await TraconTestHost.StartAsync(
            static builder => builder.AddAgent(TestData.Definition()));

        await RunAsync(host, "one", "session-a");
        await RunAsync(host, "two", "session-b");

        using var response = await host.Client.GetAsync(new Uri("/tracon/api/sessions", UriKind.Relative));
        var ids = (await TraconTestHost.ReadJsonAsync(response))
            .EnumerateArray()
            .Select(static session => session.GetProperty("id").GetString())
            .ToList();

        ids.ShouldContain(static id => string.Equals(id, "session-a", StringComparison.Ordinal));
        ids.ShouldContain(static id => string.Equals(id, "session-b", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Session_is_filtered_by_agent_name()
    {
        await using var host = await TraconTestHost.StartAsync(
            static builder => builder.AddAgent(TestData.Definition()));

        await RunAsync(host, "hello", "session-c");

        using var response = await host.Client.GetAsync(
            new Uri("/tracon/api/sessions?agentName=other-agent", UriKind.Relative));

        (await TraconTestHost.ReadJsonAsync(response)).GetArrayLength().ShouldBe(0);
    }

    [Fact]
    public async Task Session_is_deleted()
    {
        await using var host = await TraconTestHost.StartAsync(
            static builder => builder.AddAgent(TestData.Definition()));

        await RunAsync(host, "hello", "session-to-delete");

        using (var deleted = await host.Client.DeleteAsync(
            new Uri("/tracon/api/sessions/session-to-delete", UriKind.Relative)))
        {
            deleted.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        }

        using var missing = await host.Client.GetAsync(
            new Uri("/tracon/api/sessions/session-to-delete", UriKind.Relative));

        missing.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // Deleting again reports a real "gone", not an idempotent success. A
        // 204 here would make "delete then verify" unable to tell a session
        // that was removed from one that never existed.
        using var again = await host.Client.DeleteAsync(
            new Uri("/tracon/api/sessions/session-to-delete", UriKind.Relative));

        again.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Missing_session_returns_404()
    {
        await using var host = await TraconTestHost.StartAsync();

        using var response = await host.Client.GetAsync(
            new Uri("/tracon/api/sessions/no-such-session", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
    }

    [Fact]
    public async Task Metadata_is_still_returned_when_the_agent_is_removed_from_the_catalog()
    {
        // Observability does not break functionality: even if the history
        // can't be read, session metadata is still returned.
        await using var host = await TraconTestHost.StartAsync(
            static builder => builder.AddAgent(TestData.Definition()));

        await RunAsync(host, "hello", "session-orphan");

        // Instead of setting up a new host that does not share the same
        // store, the session is written directly with an agent name that is
        // not in the catalog.
        var store = (ISessionStore)host.Services.GetService(typeof(ISessionStore))!;
        var existing = await store.GetAsync("session-orphan");

        existing.ShouldNotBeNull();
        await store.SaveAsync(existing with { Id = "session-deleted-agent", AgentName = "no-longer-exists" });

        using var response = await host.Client.GetAsync(
            new Uri("/tracon/api/sessions/session-deleted-agent", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var json = await TraconTestHost.ReadJsonAsync(response);
        json.GetProperty("agentName").GetString().ShouldBe("no-longer-exists");
        json.GetProperty("messages").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task Branching_returns_501_in_an_in_memory_setup()
    {
        // 🚨 While no SQL provider is registered, the chat history lives in
        // MAF's InMemoryChatHistoryProvider, inside the session state's OPAQUE
        // block, and cannot be copied up to a specific sequence number.
        // Silently copying everything would not produce the branch the caller
        // wanted; the endpoint states this explicitly.
        await using var host = await TraconTestHost.StartAsync(
            static builder => builder.AddAgent(TestData.Definition()));

        await RunAsync(host, "hello", "session-branch");

        using var response = await host.Client.PostAsJsonAsync(
            new Uri("/tracon/api/sessions/session-branch/branch", UriKind.Relative),
            new { upToSequence = 0 });

        response.StatusCode.ShouldBe(HttpStatusCode.NotImplemented);

        var body = await TraconTestHost.ReadJsonAsync(response);

        body.GetProperty("detail").GetString().ShouldNotBeNull().ShouldContain("SQL");
    }

    [Fact]
    public async Task Missing_session_branch_reports_not_supported_BEFORE_not_found()
    {
        await using var host = await TraconTestHost.StartAsync(
            static builder => builder.AddAgent(TestData.Definition()));

        using var response = await host.Client.PostAsJsonAsync(
            new Uri("/tracon/api/sessions/no-such-session/branch", UriKind.Relative),
            new { upToSequence = 0 });

        // Because no store is registered, the "not supported" response comes
        // FIRST: a missing capability is a more general reason than a
        // missing record, and it points the caller to the right action.
        response.StatusCode.ShouldBe(HttpStatusCode.NotImplemented);
    }

    private static async Task RunAsync(TraconTestHost host, string message, string sessionId)
    {
        using var response = await host.Client.PostAsJsonAsync(
            new Uri("/tracon/api/agents/kod-agent/run", UriKind.Relative),
            new AgentRunRequest { Message = message, SessionId = sessionId });

        response.EnsureSuccessStatusCode();

        await SseReader.ReadAllAsync(await response.Content.ReadAsStreamAsync());
    }
}
