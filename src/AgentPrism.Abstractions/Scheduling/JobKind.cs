using System.Text.Json.Serialization;

namespace AgentPrism;

/// <summary>What target a job runs.</summary>
/// <remarks>
/// Written as a name in JSON, stored as <c>smallint</c> in the database. The
/// value order <strong>must not change</strong> — only append; existing rows
/// reference the numeric value.
/// </remarks>
[JsonConverter(typeof(JsonStringEnumConverter<JobKind>))]
public enum JobKind
{
    /// <summary>Runs an agent as a batch over a set of inputs.</summary>
    AgentBatch = 0,

    /// <summary>Runs a workflow, or responds to a pending workflow.</summary>
    Workflow = 1,

    /// <summary>
    /// An evaluation (eval) run. Phase 18 adds its own <c>IJobHandler</c>
    /// implementation for this value.
    /// </summary>
    Eval = 2,

    /// <summary>
    /// A single webhook delivery attempt (Phase 21). The payload carries the
    /// delivery record's identifier; the body is read from the
    /// <c>webhook_deliveries</c> table.
    /// </summary>
    WebhookDelivery = 3,

    /// <summary>
    /// A retention sweep (Phase 25). <c>TargetName</c> is either a specific
    /// <see cref="RetentionTargets"/> value or <c>"*"</c>, which processes all
    /// enabled policies.
    /// </summary>
    Retention = 4,

    /// <summary>
    /// A single queued (durable) agent run (Phase 46).
    /// </summary>
    /// <remarks>
    /// Runs started with <c>Prefer: respond-async</c> run under this kind.
    /// Unlike <see cref="AgentBatch"/>, it does NOT require an item set, and
    /// the run identifier is generated IN ADVANCE by the caller (the HTTP
    /// layer) — <c>JobRecord.Id</c> carries the same value as the run identifier.
    /// </remarks>
    AgentRun = 5,

    /// <summary>
    /// Scores a sampled production run (Phase 49).
    /// </summary>
    /// <remarks>
    /// The payload is empty or for diagnostics; the identifier of the run to
    /// score is the job item itself (<see cref="JobItemRecord.Input"/>) — the
    /// same pattern as how the <see cref="Eval"/> job carries a case identifier.
    /// </remarks>
    OnlineEval = 6,

    /// <summary>
    /// A NEW run that resumes a queued run after an approval decision (Phase 55).
    /// </summary>
    /// <remarks>
    /// SEPARATE from <see cref="AgentRun"/>: the old run row closed with
    /// <c>AwaitingApproval</c> NEVER CHANGES AGAIN (K-014, the same principle
    /// as <see cref="RunStatus.AwaitingInput"/>); this job opens a NEW
    /// <c>runs</c> row with a NEW <c>RunId</c>. The payload carries the
    /// identifier of the <see cref="PendingApproval"/> record whose decision was made.
    /// </remarks>
    ApprovalResume = 7,
}
