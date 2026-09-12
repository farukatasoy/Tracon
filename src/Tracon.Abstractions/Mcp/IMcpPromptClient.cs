namespace Tracon;

/// <summary>The result status of an MCP request.</summary>
public enum McpOperationStatus
{
    /// <summary>The request succeeded.</summary>
    Ok = 0,

    /// <summary>No server is registered with this name.</summary>
    ServerNotFound = 1,

    /// <summary>The server does not report the requested capability (<c>prompts</c>/<c>resources</c>).</summary>
    CapabilityUnsupported = 2,

    /// <summary>Could not connect to the server, or an error occurred during the request.</summary>
    ConnectionFailed = 3,

    /// <summary>The requested prompt/resource does not exist on the server.</summary>
    ItemNotFound = 4,

    /// <summary>
    /// The requested resource URI is not in the resource list reported by the
    /// server. Free-form URI reads are rejected because they carry an SSRF risk.
    /// </summary>
    UriNotDeclared = 5,
}

/// <summary>The summary of an MCP prompt argument.</summary>
public sealed record McpPromptArgumentSummary
{
    /// <summary>The argument name.</summary>
    public required string Name { get; init; }

    /// <summary>The description.</summary>
    public string? Description { get; init; }

    /// <summary>Whether the argument is required.</summary>
    public bool Required { get; init; }
}

/// <summary>The summary of an MCP prompt.</summary>
public sealed record McpPromptSummary
{
    /// <summary>The prompt name.</summary>
    public required string Name { get; init; }

    /// <summary>The title shown in the UI.</summary>
    public string? Title { get; init; }

    /// <summary>The description.</summary>
    public string? Description { get; init; }

    /// <summary>The arguments the prompt accepts.</summary>
    public IReadOnlyList<McpPromptArgumentSummary> Arguments { get; init; } = [];
}

/// <summary>The result of listing prompts.</summary>
public sealed record McpPromptListResult
{
    /// <summary>The result status.</summary>
    public required McpOperationStatus Status { get; init; }

    /// <summary>The prompt list. Empty when <see cref="Status"/> is not <see cref="McpOperationStatus.Ok"/>.</summary>
    public IReadOnlyList<McpPromptSummary> Prompts { get; init; } = [];
}

/// <summary>
/// The resolved content of a prompt.
/// </summary>
/// <remarks>
/// <strong>This is a snapshot.</strong> This content MUST BE COPIED into the agent's instructions; it is not re-fetched at run time. A remote server cannot change the agent's behavior through this path.
/// </remarks>
public sealed record McpPromptContent
{
    /// <summary>The text concatenated from the prompt's messages.</summary>
    public required string Text { get; init; }

    /// <summary>
    /// The content's SHA-256 digest (hex). The snapshot is stored in
    /// <c>AgentDefinition.Metadata</c> under the <c>mcp.prompt.hash</c> key;
    /// when the server's content changes, the UI compares this digest and
    /// shows a badge.
    /// </summary>
    public required string Hash { get; init; }
}

/// <summary>The client that lists an MCP server's prompts and resolves their content.</summary>
/// <remarks>
/// <para>
/// Capability checking is mandatory: if the server does not report
/// <c>ServerCapabilities.Prompts</c>, the request is never sent, and
/// <see cref="McpOperationStatus.CapabilityUnsupported"/> is returned.
/// </para>
/// <para>
/// Each call opens a short-lived, separate connection; it is not shared with
/// the tool-discovery connection <c>Tracon.Mcp</c> keeps in the
/// background. This lets an administrator see a fresh prompt list at any
/// moment — it does not wait for the refresh interval.
/// </para>
/// <para>
/// <strong>DI lifetime — singleton.</strong> Registered as a singleton with
/// <c>TryAdd</c>; a consumer's own registration wins. Per-call connections
/// (above) mean the implementation itself carries no per-run state.
/// </para>
/// </remarks>
public interface IMcpPromptClient
{
    /// <summary>Fetches a server's prompt list.</summary>
    /// <param name="tenantId">The tenant identifier.</param>
    /// <param name="serverName">The server name.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    ValueTask<McpPromptListResult> ListPromptsAsync(
        string tenantId,
        string serverName,
        CancellationToken cancellationToken = default);

    /// <summary>Resolves a prompt's content with arguments.</summary>
    /// <param name="tenantId">The tenant identifier.</param>
    /// <param name="serverName">The server name.</param>
    /// <param name="promptName">The prompt name.</param>
    /// <param name="arguments">The prompt arguments.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    ValueTask<(McpOperationStatus Status, McpPromptContent? Content)> GetPromptAsync(
        string tenantId,
        string serverName,
        string promptName,
        IReadOnlyDictionary<string, string>? arguments,
        CancellationToken cancellationToken = default);
}
