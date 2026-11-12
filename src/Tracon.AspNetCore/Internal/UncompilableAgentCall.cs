using Microsoft.Extensions.Logging;

namespace Tracon;

/// <summary>
/// What a remote caller of the MCP server or the A2A endpoint is told when the agent it asked for cannot be compiled.
/// </summary>
/// <remarks>
/// <para>
/// A compile refusal (<see cref="TraconCompilationException"/>, <see cref="TraconTenantProviderRefusedException"/>) is a
/// configuration error on this server: the tenant's provider binding points at a key with no value, an egress policy no
/// longer allows the provider. Its own message names the configuration key, which is the operator's business and not the
/// caller's, so it is never sent. What the protocol libraries sent instead was their own sentence ("An internal error
/// occurred."), which a caller cannot tell from a failure that a second try would cure.
/// </para>
/// <para>
/// The text names the agent and the stable error type, says that a retry will not succeed, and carries a reference that
/// finds the full failure in the server's log. The error type is one of Tracon's constants and never carries a value.
/// </para>
/// </remarks>
internal static class UncompilableAgentCall
{
    /// <summary>Whether an exception from resolving an agent is a compile refusal.</summary>
    /// <param name="exception">The exception the catalog raised.</param>
    /// <returns><see langword="true"/> for a compile refusal.</returns>
    public static bool Matches(Exception exception)
        => exception is TraconCompilationException or TraconTenantProviderRefusedException;

    /// <summary>Logs the full failure under a reference and returns the text the remote caller gets.</summary>
    /// <param name="exception">The compile refusal.</param>
    /// <param name="agentName">The agent the caller asked for.</param>
    /// <param name="protocol">The protocol, for the log line (<c>a2a</c> or <c>mcp</c>).</param>
    /// <param name="logger">The logger the full failure is written to.</param>
    /// <returns>The text; it holds no configuration key and no value.</returns>
    public static string Describe(TraconException exception, string agentName, string protocol, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(exception);
        ArgumentNullException.ThrowIfNull(logger);

        var reference = SafeErrorText.NewCorrelationId();

        logger.LogError(
            exception,
            "A remote {Protocol} call to agent '{AgentName}' was refused because the agent cannot be compiled. (ref: {Reference})",
            protocol,
            agentName,
            reference);

        return $"Agent '{agentName}' cannot be run: it could not be compiled ({exception.ErrorType}). " +
               $"This is a configuration error on the server and a retry will not succeed. (ref: {reference})";
    }
}
