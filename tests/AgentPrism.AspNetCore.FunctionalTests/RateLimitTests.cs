using System.Net;
using AgentPrism.AspNetCore.FunctionalTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace AgentPrism.AspNetCore.FunctionalTests;

/// <summary>Hiz sinirinin testleri (Faz 21).</summary>
/// <remarks>
/// Hiz siniri <strong>kotadan ayri</strong> bir mekanizmadir: saniye/dakika
/// olceginde, bellekte calisir (K-158).
/// </remarks>
public sealed class RateLimitTests
{
    private static readonly Uri Meta = new("/agentprism/api/meta", UriKind.Relative);
    private static readonly Uri Agents = new("/agentprism/api/agents", UriKind.Relative);

    [Fact]
    public async Task Varsayilan_olarak_kapalidir()
    {
        // 🚨 Yukseltme yapan bir kurulum beklenmedik 429 gormemelidir (K-165).
        await using var host = await AgentPrismTestHost.StartAsync();

        for (var index = 0; index < 50; index++)
        {
            using var response = await host.Client.GetAsync(Agents);

            response.StatusCode.ShouldNotBe(HttpStatusCode.TooManyRequests);
        }
    }

    [Fact]
    public async Task Acikken_sinir_asilinca_429_doner()
    {
        await using var host = await AgentPrismTestHost.StartAsync(
            configureServices: static services => services.Configure<AgentPrismRateLimitOptions>(
                static options =>
                {
                    options.Enabled = true;
                    options.PermitLimit = 3;
                    options.Window = TimeSpan.FromMinutes(5);
                }));

        for (var index = 0; index < 3; index++)
        {
            using var allowed = await host.Client.GetAsync(Agents);

            allowed.StatusCode.ShouldNotBe(HttpStatusCode.TooManyRequests);
        }

        using var blocked = await host.Client.GetAsync(Agents);

        blocked.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        blocked.Headers.RetryAfter.ShouldNotBeNull();

        var problem = await AgentPrismTestHost.ReadJsonAsync(blocked);
        problem.GetProperty("title").GetString().ShouldBe("Rate limit exceeded");
    }

    [Fact]
    public async Task Meta_ucu_hiz_sinirindan_etkilenmez()
    {
        // Meta grubu filtresizdir: arayuz kimlik yontemini her zaman
        // ogrenebilmelidir.
        await using var host = await AgentPrismTestHost.StartAsync(
            configureServices: static services => services.Configure<AgentPrismRateLimitOptions>(
                static options =>
                {
                    options.Enabled = true;
                    options.PermitLimit = 1;
                    options.Window = TimeSpan.FromMinutes(5);
                }));

        for (var index = 0; index < 10; index++)
        {
            using var response = await host.Client.GetAsync(Meta);

            response.StatusCode.ShouldBe(HttpStatusCode.OK);
        }
    }
}
