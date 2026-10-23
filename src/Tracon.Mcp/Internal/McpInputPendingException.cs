namespace Tracon;

/// <summary>
/// Thrown by <see cref="McpElicitationHandler"/> to leave the MCP client's retry loop when the server asked
/// the user for input and no answer exists yet: the tool call is suspended, not failed.
/// </summary>
/// <remarks>
/// <see cref="McpElicitingTool"/> turns it into the suspended call's result. It never leaves the tool: the
/// request itself is read from the call scope, not from the exception, because the SDK may wrap what a
/// handler throws.
/// </remarks>
internal sealed class McpInputPendingException : Exception
{
    /// <summary>Creates the exception.</summary>
    public McpInputPendingException()
        : base("The MCP server asked for user input; the tool call is suspended until the user answers.")
    {
    }
}
