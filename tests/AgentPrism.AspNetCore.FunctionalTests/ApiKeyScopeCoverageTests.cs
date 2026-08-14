using AgentPrism.AspNetCore.FunctionalTests.Infrastructure;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace AgentPrism.AspNetCore.FunctionalTests;

/// <summary>
/// Regresyon citi (Aile F, manuel kabul testi kapanisi): korumali gruptaki HER
/// API ucu bir <c>ApiKeyScopeRequirement</c> tasimalidir — yorumlu, sabit
/// sayida bir izin listesi disinda.
/// </summary>
/// <remarks>
/// 🚨 Bu test 21 dosyalik "kapsam denetimi yok" bosluguna (HATA-S2-009,
/// HATA-S2-011, HATA-S3-009) geri donusu engeller. Yeni bir uc eklenip
/// <c>RequireApiKeyScope</c> unutulursa bu test derleme zamaninda degil ama
/// ilk CI kosumunda kirilir — sessiz bosluk yeniden acilmaz.
/// </remarks>
public sealed class ApiKeyScopeCoverageTests
{
    /// <summary>
    /// Bearer token denetiminden GECEN ama <c>ApiKeyScopeRequirement</c>
    /// tasimayan uclar (docs/manuel-test/KAPANIS-PLANI.md §7.2 + Aile F
    /// notu). Her biri gerekcelidir; listeye eklemek bilincli bir karardir.
    /// Varsayilan test barindiricisinda HER ZAMAN esler (UI saglayicisi ve
    /// konusma surucusu kayitli degilken UI kabugu ve konusma ucu hic
    /// baglanmaz — bu ikisi <see cref="ConditionallyMappedExemptRoutePatterns"/>'de).
    /// </summary>
    private static readonly HashSet<string> ExemptRoutePatterns = new(StringComparer.Ordinal)
    {
        // Kimlik dogrulama olmadan erisilir; hicbir hassas veri tasimaz.
        "/agentprism/api/meta",
        // Saglayicinin yonlendirdigi tarayici bearer token tasiyamaz.
        "/agentprism/api/mcp-servers/{name}/oauth/callback",
        // Cagiranin kendi kimliginin tarifi; kapsam eklemek her dar kapsamli
        // anahtarin acilis yoklamasini gereksiz yere kirar.
        "/agentprism/api/tenants/current",
    };

    /// <summary>
    /// UI saglayicisi veya konusma surucusu kayitliyken ORTAYA CIKAN muaf
    /// uclar. Varsayilan test barindiricisi ikisini de kaydetmedigi icin bu
    /// satirlar <see cref="Muafiyet_listesindeki_her_satir_gercekten_haritada_var"/>
    /// icin denenmez — yalnizca ilk testin (var-olursa-muaf) kapsam disinda
    /// tutulmasi icin kullanilir.
    /// </summary>
    private static readonly HashSet<string> ConditionallyMappedExemptRoutePatterns = new(StringComparer.Ordinal)
    {
        // SPA kabugu: <script src> Authorization gonderemez, kabuk veri tasimaz.
        "/agentprism/",
        "/agentprism/{**path}",
        // Token'i statik AuthToken'a karsi KENDISI dogrular; API anahtarlari
        // bu ucu bugun hic acamaz (metadata eklemek inert olurdu).
        "/agentprism/api/voice/sessions/{sessionId}/stream",
    };

    [Fact]
    public async Task Korumali_gruptaki_her_uc_kapsam_tasir_veya_acikca_muaftir()
    {
        await using var host = await AgentPrismTestHost.StartAsync(
            configureEndpoints: static options => options.EnableDiagnosticsEndpoint = true);

        var dataSource = host.Services.GetRequiredService<EndpointDataSource>();

        var uncovered = new List<string>();

        foreach (var endpoint in dataSource.Endpoints)
        {
            if (endpoint is not RouteEndpoint routeEndpoint)
            {
                continue;
            }

            var rawText = routeEndpoint.RoutePattern.RawText ?? string.Empty;

            // Yalnizca AgentPrism API ucu adaylarini denetle: "/api/" veya
            // "/v1/" segmenti tasiyanlar. SPA kabugu ve varlik rotalari ayri
            // muafiyet satirlariyla acikca ele alinir.
            if (!rawText.Contains("/api/", StringComparison.Ordinal)
                && !rawText.Contains("/v1/", StringComparison.Ordinal))
            {
                continue;
            }

            if (ExemptRoutePatterns.Contains(rawText) || ConditionallyMappedExemptRoutePatterns.Contains(rawText))
            {
                continue;
            }

            if (routeEndpoint.Metadata.GetMetadata<ApiKeyScopeRequirement>() is null)
            {
                uncovered.Add($"{rawText} ({string.Join(", ", GetHttpMethods(routeEndpoint))})");
            }
        }

        uncovered.ShouldBeEmpty();
    }

    [Fact]
    public async Task Muafiyet_listesindeki_her_satir_gercekten_haritada_var()
    {
        // Ters yon: listedeki bir satir artik hic eslesmiyorsa (rota tasindi/
        // silindi), muafiyet sessizce anlamsizlasir — bunu da yakala. Yalniz
        // HER zaman esen 3 satir denenir; UI kabugu ve konusma ucu bu
        // barindiricida hic baglanmadigi icin kapsam disidir.
        await using var host = await AgentPrismTestHost.StartAsync(
            configureEndpoints: static options => options.EnableDiagnosticsEndpoint = true);

        var dataSource = host.Services.GetRequiredService<EndpointDataSource>();

        var actualPatterns = dataSource.Endpoints
            .OfType<RouteEndpoint>()
            .Select(static e => e.RoutePattern.RawText ?? string.Empty)
            .ToHashSet(StringComparer.Ordinal);

        var missing = ExemptRoutePatterns.Where(pattern => !actualPatterns.Contains(pattern)).ToList();

        missing.ShouldBeEmpty();
    }

    private static IEnumerable<string> GetHttpMethods(RouteEndpoint endpoint)
        => endpoint.Metadata.GetMetadata<Microsoft.AspNetCore.Routing.HttpMethodMetadata>()?.HttpMethods
           ?? ["?"];
}
