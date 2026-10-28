using Microsoft.Agents.AI;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;

namespace Tracon.AspNetCore.FunctionalTests;

// MAAI001: the session store types are marked "evaluation purposes only"; this
// file is where the A2A store's contract with them is pinned (Phase 229).
#pragma warning disable MAAI001

/// <summary>
/// Phase 229: the identity an A2A conversation is stored under, and the keys
/// <see cref="A2ASessionStore"/> refuses.
/// </summary>
/// <remarks>
/// <para>
/// 🚨 The identity is persisted data. A change to its format leaves every stored
/// A2A conversation unreachable, so the known values below are literals computed
/// outside the product code (SHA-256 over each part's 4-byte little-endian length
/// and UTF-8 bytes, in tenant, agent, isolation key, context id order). A test that
/// recomputed them with the same helper would pass after any change.
/// </para>
/// </remarks>
public sealed class A2ASessionStoreTests
{
    [Theory]
    [InlineData("default", "kod-agent", "apikey:key-1", "ctx-1", "a2a:162c630d64d5fbd563751fa0ea10eccfdd6a9cfd60ec860ef5e32fef057fb85f")]
    [InlineData("default", "kod-agent", "user:ada", "ctx-1", "a2a:30bfd327838a46b182fc12f3e874ce2fbef0a1f536a3b45bbb7155e7e60eec51")]
    public void The_identity_format_is_pinned(string tenant, string agent, string isolationKey, string contextId, string expected)
        => A2ASessionStore.SessionId(tenant, agent, isolationKey, contextId).ShouldBe(expected);

    [Fact]
    public void The_identity_is_68_characters_whatever_the_context_id_is()
    {
        foreach (var contextId in new[] { "c", new string('c', 200), new string('c', 100_000), "naïve-café-🙂" })
        {
            var id = A2ASessionStore.SessionId("default", "kod-agent", "apikey:key-1", contextId);

            id.Length.ShouldBe(68);
            id.ShouldStartWith("a2a:");
            id[4..].ShouldAllBe(static character => char.IsAsciiHexDigitLower(character));
        }
    }

    [Theory]
    [InlineData("other", "kod-agent", "apikey:key-1", "ctx-1")]
    [InlineData("default", "other", "apikey:key-1", "ctx-1")]
    [InlineData("default", "kod-agent", "apikey:key-2", "ctx-1")]
    [InlineData("default", "kod-agent", "apikey:key-1", "ctx-2")]
    public void Every_part_of_the_identity_counts(string tenant, string agent, string isolationKey, string contextId)
    {
        var baseline = A2ASessionStore.SessionId("default", "kod-agent", "apikey:key-1", "ctx-1");

        string.Equals(A2ASessionStore.SessionId(tenant, agent, isolationKey, contextId), baseline, StringComparison.Ordinal)
            .ShouldBeFalse();
    }

    [Fact]
    public void Parts_that_concatenate_to_the_same_text_stay_apart()
    {
        // The length in front of each part is what keeps a boundary in place.
        // Joined with U+001F, a context id that carries the separator moves it.
        string.Equals(
            A2ASessionStore.SessionId("default", "kod-agent", "ab", "c"),
            A2ASessionStore.SessionId("default", "kod-agent", "a", "bc"),
            StringComparison.Ordinal).ShouldBeFalse();
        string.Equals(
            A2ASessionStore.SessionId("default", "kod-agent", "a", "b\u001Fc"),
            A2ASessionStore.SessionId("default", "kod-agent", "a\u001Fb", "c"),
            StringComparison.Ordinal).ShouldBeFalse();
    }

    [Fact]
    public async Task A_key_without_an_isolation_partition_is_refused_and_writes_nothing()
    {
        await using var host = await StartAsync();
        var (store, manager, agent) = Resolve(host);
        var cancellationToken = TestContext.Current.CancellationToken;
        var session = await store.GetOrCreateSessionAsync(agent, Isolated("other"), cancellationToken);

        await Should.ThrowAsync<InvalidOperationException>(
            async () => await store.GetSessionAsync(agent, new AgentSessionStoreKey("ctx-1"), cancellationToken));
        await Should.ThrowAsync<InvalidOperationException>(
            async () => await store.GetOrCreateSessionAsync(agent, new AgentSessionStoreKey("ctx-1"), cancellationToken));
        await Should.ThrowAsync<InvalidOperationException>(
            async () => await store.SaveSessionAsync(agent, new AgentSessionStoreKey("ctx-1"), session, cancellationToken));

        (await manager.QuerySessionsAsync(new SessionQuery(), cancellationToken))
            .ShouldBeEmpty("a refused key must not store a conversation under the bare context id");
    }

    [Theory]
    [InlineData("region")]
    [InlineData("Isolation")]
    public async Task A_partition_the_store_does_not_understand_is_refused_not_ignored(string partition)
    {
        await using var host = await StartAsync();
        var (store, manager, agent) = Resolve(host);
        var cancellationToken = TestContext.Current.CancellationToken;
        var key = new AgentSessionStoreKey("ctx-1", new Dictionary<string, string>(StringComparer.Ordinal) { [partition] = "eu" });

        await Should.ThrowAsync<NotSupportedException>(async () => await store.GetSessionAsync(agent, key, cancellationToken));
        await Should.ThrowAsync<NotSupportedException>(async () => await store.GetOrCreateSessionAsync(agent, key, cancellationToken));

        (await manager.QuerySessionsAsync(new SessionQuery(), cancellationToken)).ShouldBeEmpty();
    }

    [Fact]
    public async Task A_second_partition_beside_the_isolation_one_is_refused()
    {
        await using var host = await StartAsync();
        var (store, _, agent) = Resolve(host);
        var key = new AgentSessionStoreKey(
            "ctx-1",
            new Dictionary<string, string>(StringComparer.Ordinal) { ["isolation"] = "apikey:key-1", ["region"] = "eu" });

        await Should.ThrowAsync<NotSupportedException>(
            async () => await store.GetSessionAsync(agent, key, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task A_lookup_of_a_conversation_that_does_not_exist_returns_null_and_writes_nothing()
    {
        await using var host = await StartAsync();
        var (store, manager, agent) = Resolve(host);
        var cancellationToken = TestContext.Current.CancellationToken;

        (await store.GetSessionAsync(agent, Isolated("missing"), cancellationToken)).ShouldBeNull();
        (await manager.QuerySessionsAsync(new SessionQuery(), cancellationToken)).ShouldBeEmpty();
    }

    [Fact]
    public async Task A_saved_conversation_is_restored_under_the_identity_the_digest_names()
    {
        await using var host = await StartAsync();
        var (store, manager, agent) = Resolve(host);
        var cancellationToken = TestContext.Current.CancellationToken;
        var key = Isolated("ctx-1");

        var opened = await store.GetOrCreateSessionAsync(agent, key, cancellationToken);
        await store.SaveSessionAsync(agent, key, opened, cancellationToken);

        var expected = A2ASessionStore.SessionId("default", "kod-agent", "apikey:key-1", "ctx-1");

        (await manager.QuerySessionsAsync(new SessionQuery(), cancellationToken)).ShouldHaveSingleItem().Id.ShouldBe(expected);
        AgentSessionIdentity.GetId((await store.GetSessionAsync(agent, key, cancellationToken)).ShouldNotBeNull()).ShouldBe(expected);
    }

    [Fact]
    public async Task A_delete_removes_the_conversation_under_its_digest_and_not_the_plain_identity()
    {
        // Phase 255: DelegatingAgentSessionStore forwards a delete with the key as it came. Forwarded
        // as it is, the bridge would fold the isolation partition into "apikey\:key-1::ctx-1", delete
        // nothing, and leave the conversation this store wrote under "a2a:<digest>".
        await using var host = await StartAsync();
        var (store, manager, agent) = Resolve(host);
        var cancellationToken = TestContext.Current.CancellationToken;
        var key = Isolated("ctx-1");
        var plain = new AgentSessionStoreKey(@"apikey\:key-1::ctx-1");

        await store.SaveSessionAsync(agent, key, await store.GetOrCreateSessionAsync(agent, key, cancellationToken), cancellationToken);
        await manager.SaveSessionAsync(agent, await manager.GetOrCreateSessionAsync(agent, plain.SessionId, cancellationToken), cancellationToken);

        await store.DeleteSessionAsync(agent, key, cancellationToken);

        (await manager.QuerySessionsAsync(new SessionQuery(), cancellationToken))
            .ShouldHaveSingleItem()
            .Id.ShouldBe(@"apikey\:key-1::ctx-1", "the delete reached the digest identity and nothing else");
        (await store.GetSessionAsync(agent, key, cancellationToken)).ShouldBeNull();
    }

    [Fact]
    public async Task A_delete_without_an_isolation_partition_is_refused_and_deletes_nothing()
    {
        await using var host = await StartAsync();
        var (store, manager, agent) = Resolve(host);
        var cancellationToken = TestContext.Current.CancellationToken;

        await store.SaveSessionAsync(agent, Isolated("ctx-1"), await store.GetOrCreateSessionAsync(agent, Isolated("ctx-1"), cancellationToken), cancellationToken);

        await Should.ThrowAsync<InvalidOperationException>(
            async () => await store.DeleteSessionAsync(agent, new AgentSessionStoreKey("ctx-1"), cancellationToken));

        (await manager.QuerySessionsAsync(new SessionQuery(), cancellationToken)).ShouldHaveSingleItem();
    }

    [Fact]
    public async Task A_delete_of_a_conversation_another_user_owns_is_refused_and_a_missing_one_is_a_no_op()
    {
        var attribution = new SwitchableAttribution { UserId = "u1" };
        await using var host = await TraconTestHost.StartAsync(
            static builder => builder.AddAgent(TestData.Definition()),
            configureServices: services =>
            {
                services.Configure<TraconSessionOwnershipOptions>(static options => options.Enabled = true);
                services.AddSingleton<IRunAttributionContext>(attribution);
            });
        var (store, manager, agent) = ResolveWithRequest(host, attribution);
        var cancellationToken = TestContext.Current.CancellationToken;
        var key = Isolated("ctx-1");

        await store.SaveSessionAsync(agent, key, await store.GetOrCreateSessionAsync(agent, key, cancellationToken), cancellationToken);

        attribution.UserId = "u2";

        await Should.ThrowAsync<TraconSessionOwnerRequiredException>(
            async () => await store.DeleteSessionAsync(agent, key, cancellationToken));
        (await manager.QuerySessionsAsync(new SessionQuery(), cancellationToken)).ShouldHaveSingleItem("the refusal comes before the delete");

        await store.DeleteSessionAsync(agent, Isolated("never-opened"), cancellationToken);

        attribution.UserId = "u1";

        await store.DeleteSessionAsync(agent, key, cancellationToken);

        (await manager.QuerySessionsAsync(new SessionQuery(), cancellationToken)).ShouldBeEmpty();
    }

    private static AgentSessionStoreKey Isolated(string contextId)
        => new(contextId, new Dictionary<string, string>(StringComparer.Ordinal) { ["isolation"] = "apikey:key-1" });

    private static Task<TraconTestHost> StartAsync()
        => TraconTestHost.StartAsync(static builder => builder.AddAgent(TestData.Definition()));

    private static (A2ASessionStore Store, AgentSessionManager Manager, AIAgent Agent) Resolve(TraconTestHost host)
        => Resolve(host, new HttpContextAccessor(), ownershipOptions: null, attribution: null);

    /// <summary>The same store with a request in flight and session ownership read from the host.</summary>
    private static (A2ASessionStore Store, AgentSessionManager Manager, AIAgent Agent) ResolveWithRequest(
        TraconTestHost host,
        IRunAttributionContext attribution)
        => Resolve(
            host,
            new HttpContextAccessor { HttpContext = new DefaultHttpContext { RequestServices = host.Services } },
            host.Services.GetRequiredService<IOptionsMonitor<TraconSessionOwnershipOptions>>(),
            attribution);

    private static (A2ASessionStore Store, AgentSessionManager Manager, AIAgent Agent) Resolve(
        TraconTestHost host,
        IHttpContextAccessor accessor,
        IOptionsMonitor<TraconSessionOwnershipOptions>? ownershipOptions,
        IRunAttributionContext? attribution)
    {
        var manager = host.Services.GetRequiredService<AgentSessionManager>();
        var agent = host.Services.GetRequiredService<AgentDefinitionCompiler>().Compile(TestData.Definition());
        var store = new A2ASessionStore(
            new TraconAgentSessionStore(manager, host.Services.GetRequiredService<SessionDeletion>()),
            host.Services.GetRequiredService<ITenantContext>(),
            host.Services.GetRequiredService<ISessionStore>(),
            accessor,
            ownershipOptions,
            attribution);

        return (store, manager, agent);
    }

    private sealed class SwitchableAttribution : IRunAttributionContext
    {
        public string? UserId { get; set; }

        public IReadOnlyDictionary<string, string>? Labels => null;
    }
}
#pragma warning restore MAAI001
