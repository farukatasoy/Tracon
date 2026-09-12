using Microsoft.AspNetCore.Http;

namespace Tracon;

/// <summary>
/// Carries the API key that authenticated this request through
/// <see cref="HttpContext.Items"/>.
/// </summary>
/// <remarks>
/// <see cref="AsyncLocal{T}"/> is DELIBERATELY not used: this is a fully request-scoped
/// value and <c>HttpContext.Items</c> is already cleared per request. A write to an
/// <c>AsyncLocal</c> does not flow back to the caller
///  — there is no such problem here, because the value
/// is read by the later layers that run on the same <c>HttpContext</c>.
/// </remarks>
internal static class ApiKeyRequestContext
{
    private const string ItemsKey = "Tracon.ApiKeyRecord";

    /// <summary>Stores the key record that authenticated this request.</summary>
    /// <param name="httpContext">The current request.</param>
    /// <param name="record">The authenticated record.</param>
    public static void Set(HttpContext httpContext, ApiKeyRecord record)
        => httpContext.Items[ItemsKey] = record;

    /// <summary>Reads the key record that authenticated this request.</summary>
    /// <param name="httpContext">The current request.</param>
    /// <returns>The record; <see langword="null"/> when the request was not authenticated with an API key.</returns>
    public static ApiKeyRecord? Get(HttpContext httpContext)
        => httpContext.Items.TryGetValue(ItemsKey, out var value) ? value as ApiKeyRecord : null;
}
