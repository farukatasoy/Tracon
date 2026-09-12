using Microsoft.Agents.AI;
using Tracon.Core.UnitTests.Fakes;

namespace Tracon.Core.UnitTests.Sessions;

/// <summary>
/// Pins which saves <see cref="AgentSessionManager"/> guards with an optimistic
/// concurrency check and which it deliberately writes unconditionally.
/// </summary>
/// <remarks>
/// <para>
/// Closing HATA-004 made the FIRST write of a session atomic and left every
/// later write on the unconditional path, so two concurrent turns on an
/// EXISTING session both reported success and the loser's turn was silently
/// overwritten. These tests hold both halves in place.
/// </para>
/// <para>
/// 🚨 They also pin the exception to that rule. A session may legitimately be
/// saved under a DIFFERENT identity than it was read from — the
/// OpenAI-compatible Responses endpoint restores from
/// <c>previous_response_id</c> and saves under the new response id. Comparing
/// the source record's generation against a different record rejects a
/// perfectly good write; that regression was caught by a functional test by
/// accident, not by a test that meant to check it. This file means to.
/// </para>
/// </remarks>
public sealed class AgentSessionManagerConcurrencyTests
{
    [Fact]
    public async Task A_later_save_from_a_stale_read_is_refused_instead_of_overwriting()
    {
        var store = new InMemorySessionStore(FixedTenantContext.Default);
        var manager = new AgentSessionManager(store, FixedTenantContext.Default);
        var agent = Agent();

        // Establish the session, so both saves below are LATER writes.
        var seed = await manager.GetOrCreateSessionAsync(agent, "s1");
        await manager.SaveSessionAsync(agent, seed);

        // Two readers of the same generation.
        var first = await manager.GetOrCreateSessionAsync(agent, "s1");
        var second = await manager.GetOrCreateSessionAsync(agent, "s1");

        await manager.SaveSessionAsync(agent, first);

        var conflict = await Should.ThrowAsync<TraconSessionConflictException>(
            async () => await manager.SaveSessionAsync(agent, second));

        conflict.SessionId.ShouldBe("s1");
    }

    [Fact]
    public async Task Two_saves_of_the_same_session_in_one_request_both_succeed()
    {
        // The second save replaces what the FIRST save wrote, not what the
        // request originally read — otherwise the guard would reject the
        // caller's own previous write.
        var store = new InMemorySessionStore(FixedTenantContext.Default);
        var manager = new AgentSessionManager(store, FixedTenantContext.Default);
        var agent = Agent();

        var session = await manager.GetOrCreateSessionAsync(agent, "s2");

        await manager.SaveSessionAsync(agent, session);
        await manager.SaveSessionAsync(agent, session);
        await manager.SaveSessionAsync(agent, session);

        (await store.GetAsync("s2")).ShouldNotBeNull();
    }

    [Fact]
    public async Task Saving_under_a_different_identity_writes_a_new_record()
    {
        // 🚨 The Responses endpoint's previous_response_id chaining: read from
        // one id, save under another. There is no generation to compare
        // against for the TARGET record, and treating the SOURCE record's
        // generation as the target's rejects a good write with a 409.
        var store = new InMemorySessionStore(FixedTenantContext.Default);
        var manager = new AgentSessionManager(store, FixedTenantContext.Default);
        var agent = Agent();

        var seed = await manager.GetOrCreateSessionAsync(agent, "source");
        await manager.SaveSessionAsync(agent, seed);

        var chained = await manager.GetOrCreateSessionAsync(agent, "source");

        // What TraconAgentSessionStore does right before saving.
        AgentSessionIdentity.SetId(chained, "target");

        await manager.SaveSessionAsync(agent, chained);

        (await store.GetAsync("target")).ShouldNotBeNull();
        (await store.GetAsync("source")).ShouldNotBeNull();
    }

    private static AIAgent Agent()
        => new AgentDefinitionCompiler(
                TestData.Providers(new FakeModelProvider(new FakeChatClient())),
                TestData.Registry())
            .Compile(TestData.Definition());
}
