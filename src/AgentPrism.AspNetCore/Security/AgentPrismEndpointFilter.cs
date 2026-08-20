using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace AgentPrism;

/// <summary>
/// Endpoint filter that applies the loopback restriction and the bearer token check.
/// </summary>
/// <remarks>
/// <para>
/// The third layer, the authorization policy, does not belong to this filter; it binds
/// to the ASP.NET Core authorization pipeline with <c>RequireAuthorization</c> and runs
/// <em>before</em> this filter.
/// </para>
/// <para>
/// Rejection responses state the reason clearly but give no information about the
/// expected token.
/// </para>
/// <para>
/// The bearer token layer can now recognize TWO identity sources. First it
/// tries the fixed comparison against <see cref="AgentPrismEndpointOptions.AuthToken"/>
/// (unchanged). If that does not match and an <c>IApiKeyStore</c> is registered, the
/// presented value is looked up in that store; when a valid, non-revoked and non-expired
/// key is found, the request continues with the tenant and the scopes of that key.
/// </para>
/// </remarks>
internal sealed class AgentPrismEndpointFilter : IEndpointFilter
{
    /// <summary>The last-used stamp is rewritten only after at least this interval.</summary>
    private static readonly TimeSpan LastUsedTouchInterval = TimeSpan.FromMinutes(1);

    private readonly bool _allowRemoteAccess;
    private readonly bool _requireLoopback;
    private readonly bool _requireBearerToken;
    private readonly string? _authToken;
    private readonly string? _staticAuthToken;

    /// <summary>Creates a filter from the settings.</summary>
    /// <param name="options">The access settings.</param>
    /// <param name="requireBearerToken">
    /// Whether the bearer token layer applies. <see langword="false"/> is passed for the
    /// static assets of the user interface: a browser cannot add an <c>Authorization</c>
    /// header to a <c>&lt;script src&gt;</c> request, so if this layer locked the shell,
    /// the user interface could never open and the user could not see a screen to enter
    /// the token in. The shell carries no data; the authorization policy layer still
    /// applies and every data endpoint stays fully protected.
    /// </param>
    /// <param name="requireLoopback">
    /// Whether the loopback restriction applies. <see langword="false"/> is passed for the
    /// static assets of the user interface: if the restriction applied to the shell, a
    /// non-loopback client could never download the JS bundle, <c>AccessGate</c> itself
    ///  could never run, and the user would be left with raw server JSON
    /// instead of the "Access denied" card. The shell carries no data; the real protection
    /// comes from the filter instances on the data endpoints, where this flag defaults to
    /// <see langword="true"/> — the shell only lets the user see the correct message when
    /// a probe request to those endpoints returns 403.
    /// </param>
    /// <exception cref="ArgumentNullException">When <paramref name="options"/> is <see langword="null"/>.</exception>
    public AgentPrismEndpointFilter(AgentPrismEndpointOptions options, bool requireBearerToken = true, bool requireLoopback = true)
    {
        ArgumentNullException.ThrowIfNull(options);

        // The settings are read at setup time. A change made after MapAgentPrism must not
        // affect the running endpoints; access rules that loosen silently at run time are
        // unacceptable from a security standpoint.
        _allowRemoteAccess = options.AllowRemoteAccess;
        _requireLoopback = requireLoopback;
        _requireBearerToken = requireBearerToken;
        _authToken = requireBearerToken ? options.AuthToken : null;
        _staticAuthToken = options.AuthToken;
    }

    /// <inheritdoc />
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        var httpContext = context.HttpContext;

        if (_requireLoopback && !_allowRemoteAccess && !LoopbackGuard.IsLocal(httpContext.Connection.RemoteIpAddress))
        {
            return Results.Problem(
                title: "Remote access disabled",
                detail: "AgentPrism endpoints are reachable only from the same machine by default. " +
                        "For remote access, enable the AllowRemoteAccess setting and configure an " +
                        "authentication method (AuthToken or RequireAuthorization).",
                statusCode: StatusCodes.Status403Forbidden);
        }

        var header = httpContext.Request.Headers.Authorization.ToString();

        if (header.Length == 0)
        {
            if (_authToken is { Length: > 0 })
            {
                return Unauthorized(httpContext);
            }

            // Neither a static token nor a header: today's behavior does not change (K1).
            return CheckTenancyWhitelist(httpContext) is { } rejectedNoAuth
                ? rejectedNoAuth
                : await Proceed(httpContext, next, context).ConfigureAwait(false);
        }

        if (_authToken is { Length: > 0 } expected && BearerTokenValidator.IsValid(header, expected))
        {
            return CheckTenancyWhitelist(httpContext) is { } rejectedStaticToken
                ? rejectedStaticToken
                : await Proceed(httpContext, next, context).ConfigureAwait(false);
        }

        if (!_requireBearerToken
            && _staticAuthToken is { Length: > 0 } configured
            && BearerTokenValidator.IsValid(header, configured))
        {
            // This endpoint group does NOT apply the bearer token layer (K1), but the
            // presented value matches the static AuthToken of AgentPrism itself. That is
            // not an API key, the IApiKeyStore lookup would never find it, and it would
            // fall through to the general Unauthorized() below (HATA-S1-014): a correct
            // token would return the same response as no token at all. The header is
            // treated neutrally, as if it were ABSENT — it neither rejects nor grants
            // extra rights; the endpoint's own logic is reached (for example a 404/400,
            // where one applies).
            return CheckTenancyWhitelist(httpContext) is { } rejectedGroupToken
                ? rejectedGroupToken
                : await Proceed(httpContext, next, context).ConfigureAwait(false);
        }

        var candidate = BearerTokenValidator.TryExtractToken(header);

        if (candidate is not null && httpContext.RequestServices.GetService<IApiKeyStore>() is { } apiKeyStore)
        {
            var timeProvider = httpContext.RequestServices.GetService<TimeProvider>() ?? TimeProvider.System;
            var record = await ApiKeyAuthenticator
                .AuthenticateAsync(apiKeyStore, candidate, timeProvider, httpContext.RequestAborted)
                .ConfigureAwait(false);

            if (record is not null)
            {
                var conflict = CheckTenantHeaderConflict(httpContext, record);

                if (conflict is not null)
                {
                    return conflict;
                }

                var scopeDenied = CheckScope(httpContext, record);

                if (scopeDenied is not null)
                {
                    return scopeDenied;
                }

                ApiKeyRequestContext.Set(httpContext, record);
                await TouchLastUsedIfStale(apiKeyStore, record, timeProvider, httpContext.RequestAborted)
                    .ConfigureAwait(false);

                return await Proceed(httpContext, next, context).ConfigureAwait(false);
            }
        }

        return Unauthorized(httpContext);
    }

    private static ValueTask<object?> Proceed(
        HttpContext httpContext,
        EndpointFilterDelegate next,
        EndpointFilterInvocationContext context)
    {
        // Carries the audit trail actor into the ambient context. AmbientAuditActorResolver
        // in AgentPrism.Core reads it through an AsyncLocal, so Core can answer the
        // "who did it" question without taking a dependency on ASP.NET Core.
        // Rationale: docs/arsiv/fazlar/09-YONETISIM-VE-DENETIM-IZI.md, section 9.2.
        AuditActorContext.Current = httpContext.User;

        return next(context);
    }

    /// <summary>
    /// Rejects a formally valid candidate tenant that is NOT on the allow list, while
    /// <see cref="AgentPrismTenancyOptions.AllowedTenants"/> is populated.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Without this check the <c>??</c> chain of <see cref="HttpTenantContext.TenantId"/>
    /// (<c>Resolve() ?? DefaultTenantId</c>) would SILENTLY drop a candidate that the allow
    /// list rejects down to the default tenant — exactly the case that the XML documentation
    /// of <see cref="AgentPrismTenancyOptions.AllowedTenants"/> forbids ("a value that is not
    /// on the list does not fall back to the default tenant"). The candidate is therefore
    /// read and checked here, BEFORE the request reaches the endpoint, from the same source
    /// (claim or header — exactly the same precedence as
    /// <see cref="HttpTenantContext.Resolve"/>).
    /// </para>
    /// <para>
    /// When NO candidate is supplied at all (neither claim nor header), or when it is
    /// formally invalid, this check does not engage — falling back to the default tenant is
    /// the intended behavior in that case (the no-surprises rule: nothing changes until multi-tenancy is turned
    /// on).
    /// </para>
    /// </remarks>
    private static ProblemHttpResult? CheckTenancyWhitelist(HttpContext httpContext)
    {
        var tenancyOptions = httpContext.RequestServices.GetService<IOptions<AgentPrismTenancyOptions>>()?.Value;

        if (tenancyOptions is not { Enabled: true, AllowedTenants.Count: > 0 })
        {
            return null;
        }

        string? candidate;

        if (tenancyOptions.ClaimType is { Length: > 0 } claimType)
        {
            candidate = httpContext.User.Identity?.IsAuthenticated == true
                ? httpContext.User.FindFirst(claimType)?.Value
                : null;
        }
        else if (tenancyOptions.AllowHeaderResolution)
        {
            candidate = httpContext.Request.Headers[tenancyOptions.HeaderName].ToString();
        }
        else
        {
            candidate = null;
        }

        if (!HttpTenantContext.IsValidTenantId(candidate)
            || tenancyOptions.AllowedTenants.Contains(candidate, StringComparer.Ordinal))
        {
            return null;
        }

        return TypedResults.Problem(
            title: "Tenant rejected",
            detail: "The resolved tenant is not on the allow list. This request is NOT silently " +
                    "downgraded to the default tenant's data; it is rejected.",
            statusCode: StatusCodes.Status403Forbidden);
    }

    private static ProblemHttpResult Unauthorized(HttpContext httpContext)
    {
        httpContext.Response.Headers.WWWAuthenticate = "Bearer";

        return TypedResults.Problem(
            title: "Authentication failed",
            detail: "A valid 'Authorization: Bearer <token>' header is required.",
            statusCode: StatusCodes.Status401Unauthorized);
    }

    /// <summary>
    /// Rejects the request when the <c>X-AgentPrism-Tenant</c> header names a tenant that
    /// differs from the tenant of the API key.
    /// </summary>
    /// <remarks>
    /// The header CANNOT override the key. If it could, the tenant binding
    /// of the key would mean nothing.
    /// </remarks>
    private static ProblemHttpResult? CheckTenantHeaderConflict(HttpContext httpContext, ApiKeyRecord record)
    {
        var tenancyOptions = httpContext.RequestServices.GetService<IOptions<AgentPrismTenancyOptions>>()?.Value;

        if (tenancyOptions is not { AllowHeaderResolution: true })
        {
            return null;
        }

        var declared = httpContext.Request.Headers[tenancyOptions.HeaderName].ToString();

        if (declared.Length == 0 || string.Equals(declared, record.TenantId, StringComparison.Ordinal))
        {
            return null;
        }

        return TypedResults.Problem(
            title: "Tenant mismatch",
            detail: $"The '{tenancyOptions.HeaderName}' header CANNOT override the tenant the API " +
                    "key is bound to. Remove the header or give a value matching the key's tenant.",
            statusCode: StatusCodes.Status403Forbidden);
    }

    /// <summary>
    /// Rejects the request when the called endpoint requires a scope that the key does not
    /// carry.
    /// </summary>
    private static ProblemHttpResult? CheckScope(HttpContext httpContext, ApiKeyRecord record)
    {
        var requirement = httpContext.GetEndpoint()?.Metadata.GetMetadata<ApiKeyScopeRequirement>();

        if (requirement is null || record.Scopes.Contains(requirement.Scope))
        {
            return null;
        }

        return TypedResults.Problem(
            title: "Insufficient scope",
            detail: $"This endpoint requires the '{requirement.Scope}' scope; the key does not carry it.",
            statusCode: StatusCodes.Status403Forbidden);
    }

    /// <summary>
    /// Updates the last-used stamp only when it is old enough.
    /// </summary>
    /// <remarks>
    /// Writing on every request would add an unnecessary <c>UPDATE</c> to the hot read path.
    /// </remarks>
    private static ValueTask TouchLastUsedIfStale(
        IApiKeyStore store,
        ApiKeyRecord record,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();

        if (record.LastUsedAt is { } lastUsedAt && now - lastUsedAt < LastUsedTouchInterval)
        {
            return default;
        }

        return store.TouchLastUsedAsync(record.Id, now, cancellationToken);
    }
}
