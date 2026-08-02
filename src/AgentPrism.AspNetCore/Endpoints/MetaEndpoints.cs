using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

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
    public static void Map(IEndpointRouteBuilder builder, AgentPrismEndpointOptions options, string prefix)
    {
        builder.MapGet("/api/meta", Results<Ok<AgentPrismMetaResponse>, ProblemHttpResult> (
                IAgentDefinitionStore definitions,
                IRunStore runs,
                ISessionStore sessions) =>
            {
                var persistent =
                    definitions is not InMemoryAgentDefinitionStore &&
                    runs is not InMemoryRunStore &&
                    sessions is not InMemorySessionStore;

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
                        AgentDefinitionStore = definitions.GetType().Name,
                        RunStore = runs.GetType().Name,
                        SessionStore = sessions.GetType().Name,
                    },
                });
            })
            // Meta ucu her zaman aciktir. Tuketicinin genel bir fallback policy'si
            // olsa bile arayuz hangi kimlik yontemini kullanacagini ogrenebilmelidir.
            .AllowAnonymous()
            .WithName("AgentPrismMeta")
            .WithSummary("AgentPrism surumunu, kimlik yontemini ve aktif depolari bildirir.")
            .WithDescription(
                "Kimlik dogrulamasi gerektirmez. Sir, kiraci verisi veya agent bilgisi icermez.");
    }

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
