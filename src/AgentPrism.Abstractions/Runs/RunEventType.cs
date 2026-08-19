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
    /// Pending requests are read from these events; no separate table was added
    /// (phase 16).
    /// </remarks>
    WorkflowRequest = 18,

    /// <summary>
    /// The run stopped because it waits for a human answer. It is the last event of
    /// the stream.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="RunCompleted"/> and <see cref="RunFailed"/>: the
    /// work neither finished nor failed. On this event the user interface shows the
    /// pending request card. Added in phase 16.
    /// </remarks>
    RunAwaitingInput = 19,

    /// <summary>
    /// An <see cref="IContentGuard"/> masked content. <c>Text</c> carries the guard
    /// name, the rule name and the direction; <c>Payload</c> carries the match count.
    /// </summary>
    /// <remarks>
    /// 🚨 Neither <c>Text</c> nor <c>Payload</c> carries the <strong>masked
    /// content</strong> — they only report THAT masking happened. When the text the
    /// model sees differs from what the user wrote, that is an event and it cannot
    /// stay silent (the K-089 rule: a decision that cannot be recorded is a decision
    /// that was not taken).
    /// </remarks>
    ContentMasked = 20,

    /// <summary>
    /// An <see cref="IContentGuard"/> blocked content. <c>Text</c> carries the guard
    /// name, the rule name and the direction.
    /// </summary>
    /// <remarks>
    /// 🚨 The payload <strong>does not carry the blocked content</strong>. After the
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
    /// 🚨 A model switch is never silent (phase 62). This event is written in
    /// addition to a span tag, not instead of it — an operator reading only
    /// the run record must still see which model actually answered.
    /// </remarks>
    ModelFallbackUsed = 22,

    /// <summary>
    /// A reasoning (thinking) delta arrived from the model. Off by default
    /// (<c>AgentPrismRunRecordingOptions.RecordReasoningDeltas</c>).
    /// </summary>
    ReasoningDelta = 23,
}
