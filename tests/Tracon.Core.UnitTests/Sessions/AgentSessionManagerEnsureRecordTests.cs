using Microsoft.Agents.AI;
using Tracon.Core.UnitTests.Fakes;

namespace Tracon.Core.UnitTests.Sessions;

/// <summary>
/// The first record of a session that was just opened is written by whoever comes first, the first change to the session's
/// workspace or the run's own save, and by one of them only.
/// </summary>
/// <remarks>
/// A run that keeps files for its session writes them under the session's identity while it runs, and a run that ends in a
/// failure writes no record: the files then stay behind a session that does not exist. The workspace store asks for the
/// record before its first write. These cases hold what that needs of the manager; the whole path, from a model that calls the
/// tool to a delete that finds the files, is <c>SessionWorkspaceFirstWriteTests</c>.
/// </remarks>
public sealed class AgentSessionManagerEnsureRecordTests
{
    [Fact]
    public async Task A_session_that_was_just_opened_has_its_record_after_the_first_ensure_and_the_run_s_own_save_updates_it()
    {
        var store = new CountingSessionStore();
        var manager = new AgentSessionManager(store, FixedTenantContext.Default);
        var agent = Agent();
        var session = await manager.GetOrCreateSessionAsync(agent, "s1", TestContext.Current.CancellationToken);

        (await store.GetAsync("s1", TestContext.Current.CancellationToken)).ShouldBeNull("nothing is written until a change asks for it");

        await manager.EnsureRecordAsync(session, TestContext.Current.CancellationToken);

        var created = (await store.GetAsync("s1", TestContext.Current.CancellationToken)).ShouldNotBeNull();

        created.AgentName.ShouldBe(agent.Name ?? agent.Id);
        store.Creates.ShouldBe(1);

        await manager.SaveSessionAsync(agent, session, TestContext.Current.CancellationToken);

        store.Creates.ShouldBe(1, "the run's own save is an update of the record the first change wrote, not a second creation");
        store.Updates.ShouldBe(1);
        (await store.GetAsync("s1", TestContext.Current.CancellationToken)).ShouldNotBeNull().Version.ShouldBe(created.Version + 1);
    }

    [Fact]
    public async Task Two_changes_at_once_write_one_record()
    {
        var store = new CountingSessionStore();
        var manager = new AgentSessionManager(store, FixedTenantContext.Default);
        var session = await manager.GetOrCreateSessionAsync(Agent(), "s2", TestContext.Current.CancellationToken);

        await Task.WhenAll(
            Enumerable.Range(0, 8).Select(_ => Task.Run(
                async () => await manager.EnsureRecordAsync(session, TestContext.Current.CancellationToken),
                TestContext.Current.CancellationToken)));

        store.Creates.ShouldBe(1);
        store.Conflicts.ShouldBe(0, "a writer that came second saw the record and left it alone");
    }

    [Fact]
    public async Task A_session_that_was_restored_is_left_alone()
    {
        var store = new CountingSessionStore();
        var manager = new AgentSessionManager(store, FixedTenantContext.Default);
        var agent = Agent();

        await manager.SaveSessionAsync(agent, await manager.GetOrCreateSessionAsync(agent, "s3", TestContext.Current.CancellationToken), TestContext.Current.CancellationToken);

        var restored = await manager.GetOrCreateSessionAsync(agent, "s3", TestContext.Current.CancellationToken);
        var creates = store.Creates;
        var updates = store.Updates;

        await manager.EnsureRecordAsync(restored, TestContext.Current.CancellationToken);

        store.Creates.ShouldBe(creates);
        store.Updates.ShouldBe(updates);
    }

    [Fact]
    public async Task A_session_that_another_request_opened_first_is_a_conflict_the_caller_is_told()
    {
        var store = new CountingSessionStore();
        var manager = new AgentSessionManager(store, FixedTenantContext.Default);
        var agent = Agent();

        var first = await manager.GetOrCreateSessionAsync(agent, "s4", TestContext.Current.CancellationToken);
        var second = await manager.GetOrCreateSessionAsync(agent, "s4", TestContext.Current.CancellationToken);

        await manager.EnsureRecordAsync(first, TestContext.Current.CancellationToken);

        var conflict = await Should.ThrowAsync<TraconSessionConflictException>(
            async () => await manager.EnsureRecordAsync(second, TestContext.Current.CancellationToken));

        conflict.SessionId.ShouldBe("s4");

        // The loser's end-of-run save is still refused, as it was before the first change could write a record.
        await Should.ThrowAsync<TraconSessionConflictException>(
            async () => await manager.SaveSessionAsync(agent, second, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task A_session_without_a_tracon_identity_is_not_a_session_the_manager_opened_and_has_nothing_to_ensure()
    {
        var manager = new AgentSessionManager(new CountingSessionStore(), FixedTenantContext.Default);
        var session = await Agent().CreateSessionAsync(TestContext.Current.CancellationToken);

        await manager.EnsureRecordAsync(session, TestContext.Current.CancellationToken);
    }

    private static ChatClientAgent Agent() => new(new FakeChatClient());

    /// <summary>The in-memory store that counts how its first and later writes arrive.</summary>
    private sealed class CountingSessionStore : ISessionStore
    {
        private readonly InMemorySessionStore _inner = new(FixedTenantContext.Default);
        private int _creates;
        private int _updates;
        private int _conflicts;

        public int Creates => Volatile.Read(ref _creates);

        public int Updates => Volatile.Read(ref _updates);

        public int Conflicts => Volatile.Read(ref _conflicts);

        public ValueTask SaveAsync(SessionRecord record, CancellationToken cancellationToken = default)
            => _inner.SaveAsync(record, cancellationToken);

        public async ValueTask<bool> TryCreateAsync(SessionRecord record, CancellationToken cancellationToken = default)
        {
            if (await _inner.TryCreateAsync(record, cancellationToken).ConfigureAwait(false))
            {
                Interlocked.Increment(ref _creates);

                return true;
            }

            Interlocked.Increment(ref _conflicts);

            return false;
        }

        public async ValueTask<bool> TryUpdateAsync(SessionRecord record, long expectedVersion, CancellationToken cancellationToken = default)
        {
            var updated = await _inner.TryUpdateAsync(record, expectedVersion, cancellationToken).ConfigureAwait(false);

            if (updated)
            {
                Interlocked.Increment(ref _updates);
            }

            return updated;
        }

        public ValueTask<SessionRecord?> GetAsync(string sessionId, CancellationToken cancellationToken = default)
            => _inner.GetAsync(sessionId, cancellationToken);

        public ValueTask<bool> DeleteAsync(string sessionId, CancellationToken cancellationToken = default)
            => _inner.DeleteAsync(sessionId, cancellationToken);

        public ValueTask<IReadOnlyList<SessionRecord>> QueryAsync(SessionQuery query, CancellationToken cancellationToken = default)
            => _inner.QueryAsync(query, cancellationToken);
    }
}
