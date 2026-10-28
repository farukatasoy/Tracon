using System.Net.Http.Json;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;

namespace Tracon.AspNetCore.FunctionalTests;

// MAAI001: the session store types are marked "evaluation purposes only"; this
// file is where the bridge's contract with them is pinned (Phase 214).
#pragma warning disable MAAI001

/// <summary>
/// Phase 214: <see cref="TraconAgentSessionStore"/> against Microsoft Agent
/// Framework 1.26's <see cref="AgentSessionStore"/> contract.
/// </summary>
/// <remarks>
/// <para>
/// 🚨 A tenant boundary. Since MAF 1.22 a store key carries named partitions
/// next to the session id; <c>IsolationKeyScopedAgentSessionStore</c> keeps its
/// scope there. A bridge that read only <c>key.SessionId</c> would hand one
/// caller's session to another whenever a consumer wrapped it. Nothing in the
/// build notices — the types line up either way.
/// </para>
/// <para>
/// The second contract is the lookup: <c>GetSessionAsync</c> "never creates a
/// session". Tracon's session manager only knows get-or-create, so the bridge
/// reads through a lookup of its own.
/// </para>
/// </remarks>
public sealed class SessionStoreIsolationTests
{
    private static readonly Uri Responses = new("/tracon/v1/responses", UriKind.Relative);

    [Fact]
    public async Task Two_isolation_keys_with_the_same_conversation_id_keep_separate_sessions()
    {
        var keys = new SwitchableIsolationKeyProvider();

        await using var host = await TraconTestHost.StartAsync(
            configureTracon: static builder => builder.AddAgent(TestData.Definition()),
            configureServices: services => services.AddSingleton<AgentSessionStore>(sp =>
                new IsolationKeyScopedAgentSessionStore(
                    new TraconAgentSessionStore(
                        sp.GetRequiredService<AgentSessionManager>(),
                        sp.GetRequiredService<SessionDeletion>()),
                    keys)));

        await PostTurnAsync(host, keys, "tenant-a", "first turn of a");
        await PostTurnAsync(host, keys, "tenant-b", "first turn of b");
        await PostTurnAsync(host, keys, "tenant-a", "second turn of a");

        var ids = await SessionIdsAsync(host);

        ids.ShouldContain(static id => string.Equals(id, "tenant-a::shared", StringComparison.Ordinal));
        ids.ShouldContain(static id => string.Equals(id, "tenant-b::shared", StringComparison.Ordinal));
        ids.ShouldNotContain(
            static id => string.Equals(id, "shared", StringComparison.Ordinal),
            "an unscoped record would be visible to every caller");
        ids.Count(static id => id.EndsWith("::shared", StringComparison.Ordinal))
            .ShouldBe(2, "tenant-a's second turn resumes its own session instead of opening a third");
    }

    [Fact]
    public async Task The_isolation_partition_is_folded_into_the_identity_in_the_format_MAF_wrote_before_partitions()
    {
        await using var host = await StartAsync();
        var (store, manager, agent) = Resolve(host);
        var key = new AgentSessionStoreKey("conv", new Dictionary<string, string>(StringComparer.Ordinal) { ["isolation"] = @"a:b\c" });

        var session = await store.GetOrCreateSessionAsync(agent, key, TestContext.Current.CancellationToken);
        await store.SaveSessionAsync(agent, key, session, TestContext.Current.CancellationToken);

        // The colon and the backslash are escaped, the separator is "::".
        (await manager.QuerySessionsAsync(new SessionQuery(), TestContext.Current.CancellationToken))
            .ShouldContain(static record => record.Id == @"a\:b\\c::conv");
    }

    [Theory]
    [InlineData("region")]
    [InlineData("Isolation")]
    public async Task A_partition_the_bridge_does_not_understand_is_refused_not_ignored(string partition)
    {
        await using var host = await StartAsync();
        var (store, manager, agent) = Resolve(host);
        var key = new AgentSessionStoreKey("conv", new Dictionary<string, string>(StringComparer.Ordinal) { [partition] = "eu" });
        var cancellationToken = TestContext.Current.CancellationToken;
        var session = await store.GetOrCreateSessionAsync(agent, new AgentSessionStoreKey("other"), cancellationToken);

        await Should.ThrowAsync<NotSupportedException>(async () => await store.GetSessionAsync(agent, key, cancellationToken));
        await Should.ThrowAsync<NotSupportedException>(async () => await store.GetOrCreateSessionAsync(agent, key, cancellationToken));
        await Should.ThrowAsync<NotSupportedException>(async () => await store.SaveSessionAsync(agent, key, session, cancellationToken));
        await Should.ThrowAsync<NotSupportedException>(async () => await store.DeleteSessionAsync(agent, key, cancellationToken));

        (await manager.QuerySessionsAsync(new SessionQuery(), cancellationToken))
            .ShouldBeEmpty("a refused key must not write a record under the bare session id");
    }

    [Fact]
    public async Task Deleting_through_an_isolation_partition_deletes_that_callers_session_and_no_other()
    {
        // Phase 255: MAF 1.26 added DeleteSessionAsync. Folding the partition into the identity
        // must hold for the delete as it does for every other call, or one caller's delete
        // removes the session of the caller who shares the conversation id.
        await using var host = await StartAsync();
        var (store, manager, agent) = Resolve(host);
        var cancellationToken = TestContext.Current.CancellationToken;
        var tenantA = new AgentSessionStoreKey("conv", new Dictionary<string, string>(StringComparer.Ordinal) { ["isolation"] = "tenant-a" });
        var tenantB = new AgentSessionStoreKey("conv", new Dictionary<string, string>(StringComparer.Ordinal) { ["isolation"] = "tenant-b" });
        var bare = new AgentSessionStoreKey("conv");

        foreach (var key in new[] { tenantA, tenantB, bare })
        {
            await store.SaveSessionAsync(agent, key, await store.GetOrCreateSessionAsync(agent, key, cancellationToken), cancellationToken);
        }

        await store.DeleteSessionAsync(agent, tenantA, cancellationToken);

        (await manager.QuerySessionsAsync(new SessionQuery(), cancellationToken))
            .Select(static record => record.Id)
            .Order(StringComparer.Ordinal)
            .ShouldBe(["conv", "tenant-b::conv"]);
    }

    [Fact]
    public async Task Two_partitions_on_one_key_are_refused()
    {
        await using var host = await StartAsync();
        var (store, _, agent) = Resolve(host);
        var key = new AgentSessionStoreKey(
            "conv",
            new Dictionary<string, string>(StringComparer.Ordinal) { ["isolation"] = "tenant-a", ["region"] = "eu" });

        await Should.ThrowAsync<NotSupportedException>(
            async () => await store.GetSessionAsync(agent, key, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Looking_up_a_session_that_does_not_exist_returns_null_and_writes_nothing()
    {
        await using var host = await StartAsync();
        var (store, manager, agent) = Resolve(host);
        var cancellationToken = TestContext.Current.CancellationToken;

        var session = await store.GetSessionAsync(agent, new AgentSessionStoreKey("missing"), cancellationToken);

        session.ShouldBeNull("MAF's contract: GetSessionAsync never creates a session");
        (await manager.QuerySessionsAsync(new SessionQuery(), cancellationToken)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Looking_up_a_session_that_exists_restores_it_with_its_identity()
    {
        await using var host = await StartAsync();
        var (store, _, agent) = Resolve(host);
        var cancellationToken = TestContext.Current.CancellationToken;
        var key = new AgentSessionStoreKey("known");

        var opened = await store.GetOrCreateSessionAsync(agent, key, cancellationToken);
        await store.SaveSessionAsync(agent, key, opened, cancellationToken);

        var restored = await store.GetSessionAsync(agent, key, cancellationToken);

        AgentSessionIdentity.GetId(restored.ShouldNotBeNull()).ShouldBe("known");
        restored.ShouldNotBeSameAs(opened, "MAF asks for an independent instance on every lookup");
    }

    [Fact]
    public async Task Get_or_create_opens_a_session_when_none_exists_and_does_not_write_it_yet()
    {
        await using var host = await StartAsync();
        var (store, manager, agent) = Resolve(host);
        var cancellationToken = TestContext.Current.CancellationToken;

        var session = await store.GetOrCreateSessionAsync(agent, new AgentSessionStoreKey("fresh"), cancellationToken);

        AgentSessionIdentity.GetId(session).ShouldBe("fresh");
        (await manager.QuerySessionsAsync(new SessionQuery(), cancellationToken)).ShouldBeEmpty();
    }

    private static Task<TraconTestHost> StartAsync()
        => TraconTestHost.StartAsync(static builder => builder.AddAgent(TestData.Definition()));

    private static (TraconAgentSessionStore Store, AgentSessionManager Manager, AIAgent Agent) Resolve(TraconTestHost host)
    {
        var manager = host.Services.GetRequiredService<AgentSessionManager>();
        var agent = host.Services.GetRequiredService<AgentDefinitionCompiler>().Compile(TestData.Definition());

        return (new TraconAgentSessionStore(manager, host.Services.GetRequiredService<SessionDeletion>()), manager, agent);
    }

    private static async Task PostTurnAsync(TraconTestHost host, SwitchableIsolationKeyProvider keys, string isolationKey, string input)
    {
        keys.Current = isolationKey;

        using var response = await host.Client.PostAsJsonAsync(
            Responses,
            new { model = "kod-agent", input, conversation = "shared" },
            TestContext.Current.CancellationToken);

        response.EnsureSuccessStatusCode();
    }

    private static async Task<List<string>> SessionIdsAsync(TraconTestHost host)
    {
        using var sessions = await host.Client.GetAsync(
            new Uri("/tracon/api/sessions", UriKind.Relative),
            TestContext.Current.CancellationToken);

        return (await TraconTestHost.ReadJsonAsync(sessions))
            .EnumerateArray()
            .Select(static session => session.GetProperty("id").GetString()!)
            .ToList();
    }

    /// <summary>An isolation key a test switches between calls, like a caller's identity would.</summary>
    private sealed class SwitchableIsolationKeyProvider : AgentIsolationKeyProvider
    {
        public string? Current { get; set; }

        public override ValueTask<string?> GetIsolationKeyAsync(CancellationToken cancellationToken = default)
            => ValueTask.FromResult(Current);
    }
}
#pragma warning restore MAAI001
