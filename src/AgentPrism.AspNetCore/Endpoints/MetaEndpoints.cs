using System.Reflection;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;

namespace AgentPrism;

/// <summary>
/// Kimlik dogrulamasi gerektirmeyen tanitim ucu.
/// </summary>
internal static class MetaEndpoints
{
    /// <summary>Meta ucunu baglar.</summary>
    /// <param name="builder">Uc grubu.</param>
    /// <param name="options">Erisim ayarlari.</param>
    /// <param name="prefix">Uclarin baglandigi yol oneki.</param>
    /// <param name="roles">Cozulmus rol policy'leri.</param>
    public static void Map(IEndpointRouteBuilder builder, AgentPrismEndpointOptions options, string prefix, AgentPrismRolePolicies roles)
    {
        builder.MapGet("/api/meta", async Task<Ok<AgentPrismMetaResponse>> (
                IAgentDefinitionStore definitions,
                IRunStore runs,
                ISessionStore sessions,
                IJobStore jobs,
                IOptionsMonitor<AgentPrismSchedulingOptions> scheduling,
                HttpContext httpContext,
                [FromServices] IAuthorizationService? authorizationService) =>
            {
                // Denetim izi dekoratorleri (Auditing*Store) burada saydamdir: hangi
                // depolama uygulamasinin kayitli oldugu, dekoratorun degil sardigi
                // gercek uygulamanin adiyla bildirilir.
                var definitionsInner = Unwrap(definitions);
                var sessionsInner = Unwrap(sessions);

                var persistent =
                    definitionsInner is not InMemoryAgentDefinitionStore &&
                    runs is not InMemoryRunStore &&
                    sessionsInner is not InMemorySessionStore;

                var schedulingOptions = scheduling.CurrentValue;

                return TypedResults.Ok(new AgentPrismMetaResponse
                {
                    Version = Version,
                    Prefix = prefix,
                    Authentication = new AgentPrismAuthenticationMeta
                    {
                        AllowRemoteAccess = options.AllowRemoteAccess,
                        RequiresBearerToken = !string.IsNullOrEmpty(options.AuthToken),
                        RequiresAuthorizationPolicy = !string.IsNullOrEmpty(options.AuthorizationPolicy),
                    },
                    Storage = new AgentPrismStorageMeta
                    {
                        Persistent = persistent,
                        AgentDefinitionStore = definitionsInner.GetType().Name,
                        RunStore = runs.GetType().Name,
                        SessionStore = sessionsInner.GetType().Name,
                        JobStore = jobs.GetType().Name,
                        JobWorkerEnabled = schedulingOptions.Enabled && schedulingOptions.RunWorker,
                    },
                    Roles = await ResolveRolesAsync(authorizationService, httpContext.User, roles).ConfigureAwait(false),
                });
            })
            // Meta ucu her zaman aciktir. Tuketicinin genel bir fallback policy'si
            // olsa bile arayuz hangi kimlik yontemini kullanacagini ogrenebilmelidir.
            // AllowAnonymous kimlik dogrulama boru hattini devre disi BIRAKMAZ; istek
            // gecerli bir kimlik tasiyorsa httpContext.User yine de doludur ve rol
            // alanlari gercek yetkiyi yansitir.
            .AllowAnonymous()
            .WithName("AgentPrismMeta")
            .WithTags("AgentPrism", "Meta")
            .WithSummary("AgentPrism surumunu, kimlik yontemini, aktif depolari ve rol yetkilerini bildirir.")
            .WithDescription(
                "Kimlik dogrulamasi gerektirmez. Sir, kiraci verisi veya agent bilgisi icermez.");
    }

    /// <summary>
    /// Gecerli kullanicinin uc rol policy'sini karsilayip karsilamadigini cozer.
    /// </summary>
    /// <remarks>
    /// Bir policy kayitli degilse (<paramref name="roles"/> icindeki alan
    /// <see langword="null"/>) karsilik gelen deger <see langword="true"/> doner:
    /// rol kisiti yoktur, ilgili uc grubu yalnizca mevcut uc katmanli korumadan gecer.
    /// </remarks>
    private static async Task<AgentPrismRoleMeta> ResolveRolesAsync(
        IAuthorizationService? authorizationService,
        ClaimsPrincipal user,
        AgentPrismRolePolicies roles)
        => new()
        {
            CanRead = await SatisfiesAsync(authorizationService, user, roles.Reader).ConfigureAwait(false),
            CanOperate = await SatisfiesAsync(authorizationService, user, roles.Operator).ConfigureAwait(false),
            CanAdminister = await SatisfiesAsync(authorizationService, user, roles.Admin).ConfigureAwait(false),
        };

    private static async Task<bool> SatisfiesAsync(
        IAuthorizationService? authorizationService,
        ClaimsPrincipal user,
        string? policyName)
    {
        if (policyName is null)
        {
            return true;
        }

        if (authorizationService is null)
        {
            return false;
        }

        var result = await authorizationService.AuthorizeAsync(user, policyName).ConfigureAwait(false);
        return result.Succeeded;
    }

    /// <summary>Bir denetim izi dekoratoru ise sardigi gercek depoyu dondurur.</summary>
    private static object Unwrap(object store) => store is IAuditDecorated decorated ? decorated.AuditedInner : store;

    /// <summary>
    /// Calisan derlemenin surumu. MinVer bunu <c>AssemblyInformationalVersion</c>
    /// olarak yazar; kaynak denetimi karmasi (<c>+sha</c>) atilir.
    /// </summary>
    private static string Version { get; } = ReadVersion();

    private static string ReadVersion()
    {
        var informational = typeof(MetaEndpoints).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

        if (string.IsNullOrEmpty(informational))
        {
            return typeof(MetaEndpoints).Assembly.GetName().Version?.ToString() ?? "0.0.0";
        }

        var plus = informational.IndexOf('+', StringComparison.Ordinal);

        return plus < 0 ? informational : informational[..plus];
    }
}
