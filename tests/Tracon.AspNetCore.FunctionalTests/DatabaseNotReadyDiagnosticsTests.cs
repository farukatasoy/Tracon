using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// What a caller is told when the store cannot answer. The application knows
/// the reason — <c>/health</c> and <c>/api/diagnostics</c> both report it — and
/// the run and write paths used to drop it on the floor as a bodyless 500
/// (HATA-S1-015, HATA-S1-021).
/// </summary>
public sealed class DatabaseNotReadyDiagnosticsTests
{
    private static readonly Uri Agents = new("/tracon/api/agents", UriKind.Relative);

    [Fact]
    public async Task A_write_says_the_store_is_unavailable_instead_of_a_bodyless_500()
    {
        await using var host = await StartAsync();

        using var response = await host.Client.PostAsJsonAsync(
            Agents,
            TestData.Request(),
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);

        var body = await TraconTestHost.ReadJsonAsync(response);
        body.GetProperty("detail").GetString().ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task A_reachable_store_with_pending_migrations_says_so_and_says_retrying_will_not_help()
    {
        // The decisive half of the decision: a schema that is behind and a
        // database that cannot be reached are different answers for the caller.
        // One clears on its own, the other needs an operator.
        await using var host = await StartAsync(
            new StubSqlDiagnostics(canConnect: true, pendingMigrations: ["0051_something"]));

        using var response = await host.Client.PostAsJsonAsync(
            Agents,
            TestData.Request(),
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);

        var body = await TraconTestHost.ReadJsonAsync(response);
        body.GetProperty("title").GetString().ShouldBe(StoreUnavailableProblemMiddleware.SchemaProblemTitle);
        body.GetProperty("detail").GetString()!.ShouldContain("1 migration");
    }

    [Fact]
    public async Task An_unreachable_store_is_reported_as_transient()
    {
        await using var host = await StartAsync(
            new StubSqlDiagnostics(canConnect: false, pendingMigrations: []));

        using var response = await host.Client.PostAsJsonAsync(
            Agents,
            TestData.Request(),
            TestContext.Current.CancellationToken);

        var body = await TraconTestHost.ReadJsonAsync(response);
        body.GetProperty("title").GetString().ShouldBe(StoreUnavailableProblemMiddleware.UnreachableProblemTitle);
    }

    [Fact]
    public async Task The_answer_does_not_leak_the_schema_name_or_the_sql_text()
    {
        await using var host = await StartAsync();

        using var response = await host.Client.PostAsJsonAsync(
            Agents,
            TestData.Request(),
            TestContext.Current.CancellationToken);

        var raw = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        // The defensible half of today's opacity: the reason may be named, the
        // schema and the statement may not.
        raw.ShouldNotContain("mt_secret_schema");
        raw.ShouldNotContain("SELECT");
        raw.ShouldNotContain("agent_definitions");
    }

    [Fact]
    public async Task A_run_says_the_store_is_unavailable_too()
    {
        // The record's SECOND empirical example: the write path and the run
        // path fell into the same opaque response through two different
        // triggers. The run path streams, so the mapping only reaches it while
        // the response has not started - which is where the store is read.
        await using var host = await StartAsync();

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            new Uri("/tracon/api/agents/kod-agent/run", UriKind.Relative))
        {
            Content = JsonContent.Create(new AgentRunRequest { Message = "hi" }),
        };
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));

        using var response = await host.Client.SendAsync(request, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
    }

    [Fact]
    public async Task A_read_that_needs_the_store_says_it_is_unavailable_too()
    {
        // NOT the catalog list: GET /api/agents is deliberately fault-isolated,
        // so a broken source degrades the list instead of failing it
        // (AgentSourceFaultIsolationTests holds that contract). The version
        // history has no such fallback - it reads the store directly - so it is
        // the read that shows whether the mapping reaches GET at all.
        await using var host = await StartAsync();

        using var response = await host.Client.GetAsync(
            new Uri("/tracon/api/agents/db-agent/versions", UriKind.Relative),
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
    }

    [Fact]
    public async Task The_remote_access_guard_fails_the_way_it_documents_when_the_key_store_cannot_answer()
    {
        // HATA-S1-021: on a fresh, never-migrated schema the guard's own key
        // lookup is the first thing that touches the database, and the raw
        // provider exception escaped instead of the documented
        // InvalidOperationException. Fail-closed is right — an external surface
        // must not open while nothing can prove a key exists — but the operator
        // has to be told WHICH rule stopped the host.
        var thrown = await Should.ThrowAsync<InvalidOperationException>(async () =>
        {
            await using var host = await TraconTestHost.StartAsync(
                configureServices: static services => services.Replace(
                    ServiceDescriptor.Singleton<IApiKeyStore>(new UnavailableApiKeyStore())),
                configureTracon: static builder => builder
                    .AddAgent(TestData.Definition())
                    .UseMcpServer(),
                configureEndpoints: static options => options.AllowRemoteAccess = true,
                configureAfterMap: static app => app.MapTraconMcpServer());
        });

        thrown.Message.ShouldContain("external:invoke");
    }

    private static Task<TraconTestHost> StartAsync(ISqlPersistenceDiagnostics? diagnostics = null)
        => TraconTestHost.StartAsync(
            configureServices: services =>
            {
                services.Replace(ServiceDescriptor.Singleton<IAgentDefinitionStore>(new UnavailableAgentDefinitionStore()));

                if (diagnostics is not null)
                {
                    services.Replace(ServiceDescriptor.Singleton(diagnostics));
                }
            });

    private sealed class StubSqlDiagnostics(bool canConnect, IReadOnlyList<string> pendingMigrations) : ISqlPersistenceDiagnostics
    {
        public string ProviderName => "Stub";

        public ValueTask<SqlPersistenceDiagnosticsSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default)
            => new(new SqlPersistenceDiagnosticsSnapshot
            {
                CanConnect = canConnect,
                PendingMigrations = pendingMigrations,
            });
    }

    /// <summary>A store whose every call fails the way a real provider fails when the schema is missing.</summary>
    private sealed class UnavailableAgentDefinitionStore : IAgentDefinitionStore
    {
        public ValueTask<AgentDefinition?> GetAsync(string name, CancellationToken cancellationToken = default)
            => throw Unavailable();

        public ValueTask<AgentDefinition?> GetVersionAsync(string name, int version, CancellationToken cancellationToken = default)
            => throw Unavailable();

        public ValueTask<IReadOnlyList<AgentDefinition>> ListAsync(CancellationToken cancellationToken = default)
            => throw Unavailable();

        public ValueTask<AgentDefinition> SaveAsync(AgentDefinition definition, CancellationToken cancellationToken = default)
            => throw Unavailable();

        public ValueTask<bool> DeleteAsync(string name, CancellationToken cancellationToken = default)
            => throw Unavailable();

        public ValueTask<IReadOnlyList<AgentDefinition>> ListVersionsAsync(string name, CancellationToken cancellationToken = default)
            => throw Unavailable();

        public ValueTask<AgentDefinition> RollbackAsync(string name, int version, CancellationToken cancellationToken = default)
            => throw Unavailable();

        private static MissingRelationException Unavailable()
            => new MissingRelationException("relation \"mt_secret_schema.agent_definitions\" does not exist");
    }

    private sealed class MissingRelationException(string message) : DbException(message);

    /// <summary>A key store that cannot answer, the way a never-migrated schema cannot.</summary>
    private sealed class UnavailableApiKeyStore : IApiKeyStore
    {
        public ValueTask<ApiKeyCreationResult> CreateAsync(ApiKeyDraft draft, CancellationToken cancellationToken = default)
            => throw Missing();

        public ValueTask<IReadOnlyList<ApiKeyRecord>> ListAsync(string tenantId, CancellationToken cancellationToken = default)
            => throw Missing();

        public ValueTask<ApiKeyRecord?> FindByHashAsync(ReadOnlyMemory<byte> keyHash, CancellationToken cancellationToken = default)
            => throw Missing();

        public ValueTask<bool> RevokeAsync(string tenantId, Guid id, CancellationToken cancellationToken = default)
            => throw Missing();

        public ValueTask TouchLastUsedAsync(Guid id, DateTimeOffset usedAt, CancellationToken cancellationToken = default)
            => throw Missing();

        public ValueTask<bool> HasActiveScopeAsync(ApiKeyScope scope, CancellationToken cancellationToken = default)
            => throw Missing();

        private static MissingRelationException Missing()
            => new("no such table: tracon_api_keys");
    }
}
