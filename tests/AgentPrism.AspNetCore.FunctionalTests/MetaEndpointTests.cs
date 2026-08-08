using System.Net;
using System.Text.Json;
using AgentPrism.AspNetCore.FunctionalTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AgentPrism.AspNetCore.FunctionalTests;

/// <summary>
/// <c>/api/meta</c> ucunun sozlesmesini dogrular.
/// </summary>
public sealed class MetaEndpointTests
{
    [Fact]
    public async Task Meta_surum_prefix_ve_kimlik_yontemini_bildirir()
    {
        await using var host = await AgentPrismTestHost.StartAsync(
            configureEndpoints: static options =>
            {
                options.AuthToken = "gizli";
                options.RequireAuthorization("Yonetici");
            },
            configureServices: static services => TestAuthenticationHandler.Add(services)
                .AddAuthorizationBuilder()
                .AddPolicy("Yonetici", static policy => policy.RequireAssertion(static _ => true)));

        using var response = await host.Client.GetAsync(new Uri("/agentprism/api/meta", UriKind.Relative));
        var json = await AgentPrismTestHost.ReadJsonAsync(response);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        json.GetProperty("prefix").GetString().ShouldBe("/agentprism");
        json.GetProperty("version").GetString().ShouldNotBeNullOrWhiteSpace();

        var auth = json.GetProperty("authentication");
        auth.GetProperty("allowRemoteAccess").GetBoolean().ShouldBeFalse();
        auth.GetProperty("requiresBearerToken").GetBoolean().ShouldBeTrue();
        auth.GetProperty("requiresAuthorizationPolicy").GetBoolean().ShouldBeTrue();
    }

    [Fact]
    public async Task Meta_ozel_prefixi_bildirir()
    {
        await using var host = await AgentPrismTestHost.StartAsync(prefix: "/yonetim");

        using var response = await host.Client.GetAsync(new Uri("/yonetim/api/meta", UriKind.Relative));
        var json = await AgentPrismTestHost.ReadJsonAsync(response);

        json.GetProperty("prefix").GetString().ShouldBe("/yonetim");
    }

    [Fact]
    public async Task Meta_bellek_ici_depolari_kalici_olmayan_olarak_bildirir()
    {
        await using var host = await AgentPrismTestHost.StartAsync();

        using var response = await host.Client.GetAsync(new Uri("/agentprism/api/meta", UriKind.Relative));
        var storage = (await AgentPrismTestHost.ReadJsonAsync(response)).GetProperty("storage");

        storage.GetProperty("persistent").GetBoolean().ShouldBeFalse();
        storage.GetProperty("runStore").GetString().ShouldBe(nameof(InMemoryRunStore));
        storage.GetProperty("sessionStore").GetString().ShouldBe(nameof(InMemorySessionStore));
        storage.GetProperty("agentDefinitionStore").GetString().ShouldBe(nameof(InMemoryAgentDefinitionStore));
    }

    [Fact]
    public async Task Meta_tuketicinin_kendi_deposunu_bildirir()
    {
        // K4: tuketicinin kaydi kazanir. /api/meta bunu gorunur kilar; boylece
        // kalicilik sessizce devre disi kalmis olsa bile fark edilir.
        await using var host = await AgentPrismTestHost.StartAsync(
            configureServices: static services =>
                services.TryAddSingleton<IRunStore, CustomRunStore>());

        using var response = await host.Client.GetAsync(new Uri("/agentprism/api/meta", UriKind.Relative));
        var storage = (await AgentPrismTestHost.ReadJsonAsync(response)).GetProperty("storage");

        storage.GetProperty("runStore").GetString().ShouldBe(nameof(CustomRunStore));
        storage.GetProperty("persistent").GetBoolean().ShouldBeFalse();
    }

    [Fact]
    public async Task Meta_sir_icermez()
    {
        const string Secret = "cok-gizli-token-DENEME-7b2e";

        await using var host = await AgentPrismTestHost.StartAsync(
            configureEndpoints: static options =>
            {
                options.AuthToken = Secret;
                options.RequireAuthorization("Cok-Ozel-Policy-Adi");
            },
            configureServices: static services => TestAuthenticationHandler.Add(services)
                .AddAuthorizationBuilder()
                .AddPolicy("Cok-Ozel-Policy-Adi", static policy => policy.RequireAssertion(static _ => true)));

        using var response = await host.Client.GetAsync(new Uri("/agentprism/api/meta", UriKind.Relative));
        var body = await response.Content.ReadAsStringAsync();

        body.ShouldNotContain(Secret);

        // Policy ADI da donmez: arayuz o adla bir sey yapamaz ve ad, kimlik
        // dogrulamasi olmayan bir uctan sizan yapilandirma ayrintisidir.
        body.ShouldNotContain("Cok-Ozel-Policy-Adi", Case.Sensitive);
    }

    /// <summary>Tuketicinin kendi deposunu taklit eden bos uygulama.</summary>
    private sealed class CustomRunStore : IRunStore
    {
        public ValueTask<RunRecord> StartRunAsync(RunStartInfo info, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public ValueTask AppendEventAsync(RunEvent runEvent, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

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
