using System.ComponentModel;

namespace Tracon;

/// <summary>The result status of an MCP request.</summary>
internal enum McpOperationStatus
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

[Description("The summary of an MCP prompt argument.")]
internal sealed record McpPromptArgumentSummary
{
    [Description("The argument name.")]
    public required string Name { get; init; }

    [Description("The description.")]
    public string? Description { get; init; }

    [Description("Whether the argument is required.")]
    public bool Required { get; init; }
}

[Description("The summary of an MCP prompt.")]
internal sealed record McpPromptSummary
{
    [Description("The prompt name.")]
    public required string Name { get; init; }

    [Description("The title shown in the UI.")]
    public string? Title { get; init; }

    [Description("The description.")]
    public string? Description { get; init; }

    [Description("The arguments the prompt accepts.")]
    public IReadOnlyList<McpPromptArgumentSummary> Arguments { get; init; } = [];
}

/// <summary>The result of listing prompts.</summary>
internal sealed record McpPromptListResult
{
    /// <summary>The result status.</summary>
    public required McpOperationStatus Status { get; init; }

    /// <summary>The prompt list. Empty when <see cref="Status"/> is not <see cref="McpOperationStatus.Ok"/>.</summary>
    public IReadOnlyList<McpPromptSummary> Prompts { get; init; } = [];
}

/// <remarks>
/// <strong>This is a snapshot.</strong> This content MUST BE COPIED into the agent's instructions; it is not re-fetched at run time. A remote server cannot change the agent's behavior through this path.
/// </remarks>
[Description("The resolved content of a prompt.")]
internal sealed record McpPromptContent
{
    [Description("The text concatenated from the prompt's messages.")]
    public required string Text { get; init; }

    [Description(
        "The content's SHA-256 digest (hex). The snapshot is stored in\n" +
        "`AgentDefinition.Metadata` under the `mcp.prompt.hash` key;\n" +
        "when the server's content changes, the UI compares this digest and\n" +
        "shows a badge.")]
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
/// <c>TryAdd</c>. This interface is plumbing between Tracon's own packages;
/// a consumer does not implement it. Per-call connections
/// (above) mean the implementation itself carries no per-run state.
/// </para>
/// </remarks>
internal interface IMcpPromptClient
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
