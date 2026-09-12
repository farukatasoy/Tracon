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
            ? record.TenantId
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

        if (options.AllowedTenants.Count == 0)
        {
            return candidate;
        }

        // 🚨 Read this together with TraconEndpointFilter.CheckTenancyWhitelist.
        // A value outside the allowlist returns null HERE, and TenantId's `??`
        // chain then falls back to DefaultTenantId - so this method ALONE does not
        // keep an unauthorized request away from the default tenant's data. The
        // rejection lives in the endpoint filter (K-382), which reads the same
        // candidate from the same source and answers 403 before the request ever
        // reaches an endpoint. Do not weaken that filter on the assumption that
        // this line already refuses the request.
        return options.AllowedTenants.Contains(candidate, StringComparer.Ordinal) ? candidate : null;
    }

    [GeneratedRegex("^[a-zA-Z0-9_.-]+$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex TenantIdPattern();
}
