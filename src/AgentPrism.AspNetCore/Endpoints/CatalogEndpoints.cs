using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace AgentPrism;

/// <summary>
/// Salt okunur defter uclari: tool'lar, model saglayicilari ve calistirma ozeti.
/// </summary>
internal static class CatalogEndpoints
{
    /// <summary>Defter uclarini baglar.</summary>
    /// <param name="builder">Uc grubu.</param>
    public static void Map(IEndpointRouteBuilder builder)
    {
        builder.MapGet("/api/tools", Ok<IReadOnlyList<ToolDescriptor>> (IToolRegistry tools)
                => TypedResults.Ok(tools.List()))
            .WithName("AgentPrismListTools")
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
            .WithName("AgentPrismListModels")
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
            .WithName("AgentPrismStats")
            .WithSummary("Calistirma sayilarini, token toplamlarini ve hata oranini dondurur.")
            .WithDescription(
                "Ozet deponun kendisinde hesaplanir. Maliyet bu ozette YOKTUR: maliyet, " +
                "kullanilan model adiyla model kataloguna bakmayi gerektirir ve calistirma " +
                "kaydi model adini tasimaz. Maliyet Faz 6'da telemetri ile gelir.");
    }
}
