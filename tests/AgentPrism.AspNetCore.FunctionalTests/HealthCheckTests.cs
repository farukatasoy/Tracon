using System.Net;
using AgentPrism.AspNetCore.FunctionalTests.Infrastructure;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace AgentPrism.AspNetCore.FunctionalTests;

/// <summary>
/// <c>AddAgentPrismHealthChecks()</c> + <c>/health</c> ucunun uc durumunu dogrular
/// (Faz 33, F-38). Bkz. <c>docs/33-SAGLIK-DENETIMI-VE-TESHIS.md</c>, bolum 33.3.
/// </summary>
public sealed class HealthCheckTests
{
    private static readonly Uri Health = new("/health", UriKind.Relative);

    [Fact]
    public async Task Bellek_ici_kurulum_ve_saglikli_saglayici_200_Healthy_doner()
    {
        await using var server = await FakeOpenAiCompatibleServer.StartAsync();

        await using var host = await AgentPrismTestHost.StartAsync(
            configureAgentPrism: builder => builder.UseOpenAICompatible("local-test", o => o.Endpoint = server.BaseAddress),
            configureServices: static services => services.AddHealthChecks().AddAgentPrismHealthChecks(),
            configureApp: static app => app.MapHealthChecks("/health"));

        // Onbellegi bir kez isit: teshis onbellekten okur, kendisi denetim tetiklemez.
        using (await host.Client.GetAsync(new Uri("/agentprism/api/models/health/local-test", UriKind.Relative)))
        {
        }

        using var response = await host.Client.GetAsync(Health);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).ShouldContain("Healthy");
    }

    [Fact]
    public async Task Hicbir_saglayici_denetlenmemisse_Degraded_doner()
    {
        // AgentPrismTestHost varsayilan olarak IModelProviderHealthCheck UYGULAMAYAN
        // "echo" saglayicisini kaydeder; onbellek hicbir zaman Healthy gormez.
        await using var host = await AgentPrismTestHost.StartAsync(
            configureServices: static services => services.AddHealthChecks().AddAgentPrismHealthChecks(),
            configureApp: static app => app.MapHealthChecks("/health"));

        using var response = await host.Client.GetAsync(Health);

        // ASP.NET Core Degraded icin de 200 doner (yalniz Unhealthy 503 uretir).
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).ShouldContain("Degraded");
    }

    [Fact]
    public async Task Devre_kesici_aciksa_Degraded_doner()
    {
        await using var server = await FakeOpenAiCompatibleServer.StartAsync();
        server.ChatCompletionStatusCode = Microsoft.AspNetCore.Http.StatusCodes.Status400BadRequest;

        await using var host = await AgentPrismTestHost.StartAsync(
            configureAgentPrism: builder => builder.UseOpenAICompatible("local-test", o => o.Endpoint = server.BaseAddress),
            configureServices: static services =>
            {
                services.AddHealthChecks().AddAgentPrismHealthChecks();
                services.Configure<AgentPrismOptions>(options =>
                {
                    options.CircuitBreaker.FailureThreshold = 1;
                    options.CircuitBreaker.BreakDuration = TimeSpan.FromMinutes(5);
                });
            },
            configureApp: static app => app.MapHealthChecks("/health"));

        var registry = host.Services.GetRequiredService<IModelProviderRegistry>();
        var binding = new ModelBinding { Provider = "local-test", Model = "fake-local-model" };
        Microsoft.Extensions.AI.ChatMessage[] messages = [new(Microsoft.Extensions.AI.ChatRole.User, "merhaba")];

        using (var chatClient = registry.CreateChatClient(binding))
        {
            await Should.ThrowAsync<Exception>(() => chatClient.GetResponseAsync(messages));
        }

        using var response = await host.Client.GetAsync(Health);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).ShouldContain("Degraded");
    }

    [Fact]
    public async Task Cift_SQL_kaydi_Degraded_doner()
    {
        await using var host = await AgentPrismTestHost.StartAsync(
            configureServices: static services =>
            {
                services.AddSingleton(new SqlPersistenceRegistrationMarker("PostgreSQL"));
                services.AddSingleton(new SqlPersistenceRegistrationMarker("SQLite"));
                services.AddSingleton<ISqlPersistenceDiagnostics>(new AlwaysHealthySqlDiagnostics("SQLite"));
                services.AddHealthChecks().AddAgentPrismHealthChecks();
            },
            configureApp: static app => app.MapHealthChecks("/health"));

        using var response = await host.Client.GetAsync(Health);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).ShouldContain("Degraded");
    }

    [Fact]
    public async Task Baglanti_kurulamayan_SQL_saglayicisi_503_Unhealthy_doner()
    {
        await using var host = await AgentPrismTestHost.StartAsync(
            configureServices: static services =>
            {
                services.AddSingleton(new SqlPersistenceRegistrationMarker("PostgreSQL"));
                services.AddSingleton<ISqlPersistenceDiagnostics>(new UnreachableSqlDiagnostics("PostgreSQL"));
                services.AddHealthChecks().AddAgentPrismHealthChecks();
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
