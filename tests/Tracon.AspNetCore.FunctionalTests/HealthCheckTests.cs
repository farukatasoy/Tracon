using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http;
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
    public async Task No_provider_probed_yet_returns_Healthy_and_says_nothing_has_been_probed()
    {
        // This test used to assert Degraded, which locked the defect in: nothing
        // probes a provider on its own, so a correctly installed application —
        // database reachable, migrations applied, keys resolved — reported
        // Degraded forever and an operator could not tell a real outage from the
        // resting state. The absence of a measurement is not a fault.
        //
        // TraconTestHost registers the "echo" provider by default, which does NOT
        // implement IModelProviderHealthCheck; the cache never sees Healthy.
        await using var host = await TraconTestHost.StartAsync(
            configureServices: static services => services.AddHealthChecks()
                .AddTraconHealthChecks(),
            configureApp: static app => app.MapHealthChecks(
                "/health",
                new HealthCheckOptions
                {
                    ResponseWriter = static async (context, report) =>
                    {
                        context.Response.ContentType = "text/plain";
                        await context.Response.WriteAsync(
                            report.Status + "|" + string.Join(
                                "|",
                                report.Entries.Select(entry => entry.Value.Description)));
                    },
                }));

        using var response = await host.Client.GetAsync(Health);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();

        body.ShouldStartWith("Healthy");
        body.ShouldContain("has been probed yet");
    }

    [Fact]
    public async Task A_probed_and_unhealthy_provider_still_returns_Degraded_and_names_it()
    {
        // The other side of the same branch: once something HAS been measured
        // and came back bad, the indicator must go yellow. A fake server that
        // refuses the health probe gives the cache a real Unhealthy entry.
        await using var server = await FakeOpenAiCompatibleServer.StartAsync();
        server.ModelsStatusCode = Microsoft.AspNetCore.Http.StatusCodes.Status500InternalServerError;

        await using var host = await TraconTestHost.StartAsync(
            configureTracon: builder => builder.UseOpenAICompatible("local-test", o => o.Endpoint = server.BaseAddress),
            configureServices: static services => services.AddHealthChecks().AddTraconHealthChecks(),
            configureApp: static app => app.MapHealthChecks("/health"));

        // Probe once so the cache holds a measured result rather than Unknown.
        using (await host.Client.GetAsync(new Uri("/tracon/api/models/health/local-test", UriKind.Relative)))
        {
        }

        using var response = await host.Client.GetAsync(Health);

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
