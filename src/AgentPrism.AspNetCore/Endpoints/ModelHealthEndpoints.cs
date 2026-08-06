using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace AgentPrism;

/// <summary>
/// Model saglayicisi saglik denetimi uclari.
/// </summary>
/// <remarks>
/// Denetim ucret uretmez: <c>GET {endpoint}/models</c> ucuna gider, model cagrisi
/// yapmaz. Sonuc onbelleklenir (varsayilan 60 sn); <c>?refresh=true</c> onbellegi
/// atlar. Bkz. <c>docs/08-SAGLAYICI-GENISLEMESI.md</c>, bolum 8.3.
/// </remarks>
internal static class ModelHealthEndpoints
{
    /// <summary>Saglik denetimi uclarini baglar.</summary>
    /// <param name="builder">Uc grubu.</param>
    /// <param name="roles">Cozulmus rol policy'leri.</param>
    public static void Map(IEndpointRouteBuilder builder, AgentPrismRolePolicies roles)
    {
        builder.MapGet("/api/models/health", async Task<Ok<IReadOnlyList<ModelProviderHealth>>> (
                ModelProviderHealthCache cache,
                bool? refresh,
                CancellationToken cancellationToken)
                => TypedResults.Ok(await cache.GetAllAsync(refresh ?? false, cancellationToken).ConfigureAwait(false)))
            .RequireRole(roles.Reader)
            .WithName("AgentPrismModelsHealth")
            .WithTags("AgentPrism", "Models")
            .WithSummary("Tum kayitli saglayicilarin onbellekli saglik durumunu dondurur.")
            .WithDescription(
                "Bir saglayici IModelProviderHealthCheck uygulamiyorsa durumu Unknown'dir; " +
                "bu bir hata degildir. Devresi acik olan bir saglayici Unhealthy olarak " +
                "gorunur. Hata detayi API anahtari veya uc adresi ICERMEZ.");

        builder.MapGet("/api/models/health/{provider}", async Task<Results<Ok<ModelProviderHealth>, ProblemHttpResult>> (
                string provider,
                ModelProviderHealthCache cache,
                bool? refresh,
                CancellationToken cancellationToken) =>
            {
                var health = await cache.GetAsync(provider, refresh ?? false, cancellationToken).ConfigureAwait(false);

                return health is null
                    ? TypedResults.Problem(
                        title: "Saglayici bulunamadi",
                        detail: $"'{provider}' adinda kayitli bir model saglayicisi yok.",
                        statusCode: StatusCodes.Status404NotFound)
                    : TypedResults.Ok(health);
            })
            .RequireRole(roles.Reader)
            .WithName("AgentPrismModelHealth")
            .WithTags("AgentPrism", "Models")
            .WithSummary("Tek bir saglayicinin onbellekli saglik durumunu dondurur.");
    }
}
