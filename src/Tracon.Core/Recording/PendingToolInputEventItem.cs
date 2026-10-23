namespace Tracon;

/// <summary>
/// One entry of the <see cref="RunEventType.RunAwaitingInput"/> event's <c>Payload</c>,
/// written when a root agent run closed with <see cref="RunStatus.AwaitingInput"/> because
/// an MCP server asked the user for input in the middle of a tool call.
/// </summary>
/// <remarks>
/// Names the request and where it came from, nothing more: neither the requested fields nor
/// the call's arguments are copied into the event stream. The caller got them in the response.
/// </remarks>
internal sealed record PendingToolInputEventItem
{
    /// <summary>Gets the identity Tracon gave the request.</summary>
    public required string RequestId { get; init; }

    /// <summary>Gets the qualified name of the suspended tool.</summary>
    public required string ToolName { get; init; }

    /// <summary>Gets the name of the MCP server that asked.</summary>
    public required string ServerName { get; init; }
}
