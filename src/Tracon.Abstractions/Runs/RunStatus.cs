using System.Text.Json.Serialization;

namespace Tracon;

/// <summary>Status of a run.</summary>
/// <remarks>
/// Written to JSON <strong>by name</strong> (<c>"Code"</c>), not by number. The
/// wire contract explains itself that way and survives a change in value order.
/// The converter sits on the type, so the format is the same everywhere without
/// touching the consumer's application-wide JSON options. No enum is PERSISTED as
/// JSON (RunStatus and RunEventType are smallint in the database,
/// AgentDefinitionOrigin is rebuilt on read), so a format change does not affect
/// stored data.
/// </remarks>
[JsonConverter(typeof(JsonStringEnumConverter<RunStatus>))]
public enum RunStatus
{
    /// <summary>The run is in progress.</summary>
    Running = 0,

    /// <summary>The run finished successfully.</summary>
    Completed = 1,

    /// <summary>The run ended with an error.</summary>
    Failed = 2,

    /// <summary>The run was cancelled.</summary>
    Canceled = 3,

    /// <summary>
    /// The run waits for human input and cannot advance without it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Seen only on <see cref="RunKind.Workflow"/> rows. When a workflow reaches an
    /// <em>external request port</em> — Magentic plan approval, for example —
    /// execution stops, its state is written to a checkpoint and the stream closes.
    /// The answer arrives through <c>POST /api/workflows/runs/{runId}/respond</c>,
    /// which opens a <strong>new</strong> run that resumes from the checkpoint.
    /// </para>
    /// <para>
    /// The value was appended at the end: statuses are stored as <c>smallint</c> in
    /// the database and shifting the existing values would misread old rows. An
    /// answered run <em>stays</em> <c>AwaitingInput</c>; rewriting history would
    /// break the append-only rule of the event stream. The continued work
    /// shows up in the new run row.
    /// </para>
    /// </remarks>
    AwaitingInput = 4,

    /// <summary>
    /// The run is queued and a worker has not started it yet.
    /// </summary>
    /// <remarks>
    /// Seen only for runs started with <c>Prefer: respond-async</c>. The
    /// row is written as <c>Queued</c> at enqueue time; when the worker actually
    /// runs the job the same id is written again and moves to <see cref="Running"/>.
    /// The value was appended at the end for the same reason as
    /// <see cref="AwaitingInput"/>.
    /// </remarks>
    Queued = 5,

    /// <summary>
    /// The run waits for an operator to decide on a tool call and cannot advance
    /// without that decision.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Seen on any <em>root</em> (<c>Depth == 0</c>) agent run, whatever started it
    /// — the management API, an OpenAI-compatible endpoint, MCP, A2A, or the queue
    /// (<c>Prefer: respond-async</c>). The first version covered only the queue path: the synchronous
    /// paths did not reflect this status at all, and a tool call waiting for
    /// approval silently looked <see cref="Completed"/>. However the decision
    /// arrives — <c>POST /api/approvals/{id}/decide</c> for the queue, the caller's
    /// own next turn for synchronous callers — the status label follows the same
    /// principle.
    /// </para>
    /// <para>
    /// It follows the SAME principle as <see cref="AwaitingInput"/>: an answered run
    /// <em>stays</em> in this status, because rewriting history would break the
    /// append-only rule of the event stream. On the queue path the decision
    /// is made through <c>POST /api/approvals/{id}/decide</c>, which enqueues a
    /// <strong>new</strong> run (same <c>sessionId</c>, new <c>RunId</c>).
    /// </para>
    /// <para>
    /// The value was appended at the end for the same reason as
    /// <see cref="AwaitingInput"/>.
    /// </para>
    /// </remarks>
    AwaitingApproval = 6,
}
