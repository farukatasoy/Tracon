using System.Text.Json.Serialization;

namespace AgentPrism;

/// <summary>
/// Event types produced during a run. The user interface maps these directly onto
/// visual elements, so the values must stay stable.
/// </summary>
/// <remarks>
/// Written to JSON <strong>by name</strong> (<c>"Code"</c>), not by number. The
/// wire contract explains itself that way and survives a change in value order.
/// The converter sits on the type, so the format is the same everywhere without
/// touching the consumer's application-wide JSON options. No enum is PERSISTED as
/// JSON (RunStatus and RunEventType are smallint in the database,
/// AgentDefinitionOrigin is rebuilt on read), so a format change does not affect
/// stored data.
/// </remarks>
[JsonConverter(typeof(JsonStringEnumConverter<RunEventType>))]
public enum RunEventType
{
    /// <summary>The run started.</summary>
    RunStarted = 0,

    /// <summary>A text fragment arrived from the model. Produced only for streaming runs.</summary>
    MessageDelta = 1,

    /// <summary>A message completed.</summary>
    MessageCompleted = 2,

    /// <summary>A tool is about to be called. The arguments are in the event payload.</summary>
    ToolInvoking = 3,

    /// <summary>A tool completed successfully. The result is in the event payload.</summary>
    ToolInvoked = 4,

    /// <summary>A tool failed.</summary>
    ToolFailed = 5,

    /// <summary>The run finished successfully.</summary>
    RunCompleted = 6,

    /// <summary>The run ended with an error.</summary>
    RunFailed = 7,

    /// <summary>
    /// This run started a child agent run. <c>Text</c> carries the child agent's
    /// name and <c>Payload</c> the child run id.
    /// </summary>
    /// <remarks>
    /// The child run's own events are not mirrored into the root stream; only its
    /// start and end are reported. Full mirroring would multiply the event volume
    /// along the tree and send the same text to the client twice.
    /// </remarks>
    ChildRunStarted = 8,

    /// <summary>
    /// A child agent run finished. <c>Text</c> carries the child agent's name and
    /// <c>Payload</c> the child run id.
    /// </summary>
    ChildRunCompleted = 9,

    /// <summary>
    /// The chat history was compacted. <c>Text</c> carries a short summary sentence
    /// and <c>Payload</c> the before/after message and token counts.
    /// </summary>
    HistoryCompacted = 10,

    /// <summary>
    /// A workflow execution started. <c>Text</c> carries the workflow name.
    /// </summary>
    /// <remarks>
    /// This is separate from <see cref="RunStarted"/>: that one reports that the
    /// <c>runs</c> row was opened, this one reports that the Microsoft Agent
    /// Framework execution engine actually took over the graph. Compilation and
    /// validation happen in between, and an error there ends the run before the
    /// graph ever starts.
    /// </remarks>
    WorkflowStarted = 11,

    /// <summary>
    /// A super-step started. <c>Text</c> carries the step number and <c>Payload</c>
    /// the names of the executors that sent messages.
    /// </summary>
    SuperStepStarted = 12,

    /// <summary>
    /// A super-step completed. <c>Text</c> carries the step number and
    /// <c>Payload</c> the activated executor names plus the checkpoint id, if any.
    /// </summary>
    SuperStepCompleted = 13,

    /// <summary>An executor was invoked. <c>Text</c> carries the executor id.</summary>
    ExecutorInvoked = 14,

    /// <summary>An executor completed. <c>Text</c> carries the executor id.</summary>
    ExecutorCompleted = 15,

    /// <summary>
    /// An executor failed. <c>Text</c> carries the executor id and <c>Payload</c>
    /// the error message.
    /// </summary>
    ExecutorFailed = 16,

    /// <summary>
    /// The workflow produced an output. <c>Text</c> carries a text summary of it.
    /// </summary>
    WorkflowOutput = 17,

    /// <summary>
    /// The workflow waits for an external answer (human in the loop). <c>Text</c>
    /// carries the request id and <c>Payload</c> a JSON summary of the request.
    /// </summary>
    /// <remarks>
    /// The payload carries <em>enough to rebuild</em> the pending request: port id,
    /// request id, request and response type names, and the data to display.
    /// Pending requests are read from these events; no separate table was added.
    /// </remarks>
    WorkflowRequest = 18,

    /// <summary>
    /// The run stopped because it waits for a human answer. It is the last event of
    /// the stream.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Separate from <see cref="RunCompleted"/> and <see cref="RunFailed"/>: the
    /// work neither finished nor failed. On this event the user interface shows the
    /// pending request card.
    /// </para>
    /// <para>
    /// Produced for two different terminal statuses, and <c>Payload</c> differs between
    /// them. For a workflow closing with <see cref="RunStatus.AwaitingInput"/>, the
    /// details already arrived on the preceding <see cref="WorkflowRequest"/> event and
    /// this one carries no payload of its own. For a root run closing with
    /// <see cref="RunStatus.AwaitingApproval"/>, <c>Payload</c> is a JSON array, one
    /// entry per pending tool call: <c>requestId</c>, <c>toolName</c>, and — when a
    /// consumer registered an <see cref="IToolApprovalPresenter"/> and it resolved
    /// something — <c>entityType</c>, <c>entityId</c>, <c>entityName</c>, and
    /// <c>message</c> from <see cref="ToolApprovalPresentation"/>. It never carries the
    /// call's raw arguments; those are read from <see cref="PendingApproval.Arguments"/>
    /// or from the <c>ToolApprovalRequestContent</c> already present in the response.
    /// </para>
    /// </remarks>
    RunAwaitingInput = 19,

    /// <summary>
    /// An <see cref="IContentGuard"/> masked content. <c>Text</c> carries the guard
    /// name, the rule name and the direction; <c>Payload</c> carries the same four
    /// facts as JSON (<c>guard</c>, <c>rule</c>, <c>direction</c>, <c>action</c>).
    /// </summary>
    /// <remarks>
    /// Neither <c>Text</c> nor <c>Payload</c> carries the <strong>masked
    /// content</strong> — they only report THAT masking happened. When the text the
    /// model sees differs from what the user wrote, that is an event and it cannot
    /// stay silent (that rule: a decision that cannot be recorded is a decision
    /// that was not taken).
    /// </remarks>
    ContentMasked = 20,

    /// <summary>
    /// An <see cref="IContentGuard"/> blocked content. <c>Text</c> carries the guard
    /// name, the rule name and the direction; <c>Payload</c> carries the same four
    /// facts as JSON (<c>guard</c>, <c>rule</c>, <c>direction</c>, <c>action</c>) —
    /// the same shape <see cref="ContentMasked"/> writes.
    /// </summary>
    /// <remarks>
    /// The payload <strong>does not carry the blocked content</strong>. After the
    /// event the run becomes <c>Failed</c> and <c>runs.error_type</c> is written as
    /// <c>content_blocked</c>.
    /// </remarks>
    ContentBlocked = 21,

    /// <summary>
    /// The primary model provider was unavailable and a
    /// <see cref="ModelBinding.Fallbacks"/> link answered instead. <c>Text</c>
    /// carries the fallback provider and model (<c>"{provider}/{model}"</c>);
    /// <c>Payload</c> carries the primary and fallback bindings and the reason
    /// the primary was skipped.
    /// </summary>
    /// <remarks>
    /// A model switch is never silent. This event is written in
    /// addition to a span tag, not instead of it — an operator reading only
    /// the run record must still see which model actually answered.
    /// </remarks>
    ModelFallbackUsed = 22,

    /// <summary>
    /// A reasoning (thinking) delta arrived from the model. Off by default
    /// (<c>AgentPrismRunRecordingOptions.RecordReasoningDeltas</c>).
    /// </summary>
    ReasoningDelta = 23,

    /// <summary>
    /// A document was attached to the run through the document channel.
    /// <c>Text</c> carries the document's name; <c>Payload</c> carries its
    /// size in bytes and a content hash as JSON - never the document's
    /// content itself.
    /// </summary>
    DocumentAttached = 24,

    /// <summary>
    /// Automatic continuation of this interrupted run was refused. <c>Text</c>
    /// carries the reason (a tool whose effect cannot be safely repeated).
    /// Written on a run already closed <see cref="RunStatus.Failed"/> by
    /// orphaned-run reconciliation; the row itself does not change again.
    /// </summary>
    RunContinuationBlocked = 25,

    /// <summary>
    /// A tool result was trimmed to its byte limit. <c>ToolName</c> carries
    /// the tool's name; <c>Text</c> a short summary of how many bytes were
    /// dropped and the limit. <c>Payload</c> carries the same information as
    /// JSON, subject to <c>AgentPrismRunRecordingOptions.RecordToolPayloads</c>.
    /// </summary>
    /// <remarks>
    /// Neither field carries the dropped content itself — the point of
    /// trimming is to reduce volume, and writing the discarded part back
    /// into <c>run_events</c> would undo that.
    /// </remarks>
    ToolOutputTruncated = 26,

    /// <summary>
    /// The response failed structured output validation and the run is
    /// ending as <see cref="RunStatus.Failed"/>. <c>Text</c> carries the
    /// safe rejection reason; <c>Payload</c> carries the same reason plus
    /// <c>kind</c>, <c>schemaName</c>, <c>provider</c> and <c>model</c> as
    /// JSON, subject to <c>AgentPrismRunRecordingOptions.RecordToolPayloads</c>.
    /// </summary>
    /// <remarks>
    /// Neither field carries the model's raw response text — see
    /// <see cref="IStructuredResponseValidator"/>.
    /// </remarks>
    StructuredResponseRejected = 27,

    /// <summary>
    /// A bounded repair turn is about to run after a rejected structured
    /// response. <c>Text</c> carries the attempt count as <c>"{attempt}/{max}"</c>;
    /// <c>Payload</c> carries the same two numbers plus <c>kind</c> and
    /// <c>schemaName</c> as JSON.
    /// </summary>
    /// <remarks>
    /// Written once per repair round, immediately before the extra model call
    /// it describes, and only when <c>AgentPrismStructuredResponseOptions.MaxRepairAttempts</c>
    /// is greater than zero. See <see cref="StructuredResponseRejected"/>, which
    /// always precedes it: a repair round only starts after a rejection, never
    /// on its own.
    /// </remarks>
    StructuredResponseRepairAttempted = 28,

    /// <summary>
    /// A consumer-defined event, written directly with
    /// <c>AgentRunScope.Writer.AppendAsync</c>. <c>CustomType</c> (a
    /// namespaced string, see <see cref="RunEventCustomTypes"/>) names it;
    /// <c>Payload</c> carries whatever the consumer put there. AgentPrism
    /// makes no claim about the payload's shape — it is the consumer's own
    /// and out of scope for this contract. Like every other event's payload,
    /// it is written only when <c>AgentPrismRunRecordingOptions.RecordToolPayloads</c>
    /// is on — with it off, <c>Custom</c> still appears in the stream but
    /// <c>Payload</c> is <see langword="null"/>.
    /// </summary>
    /// <remarks>
    /// The escape hatch for the otherwise closed set of event types above.
    /// When a built-in event type already fits, use that instead —
    /// <see cref="Custom"/> is for events AgentPrism has no name for. A type
    /// under the <see cref="RunEventCustomTypes.ReservedPrefix"/> namespace
    /// is rejected at write time, so a future built-in custom type can never
    /// collide with a consumer's own.
    /// </remarks>
    Custom = 29,
}
