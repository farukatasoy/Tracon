using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// Verifies the contract of the <c>/api/meta</c> endpoint.
/// </summary>
public sealed class MetaEndpointTests
{
    [Fact]
    public async Task Meta_reports_version_prefix_and_authentication_method()
    {
        await using var host = await TraconTestHost.StartAsync(
            configureEndpoints: static options =>
            {
                options.AuthToken = "secret";
                options.RequireAuthorization("Administrator");
            },
            configureServices: static services => TestAuthenticationHandler.Add(services)
                .AddAuthorizationBuilder()
                .AddPolicy("Administrator", static policy => policy.RequireAssertion(static _ => true)));

        using var response = await host.Client.GetAsync(new Uri("/tracon/api/meta", UriKind.Relative));
        var json = await TraconTestHost.ReadJsonAsync(response);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        json.GetProperty("prefix").GetString().ShouldBe("/tracon");
        json.GetProperty("version").GetString().ShouldNotBeNullOrWhiteSpace();

        var auth = json.GetProperty("authentication");
        auth.GetProperty("allowRemoteAccess").GetBoolean().ShouldBeFalse();
        auth.GetProperty("requiresBearerToken").GetBoolean().ShouldBeTrue();
        auth.GetProperty("requiresAuthorizationPolicy").GetBoolean().ShouldBeTrue();
    }

    [Fact]
    public async Task Meta_reports_a_custom_prefix()
    {
        await using var host = await TraconTestHost.StartAsync(prefix: "/management");

        using var response = await host.Client.GetAsync(new Uri("/management/api/meta", UriKind.Relative));
        var json = await TraconTestHost.ReadJsonAsync(response);

        json.GetProperty("prefix").GetString().ShouldBe("/management");
    }

    [Fact]
    public async Task Meta_reports_in_memory_stores_as_non_persistent()
    {
        await using var host = await TraconTestHost.StartAsync();

        using var response = await host.Client.GetAsync(new Uri("/tracon/api/meta", UriKind.Relative));
        var storage = (await TraconTestHost.ReadJsonAsync(response)).GetProperty("storage");

        storage.GetProperty("persistent").GetBoolean().ShouldBeFalse();
        storage.GetProperty("runStore").GetString().ShouldBe(nameof(InMemoryRunStore));
        storage.GetProperty("sessionStore").GetString().ShouldBe(nameof(InMemorySessionStore));
        storage.GetProperty("agentDefinitionStore").GetString().ShouldBe(nameof(InMemoryAgentDefinitionStore));
    }

    [Fact]
    public async Task Meta_reports_the_consumers_own_store()
    {
        // K4: the consumer's registration wins. /api/meta makes this visible,
        // so persistence being silently disabled is still noticed.
        await using var host = await TraconTestHost.StartAsync(
            configureServices: static services =>
                services.TryAddSingleton<IRunStore, CustomRunStore>());

        using var response = await host.Client.GetAsync(new Uri("/tracon/api/meta", UriKind.Relative));
        var storage = (await TraconTestHost.ReadJsonAsync(response)).GetProperty("storage");

        storage.GetProperty("runStore").GetString().ShouldBe(nameof(CustomRunStore));
        storage.GetProperty("persistent").GetBoolean().ShouldBeFalse();
    }

    [Fact]
    public async Task Meta_contains_no_secret()
    {
        const string Secret = "super-secret-token-TEST-7b2e";

        await using var host = await TraconTestHost.StartAsync(
            configureEndpoints: static options =>
            {
                options.AuthToken = Secret;
                options.RequireAuthorization("Very-Special-Policy-Name");
            },
            configureServices: static services => TestAuthenticationHandler.Add(services)
                .AddAuthorizationBuilder()
                .AddPolicy("Very-Special-Policy-Name", static policy => policy.RequireAssertion(static _ => true)));

        using var response = await host.Client.GetAsync(new Uri("/tracon/api/meta", UriKind.Relative));
        var body = await response.Content.ReadAsStringAsync();

        body.ShouldNotContain(Secret);

        // The policy NAME does not come back either: the UI cannot do anything
        // with that name, and it is a configuration detail that would leak
        // from an unauthenticated endpoint.
        body.ShouldNotContain("Very-Special-Policy-Name", Case.Sensitive);
    }

    /// <summary>An empty implementation that fakes the consumer's own store.</summary>
    private sealed class CustomRunStore : IRunStore
    {
        public ValueTask<RunRecord> StartRunAsync(RunStartInfo info, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public ValueTask AppendEventAsync(RunEvent runEvent, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public ValueTask<long?> GetLastEventSequenceAsync(Guid runId, CancellationToken cancellationToken = default)
            => ValueTask.FromResult<long?>(null);

        public ValueTask CompleteRunAsync(RunCompletion completion, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public ValueTask<RunRecord?> GetRunAsync(Guid runId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public ValueTask<IReadOnlyList<RunRecord>> QueryRunsAsync(RunQuery query, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public ValueTask<RunStatistics> GetStatisticsAsync(RunStatisticsQuery query, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public IAsyncEnumerable<RunEvent> ReadEventsAsync(Guid runId, long fromSequence = 0, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public ValueTask RecordToolInvocationAsync(ToolInvocationRecord invocation, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public ValueTask<IReadOnlyList<ToolInvocationRecord>> ListToolInvocationsAsync(Guid runId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public ValueTask<IReadOnlyList<ToolUsage>> GetToolUsageAsync(ToolUsageQuery query, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public ValueTask<IReadOnlyList<ExperimentVariantResult>> GetExperimentResultsAsync(ExperimentResultsQuery query, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public ValueTask<IReadOnlyList<TimeSeriesPoint>> GetTimeSeriesAsync(RunTimeSeriesQuery query, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public ValueTask UpdateRunCostAsync(
            Guid runId,
            RunCost? cost,
            string? tenantId = null,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public ValueTask TouchHeartbeatAsync(
            IReadOnlyCollection<Guid> runIds,
            DateTimeOffset at,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public ValueTask<IReadOnlyList<RunRecord>> ClaimOrphanedRunsAsync(
            DateTimeOffset staleBefore,
            int max,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }
}
