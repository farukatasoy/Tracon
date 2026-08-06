using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace AgentPrism;

/// <summary>Kurulumun kendi kendini denetleyen teshis ucu (Faz 33, F-62).</summary>
/// <remarks>
/// <para>
/// 🚨 Yanit hicbir <c>secret</c> degeri tasimaz (K-059): yalniz yapilandirma
/// anahtarinin adi ve cozulup cozulmedigi bilgisi doner, deger hicbir kosulda yer
/// almaz.
/// </para>
/// <para>
/// Varsayilan <strong>kapali</strong> — <see cref="AgentPrismEndpointOptions.EnableDiagnosticsEndpoint"/>
/// acilmadikca bu uc hic baglanmaz. Acikken <see cref="AgentPrismPolicies.Admin"/> ister.
/// </para>
/// </remarks>
internal static class DiagnosticsEndpoints
{
    /// <summary>Teshis ucunu baglar.</summary>
    /// <param name="builder">Uc grubu.</param>
    /// <param name="services">Kurulu servis saglayici. Arayuz gomulu mu bilgisini bir kez cozmek icindir.</param>
    /// <param name="roles">Cozulmus rol policy'leri.</param>
    public static void Map(IEndpointRouteBuilder builder, IServiceProvider services, AgentPrismRolePolicies roles)
    {
        // IAgentPrismUiProvider kayitli olmayabilir (AgentPrism.UI paketi eklenmemis
        // olabilir). Faz 28 dersi: nullable bir servisi endpoint parametresi olarak
        // isaretlemek yerine, MapUi'nin izledigi desenle burada BIR KEZ cozulur ve
        // kapanista yakalanir; kayitli olmayan servis "govde" saniIp tum uclari kirmaz.
        var uiProvider = services.GetService<IAgentPrismUiProvider>();

        builder.MapGet("/api/diagnostics", async Task<Ok<AgentPrismDiagnosticsReport>> (
                [FromServices] AgentPrismDiagnosticsCollector collector,
                CancellationToken cancellationToken) =>
            {
                var report = await collector.CollectAsync(cancellationToken).ConfigureAwait(false);

                return TypedResults.Ok(report with { UiEmbedded = uiProvider?.HasAssets ?? false });
            })
            .RequireRole(roles.Admin)
            .WithName("AgentPrismDiagnostics")
            .WithTags("AgentPrism", "Diagnostics")
            .WithSummary("Kurulumun kendi kendini denetleyen ozet raporunu dondurur.")
            .WithDescription(
                "Hicbir secret degeri tasimaz (K-059). Model saglayicisi durumu onbellekten " +
                "okunur; hicbir model cagrisi veya migration uygulamasi yapmaz.");
    }
}
