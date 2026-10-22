using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Tracon.Core.UnitTests.Fakes;
using Tracon.Tests.Common;

namespace Tracon.Core.UnitTests.Memory;

/// <summary>
/// What scopes conversation memory: whose records a run reads, whose it writes, and when it does neither.
/// </summary>
/// <remarks>
/// The provider is driven the way the framework drives it, with the run's identity set in
/// <see cref="TraconRunContext"/>. Each test sets and clears that scope in its own body (it is an
/// <c>AsyncLocal</c>).
/// </remarks>
// MAAI001: the context provider contexts are marked "evaluation purposes only" - see K-020.
#pragma warning disable MAAI001
public sealed class ConversationMemoryScopeProviderTests : IDisposable
{
    private const string Collection = "memory";

    private readonly InMemoryVectorStore _store = new();
    private readonly AIAgent _agent = new ChatClientAgent(new FakeChatClient());

    public void Dispose()
    {
        TraconRunContext.SetCurrent(null);
        _store.Dispose();
    }

    [Fact]
    public async Task What_a_user_said_in_one_session_is_found_by_the_same_user_in_another_session_of_the_same_agent()
    {
        var provider = Provider("agent-a");

        await RememberAsync(provider, Run("tenant-a", "agent-a", "ada", "session-1"), "My favourite colour is teal.");

        var found = await RecallAsync(provider, Run("tenant-a", "agent-a", "ada", "session-2"), "What is my favourite colour?");

        found.ShouldContain("teal");
    }

    [Fact]
    public async Task Another_user_does_not_find_it()
    {
        var provider = Provider("agent-a");
        await RememberAsync(provider, Run("tenant-a", "agent-a", "ada", "session-1"), "My favourite colour is teal.");

        var found = await RecallAsync(provider, Run("tenant-a", "agent-a", "grace", "session-2"), "What is my favourite colour?");

        found.ShouldNotContain("teal");
    }

    [Fact]
    public async Task Another_agent_does_not_find_it()
    {
        var writer = Provider("agent-a");
        var reader = Provider("agent-b");
        await RememberAsync(writer, Run("tenant-a", "agent-a", "ada", "session-1"), "My favourite colour is teal.");

        var found = await RecallAsync(reader, Run("tenant-a", "agent-b", "ada", "session-2"), "What is my favourite colour?");

        found.ShouldNotContain("teal");
    }

    [Fact]
    public async Task Another_tenant_does_not_find_it()
    {
        var writer = Provider("agent-a", tenant: "tenant-a");
        var reader = Provider("agent-a", tenant: "tenant-b");
        await RememberAsync(writer, Run("tenant-a", "agent-a", "ada", "session-1"), "My favourite colour is teal.");

        var found = await RecallAsync(reader, Run("tenant-b", "agent-a", "ada", "session-2"), "What is my favourite colour?");

        found.ShouldNotContain("teal");
    }

    [Fact]
    public async Task A_run_with_no_user_neither_writes_nor_reads()
    {
        var provider = Provider("agent-a");
        await RememberAsync(provider, Run("tenant-a", "agent-a", "ada", "session-1"), "My favourite colour is teal.");
        var before = _store.Records(Collection).Count;

        await RememberAsync(provider, Run("tenant-a", "agent-a", user: null, "session-2"), "The anonymous secret is plum.");
        var found = await RecallAsync(provider, Run("tenant-a", "agent-a", user: null, "session-3"), "What is my favourite colour?");

        _store.Records(Collection).Count.ShouldBe(before);
        found.ShouldNotContain("teal");
    }

    [Fact]
    public async Task A_run_with_no_session_neither_writes_nor_reads()
    {
        // A record without a session could not be deleted with its session.
        var provider = Provider("agent-a");
        await RememberAsync(provider, Run("tenant-a", "agent-a", "ada", "session-1"), "My favourite colour is teal.");
        var before = _store.Records(Collection).Count;

        await RememberAsync(provider, Run("tenant-a", "agent-a", "ada", session: null), "The sessionless secret is plum.");
        var found = await RecallAsync(provider, Run("tenant-a", "agent-a", "ada", session: null), "What is my favourite colour?");

        _store.Records(Collection).Count.ShouldBe(before);
        found.ShouldNotContain("teal");
    }

    [Fact]
    public async Task A_run_whose_session_has_no_record_neither_writes_nor_reads()
    {
        // A workflow run's session id, and every run below it: nothing could delete what they kept.
        var provider = Provider("agent-a");
        await RememberAsync(provider, Run("tenant-a", "agent-a", "ada", "session-1"), "My favourite colour is teal.");
        var before = _store.Records(Collection).Count;

        await RememberAsync(provider, Run("tenant-a", "agent-a", "ada", "workflow-session", sessionHasNoRecord: true), "The workflow secret is plum.");
        var found = await RecallAsync(provider, Run("tenant-a", "agent-a", "ada", "workflow-session", sessionHasNoRecord: true), "What is my favourite colour?");

        _store.Records(Collection).Count.ShouldBe(before);
        found.ShouldNotContain("teal");
    }

    [Fact]
    public async Task A_run_outside_any_run_scope_is_left_alone()
    {
        var provider = Provider("agent-a");

        TraconRunContext.SetCurrent(null);

        var context = await provider.InvokingAsync(
            new AIContextProvider.InvokingContext(_agent, null, new AIContext { Messages = [new ChatMessage(ChatRole.User, "hello")] }));

        context.Messages.ShouldHaveSingleItem().Text.ShouldBe("hello");
        _store.Records(Collection).ShouldBeEmpty();
    }

    [Fact]
    public async Task A_run_of_another_tenant_than_the_agent_was_compiled_for_is_left_alone()
    {
        var provider = Provider("agent-a", tenant: "tenant-a");
        await RememberAsync(provider, Run("tenant-a", "agent-a", "ada", "session-1"), "My favourite colour is teal.");
        var before = _store.Records(Collection).Count;

        await RememberAsync(provider, Run("tenant-b", "agent-a", "ada", "session-2"), "Tenant b secret is plum.");
        var found = await RecallAsync(provider, Run("tenant-b", "agent-a", "ada", "session-3"), "What is my favourite colour?");

        _store.Records(Collection).Count.ShouldBe(before);
        found.ShouldNotContain("teal");
    }

    [Fact]
    public async Task Two_users_sharing_one_session_id_each_write_under_their_own_scope()
    {
        // Session ownership off: nothing stops grace from naming ada's session. The scope comes from the
        // run, not from state the first user left in the session.
        var provider = Provider("agent-a");

        await RememberAsync(provider, Run("tenant-a", "agent-a", "ada", "shared-session"), "Ada likes teal.");
        await RememberAsync(provider, Run("tenant-a", "agent-a", "grace", "shared-session"), "Grace likes plum.");

        var records = _store.Records(Collection);
        records.Where(static record => string.Equals((string?)record["UserId"], "ada", StringComparison.Ordinal))
            .Select(static record => (string?)record["Content"]).ShouldBe(["Ada likes teal."]);
        records.Where(static record => string.Equals((string?)record["UserId"], "grace", StringComparison.Ordinal))
            .Select(static record => (string?)record["Content"]).ShouldBe(["Grace likes plum."]);

        (await RecallAsync(provider, Run("tenant-a", "agent-a", "grace", "shared-session"), "Which colour does Ada like?"))
            .ShouldNotContain("teal");
    }

    [Fact]
    public async Task A_record_carries_the_tenant_agent_user_and_session_of_the_run_that_wrote_it()
    {
        var provider = Provider("agent-a");

        await RememberAsync(provider, Run("tenant-a", "agent-a", "ada", "session-1"), "My favourite colour is teal.");

        var record = _store.Records(Collection).First(static r => string.Equals((string?)r["Role"], "user", StringComparison.OrdinalIgnoreCase));
        record["ApplicationId"].ShouldBe("tenant-a");
        record["AgentId"].ShouldBe("agent-a");
        record["UserId"].ShouldBe("ada");
        record["SessionId"].ShouldBe("session-1");
    }

    [Fact]
    public async Task Only_what_the_user_and_the_agent_said_is_stored_not_a_memory_the_provider_added_nor_a_tool_result()
    {
        var provider = Provider("agent-a");
        var run = Run("tenant-a", "agent-a", "ada", "session-1");

        TraconRunContext.SetCurrent(run);

        await provider.InvokedAsync(new AIContextProvider.InvokedContext(
            _agent,
            null,
            [
                new ChatMessage(ChatRole.User, "My favourite colour is teal."),
                new ChatMessage(ChatRole.User, "## Memories\nSomething older.")
                    .WithAgentRequestMessageSource(AgentRequestMessageSourceType.AIContextProvider, "other"),
            ],
            [
                new ChatMessage(ChatRole.Assistant, [new FunctionCallContent("call-1", "lookup")]),
                new ChatMessage(ChatRole.Tool, [new FunctionResultContent("call-1", "TOOL-SECRET")]),
                new ChatMessage(ChatRole.Assistant, "Noted: teal."),
            ]));

        _store.Records(Collection).Select(static record => (string?)record["Content"])
            .ShouldBe(["My favourite colour is teal.", "Noted: teal."], ignoreOrder: true);
    }

    [Fact]
    public async Task A_run_that_failed_stores_nothing()
    {
        var provider = Provider("agent-a");
        TraconRunContext.SetCurrent(Run("tenant-a", "agent-a", "ada", "session-1"));

        await provider.InvokedAsync(new AIContextProvider.InvokedContext(
            _agent, null, [new ChatMessage(ChatRole.User, "My favourite colour is teal.")], new InvalidOperationException("model failed")));

        _store.Records(Collection).ShouldBeEmpty();
    }

    [Fact]
    public async Task The_search_tool_of_the_on_demand_mode_reads_only_the_callers_scope()
    {
        var provider = Provider("agent-a", ConversationMemorySearch.OnDemand);
        await RememberAsync(provider, Run("tenant-a", "agent-a", "ada", "session-1"), "My favourite colour is teal.");
        await RememberAsync(provider, Run("tenant-a", "agent-a", "grace", "session-1"), "My favourite colour is plum.");

        TraconRunContext.SetCurrent(Run("tenant-a", "agent-a", "ada", "session-2"));

        var context = await provider.InvokingAsync(
            new AIContextProvider.InvokingContext(_agent, null, new AIContext { Messages = [new ChatMessage(ChatRole.User, "colour?")] }));

        var tool = context.Tools.ShouldNotBeNull().OfType<AIFunction>().ShouldHaveSingleItem();
        tool.Name.ShouldBe(ConversationMemoryScopeProvider.SearchToolName);

        var result = (await tool.InvokeAsync(new AIFunctionArguments(StringComparer.Ordinal) { ["userQuestion"] = "favourite colour" }))?.ToString();

        result.ShouldNotBeNull().ShouldContain("teal");
        result.ShouldNotContain("plum");
    }

    [Fact]
    public async Task One_search_returns_no_more_records_than_the_limit_of_the_definition()
    {
        var provider = Provider("agent-a", maxResults: 2);

        for (var index = 0; index < 6; index++)
        {
            await RememberAsync(provider, Run("tenant-a", "agent-a", "ada", $"session-{index}"), $"Colour number {index} is teal.");
        }

        var found = await RecallAsync(provider, Run("tenant-a", "agent-a", "ada", "session-9"), "Which colour is teal?");

        found.Split('\n').Count(static line => line.StartsWith("Colour number", StringComparison.Ordinal)).ShouldBe(2);
    }

    [Fact]
    public async Task A_store_that_fails_on_search_gives_the_run_no_memory_and_does_not_fail_it()
    {
        // Open question 4: memory is context, not the answer. A store that is down costs the run its memory.
        var provider = Provider("agent-a");
        await RememberAsync(provider, Run("tenant-a", "agent-a", "ada", "session-1"), "My favourite colour is teal.");
        _store.FailOn = static operation => string.Equals(operation, "search", StringComparison.Ordinal) ? new InvalidOperationException("store is down") : null;

        var found = await RecallAsync(provider, Run("tenant-a", "agent-a", "ada", "session-2"), "What is my favourite colour?");

        found.ShouldNotContain("teal");
        found.ShouldContain("What is my favourite colour?");
    }

    [Fact]
    public async Task A_store_that_fails_on_write_does_not_fail_the_run()
    {
        var provider = Provider("agent-a");
        _store.FailOn = static operation => string.Equals(operation, "upsert", StringComparison.Ordinal) ? new InvalidOperationException("store is down") : null;

        await Should.NotThrowAsync(async () => await RememberAsync(provider, Run("tenant-a", "agent-a", "ada", "session-1"), "My favourite colour is teal."));

        _store.Records(Collection).ShouldBeEmpty();
    }

    [Fact]
    public async Task A_cancelled_search_is_not_swallowed()
    {
        var provider = Provider("agent-a");
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        TraconRunContext.SetCurrent(Run("tenant-a", "agent-a", "ada", "session-2"));

        await Should.ThrowAsync<OperationCanceledException>(async () => await provider.InvokingAsync(
            new AIContextProvider.InvokingContext(_agent, null, new AIContext { Messages = [new ChatMessage(ChatRole.User, "What is my favourite colour?")] }),
            cancelled.Token));
    }

    private ConversationMemoryScopeProvider Provider(
        string agent,
        ConversationMemorySearch search = ConversationMemorySearch.BeforeEachTurn,
        string tenant = "tenant-a",
        int? maxResults = null)
        => new(
            _store,
            new TraconConversationMemoryOptions { CollectionName = Collection, VectorDimensions = 8 },
            tenant,
            agent,
            search,
            maxResults,
            loggerFactory: null);

    private static AgentRunScope Run(string tenant, string agent, string? user, string? session, bool sessionHasNoRecord = false)
        => new()
        {
            RunId = Guid.NewGuid(),
            RootRunId = Guid.NewGuid(),
            AgentName = agent,
            TenantId = tenant,
            UserId = user,
            SessionId = session,
            SessionHasNoRecord = sessionHasNoRecord,
        };

    private readonly Dictionary<string, AgentSession> _sessions = new(StringComparer.Ordinal);

    /// <summary>
    /// The session the framework would hand the provider: one object per session id, the same for both users that
    /// name it. A wrapper that passed it on would freeze the first user's scope in its state bag.
    /// </summary>
    private async Task<AgentSession> SessionAsync(AgentRunScope run)
    {
        var key = run.SessionId ?? "(none)";

        if (!_sessions.TryGetValue(key, out var session))
        {
            _sessions[key] = session = await _agent.CreateSessionAsync();
        }

        return session;
    }

    private async Task RememberAsync(ConversationMemoryScopeProvider provider, AgentRunScope run, string said)
    {
        TraconRunContext.SetCurrent(run);

        await provider.InvokedAsync(new AIContextProvider.InvokedContext(_agent, await SessionAsync(run), [new ChatMessage(ChatRole.User, said)], []));
    }

    private async Task<string> RecallAsync(ConversationMemoryScopeProvider provider, AgentRunScope run, string asked)
    {
        TraconRunContext.SetCurrent(run);

        var context = await provider.InvokingAsync(
            new AIContextProvider.InvokingContext(_agent, await SessionAsync(run), new AIContext { Messages = [new ChatMessage(ChatRole.User, asked)] }));

        return string.Join("\n", (context.Messages ?? []).Select(static message => message.Text));
    }
}
