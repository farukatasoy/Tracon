using Microsoft.Extensions.Options;

namespace Tracon;

/// <summary>
/// The configuration key space an MCP server definition may name its
/// secrets in: the allowed prefix plus the tenant rule of
/// <see cref="ConfigurationKeyGuard.RequireTenantKey"/>.
/// </summary>
/// <param name="AllowedPrefix"><see cref="TraconMcpSecurityOptions.AllowedConfigurationPrefix"/>.</param>
/// <param name="DefaultTenantId">
/// <see cref="TraconOptions.DefaultTenantId"/>; the only tenant that may use a
/// flat name directly under the prefix.
/// </param>
/// <remarks>
/// The two values travel together so that no connection path can check the
/// prefix while forgetting the tenant.
/// </remarks>
internal sealed record McpKeySpace(string AllowedPrefix, string DefaultTenantId)
{
    /// <summary>Builds the key space from the registered options.</summary>
    /// <param name="securityOptions">The MCP security options, or <see langword="null"/> for the defaults.</param>
    /// <param name="coreOptions">The core options.</param>
    /// <returns>The key space.</returns>
    public static McpKeySpace From(IOptions<TraconMcpSecurityOptions>? securityOptions, IOptions<TraconOptions> coreOptions)
    {
        ArgumentNullException.ThrowIfNull(coreOptions);

        return new McpKeySpace(
            (securityOptions?.Value ?? new TraconMcpSecurityOptions()).AllowedConfigurationPrefix,
            coreOptions.Value.DefaultTenantId);
    }

    /// <summary>Throws unless <paramref name="configurationKeyName"/> sits inside the server's tenant.</summary>
    /// <param name="configurationKeyName">The configuration key name the definition carries.</param>
    /// <param name="server">The definition; its <see cref="McpServerDefinition.TenantId"/> owns the key.</param>
    /// <param name="fieldName">The field name, as it appears to the caller.</param>
    /// <exception cref="TraconException">The name is outside the prefix or outside the tenant.</exception>
    public void Require(string configurationKeyName, McpServerDefinition server, string fieldName)
        => ConfigurationKeyGuard.RequireTenantKey(
            configurationKeyName,
            AllowedPrefix,
            server.TenantId,
            DefaultTenantId,
            fieldName);
}
