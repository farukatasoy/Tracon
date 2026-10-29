using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Tracon;

/// <summary>
/// The only place that builds the keys the MCP OAuth state uses in the distributed cache and in
/// the lease store.
/// </summary>
/// <remarks>
/// <para>
/// A key is never an interpolated string of names. The tenant and the server name
/// are encoded with the length of the tenant in front, so two different pairs cannot produce
/// the same text, and the key is a SHA-256 digest of that text: it carries no tenant or server
/// name into the cache, and no name can add a separator of its own.
/// </para>
/// <para>
/// The <c>state</c> of an authorization is a bearer value: whoever knows it can finish the
/// flow. It is never a key and never logged; the key holds its digest.
/// </para>
/// </remarks>
internal static class McpOAuthCacheKeys
{
    private const string Prefix = "tracon:mcp-oauth:v1:";

    /// <summary>Builds the key of the token of one tenant's binding to one server.</summary>
    /// <param name="key">The tenant and server.</param>
    /// <returns>The cache key.</returns>
    public static string Token(McpTenantServerKey key) => Prefix + "token:" + Digest(key);

    /// <summary>Builds the name of the lease that lets one instance refresh a token at a time.</summary>
    /// <param name="key">The tenant and server.</param>
    /// <returns>The lease name; short enough for every lease table.</returns>
    public static string RefreshLease(McpTenantServerKey key) => "mcp-oauth-refresh:" + Digest(key);

    /// <summary>Builds the key of the marker that says an instance is waiting for this authorization.</summary>
    /// <param name="state">The <c>state</c> the SDK put into the authorization address.</param>
    /// <returns>The cache key.</returns>
    public static string Flow(string state) => Prefix + "flow:" + Digest(state);

    /// <summary>Builds the key of the callback an instance leaves for the instance that owns the flow.</summary>
    /// <param name="state">The <c>state</c> of the flow.</param>
    /// <returns>The cache key.</returns>
    public static string Callback(string state) => Prefix + "callback:" + Digest(state);

    /// <summary>Builds the key of the result the owner leaves for the instance that received the callback.</summary>
    /// <param name="state">The <c>state</c> of the flow.</param>
    /// <returns>The cache key.</returns>
    public static string Outcome(string state) => Prefix + "outcome:" + Digest(state);

    private static string Digest(McpTenantServerKey key)
        => Digest(string.Create(
            CultureInfo.InvariantCulture,
            $"{key.TenantId.Length}:{key.TenantId}{key.ServerName}"));

    private static string Digest(string text)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
}
