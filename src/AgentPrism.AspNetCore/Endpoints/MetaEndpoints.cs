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
/// Discovery endpoint that requires no authentication.
/// </summary>
internal static class MetaEndpoints
{
    /// <summary>Maps the meta endpoint.</summary>
    /// <param name="builder">The endpoint group.</param>
    /// <param name="options">The access settings.</param>
    /// <param name="prefix">The path prefix the endpoints are mapped under.</param>
    /// <param name="roles">The resolved role policies.</param>
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
                // Audit trail decorators (Auditing*Store) are transparent here: which
                // storage implementation is registered is reported using the name of the
                // real implementation it wraps, not the decorator.
                //
                // Both the unwrapping and the persistence judgement come from
                // StorePersistence, the same source the startup warning
                // (NonPersistentStorageWarningService) reads. A fourth in-memory store
                // added later cannot make this endpoint and that warning disagree.
                var definitionsInner = StorePersistence.Unwrap(definitions);
                var runsInner = StorePersistence.Unwrap(runs);
                var sessionsInner = StorePersistence.Unwrap(sessions);

                var persistent = StorePersistence.IsPersistent(definitions, runs, sessions);

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
                        RunStore = runsInner.GetType().Name,
                        SessionStore = sessionsInner.GetType().Name,
                        JobStore = jobs.GetType().Name,
                        JobWorkerEnabled = schedulingOptions.Enabled && schedulingOptions.RunWorker,
                    },
                    Roles = await ResolveRolesAsync(authorizationService, httpContext.User, roles).ConfigureAwait(false),
                });
            })
            // The meta endpoint is always open. Even if the consumer has a general
            // fallback policy, the UI must be able to learn which authentication method
            // to use. AllowAnonymous does NOT disable the authentication pipeline; if the
            // request carries a valid identity, httpContext.User is still populated and
            // the role fields reflect the actual authorization.
            .AllowAnonymous()
            .WithName("AgentPrismMeta")
            .WithTags("AgentPrism", "Meta")
            .WithSummary("Reports the AgentPrism version, authentication method, active stores, and role authorizations.")
            .WithDescription(
                "Requires no authentication. Contains no secret, tenant data, or agent information.");
    }

    /// <summary>
    /// Resolves whether the current user satisfies each role policy.
    /// </summary>
    /// <remarks>
    /// If a policy is not registered (the field inside <paramref name="roles"/> is
    /// <see langword="null"/>), the corresponding value returns <see langword="true"/>:
    /// there is no role restriction, and the corresponding endpoint group only goes
    /// through the existing three-layer protection.
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

    /// <summary>
    /// The running assembly's version. MinVer writes this as
    /// <c>AssemblyInformationalVersion</c>; the source control hash (<c>+sha</c>) is dropped.
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
