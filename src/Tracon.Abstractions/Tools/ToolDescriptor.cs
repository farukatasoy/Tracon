namespace Tracon;

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

    /// <summary>
    /// Whether the tool's body runs on the caller (typically a browser)
    /// instead of on the server.
    /// </summary>
    /// <remarks>
    /// A client-side tool (<c>AddClientTool</c>) is registered as a
    /// declaration only: the model can call it, but the server never runs
    /// it. The caller must send the result back via
    /// <c>AgentRunRequest.ToolResults</c> for the run to continue.
    /// </remarks>
    public bool RunsOnClient { get; init; }

    /// <summary>The tool's effect class. Defaults to <see cref="ToolEffect.Read"/>.</summary>
    /// <remarks>
    /// Information, not a gate — see <see cref="ToolEffect"/>. An MCP-sourced
    /// tool defaults to <see cref="ToolEffect.External"/> instead: its
    /// definition lives on a remote server and can change, so the most
    /// cautious class is the honest default.
    /// </remarks>
    public ToolEffect Effect { get; init; }

    /// <summary>
    /// The permission name a caller must hold to call this tool, or
    /// <see langword="null"/> when the tool declares none.
    /// </summary>
    /// <remarks>
    /// Passed to <see cref="IToolAuthorizationHandler"/> as-is; Tracon
    /// does not resolve or validate its meaning.
    /// </remarks>
    public string? RequiredPermission { get; init; }

    /// <summary>
    /// The longest duration this tool's call may run, or <see langword="null"/>
    /// to use the installation default (<c>TraconOptions.Tools.DefaultTimeout</c>).
    /// </summary>
    public TimeSpan? Timeout { get; init; }

    /// <summary>
    /// Whether this tool's call may run again when an interrupted run is
    /// continued. See <c>TraconToolRegistration.SafeToRepeat</c>.
    /// </summary>
    public bool SafeToRepeat { get; init; }

    /// <summary>
    /// The most bytes (UTF-8) this tool's result may carry, or
    /// <see langword="null"/> to use the installation default
    /// (<c>TraconOptions.Tools.DefaultMaxOutputBytes</c>).
    /// </summary>
    public int? MaxOutputBytes { get; init; }
}
