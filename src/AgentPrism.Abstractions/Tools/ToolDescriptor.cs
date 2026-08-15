namespace AgentPrism;

/// <summary>
/// The UI-facing definition of a tool registered in code. The agent editor
/// shows a selection from this list; it does not accept free-text input.
/// </summary>
public sealed record ToolDescriptor
{
    /// <summary>The tool name. Used in agent definitions.</summary>
    public required string Name { get; init; }

    /// <summary>The description that lets the model understand when to call the tool.</summary>
    public string? Description { get; init; }

    /// <summary>The arguments' JSON schema.</summary>
    public string? JsonSchema { get; init; }

    /// <summary>
    /// Whether explicit approval is required before the call.
    /// </summary>
    /// <remarks>
    /// If <see langword="true"/>, the compiler wraps the tool with
    /// <c>ApprovalRequiredAIFunction</c>; Microsoft Agent Framework produces a
    /// <c>ToolApprovalRequestContent</c> instead of running the tool, and the
    /// call waits for the user's approval.
    /// </remarks>
    public bool RequiresApproval { get; init; }

    /// <summary>
    /// The tool's source. <see langword="null"/> for tools defined in code;
    /// the server name for tools coming from a remote MCP server.
    /// </summary>
    /// <remarks>
    /// The UI shows this field as a <em>separate badge</em>: the tool
    /// definition lives not in code but on a remote server, and that server
    /// may change the definition.
    /// </remarks>
    public string? Source { get; init; }
}
