using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Extensions.Logging;
using Tracon.Core.UnitTests.Fakes;

namespace Tracon.Core.UnitTests.Privacy;

/// <summary>
/// What <see cref="SessionConversationResolver"/> finds and what it counts as a conversation it could not look up.
/// </summary>
/// <remarks>
/// <para>
/// One test per row of the counting rule. The rule has a place for every way a session can leave the loop; a way that is
/// added to the resolver needs its row here, because a skip nobody counts is a silent skip again.
/// </para>
/// <para>
/// Sessions are saved through <see cref="AgentSessionManager"/> with a real agent, so their state is what an agent
/// writes and not a hand-made JSON the resolver would happen to accept.
/// </para>
/// </remarks>
public sealed class SessionConversationResolverTests
{
    private static readonly Guid ConversationA = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid ConversationB = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private readonly InMemorySessionStore _sessions = new(FixedTenantContext.Default);
    private readonly ScriptedCatalog _catalog = new();
    private readonly ListLogger _logger = new();

    [Fact]
    public async Task No_sessions_are_nothing_to_look_up_and_the_catalog_is_not_asked()
    {
        var lookup = await Resolver().ResolveAsync([], TestContext.Current.CancellationToken);

        lookup.ConversationIds.ShouldBeEmpty();
        lookup.Unresolved.ShouldBe(0);
        _catalog.Resolutions.ShouldBe(0);
    }

    [Fact]
    public async Task A_session_that_does_not_exist_is_not_counted()
    {
        // A session id of another tenant answers null exactly like an id that exists nowhere: counting it would tell
        // the caller the id exists.
        var lookup = await Resolver().ResolveAsync(["no-such-session"], TestContext.Current.CancellationToken);

        lookup.ConversationIds.ShouldBeEmpty();
        lookup.Unresolved.ShouldBe(0);
        _catalog.Resolutions.ShouldBe(0, "a session that is not there has no agent to ask for");
    }

    [Fact]
    public async Task A_session_whose_agent_is_not_registered_is_read_from_its_stored_state_and_not_counted()
    {
        await SaveAsync("orphan", "vanished-agent", ConversationA);

        var lookup = await Resolver().ResolveAsync(["orphan"], TestContext.Current.CancellationToken);

        lookup.ConversationIds.ShouldBe([ConversationA], "the id is plain JSON in the state the agent wrote; the agent is not needed to read it");
        lookup.Unresolved.ShouldBe(0);
        _logger.Warnings.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_session_whose_agent_is_not_registered_and_that_has_no_chat_history_is_not_counted()
    {
        await SaveAsync("orphan-fresh", "vanished-agent", conversationId: null);
        await SaveAsync("orphan-empty", "vanished-agent", Guid.Empty);

        var lookup = await Resolver().ResolveAsync(["orphan-fresh", "orphan-empty"], TestContext.Current.CancellationToken);

        lookup.ConversationIds.ShouldBeEmpty();
        lookup.Unresolved.ShouldBe(0, "a readable state without a conversation has nothing to miss");
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("5")]
    [InlineData("""{"stateBag":"not an object"}""")]
    [InlineData("""{"stateBag":{"Tracon.ChatHistory":"not an object"}}""")]
    [InlineData("""{"stateBag":{"Tracon.ChatHistory":{"conversationId":"not a guid"}}}""")]
    [InlineData("""{"stateBag":{"Tracon.ChatHistory":{"conversationId":5}}}""")]
    public async Task A_session_whose_agent_is_not_registered_and_whose_state_has_another_shape_is_counted_and_logged(string state)
    {
        await SaveStateAsync("orphan", "vanished-agent", JsonDocument.Parse(state).RootElement.Clone());

        var lookup = await Resolver().ResolveAsync(["orphan"], TestContext.Current.CancellationToken);

        lookup.ConversationIds.ShouldBeEmpty();
        lookup.Unresolved.ShouldBe(1, "without a readable state, whether the session had a conversation cannot be known");
        _logger.Warnings.ShouldHaveSingleItem().ShouldContain("orphan");
    }

    [Fact]
    public async Task A_session_whose_state_cannot_be_restored_is_counted_and_logged()
    {
        _catalog.Register("support");
        await _sessions.SaveAsync(
            new SessionRecord
            {
                Id = "broken",
                AgentName = "support",
                State = JsonDocument.Parse("5").RootElement.Clone(),
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
            },
            TestContext.Current.CancellationToken);

        var lookup = await Resolver().ResolveAsync(["broken"], TestContext.Current.CancellationToken);

        lookup.ConversationIds.ShouldBeEmpty();
        lookup.Unresolved.ShouldBe(1);
        _logger.Warnings.ShouldHaveSingleItem().ShouldContain("broken");
    }

    [Fact]
    public async Task A_session_without_a_chat_history_is_not_counted_because_there_is_nothing_to_miss()
    {
        _catalog.Register("support");
        await SaveAsync("fresh", "support", conversationId: null);

        var lookup = await Resolver().ResolveAsync(["fresh"], TestContext.Current.CancellationToken);

        lookup.ConversationIds.ShouldBeEmpty();
        lookup.Unresolved.ShouldBe(0);
        _logger.Warnings.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_session_with_an_empty_conversation_id_is_not_counted()
    {
        _catalog.Register("support");
        await SaveAsync("empty-id", "support", Guid.Empty);

        var lookup = await Resolver().ResolveAsync(["empty-id"], TestContext.Current.CancellationToken);

        lookup.ConversationIds.ShouldBeEmpty();
        lookup.Unresolved.ShouldBe(0);
    }

    [Fact]
    public async Task A_conversation_that_is_found_is_returned_and_not_counted()
    {
        _catalog.Register("support");
        await SaveAsync("found", "support", ConversationA);

        var lookup = await Resolver().ResolveAsync(["found"], TestContext.Current.CancellationToken);

        lookup.ConversationIds.ShouldBe([ConversationA]);
        lookup.Unresolved.ShouldBe(0);
    }

    [Fact]
    public async Task Every_row_of_the_rule_in_one_request_is_counted_on_its_own()
    {
        _catalog.Register("support");
        await SaveAsync("found-1", "support", ConversationA);
        await SaveAsync("found-2", "support", ConversationB);
        await SaveAsync("fresh", "support", conversationId: null);
        await SaveAsync("orphan-1", "vanished-agent", ConversationA);
        await SaveAsync("orphan-2", "renamed-agent", ConversationB);
        await SaveStateAsync("unreadable-1", "vanished-agent", JsonDocument.Parse("{}").RootElement.Clone());
        await SaveStateAsync("unreadable-2", "renamed-agent", JsonDocument.Parse("5").RootElement.Clone());

        var lookup = await Resolver().ResolveAsync(
            ["found-1", "no-such-session", "orphan-1", "fresh", "unreadable-1", "found-2", "orphan-2", "unreadable-2"],
            TestContext.Current.CancellationToken);

        lookup.ConversationIds.ShouldBe([ConversationA, ConversationA, ConversationB, ConversationB]);
        lookup.Unresolved.ShouldBe(2);
    }

    [Fact]
    public async Task An_agent_the_catalog_refuses_to_compile_throws_even_after_a_skip_was_counted()
    {
        // The skip counted before the refusal is lost with the exception: the caller has no answer to act on.
        _catalog.Register("support");
        _catalog.Refuse("doomed-agent", new TraconException("Tracon:ProviderKeys:missing-on-purpose has no value."));
        await SaveStateAsync("orphan", "vanished-agent", JsonDocument.Parse("{}").RootElement.Clone());
        await SaveAsync("doomed", "doomed-agent", ConversationB);

        foreach (var order in (string[][])[["orphan", "doomed"], ["doomed", "orphan"]])
        {
            var refusal = await Should.ThrowAsync<TraconException>(async () =>
                await Resolver().ResolveAsync(order, TestContext.Current.CancellationToken));

            refusal.Message.ShouldContain("missing-on-purpose");
        }
    }

    [Fact]
    public async Task A_caller_that_deletes_reads_the_state_of_a_session_whose_agent_the_catalog_refuses_to_compile()
    {
        // A data subject request fails on such an agent (the test above). Deleting a session, or expiring it, must not:
        // the session could then never be deleted until the configuration is repaired.
        _catalog.Register("support");
        _catalog.Refuse("doomed-agent", new TraconException("Tracon:ProviderKeys:missing-on-purpose has no value."));
        await SaveAsync("doomed", "doomed-agent", ConversationB);
        await SaveAsync("found", "support", ConversationA);

        var lookup = await Resolver().ResolveForDeletionAsync(["doomed", "found"], TestContext.Current.CancellationToken);

        lookup.ConversationIds.ShouldBe([ConversationB, ConversationA]);
        lookup.Unresolved.ShouldBe(0);
        _logger.Warnings.ShouldHaveSingleItem().ShouldContain("doomed");
    }

    [Fact]
    public async Task A_caller_that_deletes_counts_a_session_the_catalog_refuses_and_whose_state_has_another_shape()
    {
        _catalog.Refuse("doomed-agent", new TraconException("Tracon:ProviderKeys:missing-on-purpose has no value."));
        await SaveStateAsync("doomed", "doomed-agent", JsonDocument.Parse("{}").RootElement.Clone());

        var lookup = await Resolver().ResolveForDeletionAsync(["doomed"], TestContext.Current.CancellationToken);

        lookup.ConversationIds.ShouldBeEmpty();
        lookup.Unresolved.ShouldBe(1);
    }

    [Fact]
    public async Task A_caller_that_deletes_still_fails_on_a_failure_that_is_not_the_catalogs_refusal()
    {
        _catalog.Refuse("flaky-agent", new InvalidOperationException("catalog is down"));
        await SaveAsync("flaky", "flaky-agent", ConversationA);

        await Should.ThrowAsync<InvalidOperationException>(async () =>
            await Resolver().ResolveForDeletionAsync(["flaky"], TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task A_failure_that_is_not_the_catalogs_refusal_is_not_swallowed()
    {
        _catalog.Refuse("flaky-agent", new InvalidOperationException("catalog is down"));
        await SaveAsync("flaky", "flaky-agent", ConversationA);

        await Should.ThrowAsync<InvalidOperationException>(async () =>
            await Resolver().ResolveAsync(["flaky"], TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task A_lookup_cancelled_after_a_skip_was_counted_throws_and_returns_no_partial_answer()
    {
        // The first session is a skip that is already counted when the token is cancelled during the second.
        using var cancelled = new CancellationTokenSource();
        _catalog.Register("support");
        _catalog.CancelOn("support", cancelled);
        await SaveStateAsync("orphan", "vanished-agent", JsonDocument.Parse("{}").RootElement.Clone());
        await SaveAsync("found", "support", ConversationA);

        await Should.ThrowAsync<OperationCanceledException>(async () =>
            await Resolver().ResolveAsync(["orphan", "found"], cancelled.Token));
    }

    [Fact]
    public async Task A_session_named_twice_is_one_session_to_look_up()
    {
        await SaveStateAsync("orphan", "vanished-agent", JsonDocument.Parse("{}").RootElement.Clone());

        var lookup = await Resolver().ResolveAsync(["orphan", "orphan"], TestContext.Current.CancellationToken);

        lookup.Unresolved.ShouldBe(1);
    }

    [Fact]
    public async Task Ten_thousand_sessions_of_a_missing_agent_are_ten_thousand_unresolved()
    {
        var ids = new string[10_000];

        for (var i = 0; i < ids.Length; i++)
        {
            ids[i] = $"orphan-{i}";
            await _sessions.SaveAsync(
                new SessionRecord
                {
                    Id = ids[i],
                    AgentName = "vanished-agent",
                    State = JsonDocument.Parse("{}").RootElement.Clone(),
                    CreatedAt = DateTimeOffset.UtcNow,
                    UpdatedAt = DateTimeOffset.UtcNow,
                },
                TestContext.Current.CancellationToken);
        }

        var lookup = await Resolver().ResolveAsync(ids, TestContext.Current.CancellationToken);

        lookup.Unresolved.ShouldBe(10_000);
        lookup.ConversationIds.ShouldBeEmpty();
    }

    private SessionConversationResolver Resolver() => new(_sessions, _catalog, _logger);

    /// <summary>Saves a session the way an agent of that name would have, with the given conversation in its chat history state.</summary>
    private async Task SaveAsync(string sessionId, string agentName, Guid? conversationId)
    {
        var manager = new AgentSessionManager(_sessions, FixedTenantContext.Default);
        var agent = new ChatClientAgent(new FakeChatClient());
        var session = await manager.GetOrCreateSessionAsync(agent, sessionId, TestContext.Current.CancellationToken);

        if (conversationId is { } id)
        {
            session.StateBag.SetValue(
                TraconSessionStateKeys.ChatHistory,
                new ChatHistoryState { ConversationId = id },
                TraconCoreJsonContext.Default.Options);
        }

        await manager.SaveSessionAsync(agent, session, TestContext.Current.CancellationToken);

        // The manager records the agent the session was made with; the test needs the session to name another one.
        var saved = (await _sessions.GetAsync(sessionId, TestContext.Current.CancellationToken))!;
        await _sessions.SaveAsync(saved with { AgentName = agentName }, TestContext.Current.CancellationToken);
    }

    /// <summary>Saves a session whose stored state is the given JSON, for the agent of that name.</summary>
    private async Task SaveStateAsync(string sessionId, string agentName, JsonElement state)
        => await _sessions.SaveAsync(
            new SessionRecord
            {
                Id = sessionId,
                AgentName = agentName,
                State = state,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
            },
            TestContext.Current.CancellationToken);

    private sealed class ScriptedCatalog : IAgentCatalog
    {
        private readonly Dictionary<string, AIAgent> _agents = new(StringComparer.Ordinal);
        private readonly Dictionary<string, Exception> _refusals = new(StringComparer.Ordinal);
        private string? _cancelOn;
        private CancellationTokenSource? _cancelled;

        public int Resolutions { get; private set; }

        /// <summary>Cancels the given source while the given agent is being resolved.</summary>
        public void CancelOn(string name, CancellationTokenSource source)
        {
            _cancelOn = name;
            _cancelled = source;
        }

        public void Register(string name) => _agents[name] = new ChatClientAgent(new FakeChatClient());

        public void Refuse(string name, Exception failure) => _refusals[name] = failure;

        public ValueTask<IReadOnlyList<AgentDescriptor>> ListAsync(CancellationToken cancellationToken = default)
            => new(Array.Empty<AgentDescriptor>());

        public ValueTask<AIAgent?> ResolveAsync(string agentName, AgentResolveOptions? options = null, CancellationToken cancellationToken = default)
        {
            Resolutions++;

            if (string.Equals(agentName, _cancelOn, StringComparison.Ordinal))
            {
                _cancelled?.Cancel();
            }

            cancellationToken.ThrowIfCancellationRequested();

            if (_refusals.TryGetValue(agentName, out var failure))
            {
                throw failure;
            }

            return new(_agents.GetValueOrDefault(agentName));
        }
    }

    private sealed class ListLogger : ILogger<SessionConversationResolver>
    {
        public List<string> Warnings { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
            => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Warning)
            {
                Warnings.Add(formatter(state, exception));
            }
        }
    }
}
