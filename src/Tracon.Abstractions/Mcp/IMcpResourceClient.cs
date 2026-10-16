using System.ComponentModel;

namespace Tracon;

[Description("The summary of an MCP resource.")]
internal sealed record McpResourceSummary
{
    [Description("The resource URI.")]
    public required string Uri { get; init; }

    [Description("The resource name.")]
    public required string Name { get; init; }

    [Description("The MIME type.")]
    public string? MimeType { get; init; }

    [Description("The description.")]
    public string? Description { get; init; }
}

/// <summary>The result of listing resources.</summary>
internal sealed record McpResourceListResult
{
    /// <summary>The result status.</summary>
    public required McpOperationStatus Status { get; init; }

    /// <summary>The resource list. Empty when <see cref="Status"/> is not <see cref="McpOperationStatus.Ok"/>.</summary>
    public IReadOnlyList<McpResourceSummary> Resources { get; init; } = [];
}

[Description("The content of a resource that was read.")]
internal sealed record McpResourceContent
{
    [Description("The resource URI.")]
    public required string Uri { get; init; }

    [Description("The MIME type.")]
    public string? MimeType { get; init; }

    [Description("The text content. `null` for binary resources.")]
    public string? Text { get; init; }

    [Description("Whether the resource is binary (a blob).")]
    public bool IsBinary { get; init; }

    [Description("The byte size of the raw content (before truncation).")]
    public int ByteSize { get; init; }

    [Description("Whether the content was truncated because it exceeded the size limit.")]
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
/// <c>TryAdd</c>. This interface is plumbing between Tracon's own packages;
/// a consumer does not implement it. Each call opens its own
/// short-lived connection, so the implementation carries no per-run state.
/// </para>
/// </remarks>
internal interface IMcpResourceClient
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
