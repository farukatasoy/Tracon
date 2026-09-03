using System.Text.RegularExpressions;

namespace AgentPrism;

/// <summary>
/// The built-in job handler keys, plus validation for the keys a consumer
/// registers.
/// </summary>
/// <remarks>
/// <para>
/// A handler key is a job's identity: it is both how the job is classified
/// (<see cref="JobRecord.HandlerKey"/>, the <c>handlerKey</c> query filter,
/// the <c>agentprism.job.handler_key</c> metric tag) and how the background
/// worker picks the <see cref="IJobHandler"/> that executes it. There is no
/// second field; a key registered with
/// <c>AddJobHandler&lt;T&gt;(key)</c> is dispatched by exact, ordinal match,
/// so registration order never decides the winner.
/// </para>
/// <para>
/// Grouping is done with the namespace prefix: everything AgentPrism ships
/// starts with <c>agentprism.</c>, and a consumer picks its own prefix
/// (<c>contoso.</c>). <see cref="JobRecord.Lane"/> (capacity) and
/// <see cref="JobRecord.TargetName"/> (the handler's input) stay separate
/// from the key.
/// </para>
/// </remarks>
public static partial class JobHandlerKeys
{
    /// <summary>The prefix reserved for the handlers AgentPrism itself ships.</summary>
    /// <remarks>
    /// Registering a consumer handler under this prefix throws at startup, so
    /// a built-in key can never be shadowed by accident.
    /// </remarks>
    public const string ReservedPrefix = "agentprism.";

    /// <summary>Runs an agent as a batch over a set of inputs.</summary>
    public const string AgentBatch = "agentprism.agent-batch";

    /// <summary>Runs a workflow, or responds to a pending workflow.</summary>
    public const string Workflow = "agentprism.workflow";

    /// <summary>An evaluation (eval) run over a suite's cases.</summary>
    public const string Eval = "agentprism.eval";

    /// <summary>
    /// A single webhook delivery attempt. The payload carries the delivery
    /// record's identifier; the body is read from the
    /// <c>webhook_deliveries</c> table.
    /// </summary>
    public const string WebhookDelivery = "agentprism.webhook-delivery";

    /// <summary>
    /// A retention sweep. <see cref="JobRecord.TargetName"/> is either a
    /// specific <see cref="RetentionTargets"/> value or <c>"*"</c>, which
    /// processes all enabled policies.
    /// </summary>
    public const string Retention = "agentprism.retention";

    /// <summary>A single queued (durable) agent run.</summary>
    /// <remarks>
    /// Runs started with <c>Prefer: respond-async</c> run under this key.
    /// Unlike <see cref="AgentBatch"/>, it does NOT require an item set, and
    /// the run identifier is generated IN ADVANCE by the caller (the HTTP
    /// layer) — <see cref="JobRecord.Id"/> carries the same value as the run
    /// identifier.
    /// </remarks>
    public const string AgentRun = "agentprism.agent-run";

    /// <summary>Scores a sampled production run.</summary>
    /// <remarks>
    /// The payload is empty or for diagnostics; the identifier of the run to
    /// score is the job item itself (<see cref="JobItemRecord.Input"/>) — the
    /// same pattern as how the <see cref="Eval"/> job carries a case identifier.
    /// </remarks>
    public const string OnlineEval = "agentprism.online-eval";

    /// <summary>A NEW run that resumes a queued run after an approval decision.</summary>
    /// <remarks>
    /// SEPARATE from <see cref="AgentRun"/>: the old run row closed with
    /// <see cref="RunStatus.AwaitingApproval"/> NEVER CHANGES AGAIN (the same
    /// principle as <see cref="RunStatus.AwaitingInput"/>); this job opens a
    /// NEW <c>runs</c> row with a NEW run identifier. The payload carries the
    /// identifier of the <see cref="PendingApproval"/> record whose decision
    /// was made.
    /// </remarks>
    public const string ApprovalResume = "agentprism.approval-resume";

    /// <summary>A NEW run, in the SAME session, that continues an interrupted run.</summary>
    /// <remarks>
    /// SEPARATE from <see cref="AgentRun"/> and <see cref="ApprovalResume"/>:
    /// triggered by orphaned-run reconciliation, not by a client request. The
    /// old run row stays <see cref="RunStatus.Failed"/> and never changes
    /// again; this job opens a new <c>runs</c> row whose
    /// <c>ContinuedFromRunId</c> points at it.
    /// </remarks>
    public const string RunContinuation = "agentprism.run-continuation";

    /// <summary>Every key AgentPrism itself ships, in no particular order.</summary>
    public static IReadOnlyList<string> BuiltIn { get; } =
    [
        AgentBatch,
        Workflow,
        Eval,
        WebhookDelivery,
        Retention,
        AgentRun,
        OnlineEval,
        ApprovalResume,
        RunContinuation,
    ];

    /// <summary>
    /// Checks whether <paramref name="key"/> is a valid handler key: 1-128
    /// characters, lowercase ASCII letters, digits, <c>.</c>, <c>_</c>, or
    /// <c>-</c>, starting with a letter or digit.
    /// </summary>
    /// <param name="key">The candidate key.</param>
    /// <returns><see langword="true"/> if the key is valid.</returns>
    /// <remarks>
    /// Uppercase letters are rejected, not normalized — the same reasoning as
    /// <see cref="JobLanes.IsValidName"/>. Keys are compared ordinally
    /// everywhere (dispatch, queries, metric tags), so allowing case variants
    /// would let <c>"Contoso.Report"</c> and <c>"contoso.report"</c> silently
    /// become two different handlers, and a job queued under one of them would
    /// never find its handler.
    /// </remarks>
    public static bool IsValidKey(string? key) => key is not null && KeyPattern().IsMatch(key);

    /// <summary>
    /// Checks whether <paramref name="key"/> falls inside the
    /// <see cref="ReservedPrefix"/> namespace.
    /// </summary>
    /// <param name="key">The candidate key.</param>
    /// <returns><see langword="true"/> if the key is reserved for AgentPrism.</returns>
    public static bool IsReserved(string? key)
        => key is not null && key.StartsWith(ReservedPrefix, StringComparison.Ordinal);

    [GeneratedRegex("^[a-z0-9][a-z0-9._-]{0,127}$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex KeyPattern();
}
