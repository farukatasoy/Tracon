using System.Text.Json.Serialization;

namespace AgentPrism;

/// <summary>
/// A single event produced during a run. Events are <em>append-only</em>: they are
/// never updated, only added.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Sequence"/> comes from a single writer and increases from 0 within a
/// run. That is what lets live streaming (SSE) and historical replay share one code
/// path; a client can resume from the sequence number it reached when a connection
/// dropped.
/// </para>
/// <para>
/// Which fields are populated for each event type:
/// <list type="table">
///   <item><term><see cref="RunEventType.RunStarted"/></term><description>
///     <c>Text</c>, the first user message that triggered the run.
///     It is the ONLY place the input text is persisted.</description></item>
///   <item><term><see cref="RunEventType.MessageDelta"/></term><description><c>Text</c></description></item>
/// <item>
/// <term><see cref="RunEventType.ToolInvoking"/></term><description><see cref="ToolName"/>, <see cref="ToolCallId"/>, <see cref="Payload"/> (arguments)</description>
/// </item>
/// <item>
/// <term><see cref="RunEventType.ToolInvoked"/></term><description><see cref="ToolName"/>, <see cref="ToolCallId"/>, <see cref="Payload"/> (result)</description>
/// </item>
/// <item>
/// <term><see cref="RunEventType.ToolFailed"/></term><description><see cref="ToolName"/>, <see cref="ToolCallId"/>, <c>Text</c> (error message)</description>
/// </item>
///   <item><term><see cref="RunEventType.RunFailed"/></term><description><c>Text</c> (error message)</description></item>
/// </list>
/// </para>
/// </remarks>
public sealed record RunEvent
{
    /// <summary>Gets the run the event belongs to.</summary>
    public required Guid RunId { get; init; }

    /// <summary>Gets the sequence number within the run. It starts at 0 and increases without gaps.</summary>
    public required long Sequence { get; init; }

    /// <summary>Gets the event type.</summary>
    public required RunEventType Type { get; init; }

    /// <summary>Gets the moment the event occurred (UTC).</summary>
    public required DateTimeOffset Timestamp { get; init; }

    /// <summary>Gets the text content. Its meaning depends on the event type.</summary>
    public string? Text { get; init; }

    /// <summary>Gets the tool name. Populated only on tool events.</summary>
    public string? ToolName { get; init; }

    /// <summary>Gets the tool call id, which separates several calls to the same tool.</summary>
    public string? ToolCallId { get; init; }

    /// <summary>Gets the free-form JSON payload that carries tool arguments and results.</summary>
    public string? Payload { get; init; }

    /// <summary>
    /// Gets the EXPECTED tenant of the run the event is written to. Defence in depth.
    /// </summary>
    /// <remarks>
    /// <para>
    /// When populated, the store applies the write only if the target run belongs to
    /// that tenant; otherwise the write is dropped and an error is raised. When <see
    /// langword="null"/> no tenant check is made.
    /// </para>
    /// <para>
    /// This field is NOT filled from the ambient tenant. <c>TenantId</c> may
    /// deliberately override the ambient tenant — that is how workflows and the job
    /// queue work — and filtering by the ambient value would silently drop legitimate
    /// writes. The value is the tenant known to whoever opened the run.
    /// </para>
    /// <para>
    /// The field is WRITE-side only: it is not stored in a column, it is only the
    /// <c>WHERE</c> guard of the write. Reading it back would always give <see
    /// langword="null"/>, so it is removed from the transport contract with <see
    /// cref="JsonIgnoreAttribute"/>.
    /// </para>
    /// </remarks>
    [JsonIgnore]
    public string? TenantId { get; init; }
}
