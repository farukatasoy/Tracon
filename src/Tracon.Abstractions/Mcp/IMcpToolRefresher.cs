namespace Tracon;

/// <summary>
/// Refreshes the tool list of remote MCP servers on demand.
/// </summary>
/// <remarks>
/// <para>
/// Refreshing normally happens in the background, at fixed intervals. This
/// interface exists so that when a server is <em>newly added</em>, the user
/// does not have to wait for the next scheduled refresh.
/// </para>
/// <para>
/// The abstraction lives in <c>Tracon.Abstractions</c> because the HTTP
/// layer triggers the refresh but does not depend on the <c>Tracon.Mcp</c>
/// package: MCP stays an optional package.
/// </para>
/// <para>
/// <strong>DI lifetime — singleton.</strong> Registered as a singleton with
/// <c>TryAdd</c>; a consumer's own registration wins. The same instance also
/// runs the background, fixed-interval refresh, so an implementation must be
/// safe under a concurrent on-demand <see cref="RefreshAsync"/> call.
/// </para>
/// </remarks>
public interface IMcpToolRefresher
{
    /// <summary>Refreshes the tool list now.</summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The result of this refresh.</returns>
    ValueTask<McpRefreshOutcome> RefreshAsync(CancellationToken cancellationToken = default);
}

/// <summary>The result of an <see cref="IMcpToolRefresher.RefreshAsync"/> call.</summary>
public readonly record struct McpRefreshOutcome
{
    /// <summary>The total number of tools discovered in this refresh.</summary>
    public required int ToolCount { get; init; }

    /// <summary>
    /// Whether this refresh ACTUALLY attempted to connect to at least one MCP
    /// server and failed (a network error such as a timeout or an active
    /// connection refusal). Deliberately skipping a server (a persistent
    /// configuration problem such as an invalid name/address or a missing
    /// OAuth callback configuration) is NOT included here — that is never the
    /// kind of situation that "fixes itself on retry".
    /// </summary>
    public required bool HadUnreachableServers { get; init; }
}
