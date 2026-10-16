using Microsoft.Agents.AI;
using Tracon.Core.UnitTests.Fakes;

namespace Tracon.Core.UnitTests.Sessions;

/// <summary>
/// Phase 214: the lookup that never opens a session.
/// </summary>
/// <remarks>
/// Microsoft Agent Framework 1.25's <c>AgentSessionStore.GetSessionAsync</c>
/// "never creates a session". <see cref="AgentSessionManager"/> had only
/// get-or-create, which opens a session and arms the first-save claim; the
/// bridge needs the other half without that side effect.
/// </remarks>
public sealed class AgentSessionManagerLookupTests
{
    [Fact]
    public async Task A_missing_session_is_null_and_nothing_is_written()
    {
        var store = new InMemorySessionStore(FixedTenantContext.Default);
        var manager = new AgentSessionManager(store, FixedTenantContext.Default);

        var session = await manager.GetExistingSessionAsync(Agent(), "missing", TestContext.Current.CancellationToken);

        session.ShouldBeNull();
        (await store.GetAsync("missing", TestContext.Current.CancellationToken)).ShouldBeNull();
    }

    [Fact]
    public async Task A_stored_session_is_restored_with_its_identity()
    {
        var store = new InMemorySessionStore(FixedTenantContext.Default);
        var manager = new AgentSessionManager(store, FixedTenantContext.Default);
        var agent = Agent();

        var opened = await manager.GetOrCreateSessionAsync(agent, "known", TestContext.Current.CancellationToken);
        await manager.SaveSessionAsync(agent, opened, TestContext.Current.CancellationToken);

        var restored = await manager.GetExistingSessionAsync(agent, "known", TestContext.Current.CancellationToken);

        AgentSessionIdentity.GetId(restored.ShouldNotBeNull()).ShouldBe("known");
    }

    [Fact]
    public async Task A_restored_session_is_saved_as_a_later_write_not_as_a_first_save()
    {
        // The lookup shares the restore path of get-or-create: it remembers the
        // generation it read, so a stale second reader is still refused.
        var store = new InMemorySessionStore(FixedTenantContext.Default);
        var manager = new AgentSessionManager(store, FixedTenantContext.Default);
        var agent = Agent();

        var seed = await manager.GetOrCreateSessionAsync(agent, "s1", TestContext.Current.CancellationToken);
        await manager.SaveSessionAsync(agent, seed, TestContext.Current.CancellationToken);

        var first = await manager.GetExistingSessionAsync(agent, "s1", TestContext.Current.CancellationToken);
        var second = await manager.GetExistingSessionAsync(agent, "s1", TestContext.Current.CancellationToken);

        await manager.SaveSessionAsync(agent, first.ShouldNotBeNull(), TestContext.Current.CancellationToken);

        await Should.ThrowAsync<TraconSessionConflictException>(
            async () => await manager.SaveSessionAsync(agent, second.ShouldNotBeNull(), TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public async Task A_blank_identity_is_rejected(string sessionId)
    {
        var manager = new AgentSessionManager(new InMemorySessionStore(FixedTenantContext.Default), FixedTenantContext.Default);

        await Should.ThrowAsync<ArgumentException>(
            async () => await manager.GetExistingSessionAsync(Agent(), sessionId, TestContext.Current.CancellationToken));
    }

    private static AIAgent Agent()
        => new AgentDefinitionCompiler(
                TestData.Providers(new FakeModelProvider(new FakeChatClient())),
                TestData.Registry())
            .Compile(TestData.Definition());
}
