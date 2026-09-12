using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// Verifies edge cases of <c>AddTraconHealthChecks()</c> + the <c>/health</c> endpoint
/// (Phase 33, F-38). See <c>docs/arsiv/fazlar/33-SAGLIK-DENETIMI-VE-TESHIS.md</c>, section 33.3.
/// </summary>
public sealed class HealthCheckTests
{
    private static readonly Uri Health = new("/health", UriKind.Relative);

    [Fact]
    public async Task In_memory_setup_with_a_healthy_provider_returns_200_Healthy()
    {
        await using var server = await FakeOpenAiCompatibleServer.StartAsync();

        await using var host = await TraconTestHost.StartAsync(
            configureTracon: builder => builder.UseOpenAICompatible("local-test", o => o.Endpoint = server.BaseAddress),
            configureServices: static services => services.AddHealthChecks().AddTraconHealthChecks(),
            configureApp: static app => app.MapHealthChecks("/health"));

        // Warm the cache once: diagnostics reads from the cache, it does not itself trigger a check.
        using (await host.Client.GetAsync(new Uri("/tracon/api/models/health/local-test", UriKind.Relative)))
        {
        }

        using var response = await host.Client.GetAsync(Health);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).ShouldContain("Healthy");
    }

    [Fact]
    public async Task No_provider_checked_yet_returns_Degraded()
    {
        // TraconTestHost registers the "echo" provider by default, which does NOT
        // implement IModelProviderHealthCheck; the cache never sees Healthy.
        await using var host = await TraconTestHost.StartAsync(
            configureServices: static services => services.AddHealthChecks().AddTraconHealthChecks(),
            configureApp: static app => app.MapHealthChecks("/health"));

        using var response = await host.Client.GetAsync(Health);

        // ASP.NET Core also returns 200 for Degraded (only Unhealthy produces 503).
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).ShouldContain("Degraded");
    }

    [Fact]
    public async Task Open_circuit_breaker_returns_Degraded()
    {
        await using var server = await FakeOpenAiCompatibleServer.StartAsync();
        server.ChatCompletionStatusCode = Microsoft.AspNetCore.Http.StatusCodes.Status400BadRequest;

        await using var host = await TraconTestHost.StartAsync(
            configureTracon: builder => builder.UseOpenAICompatible("local-test", o => o.Endpoint = server.BaseAddress),
            configureServices: static services =>
            {
                services.AddHealthChecks().AddTraconHealthChecks();
                services.Configure<TraconOptions>(options =>
                {
                    options.CircuitBreaker.FailureThreshold = 1;
                    options.CircuitBreaker.BreakDuration = TimeSpan.FromMinutes(5);
                });
            },
            configureApp: static app => app.MapHealthChecks("/health"));

        var registry = host.Services.GetRequiredService<IModelProviderRegistry>();
        var binding = new ModelBinding { Provider = "local-test", Model = "fake-local-model" };
        Microsoft.Extensions.AI.ChatMessage[] messages = [new(Microsoft.Extensions.AI.ChatRole.User, "hello")];

        using (var chatClient = registry.CreateChatClient(binding))
        {
            await Should.ThrowAsync<Exception>(() => chatClient.GetResponseAsync(messages));
        }

        using var response = await host.Client.GetAsync(Health);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).ShouldContain("Degraded");
    }

    [Fact]
    public async Task Duplicate_SQL_registration_returns_Degraded()
    {
        await using var host = await TraconTestHost.StartAsync(
            configureServices: static services =>
            {
                services.AddSingleton(new SqlPersistenceRegistrationMarker("PostgreSQL"));
                services.AddSingleton(new SqlPersistenceRegistrationMarker("SQLite"));
                services.AddSingleton<ISqlPersistenceDiagnostics>(new AlwaysHealthySqlDiagnostics("SQLite"));
                services.AddHealthChecks().AddTraconHealthChecks();
            },
            configureApp: static app => app.MapHealthChecks("/health"));

        using var response = await host.Client.GetAsync(Health);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).ShouldContain("Degraded");
    }

    [Fact]
    public async Task Unreachable_SQL_provider_returns_503_Unhealthy()
    {
        await using var host = await TraconTestHost.StartAsync(
            configureServices: static services =>
            {
                services.AddSingleton(new SqlPersistenceRegistrationMarker("PostgreSQL"));
                services.AddSingleton<ISqlPersistenceDiagnostics>(new UnreachableSqlDiagnostics("PostgreSQL"));
                services.AddHealthChecks().AddTraconHealthChecks();
            },
            configureApp: static app => app.MapHealthChecks("/health"));

        using var response = await host.Client.GetAsync(Health);

        response.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        (await response.Content.ReadAsStringAsync()).ShouldContain("Unhealthy");
    }

    private sealed class AlwaysHealthySqlDiagnostics(string providerName) : ISqlPersistenceDiagnostics
    {
        public string ProviderName { get; } = providerName;

        public ValueTask<SqlPersistenceDiagnosticsSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default)
            => ValueTask.FromResult(new SqlPersistenceDiagnosticsSnapshot { CanConnect = true, PendingMigrations = [] });
    }

    private sealed class UnreachableSqlDiagnostics(string providerName) : ISqlPersistenceDiagnostics
    {
        public string ProviderName { get; } = providerName;

        public ValueTask<SqlPersistenceDiagnosticsSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default)
            => ValueTask.FromResult(new SqlPersistenceDiagnosticsSnapshot { CanConnect = false, PendingMigrations = [] });
    }
}
