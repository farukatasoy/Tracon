namespace AgentPrism;

/// <summary>The summary of an MCP resource.</summary>
public sealed record McpResourceSummary
{
    /// <summary>The resource URI.</summary>
    public required string Uri { get; init; }

    /// <summary>The resource name.</summary>
    public required string Name { get; init; }

    /// <summary>The MIME type.</summary>
    public string? MimeType { get; init; }

    /// <summary>The description.</summary>
    public string? Description { get; init; }
}

/// <summary>The result of listing resources.</summary>
public sealed record McpResourceListResult
{
    /// <summary>The result status.</summary>
    public required McpOperationStatus Status { get; init; }

    /// <summary>The resource list. Empty when <see cref="Status"/> is not <see cref="McpOperationStatus.Ok"/>.</summary>
    public IReadOnlyList<McpResourceSummary> Resources { get; init; } = [];
}

/// <summary>The content of a resource that was read.</summary>
public sealed record McpResourceContent
{
    /// <summary>The resource URI.</summary>
    public required string Uri { get; init; }

    /// <summary>The MIME type.</summary>
    public string? MimeType { get; init; }

    /// <summary>The text content. <see langword="null"/> for binary resources.</summary>
    public string? Text { get; init; }

    /// <summary>Whether the resource is binary (a blob).</summary>
    public bool IsBinary { get; init; }

    /// <summary>The byte size of the raw content (before truncation).</summary>
    public int ByteSize { get; init; }

    /// <summary>
    /// Whether the content was truncated because it exceeded the size limit.
    /// </summary>
    public bool Truncated { get; init; }
}

/// <summary>The client that lists and reads an MCP server's resources.</summary>
/// <remarks>
/// <para>
/// Capability checking is mandatory: if the server does not report
/// <c>ServerCapabilities.Resources</c>, the request is never sent.
/// </para>
/// <para>
/// <strong>Only declared URIs can be read.</strong> <see cref="ReadResourceAsync"/>
/// first compares against the set the server reported via
/// <c>ListResourcesAsync</c>; a URI not in that set is rejected with
/// <see cref="McpOperationStatus.UriNotDeclared"/>. Otherwise this would be an
/// SSRF tool.
/// </para>
/// <para>
/// <strong>DI lifetime — singleton.</strong> Registered as a singleton with
/// <c>TryAdd</c>; a consumer's own registration wins. Each call opens its own
/// short-lived connection, so the implementation carries no per-run state.
/// </para>
/// </remarks>
public interface IMcpResourceClient
{
    /// <summary>Fetches a server's resource list.</summary>
    /// <param name="tenantId">The tenant identifier.</param>
    /// <param name="serverName">The server name.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    ValueTask<McpResourceListResult> ListResourcesAsync(
        string tenantId,
        string serverName,
        CancellationToken cancellationToken = default);

    /// <summary>Reads a resource.</summary>
    /// <param name="tenantId">The tenant identifier.</param>
    /// <param name="serverName">The server name.</param>
    /// <param name="uri">The URI of the resource to read; must be in the server's declared set.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    ValueTask<(McpOperationStatus Status, McpResourceContent? Content)> ReadResourceAsync(
        string tenantId,
        string serverName,
        string uri,
        CancellationToken cancellationToken = default);
}
