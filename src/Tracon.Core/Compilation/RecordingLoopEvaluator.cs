using System.Globalization;
using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Extensions.Logging;

namespace Tracon;

// MAAI001: Microsoft.Agents.AI's loop API is marked "evaluation purposes only".
// The suppression covers the whole file because the file exists to implement
// that API. If MAF changes it, only this file and its two neighbours
// (LoopEvaluatorRegistry, AgentDefinitionCompiler.Agents) need updating.
// Rationale: docs/KARARLAR.md, decision K-020.
#pragma warning disable MAAI001

/// <summary>
/// One declarative criterion, paired with the <see cref="LoopCriterion.Kind"/>
/// name it was built from so the run record can name it.
/// </summary>
/// <param name="Kind">The criterion kind.</param>
/// <param name="Evaluator">The criterion built from it.</param>
internal sealed record LoopCriterionBinding(string Kind, LoopEvaluator Evaluator);

/// <summary>
/// The single <see cref="LoopEvaluator"/> Tracon hands the harness. It runs
/// a definition's criteria in order and writes one
/// <see cref="RunEventType.LoopIterationCompleted"/> event per iteration.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Why one composite instead of handing MAF the list.</strong> MAF
/// accepts many evaluators and aggregates them itself, but Tracon needs
/// three things that aggregation does not give: exactly one run event per
/// iteration (a wrapper per criterion would write one event each), the NAME of
/// the criterion that asked for another iteration, and containment of a
/// criterion that throws. Measured against MAF 1.20.0 (2026-09-07), MAF's own
/// aggregation short-circuits on the first evaluator that asks to continue and
/// skips the rest; this type reproduces that order exactly, so a costly
/// <c>aiJudge</c> placed after a cheap marker still costs nothing on the
/// iterations the marker already keeps going.
/// </para>
/// <para>
/// <strong>The event marks an EVALUATED iteration, not a model turn.</strong>
/// Measured against MAF 1.20.0: the loop does not consult its evaluator after
/// the turn that reaches <c>MaxIterations</c> — there is nothing left to
/// decide. A ceiling-bounded loop therefore produces one event fewer than it
/// produces model turns, and the last event carries
/// <c>ceilingReached</c> so the run record still answers "why did this stop?".
/// </para>
/// <para>
/// <strong>A criterion that throws stops the loop; it does not fail the run.</strong>
/// Measured: an evaluator exception propagates out of MAF's loop and ends the
/// whole run. An <c>aiJudge</c> criterion calls a model, so an unreachable
/// provider would throw away work the agent already finished. The failure is
/// not swallowed either — it is logged and named in the iteration event's
/// <c>failedCriterion</c> field, so a criterion that never runs cannot look
/// like a criterion that was satisfied.
/// </para>
/// </remarks>
internal sealed class RecordingLoopEvaluator : LoopEvaluator
{
    private readonly string _agentName;
    private readonly int _maxIterations;
    private readonly ILogger _logger;

    /// <summary>Initializes the composite evaluator.</summary>
    /// <param name="agentName">The agent the loop belongs to, for the log message.</param>
    /// <param name="criteria">The criteria, in the order they are evaluated.</param>
    /// <param name="maxIterations">
    /// The resolved iteration ceiling. It is not enforced here — the framework
    /// owns that — but it is what lets the last event say that the ceiling, not
    /// a satisfied criterion, is about to end the loop.
    /// </param>
    /// <param name="logger">The logger a failing criterion is reported to.</param>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    public RecordingLoopEvaluator(
        string agentName,
        IReadOnlyList<LoopCriterionBinding> criteria,
        int maxIterations,
        ILogger logger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentName);
        ArgumentNullException.ThrowIfNull(criteria);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxIterations);
        ArgumentNullException.ThrowIfNull(logger);

        _agentName = agentName;
        Criteria = criteria;
        _maxIterations = maxIterations;
        _logger = logger;
    }

    /// <summary>Gets the criteria, in evaluation order. Tests read it for structural verification.</summary>
    internal IReadOnlyList<LoopCriterionBinding> Criteria { get; }

    /// <inheritdoc />
    public override async ValueTask<LoopEvaluation> EvaluateAsync(
        LoopContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        LoopEvaluation? continuation = null;
        string? continuedBy = null;
        string? failedCriterion = null;

        foreach (var criterion in Criteria)
        {
            LoopEvaluation evaluation;

            try
            {
                evaluation = await criterion.Evaluator
                    .EvaluateAsync(context, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // The run itself is ending. Cancellation is not a criterion
                // failure and must not be reported as one.
                throw;
            }
            catch (Exception ex)
            {
                failedCriterion = criterion.Kind;

                _logger.LogError(
                    ex,
                    "Tracon loop criterion '{Kind}' of agent '{Agent}' failed to evaluate on iteration " +
                    "{Iteration}. The loop stops and the last response is returned.",
                    criterion.Kind,
                    _agentName,
                    context.Iteration);

                break;
            }

            if (evaluation.ShouldReinvoke)
            {
                continuation = evaluation;
                continuedBy = criterion.Kind;
                break;
            }
        }

        // The framework evaluates after every turn EXCEPT the one that reaches
        // the ceiling, so this is the last chance to record that the ceiling —
        // and not a satisfied criterion — is what ends the loop.
        var ceilingReached = continuedBy is not null && context.Iteration >= _maxIterations - 1;

        await WriteIterationEventAsync(
                context, continuedBy, continuation, failedCriterion, ceilingReached, cancellationToken)
            .ConfigureAwait(false);

        // The winning evaluation is returned UNCHANGED, never rebuilt from its
        // Feedback: an evaluator may have answered with ContinueWithMessages,
        // and rebuilding would drop those messages on the floor.
        return continuation ?? LoopEvaluation.Stop();
    }

    private static async ValueTask WriteIterationEventAsync(
        LoopContext context,
        string? continuedBy,
        LoopEvaluation? continuation,
        string? failedCriterion,
        bool ceilingReached,
        CancellationToken cancellationToken)
    {
        if (TraconRunContext.Current?.Writer is not { } writer)
        {
            return;
        }

        var hasFeedback = !string.IsNullOrEmpty(continuation?.Feedback);

        var payload = JsonSerializer.Serialize(
            new LoopIterationCompletedEventPayload
            {
                Iteration = context.Iteration,
                Continued = continuedBy is not null,
                ContinuedBy = continuedBy,
                HasFeedback = hasFeedback,
                FailedCriterion = failedCriterion,
                CeilingReached = ceilingReached,
            },
            TraconCoreJsonContext.Default.LoopIterationCompletedEventPayload);

        var text = failedCriterion is not null
            ? string.Create(
                CultureInfo.InvariantCulture,
                $"iteration {context.Iteration} stopped: criterion '{failedCriterion}' failed")
            : ceilingReached
                ? string.Create(
                    CultureInfo.InvariantCulture,
                    $"iteration {context.Iteration} continued by '{continuedBy}'; the iteration ceiling ends the loop")
                : continuedBy is not null
                    ? string.Create(
                        CultureInfo.InvariantCulture,
                        $"iteration {context.Iteration} continued by '{continuedBy}'")
                    : string.Create(CultureInfo.InvariantCulture, $"iteration {context.Iteration} stopped");

        await writer.AppendAsync(
            new RunEventDraft(RunEventType.LoopIterationCompleted)
            {
                Text = text,
                Payload = payload,
            },
            cancellationToken).ConfigureAwait(false);
    }
}
#pragma warning restore MAAI001

/// <summary>The <see cref="RunEventType.LoopIterationCompleted"/> event payload.</summary>
internal sealed record LoopIterationCompletedEventPayload
{
    /// <summary>Gets the 1-based iteration number that just finished.</summary>
    public required int Iteration { get; init; }

    /// <summary>Gets whether a criterion asked for another iteration.</summary>
    public required bool Continued { get; init; }

    /// <summary>
    /// Gets the kind of the criterion that asked for another iteration, or
    /// <see langword="null"/> when the loop stopped.
    /// </summary>
    public required string? ContinuedBy { get; init; }

    /// <summary>
    /// Gets whether that criterion handed the agent feedback. The feedback TEXT
    /// is deliberately not carried; it is free-form text written for the model.
    /// </summary>
    public required bool HasFeedback { get; init; }

    /// <summary>
    /// Gets the kind of the criterion that failed to evaluate, or
    /// <see langword="null"/> when every criterion answered.
    /// </summary>
    public required string? FailedCriterion { get; init; }

    /// <summary>
    /// Gets whether the iteration ceiling, rather than a satisfied criterion,
    /// is what ends the loop. The turn that follows this event runs and then
    /// stops without being evaluated, so it produces no event of its own.
    /// </summary>
    public required bool CeilingReached { get; init; }
}
