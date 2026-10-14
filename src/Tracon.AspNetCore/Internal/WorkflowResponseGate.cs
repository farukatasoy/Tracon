using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Tracon;

/// <summary>
/// Decides what an answer to a workflow's pending human request does: start a run, follow the
/// run an identical answer already started, or conflict with a different answer.
/// </summary>
/// <remarks>
/// <para>
/// Runs after every other check of <c>POST /api/workflows/runs/{runId}/respond</c> and before
/// the stream opens or the job is queued, so each outcome is a status code.
/// </para>
/// <para>
/// <strong>The audit entry comes first.</strong> Every answer that starts a run, in either mode,
/// is written as <c>workflow.respond</c> with <see cref="AuditRecorder.WriteOrThrowAsync"/>
/// BEFORE its record and its run: an entry that cannot be written refuses the answer. An answer
/// that starts nothing (it follows, or it conflicts) changes nothing and writes nothing - the
/// approval inbox does not write a repeated decision again either. The entry carries a hash of
/// the answer, never the answer: the audit trail is not protected at rest.
/// </para>
/// <para>
/// <strong>The record is the authority.</strong> In
/// <see cref="WorkflowResponseMode.SingleAnswer"/> the claim in
/// <see cref="IWorkflowResponseStore"/> decides which of two concurrent answers starts the run;
/// in <see cref="WorkflowResponseMode.Branch"/> the record is the link from the source run to
/// its answers. A record that cannot be written refuses the answer in both modes.
/// </para>
/// </remarks>
internal static class WorkflowResponseGate
{
    /// <summary>The audit action of an answer that starts a run.</summary>
    public const string AuditAction = "workflow.respond";

    /// <summary>
    /// How many times a claim that keeps changing hands under this answer is read again before
    /// the answer gives up. Each turn means another answer won a race; three in a row is not a
    /// state a request reaches in practice.
    /// </summary>
    private const int DecisionAttempts = 3;

    /// <summary>What an answer does.</summary>
    public enum Outcome
    {
        /// <summary>Start a run under <see cref="Decision.RunId"/>.</summary>
        Start,

        /// <summary>Follow the run an identical answer started.</summary>
        Follow,

        /// <summary>A different answer holds the request.</summary>
        Conflict,
    }

    /// <summary>Reads the mode the source run is answered under.</summary>
    /// <param name="source">The run that waits.</param>
    /// <param name="runner">The workflow engine, which knows a workflow defined in code.</param>
    /// <param name="definitions">The definition store.</param>
    /// <param name="tenantId">The caller's tenant.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The mode of the definition version the source recorded; otherwise the current one's.</returns>
    /// <remarks>
    /// The source run's recorded version decides, the same version the answer's run compiles by
    /// default: an edit made while the run waits changes neither the graph nor the rule. A run
    /// that recorded no version, a version no longer kept, and a workflow defined in code read
    /// the mode the catalog describes now.
    /// </remarks>
    public static async ValueTask<WorkflowResponseMode> ResolveModeAsync(
        RunRecord source,
        IWorkflowRunner runner,
        IWorkflowDefinitionStore definitions,
        string tenantId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(runner);
        ArgumentNullException.ThrowIfNull(definitions);

        var name = source.WorkflowName!;

        if (source.AgentVersion is { } recorded)
        {
            var current = await definitions.GetAsync(tenantId, name, cancellationToken).ConfigureAwait(false);

            if (current is not null && current.Version == recorded)
            {
                return current.ResponseMode;
            }

            if (current is not null &&
                await definitions.GetVersionAsync(tenantId, name, recorded, cancellationToken).ConfigureAwait(false) is { } version)
            {
                return version.ResponseMode;
            }
        }

        return (await runner.GetAsync(name, cancellationToken).ConfigureAwait(false))?.ResponseMode
            ?? WorkflowResponseMode.Branch;
    }

    /// <summary>Decides, records and audits one answer.</summary>
    /// <param name="answer">The answer and the run it answers.</param>
    /// <param name="services">The request's services.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>What the answer does.</returns>
    /// <exception cref="TraconException">The audit entry could not be written; nothing was recorded.</exception>
    public static async ValueTask<Decision> DecideAsync(
        Answer answer,
        IServiceProvider services,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(answer);
        ArgumentNullException.ThrowIfNull(services);

        var store = services.GetRequiredService<IWorkflowResponseStore>();
        var actor = services.GetRequiredService<IAuditActorResolver>().Resolve();

        if (answer.Mode == WorkflowResponseMode.Branch)
        {
            // K-130 kept: every answer opens its own run. The record claims nothing.
            await AuditAsync(answer, answer.NewRunId, redrive: false, actor, services, cancellationToken).ConfigureAwait(false);
            await store.AddAsync(Record(answer, answer.NewRunId, claimScope: null, actor), cancellationToken).ConfigureAwait(false);

            return Start(answer.NewRunId, redrive: false);
        }

        var scope = answer.Source.SessionId!;

        for (var attempt = 1; attempt <= DecisionAttempts; attempt++)
        {
            var existing = await store.FindClaimAsync(scope, answer.RequestId, cancellationToken).ConfigureAwait(false);

            if (existing is null)
            {
                await AuditAsync(answer, answer.NewRunId, redrive: false, actor, services, cancellationToken).ConfigureAwait(false);

                var claim = await store
                    .AddAsync(Record(answer, answer.NewRunId, scope, actor), cancellationToken)
                    .ConfigureAwait(false);

                if (claim.Claimed)
                {
                    return Start(answer.NewRunId, redrive: false);
                }

                // Lost the race. The entry written above stays as the answer that was tried,
                // the way a lost race of two approval decisions leaves its entry.
                existing = claim.Existing;

                if (existing is null)
                {
                    continue;
                }
            }

            if (!string.Equals(existing.AnswerHash, answer.AnswerHash, StringComparison.Ordinal))
            {
                return new Decision(Outcome.Conflict, Guid.Empty, Guid.Empty, Redrive: false);
            }

            var (stopped, inFlight) = await StoppedBeforeFinishingAsync(existing, answer.TenantId, services, cancellationToken)
                .ConfigureAwait(false);

            if (!stopped)
            {
                return new Decision(Outcome.Follow, existing.ResponseRunId, inFlight, Redrive: false);
            }

            // The run this answer started stopped before it finished with it: the same answer
            // starts the work again (the approval inbox's "the same decision re-drives the
            // hand-off"). The replacement is a compare-and-swap; of two re-drives one wins and
            // the other follows it on the next turn.
            var redriveRunId = TraconId.NewId();

            await AuditAsync(answer, redriveRunId, redrive: true, actor, services, cancellationToken).ConfigureAwait(false);

            if (await store
                    .ReplaceAsync(existing.ResponseRunId, Record(answer, redriveRunId, scope, actor), cancellationToken)
                    .ConfigureAwait(false))
            {
                return Start(redriveRunId, redrive: true);
            }
        }

        throw new TraconException(
            $"The answer to request '{answer.RequestId}' of run '{answer.Source.Id}' kept losing to other answers " +
            "given at the same time. Read the run's answers with GET /api/workflows/runs/{runId}/responses and try again.");
    }

    /// <summary>
    /// Whether the run an answer started stopped before it finished with the answer, so the same
    /// answer may start the work again.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The run's recovery chain (<see cref="WorkflowContinuationChain"/>) is read forward from the
    /// run the answer started; its LAST run decides, because a queued answer that was interrupted
    /// continues under a new run. Only a chain that ended
    /// <see cref="RunStatus.Failed"/> or <see cref="RunStatus.Canceled"/> stopped; a running,
    /// queued, waiting or completed one did not.
    /// </para>
    /// <para>
    /// A run with no row yet is in flight, not stopped. The row is opened right after the
    /// claim, by the same request; a second identical answer that reads the claim between the two
    /// writes must follow it. Treating "no row" as stopped started a second run for a double click.
    /// </para>
    /// <para>
    /// A record still <see cref="WorkflowResponseStatus.Accepted"/> was never handed to the
    /// workflow, so starting again is safe. A <see cref="WorkflowResponseStatus.Delivered"/> one
    /// starts again only when no run of its chain wrote a checkpoint: there is nothing to resume
    /// from, and leaving it would lock the request for good.
    /// </para>
    /// </remarks>
    /// <returns>
    /// Whether the work stopped, and the run a follower watches: the chain's last run, or the
    /// answer's own run while it has no row yet.
    /// </returns>
    private static async ValueTask<(bool Stopped, Guid InFlight)> StoppedBeforeFinishingAsync(
        WorkflowResponseRecord existing,
        string tenantId,
        IServiceProvider services,
        CancellationToken cancellationToken)
    {
        var runs = services.GetRequiredService<IRunStore>();
        var chain = await WorkflowContinuationChain
            .ReadAsync(runs, existing.ResponseRunId, tenantId, cancellationToken)
            .ConfigureAwait(false);

        if (chain.Count == 0)
        {
            return (false, existing.ResponseRunId);
        }

        var last = chain[^1];

        if (last.Status is not (RunStatus.Failed or RunStatus.Canceled))
        {
            return (false, last.Id);
        }

        if (existing.Status == WorkflowResponseStatus.Accepted)
        {
            return (true, last.Id);
        }

        var checkpoints = services.GetRequiredService<IWorkflowCheckpointStore>();

        foreach (var run in chain)
        {
            if ((await checkpoints.ListByRunAsync(tenantId, run.Id, cancellationToken).ConfigureAwait(false)).Count > 0)
            {
                return (false, last.Id);
            }
        }

        return (true, last.Id);
    }

    private static Decision Start(Guid runId, bool redrive) => new(Outcome.Start, runId, runId, redrive);

    private static WorkflowResponseRecord Record(Answer answer, Guid runId, string? claimScope, string? actor)
        => new()
        {
            ResponseRunId = runId,
            SourceRunId = answer.Source.Id,
            SessionId = answer.Source.SessionId!,
            RequestId = answer.RequestId,
            AnswerHash = answer.AnswerHash,
            ClaimScope = claimScope,
            Status = WorkflowResponseStatus.Accepted,
            RespondedBy = actor,
            RespondedAt = DateTimeOffset.UtcNow,
            TenantId = answer.TenantId,
        };

    private static ValueTask AuditAsync(
        Answer answer,
        Guid responseRunId,
        bool redrive,
        string? actor,
        IServiceProvider services,
        CancellationToken cancellationToken)
        => AuditRecorder.WriteOrThrowAsync(
            services.GetRequiredService<IAuditLog>(),
            actor,
            services.GetRequiredService<ILoggerFactory>().CreateLogger("Tracon.WorkflowEndpoints"),
            services.GetService<TraconMetrics>(),
            answer.TenantId,
            action: AuditAction,
            entity: $"workflow:{answer.Source.WorkflowName}",
            before: null,
            after: AuditPayload.Write(writer =>
            {
                writer.WriteString("sourceRunId", answer.Source.Id);
                writer.WriteString("requestId", answer.RequestId);
                writer.WriteString("responseRunId", responseRunId);
                writer.WriteString("answerHash", answer.AnswerHash);
                writer.WriteString("mode", answer.Mode.ToString());
                writer.WriteBoolean("redrive", redrive);
            }),
            refusal: $"The answer to request '{answer.RequestId}' of run '{answer.Source.Id}' was not applied",
            timeProvider: null,
            cancellationToken);

    /// <summary>One answer, as the gate needs it.</summary>
    /// <param name="Source">The run that waits; a workflow run with a session.</param>
    /// <param name="RequestId">The request answered.</param>
    /// <param name="AnswerHash">The answer's digest (<see cref="WorkflowAnswerDigest"/>).</param>
    /// <param name="Mode">The mode the source is answered under.</param>
    /// <param name="NewRunId">The run id a fresh answer starts.</param>
    /// <param name="TenantId">The caller's tenant.</param>
    public sealed record Answer(
        RunRecord Source,
        string RequestId,
        string AnswerHash,
        WorkflowResponseMode Mode,
        Guid NewRunId,
        string TenantId);

    /// <summary>What the gate decided.</summary>
    /// <param name="Outcome">What the answer does.</param>
    /// <param name="RunId">
    /// The run to start, or the run the identical answer started (what its <c>202</c> named);
    /// empty for a conflict.
    /// </param>
    /// <param name="FollowRunId">
    /// The run a streamed follower watches: the last run of <paramref name="RunId"/>'s recovery
    /// chain, which is the work in flight when a queued answer was interrupted and continued.
    /// </param>
    /// <param name="Redrive">Whether the run starts the work of a stopped answer again.</param>
    public readonly record struct Decision(Outcome Outcome, Guid RunId, Guid FollowRunId, bool Redrive);
}
