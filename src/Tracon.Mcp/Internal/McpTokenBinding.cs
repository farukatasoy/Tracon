namespace Tracon;

/// <summary>The server definition fields an OAuth token is bound to.</summary>
/// <param name="Endpoint">The server's absolute endpoint address.</param>
/// <param name="ClientId">The OAuth client id.</param>
/// <remarks>
/// A token belongs to the resource it was issued for. The MCP SDK sends a valid cached token
/// to whatever endpoint its transport points at; it does not check which server the token came
/// from. Both token caches keep the binding next to the token and give no token to a definition
/// whose binding differs.
/// </remarks>
internal readonly record struct McpTokenBinding(string Endpoint, string? ClientId)
{
    /// <summary>Reads the binding of a server definition.</summary>
    /// <param name="server">The server definition.</param>
    /// <returns>The binding.</returns>
    public static McpTokenBinding Of(McpServerDefinition server)
    {
        ArgumentNullException.ThrowIfNull(server);

        return new McpTokenBinding(server.Endpoint.AbsoluteUri, server.OAuthClientId);
    }
}
