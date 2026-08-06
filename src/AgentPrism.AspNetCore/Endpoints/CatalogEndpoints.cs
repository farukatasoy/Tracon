using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;

namespace AgentPrism;

/// <summary>
/// Salt okunur defter uclari: tool'lar, model saglayicilari ve calistirma ozeti.
/// </summary>
internal static class CatalogEndpoints
{
    /// <summary>Defter uclarini baglar.</summary>
    /// <param name="builder">Uc grubu.</param>
    /// <param name="roles">Cozulmus rol policy'leri.</param>
    public static void Map(IEndpointRouteBuilder builder, AgentPrismRolePolicies roles)
    {
        builder.MapGet("/api/tools", Ok<IReadOnlyList<ToolDescriptor>> (IToolRegistry tools)
                => TypedResults.Ok(tools.List()))
            .RequireRole(roles.Reader)
            .WithName("AgentPrismListTools")
            .WithTags("AgentPrism", "Agents")
            .WithSummary("Kayitli tool'lari ve JSON semalarini listeler.")
            .WithDescription(
                "Tool'lar yalnizca kodda tanimlanir. Bu uc bir yazma yolu sunmaz; " +
                "arayuz agent tanimlarken bu listeden secim yaptirir.");

        builder.MapGet("/api/models", Ok<IReadOnlyList<ModelProviderDescriptor>> (
                IModelProviderRegistry models,
                ModelProviderHealthCache healthCache) =>
            {
                var descriptors = models.List();
                var withStatus = new List<ModelProviderDescriptor>(descriptors.Count);

                foreach (var descriptor in descriptors)
                {
                    withStatus.Add(healthCache.TryPeek(descriptor.Name, out var health)
                        ? descriptor with { Status = health.Status }
                        : descriptor);
                }

                return TypedResults.Ok<IReadOnlyList<ModelProviderDescriptor>>(withStatus);
            })
            .RequireRole(roles.Reader)
            .WithName("AgentPrismListModels")
            .WithTags("AgentPrism", "Agents")
            .WithSummary("Kayitli model saglayicilarini ve modellerini listeler.")
            .WithDescription(
                "Model katalogu yapilandirmadan gelir; AgentPrism yerlesik model listesi tasimaz. " +
                "Bos liste bir hata degildir. Katalog bir dogrulama listesi de degildir: " +
                "burada olmayan bir model adi da kullanilabilir. `status` alani ONBELLEKTEN " +
                "gelir ve bu uc saglayiciya ag cagrisi yapmaz; guncel bir denetim icin " +
                "/api/models/health kullanin.");

        builder.MapGet("/api/stats", async Task<Ok<RunStatistics>> (
                IRunStore runs,
                string? agentName,
                DateTimeOffset? startedAfter,
                int? maxAgents,
                CancellationToken cancellationToken) =>
            {
                var statistics = await runs.GetStatisticsAsync(
                    new RunStatisticsQuery
                    {
                        AgentName = agentName,
                        StartedAfter = startedAfter,
                        MaxAgents = maxAgents is { } max ? Math.Clamp(max, 0, 200) : 20,
                    },
                    cancellationToken).ConfigureAwait(false);

                return TypedResults.Ok(statistics);
            })
            .RequireRole(roles.Reader)
            .WithName("AgentPrismStats")
            .WithTags("AgentPrism", "Agents")
            .WithSummary("Calistirma sayilarini, token toplamlarini ve hata oranini dondurur.")
            .WithDescription(
                "Ozet deponun kendisinde hesaplanir. Maliyet (Faz 20) yalniz fiyat " +
                "yapilandirildiginda (model kataloğu veya AgentPrism:Pricing) doludur; " +
                "fiyati tanimsiz modellerin sayisi RunsWithUnknownPricing alaninda " +
                "ayrica sayilir — sifir yazilmaz.");

        builder.MapGet("/api/stats/timeseries", async Task<Results<Ok<IReadOnlyList<TimeSeriesPoint>>, ProblemHttpResult>> (
                IRunStore runs,
                DateTimeOffset? from,
                DateTimeOffset? to,
                TimeSeriesBucket? bucket,
                string? agentName,
                string? modelId,
                RunKind? kind,
                [FromServices] TimeProvider? timeProvider,
                CancellationToken cancellationToken) =>
            {
                var effectiveTo = to ?? (timeProvider ?? TimeProvider.System).GetUtcNow();
                var effectiveFrom = from ?? effectiveTo.AddHours(-24);

                if (effectiveFrom >= effectiveTo)
                {
                    return TypedResults.Problem(
                        title: "Aralik gecersiz",
                        detail: "'from' 'to''dan once olmalidir.",
                        statusCode: StatusCodes.Status400BadRequest);
                }

                try
                {
                    var points = await runs.GetTimeSeriesAsync(
                        new RunTimeSeriesQuery
                        {
                            From = effectiveFrom,
                            To = effectiveTo,
                            Bucket = bucket ?? TimeSeriesBucket.Hour,
                            AgentName = agentName,
                            ModelId = modelId,
                            Kind = kind,
                        },
                        cancellationToken).ConfigureAwait(false);

                    return TypedResults.Ok(points);
                }
                catch (AgentPrismException ex)
                {
                    return TypedResults.Problem(
                        title: "Kova sayisi asildi",
                        detail: ex.Message,
                        statusCode: StatusCodes.Status400BadRequest);
                }
            })
            .RequireRole(roles.Reader)
            .WithName("AgentPrismStatsTimeSeries")
            .WithTags("AgentPrism", "Agents")
            .WithSummary("Kova basina calistirma, hata, token ve maliyet zaman serisi.")
            .WithDescription(
                "Bos kovalar da doner. Varsayilan aralik son 24 saat, varsayilan kova " +
                "saattir. En fazla 500 kova; asilirsa 400. Bu uc, /api/stats'in aksine " +
                "Eval/Workflow calistirmalarini varsayilan olarak haric TUTMAZ " +
                "(bkz. docs/KARARLAR.md K-152); ?kind= ile filtrelenebilir.");

        builder.MapPost("/api/stats/recalculate-costs", async Task<Ok<RunCostRecalculationResult>> (
                RunCostRecalculationService recalculation,
                IAuditLog auditLog,
                IAuditActorResolver actorResolver,
                ITenantContext tenants,
                ILoggerFactory loggerFactory,
                CancellationToken cancellationToken) =>
            {
                var result = await recalculation.RecalculateAsync(tenants.TenantId, cancellationToken)
                    .ConfigureAwait(false);

                await AuditRecorder.WriteAsync(
                    auditLog,
                    actorResolver,
                    loggerFactory.CreateLogger("AgentPrism.CatalogEndpoints"),
                    tenants.TenantId,
                    action: "stats.recalculate-costs",
                    entity: "runs:*",
                    before: null,
                    after: $$"""
                        {"considered":{{result.RunsConsidered}},"updated":{{result.RunsUpdated}},"stillUnknown":{{result.RunsStillUnknown}}}
                        """,
                    cancellationToken).ConfigureAwait(false);

                return TypedResults.Ok(result);
            })
            .RequireRole(roles.Admin)
            .WithName("AgentPrismRecalculateCosts")
            .WithTags("AgentPrism", "Agents")
            .WithSummary("Tum calistirmalarin maliyetini guncel fiyat kaynagina gore yeniden hesaplar.")
            .WithDescription(
                "Bakim ucudur. Fiyat sonradan tanimlandiginda gecmis calistirmalari " +
                "tazelemek icin kullanilir. Saglayici gecmis satirlarda tutulmaz; ayni " +
                "model adi birden fazla saglayicida tanimliysa alfabetik ilk eslesen " +
                "kazanir (bkz. docs/KARARLAR.md K-154). Admin ister; cagri denetim " +
                "izine yazilir.");
    }
}
