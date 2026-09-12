namespace Tracon;

// 🚨 This type exists because the key used to be an interpolated string. Three
// call sites separated the two names with a U+001F control character; the READ
// path (McpToolCatalog.TryGetConnection) did not. Because the separator is
// invisible, reading the source could not reveal the difference - only a runtime
// probe did. The write path stored "acme<US>prod" and the read path asked for
// "acmeprod", so the lookup never hit and MCP resource injection silently
// reported the server as unreachable.
//
// A typed key removes the failure mode instead of documenting it: a read and a
// write cannot use different formats. Do not go back to string interpolation.

/// <summary>
/// Identifies one tenant's binding to one MCP server. Keys the connection cache
/// and the OAuth token cache.
/// </summary>
/// <param name="TenantId">The tenant identifier.</param>
/// <param name="ServerName">The MCP server name, unique within the tenant.</param>
internal readonly record struct McpTenantServerKey(string TenantId, string ServerName);
