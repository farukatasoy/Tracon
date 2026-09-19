using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Tracon;

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
/// tries the fixed comparison against <see cref="TraconEndpointOptions.AuthToken"/>
/// (unchanged). If that does not match and an <c>IApiKeyStore</c> is registered, the
/// presented value is looked up in that store; when a valid, non-revoked and non-expired
/// key is found, the request continues with the tenant and the scopes of that key.
/// </para>
/// </remarks>
internal sealed class TraconEndpointFilter : IEndpointFilter
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
    public TraconEndpointFilter(TraconEndpointOptions options, bool requireBearerToken = true, bool requireLoopback = true)
    {
        ArgumentNullException.ThrowIfNull(options);

        // The settings are read at setup time. A change made after MapTracon must not
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
                detail: "Tracon endpoints are reachable only from the same machine by default. " +
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

            // Neither a static token nor a header: today's behavior does not change (K1)
            // -- EXCEPT on a surface that declares an API key mandatory.
            if (RejectWhenApiKeyIsMandatory(httpContext) is { } rejectedMissingKey)
            {
                return rejectedMissingKey;
            }

            return CheckTenancyWhitelist(httpContext) is { } rejectedNoAuth
                ? rejectedNoAuth
                : await Proceed(httpContext, next, context).ConfigureAwait(false);
        }

        if (_authToken is { Length: > 0 } expected && BearerTokenValidator.IsValid(header, expected))
        {
            // A static bearer token is deliberately NOT enough for a mandatory
            // surface: it identifies the installation, not a caller, and it
            // carries no scope.
            if (RejectWhenApiKeyIsMandatory(httpContext) is { } rejectedStaticForMandatory)
            {
                return rejectedStaticForMandatory;
            }

            return CheckTenancyWhitelist(httpContext) is { } rejectedStaticToken
                ? rejectedStaticToken
                : await Proceed(httpContext, next, context).ConfigureAwait(false);
        }

        if (!_requireBearerToken
            && _staticAuthToken is { Length: > 0 } configured
            && BearerTokenValidator.IsValid(header, configured))
        {
            // This endpoint group does NOT apply the bearer token layer (K1), but the
            // presented value matches the static AuthToken of Tracon itself. That is
            // not an API key, the IApiKeyStore lookup would never find it, and it would
            // fall through to the general Unauthorized() below (HATA-S1-014): a correct
            // token would return the same response as no token at all. The header is
            // treated neutrally, as if it were ABSENT — it neither rejects nor grants
            // extra rights; the endpoint's own logic is reached (for example a 404/400,
            // where one applies).
            if (RejectWhenApiKeyIsMandatory(httpContext) is { } rejectedGroupForMandatory)
            {
                return rejectedGroupForMandatory;
            }

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
        // in Tracon.Core reads it through an AsyncLocal, so Core can answer the
        // "who did it" question without taking a dependency on ASP.NET Core.
        // Rationale: docs/arsiv/fazlar/09-YONETISIM-VE-DENETIM-IZI.md, section 9.2.
        //
        // 🚨 An API-key request used to leave the actor EMPTY. Nothing assigns
        // HttpContext.User for those requests, so the resolver saw an anonymous
        // principal and every audit row written on behalf of a key recorded
        // "who did this" as null - while the key's id and name were sitting in
        // ApiKeyRequestContext the whole time.
        AuditActorContext.Current = httpContext.User?.Identity?.IsAuthenticated == true
            ? httpContext.User
            : ApiKeyRequestContext.Get(httpContext) is { } apiKey
                ? ApiKeyActor(apiKey)
                : httpContext.User;

        return next(context);
    }

    /// <summary>Builds the audit actor that stands for an API key.</summary>
    /// <remarks>
    /// The key's identifier is used, never its value or its hash. The name is
    /// carried as well so an operator reading the trail sees the label they gave
    /// the key rather than an opaque id.
    /// </remarks>
    private static ClaimsPrincipal ApiKeyActor(ApiKeyRecord record)
        => new(new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, $"apikey:{record.Id}"),
                new Claim(ClaimTypes.Name, record.Name),
            ],
            authenticationType: "TraconApiKey"));

    /// <summary>
    /// Rejects a formally valid candidate tenant that is NOT on the allow list, while
    /// <see cref="TraconTenancyOptions.AllowedTenants"/> is populated.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Without this check the <c>??</c> chain of <see cref="HttpTenantContext.TenantId"/>
    /// (<c>Resolve() ?? DefaultTenantId</c>) would SILENTLY drop a candidate that the allow
    /// list rejects down to the default tenant — exactly the case that the XML documentation
    /// of <see cref="TraconTenancyOptions.AllowedTenants"/> forbids ("a value that is not
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
        var tenancyOptions = httpContext.RequestServices.GetService<IOptions<TraconTenancyOptions>>()?.Value;

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

        // 🚨 The same canonical form HttpTenantContext.Accept produces, through
        // the same helper (K-382). If the two surfaces folded letter case
        // differently, one of them would refuse a request the other admits.
        if (!HttpTenantContext.IsValidTenantId(candidate)
            || HttpTenantContext.IsAllowed(AmbientTenantScope.Normalize(candidate!), tenancyOptions))
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
    /// Rejects the request when the <c>X-Tracon-Tenant</c> header names a tenant that
    /// differs from the tenant of the API key.
    /// </summary>
    /// <remarks>
    /// The header CANNOT override the key. If it could, the tenant binding
    /// of the key would mean nothing.
    /// </remarks>
    private static ProblemHttpResult? CheckTenantHeaderConflict(HttpContext httpContext, ApiKeyRecord record)
    {
        var tenancyOptions = httpContext.RequestServices.GetService<IOptions<TraconTenancyOptions>>()?.Value;

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
    /// <summary>
    /// Rejects a request that presented no API key on an endpoint that declares
    /// one mandatory, once the surface is reachable beyond loopback.
    /// </summary>
    /// <remarks>
    /// Scoped to <c>AllowRemoteAccess</c> on purpose, and to exactly the same
    /// condition <c>ExternalSurfaceGuard.EnsureRemoteAccessNotCombined</c> uses.
    /// On loopback the installation is already limited to the local machine and
    /// the zero-configuration default stays intact; the moment the surface is
    /// published, the key the startup guard demanded has to actually be presented.
    /// </remarks>
    private ProblemHttpResult? RejectWhenApiKeyIsMandatory(HttpContext httpContext)
    {
        if (!_allowRemoteAccess)
        {
            return null;
        }

        var requirement = httpContext.GetEndpoint()?.Metadata.GetMetadata<ApiKeyScopeRequirement>();

        if (requirement is not { Mandatory: true })
        {
            return null;
        }

        return TypedResults.Problem(
            title: "API key required",
            detail: $"This surface is published beyond loopback and can only be called with an API key " +
                    $"carrying the '{requirement.Scope}' scope. Create one through 'POST /api/api-keys' " +
                    "and send it as 'Authorization: Bearer <key>'.",
            statusCode: StatusCodes.Status401Unauthorized);
    }

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
