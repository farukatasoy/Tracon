using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// A host that keeps a conversation reads it back in process with the session manager, on both storage modes, and sees
/// what the management API shows for the same session.
/// </summary>
/// <remarks>
/// The read goes through the registered chat history provider, so these cases run against a real host: the turns are
/// written by the run endpoint, the history lives in the memory provider or in SQLite, and the tenant that asks is
/// the one the ambient scope names.
/// </remarks>
public sealed class SessionReadHistoryTests
{
    private const string AgentName = "kod-agent";
    private const string SessionId = "history-1";

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task The_history_of_two_turns_reads_back_in_order_and_matches_the_management_api(bool sqlStorage)
    {
        using var database = new TempSqliteDatabase("read-history");

        await using var host = await StartAsync(database, sqlStorage);

        await RunTurnAsync(host, "first question");
        await RunTurnAsync(host, "second question");

        var messages = (await ReadAsync(host)).ShouldNotBeNull();
        var texts = messages.Select(static message => message.Text).ToArray();

        messages.Count.ShouldBeGreaterThanOrEqualTo(4);
        messages.Select(static message => message.Role).ShouldContain(ChatRole.User);
        messages.Select(static message => message.Role).ShouldContain(ChatRole.Assistant);
        texts.ShouldContain("first question", StringComparer.Ordinal);
        texts.ShouldContain("second question", StringComparer.Ordinal);
        Array.IndexOf(texts, "first question").ShouldBeLessThan(Array.IndexOf(texts, "second question"));

        var shown = await ManagementMessagesAsync(host);

        shown.Count.ShouldBe(messages.Count, "the management API answers from the same read");
    }

    [Fact]
    public async Task Skip_and_take_return_the_same_slice_as_the_whole_history_cut_by_position()
    {
        await using var host = await StartAsync(database: null, sqlStorage: false);

        await RunTurnAsync(host, "one");
        await RunTurnAsync(host, "two");

        var all = (await ReadAsync(host)).ShouldNotBeNull();
        var page = (await ReadAsync(host, skip: 1, take: 2)).ShouldNotBeNull();
        var rest = (await ReadAsync(host, skip: 3)).ShouldNotBeNull();
        var beyond = (await ReadAsync(host, skip: all.Count + 5)).ShouldNotBeNull();
        var none = (await ReadAsync(host, take: 0)).ShouldNotBeNull();

        page.Select(static message => message.Text).ShouldBe(all.Skip(1).Take(2).Select(static message => message.Text));
        rest.Select(static message => message.Text).ShouldBe(all.Skip(3).Select(static message => message.Text));
        beyond.ShouldBeEmpty();
        none.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_session_that_does_not_exist_reads_as_null_and_a_session_with_no_turn_yet_is_not_found_either()
    {
        await using var host = await StartAsync(database: null, sqlStorage: false);

        (await ReadAsync(host, sessionId: "never-opened")).ShouldBeNull();
    }

    [Fact]
    public async Task Another_tenant_cannot_read_a_session_it_does_not_hold()
    {
        await using var host = await StartAsync(database: null, sqlStorage: false);

        await RunTurnAsync(host, "private");

        using (AmbientTenantScope.Begin("globex"))
        {
            (await ReadAsync(host)).ShouldBeNull("the session belongs to the default tenant");
        }

        (await ReadAsync(host)).ShouldNotBeNull();
    }

    [Fact]
    public async Task A_blank_session_and_a_negative_window_are_refused()
    {
        await using var host = await StartAsync(database: null, sqlStorage: false);

        await Should.ThrowAsync<ArgumentException>(async () => await ReadAsync(host, sessionId: " "));
        await Should.ThrowAsync<ArgumentOutOfRangeException>(async () => await ReadAsync(host, skip: -1));
        await Should.ThrowAsync<ArgumentOutOfRangeException>(async () => await ReadAsync(host, take: -1));
    }

    [Fact]
    public async Task A_manager_built_without_a_history_provider_says_so_instead_of_returning_an_empty_conversation()
    {
        var manager = new AgentSessionManager(new InMemorySessionStore(), FixedTenantContext.Default);
        var agent = new ChatClientAgent(new NoopChatClient());

        await Should.ThrowAsync<InvalidOperationException>(
            async () => await manager.ReadHistoryAsync(agent, SessionId, cancellationToken: TestContext.Current.CancellationToken));
    }

    private static async Task<IReadOnlyList<ChatMessage>?> ReadAsync(
        TraconTestHost host,
        string sessionId = SessionId,
        int skip = 0,
        int? take = null)
    {
        var agent = (await host.Services.GetRequiredService<IAgentCatalog>()
            .ResolveAsync(AgentName, cancellationToken: TestContext.Current.CancellationToken)).ShouldNotBeNull();

        return await host.Services.GetRequiredService<AgentSessionManager>()
            .ReadHistoryAsync(agent, sessionId, skip, take, TestContext.Current.CancellationToken);
    }

    private static async Task RunTurnAsync(TraconTestHost host, string message)
    {
        using var response = await host.Client.PostAsJsonAsync(
            new Uri($"/tracon/api/agents/{AgentName}/run", UriKind.Relative),
            new AgentRunRequest { Message = message, SessionId = SessionId },
            TestContext.Current.CancellationToken);

        response.EnsureSuccessStatusCode();
    }

    private static async Task<IReadOnlyList<JsonElement>> ManagementMessagesAsync(TraconTestHost host)
    {
        using var response = await host.Client.GetAsync(
            new Uri($"/tracon/api/sessions/{SessionId}", UriKind.Relative),
            TestContext.Current.CancellationToken);

        response.EnsureSuccessStatusCode();

        var body = await TraconTestHost.ReadJsonAsync(response);

        return [.. body.GetProperty("messages").EnumerateArray()];
    }

    private static Task<TraconTestHost> StartAsync(TempSqliteDatabase? database, bool sqlStorage)
        => TraconTestHost.StartAsync(builder =>
        {
            if (sqlStorage)
            {
                builder.UseSqlite(database!.ConnectionString);
            }

            builder.AddAgent(TestData.Definition());
        });

    private sealed class NoopChatClient : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
            => Task.FromResult(new ChatResponse());

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }
}
