using Microsoft.AspNetCore.Http;

namespace Tracon;

/// <summary>
/// Records through <see cref="HttpContext.Items"/> that the static
/// <see cref="TraconEndpointOptions.AuthToken"/> authenticated this request.
/// </summary>
/// <remarks>
/// The static token identifies the installation, not a tenant and not a
/// caller. A later layer that asks "may this caller act on another tenant?"
/// needs to tell it apart from an anonymous request, and nothing else on the
/// request records it: the token sets no principal and no key record. The
/// same request-scoped shape as <see cref="ApiKeyRequestContext"/>.
/// </remarks>
internal static class StaticTokenRequestContext
{
    private const string ItemsKey = "Tracon.StaticToken";

    /// <summary>Records that the static token authenticated this request.</summary>
    /// <param name="httpContext">The current request.</param>
    public static void Set(HttpContext httpContext)
        => httpContext.Items[ItemsKey] = true;

    /// <summary>Reports whether the static token authenticated this request.</summary>
    /// <param name="httpContext">The current request.</param>
    /// <returns><see langword="true"/> when the request carried the valid static token.</returns>
    public static bool IsSet(HttpContext httpContext)
        => httpContext.Items.ContainsKey(ItemsKey);
}
