using System.Text.Json.Serialization;

namespace Tracon;

/// <summary>
/// How a workflow treats more than one answer to the same pending human request.
/// </summary>
/// <remarks>
/// <para>
/// The mode is chosen per workflow: <see cref="WorkflowDefinition.ResponseMode"/> for a
/// stored definition, <c>WorkflowRegistrationOptions.ResponseMode</c> for a workflow defined
/// in code. A run that waits keeps the mode of the definition version it recorded, so an
/// edit made while it waits does not change how it is answered.
/// </para>
/// <para>
/// Every answer that starts a run is written to the audit trail before the run starts,
/// whatever the mode, and refused when it cannot be written.
/// </para>
/// <para>
/// Written as a <strong>name</strong> in JSON (<c>"SingleAnswer"</c>), not a number.
/// </para>
/// </remarks>
[JsonConverter(typeof(JsonStringEnumConverter<WorkflowResponseMode>))]
public enum WorkflowResponseMode
{
    /// <summary>
    /// Every answer opens a new run that continues from the request, so a second answer
    /// to the same request opens a second branch. The default.
    /// </summary>
    Branch = 0,

    /// <summary>
    /// One answer per request. Repeating the same answer follows the run the first one
    /// started instead of opening another; a different answer is refused with
    /// <c>409 Conflict</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Two answers are the same when their <c>approved</c>, <c>text</c> and <c>data</c>
    /// are the same; the checkpoint and definition version they name do not count. An
    /// answer whose run stopped before the workflow received it (the connection dropped,
    /// the run failed or was cancelled) can be given again, and it starts a new run. A
    /// queued answer whose job is still waiting or running is followed instead: the job's
    /// recovery owns that work.
    /// </para>
    /// <para>
    /// <strong>A run its process left running locks the request.</strong> When a process
    /// stops in the middle of a streamed answer's run, the run row stays
    /// <see cref="RunStatus.Running"/>, and a repeat of the answer follows a run that never
    /// moves. Only run reconciliation (<see cref="RunReconciliationOptions.Enabled"/>,
    /// <c>Tracon:RunReconciliation:Enabled</c>, off by default) closes such a run; turn it on where a single-answer workflow must be answerable again
    /// after a crash. A queued answer is not affected: its job continues or closes the run.
    /// </para>
    /// </remarks>
    SingleAnswer = 1,
}
