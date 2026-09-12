using System.Text.Json.Serialization;

namespace Tracon;

/// <summary>
/// A span's OpenTelemetry kind. Values are stored in the database as
/// <c>smallint</c>; the numbers are stable.
/// </summary>
/// <remarks>
/// Follows <see cref="System.Diagnostics.ActivityKind"/>'s order exactly. We
/// carry our own enum because <c>Tracon.Abstractions</c> must not leak the
/// <c>System.Diagnostics.DiagnosticSource</c> type into its public surface.
/// </remarks>
[JsonConverter(typeof(JsonStringEnumConverter<TraceSpanKind>))]
public enum TraceSpanKind
{
    /// <summary>An internal operation. The default.</summary>
    Internal = 0,

    /// <summary>The span handling an incoming request.</summary>
    Server = 1,

    /// <summary>The span making an outgoing call.</summary>
    Client = 2,

    /// <summary>The span producing a message.</summary>
    Producer = 3,

    /// <summary>The span consuming a message.</summary>
    Consumer = 4,
}

/// <summary>A span's result status. <c>smallint</c> in the database.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<TraceSpanStatus>))]
public enum TraceSpanStatus
{
    /// <summary>No status was reported.</summary>
    Unset = 0,

    /// <summary>The operation completed successfully.</summary>
    Ok = 1,

    /// <summary>The operation failed.</summary>
    Error = 2,
}

/// <summary>
/// A single persisted OpenTelemetry span.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Id"/> and <see cref="ParentId"/> are <strong>derived</strong>
/// from the W3C identifiers, not randomly generated. When a span
/// completes, its parent may not have completed yet, and the parent's
/// database identifier is unknown. Deriving it (<c>trace_id</c> + <c>span_id</c>
/// → SHA-256 → first 16 bytes) makes matching work without a map and without
/// ordering; writing the same span twice also produces the same identifier.
/// </para>
/// <para>
/// The W3C identifiers are stored separately in the <see cref="SpanId"/>
/// field: this is needed so the user can find the same span in their own APM
/// system (Jaeger, Application Insights).
/// </para>
/// </remarks>
public sealed record TraceSpan
{
    /// <summary>The database identifier. Derived from the W3C identifiers.</summary>
    public required Guid Id { get; init; }

    /// <summary>The parent span's database identifier. <see langword="null"/> for the root span.</summary>
    public Guid? ParentId { get; init; }

    /// <summary>The W3C span identifier (16-character hex).</summary>
    public required string SpanId { get; init; }

    /// <summary>The span name. Example: <c>chat gpt-5.4-mini</c>, <c>invoke_agent support</c>.</summary>
    public required string Name { get; init; }

    /// <summary>The span kind.</summary>
    public TraceSpanKind Kind { get; init; }

    /// <summary>The start time (UTC).</summary>
    public required DateTimeOffset StartedAt { get; init; }

    /// <summary>The end time (UTC).</summary>
    public DateTimeOffset? EndedAt { get; init; }

    /// <summary>The result status.</summary>
    public TraceSpanStatus Status { get; init; }

    /// <summary>
    /// The span attributes. GenAI semantic convention keys
    /// (<c>gen_ai.request.model</c>, <c>gen_ai.usage.input_tokens</c>) go here.
    /// </summary>
    public IReadOnlyDictionary<string, string> Attributes { get; init; } =
        new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>The span's duration. <see langword="null"/> if the span has not ended.</summary>
    [JsonIgnore]
    public TimeSpan? Duration => EndedAt is { } ended ? ended - StartedAt : null;
}

/// <summary>
/// A run's span tree. The waterfall view is built on top of this.
/// </summary>
public sealed record RunTrace
{
    /// <summary>The database identifier.</summary>
    public required Guid Id { get; init; }

    /// <summary>The W3C trace identifier (32-character hex).</summary>
    public required string TraceId { get; init; }

    /// <summary>The associated run. <see langword="null"/> for spans without a run.</summary>
    public Guid? RunId { get; init; }

    /// <summary>The tenant identifier.</summary>
    public required string TenantId { get; init; }

    /// <summary>The first span's start (UTC).</summary>
    public required DateTimeOffset StartedAt { get; init; }

    /// <summary>The last span's end (UTC).</summary>
    public DateTimeOffset? EndedAt { get; init; }

    /// <summary>The spans, ordered by start time.</summary>
    public IReadOnlyList<TraceSpan> Spans { get; init; } = [];
}
