using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace Tracon.Embedded;

/// <summary>
/// 🚨 <strong>DEMONSTRATION ONLY.</strong> Resolves the tenant from a header
/// that stands in for the host application's OWN identity layer — a claim, a
/// subdomain, a resolved principal in a real deployment.
/// </summary>
/// <remarks>
/// <para>
/// This class replaces Tracon's built-in <c>SingleTenantContext</c>
/// entirely, rather than turning on <c>UseTenancy()</c>'s claim/header
/// resolution chain — see <c>docs-site/src/content/docs/guides/embedding.md</c>
/// for why the two are different embedding choices.
/// </para>
/// <para>
/// Registered <strong>before</strong> <c>AddTracon()</c>: every contract
/// here uses <c>TryAdd</c>, so a registration made first wins.
/// </para>
/// </remarks>
internal sealed class EmbeddedTenantContext(IHttpContextAccessor accessor, IOptions<TraconOptions> options)
    : ITenantContext
{
    /// <summary>The header carrying the host's own tenant identity.</summary>
    public const string TenantHeader = "X-Host-Tenant";

    /// <inheritdoc />
    /// <remarks>
    /// Checks <see cref="AmbientTenantScope.Current"/> first: Tracon's own
    /// background maintenance (retention sweeps, canary evaluation) calls this
    /// property with no HTTP request and no ambient scope of its own, so the
    /// fallback below must never throw — the same safety property Tracon's
    /// built-in resolvers keep.
    /// </remarks>
    public string TenantId =>
        AmbientTenantScope.Current
        ?? ResolveFromHeader()
        ?? options.Value.DefaultTenantId;

    private string? ResolveFromHeader()
    {
        var value = accessor.HttpContext?.Request.Headers[TenantHeader].ToString();

        return string.IsNullOrWhiteSpace(value) ? null : value;
    }
}
