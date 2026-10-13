using System.Text.Json.Serialization;

namespace Tracon;

/// <summary>
/// One non-token item a workflow function node reported: the persistent record behind
/// <c>GET /api/runs/{runId}/usage</c>.
/// </summary>
/// <remarks>
/// A node reports with <c>IWorkflowContext.ReportUsageAsync</c> and every report is an
/// item. A retried node keeps the items of its failed attempts: the work was done and
/// the provider billed it.
/// </remarks>
public sealed record NonTokenUsageRecord
{
    /// <summary>Gets the record id. A time-ordered UUID (v7).</summary>
    public required Guid Id { get; init; }

    /// <summary>Gets the run the item belongs to.</summary>
    public required Guid RunId { get; init; }

    /// <summary>
    /// Gets the name of the function node that reported the item, or <see langword="null"/>
    /// when the reporting code did not run inside a function node Tracon registered
    /// (a custom executor in a graph built in code, for example).
    /// </summary>
    public string? NodeName { get; init; }

    /// <summary>Gets the measurement and its amount.</summary>
    public required ToolCallUsage Usage { get; init; }

    /// <summary>Gets the moment the item was reported (UTC).</summary>
    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>
    /// Gets the EXPECTED tenant of the run. Defense in depth; when <see langword="null"/>
    /// no tenant check is made.
    /// </summary>
    /// <remarks>
    /// Write-side only, like <see cref="ToolInvocationRecord.TenantId"/>: it is the
    /// <c>WHERE</c> guard of the insert, never a column, and is left out of the HTTP
    /// contract.
    /// </remarks>
    [JsonIgnore]
    public string? TenantId { get; init; }
}
