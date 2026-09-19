using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace Tracon;

/// <summary>
/// Resolves the tenant from the current HTTP request.
/// </summary>
/// <remarks>
/// <para>
/// The class is a <strong>singleton</strong> and works through
/// <see cref="IHttpContextAccessor"/>. A scoped implementation would produce a
/// captive dependency in singleton services that inject <see cref="ITenantContext"/>
/// (stores, decorators), and ASP.NET Core would report this as a startup error.
/// </para>
/// <para>
/// Resolution order:
/// </para>
/// <list type="number">
///   <item><description>If multi-tenancy is disabled → the default tenant.</description></item>
///   <item><description>If a claim type is set and the user passed authentication → the claim.</description></item>
///   <item><description>If header resolution is enabled → the header.</description></item>
///   <item><description>If none apply → the default tenant.</description></item>
/// </list>
/// <para>
/// While the claim is set, the <strong>header is never read</strong>. Otherwise
/// an authenticated user could access another tenant's data simply by adding a
/// header.
/// </para>
/// </remarks>
public sealed partial class HttpTenantContext : ITenantContext
{
    private readonly IHttpContextAccessor _accessor;
    private readonly IOptions<TraconOptions> _coreOptions;
    private readonly IOptions<TraconTenancyOptions> _tenancyOptions;

    /// <summary>Creates a new HTTP tenant context.</summary>
    /// <param name="accessor">The accessor that resolves the current request.</param>
    /// <param name="coreOptions">The Tracon settings.</param>
    /// <param name="tenancyOptions">The tenant resolution settings.</param>
    /// <exception cref="ArgumentNullException">A dependency is <see langword="null"/>.</exception>
    public HttpTenantContext(
        IHttpContextAccessor accessor,
        IOptions<TraconOptions> coreOptions,
        IOptions<TraconTenancyOptions> tenancyOptions)
    {
        ArgumentNullException.ThrowIfNull(accessor);
        ArgumentNullException.ThrowIfNull(coreOptions);
        ArgumentNullException.ThrowIfNull(tenancyOptions);

        _accessor = accessor;
        _coreOptions = coreOptions;
        _tenancyOptions = tenancyOptions;
    }

    /// <inheritdoc />
    /// <remarks>
    /// <para>
    /// If <see cref="AmbientTenantScope.Current"/> is set (a scheduled job is
    /// running and there is no HTTP context), that value is preferred over HTTP
    /// resolution.
    /// </para>
    /// <para>
    /// On a request authenticated with an API key, the tenant is resolved from
    /// the KEY's <c>tenant_id</c> — this comes BEFORE the claim or the header.
    /// The key PROVES a secret; the header is only the client's
    /// DECLARATION. <see cref="TraconEndpointFilter"/> already rejects the
    /// request with 403 if the header conflicts with the key's tenant, so by the
    /// time a request reaches this point, the two either match or the header is
    /// absent.
    /// </para>
    /// </remarks>
    public string TenantId =>
        AmbientTenantScope.Current ?? ResolveFromApiKey() ?? Resolve() ?? _coreOptions.Value.DefaultTenantId;

    /// <summary>
    /// Says whether a tenant id is well-formed.
    /// </summary>
    /// <param name="tenantId">The value to check.</param>
    /// <returns><see langword="true"/> if the value is acceptable.</returns>
    /// <remarks>
    /// The id is a text column in the database and travels as a query parameter;
    /// SQL injection is not possible. A format constraint still applies: an
    /// unchecked value would be reflected as-is into logs, the audit trail, and the UI.
    /// </remarks>
    public static bool IsValidTenantId(string? tenantId)
        => !string.IsNullOrWhiteSpace(tenantId)
            && tenantId.Length <= 64
            && TenantIdPattern().IsMatch(tenantId);

    private string? ResolveFromApiKey()
        => _accessor.HttpContext is { } context && ApiKeyRequestContext.Get(context) is { } record
            ? AmbientTenantScope.Normalize(record.TenantId)
            : null;

    private string? Resolve()
    {
        var options = _tenancyOptions.Value;

        if (!options.Enabled || _accessor.HttpContext is not { } context)
        {
            return null;
        }

        // If the claim is set, the header is NEVER read.
        if (options.ClaimType is { Length: > 0 } claimType)
        {
            return context.User.Identity?.IsAuthenticated == true
                ? Accept(context.User.FindFirst(claimType)?.Value, options)
                : null;
        }

        if (!options.AllowHeaderResolution)
        {
            return null;
        }

        return Accept(context.Request.Headers[options.HeaderName].ToString(), options);
    }

    private static string? Accept(string? candidate, TraconTenancyOptions options)
    {
        if (!IsValidTenantId(candidate))
        {
            return null;
        }

        // The format check accepts upper case; the value is canonical from here
        // on. Everything downstream - the allowlist below, the endpoint filter,
        // and every store - compares and persists this form and no other.
        var tenantId = AmbientTenantScope.Normalize(candidate!);

        if (options.AllowedTenants.Count == 0)
        {
            return tenantId;
        }

        // 🚨 Read this together with TraconEndpointFilter.CheckTenancyWhitelist.
        // A value outside the allowlist returns null HERE, and TenantId's `??`
        // chain then falls back to DefaultTenantId - so this method ALONE does not
        // keep an unauthorized request away from the default tenant's data. The
        // rejection lives in the endpoint filter (K-382), which reads the same
        // candidate from the same source and answers 403 before the request ever
        // reaches an endpoint. Do not weaken that filter on the assumption that
        // this line already refuses the request.
        //
        // 🚨 BOTH sides are normalized. The operator writes the allowlist by
        // hand, so "Acme" in configuration is as likely as "Acme" in a header,
        // and comparing a canonical candidate against a raw entry would refuse
        // the very tenant the operator meant to allow.
        return IsAllowed(tenantId, options) ? tenantId : null;
    }

    /// <summary>
    /// Returns the canonical form of a tenant id that arrived in a route segment.
    /// </summary>
    /// <param name="tenantId">The raw route value.</param>
    /// <returns>The canonical form, or the value unchanged when it is blank.</returns>
    /// <remarks>
    /// An admin endpoint takes the tenant straight from the URL and writes
    /// it to a store. Without this fold, <c>PUT /api/tenants/Acme/egress</c>
    /// saves a policy row the runtime — which resolves <c>acme</c> — never
    /// finds, and the tenant then runs with no egress restriction at all.
    /// <para>
    /// A blank value passes through unchanged rather than throwing: these
    /// handlers answer "no such tenant" for a value that cannot exist, and a
    /// blank id matches no row, which is the same answer.
    /// </para>
    /// </remarks>
    internal static string NormalizeRouteTenantId(string tenantId)
        => string.IsNullOrWhiteSpace(tenantId) ? tenantId : AmbientTenantScope.Normalize(tenantId);

    /// <summary>
    /// Says whether a <strong>canonical</strong> tenant id is on the allowlist.
    /// </summary>
    /// <param name="canonicalTenantId">The tenant id, already normalized.</param>
    /// <param name="options">The tenancy options carrying the allowlist.</param>
    /// <returns><see langword="true"/> when the allowlist contains the value.</returns>
    /// <remarks>
    /// <para>
    /// Shared with <c>TraconEndpointFilter</c> so the two surfaces cannot drift
    /// apart: a request the filter answers 403 for must be the same
    /// request this context refuses, and the reverse.
    /// </para>
    /// <para>
    /// An EMPTY allowlist answers <see langword="false"/> here, not
    /// <see langword="true"/>. "Empty means everything is allowed" is the
    /// CALLER's rule and both callers apply it before reaching this method.
    /// A future caller that trusts this method to handle the empty case would
    /// refuse every request.
    /// </para>
    /// </remarks>
    internal static bool IsAllowed(string canonicalTenantId, TraconTenancyOptions options)
    {
        for (var i = 0; i < options.AllowedTenants.Count; i++)
        {
            var allowed = options.AllowedTenants[i];

            if (!string.IsNullOrWhiteSpace(allowed)
                && string.Equals(
                    AmbientTenantScope.Normalize(allowed),
                    canonicalTenantId,
                    StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    [GeneratedRegex("^[a-zA-Z0-9_.-]+$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex TenantIdPattern();
}
