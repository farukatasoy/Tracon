using System.Globalization;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
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
/// criterion that throws. Measured against MAF 1.25.0 (2026-10-16, decompiled), MAF's own
/// aggregation short-circuits on the first evaluator that asks to continue and
/// skips the rest; this type reproduces that order exactly, so a costly
/// <c>aiJudge</c> placed after a cheap marker still costs nothing on the
/// iterations the marker already keeps going.
/// </para>
/// <para>
/// <strong>The event marks an EVALUATED iteration, not a model turn.</strong>
/// Measured against MAF 1.25.0: the loop does not consult its evaluator after
/// the turn that reaches <c>MaxIterations</c> — there is nothing left to
/// decide. A ceiling-bounded loop therefore produces one event fewer than it
/// produces model turns, and the last event carries
/// <c>ceilingReached</c> so the run record still answers "why did this stop?".
/// </para>
/// <para>
/// <strong>A criterion that throws stops the loop; by default it does not fail
/// the run.</strong> Measured: an evaluator exception propagates out of MAF's loop
/// and ends the whole run. An <c>aiJudge</c> criterion calls a model, so an
/// unreachable provider would throw away work the agent already finished. The
/// failure is not swallowed either — it is logged and named in the iteration
/// event's <c>failedCriterion</c> field, so a criterion that never runs cannot
/// look like a criterion that was satisfied. <see cref="LoopSettings.OnCriterionError"/>
/// set to <see cref="LoopOutcome.FailRun"/> rethrows the criterion's own exception
/// instead, so the run keeps its real error class.
/// </para>
/// <para>
/// <strong>Exhaustion can fail the run.</strong> With
/// <see cref="LoopSettings.OnExhausted"/> set to <see cref="LoopOutcome.FailRun"/>
/// the registry hands MAF one iteration more than the definition's ceiling, so the
/// turn at the ceiling IS evaluated here; if a criterion still asks for another
/// iteration, the event is written with <c>failsRun</c> and
/// <see cref="TraconLoopUnsatisfiedException"/> ends the run. MAF never runs the
/// extra turn: this evaluator either stops or throws at the ceiling.
/// </para>
/// <para>
/// <strong>A failed run keeps what the loop spent.</strong> A run that ends in an
/// exception has no response, so the framework's usage total over every iteration
/// is lost with it. The evaluator therefore sums each evaluated turn's usage itself
/// and, only when it fails the run, adds the sum to the run's side-channel usage.
/// On every other path the run's usage comes from its response or its stream, and
/// adding the sum there too would count it twice.
/// </para>
/// <para>
/// <strong>The caller's stream sees the event it was recorded as.</strong> When the
/// run's scope carries <see cref="TraconRunOptions.AfterLoopIterationRecorded"/>,
/// the recorded event goes to it, after the run's usage is settled and outside the
/// criteria's <c>try</c>/<c>catch</c>. The evaluator runs between two iterations, while
/// the caller waits for the next update, so the callback's write lands between the
/// last update of this iteration and the first of the next. A run with no event
/// writer has no event, so it calls nothing.
/// </para>
/// <para>
/// The evaluator holds no state between calls; every decision is made from the
/// <see cref="LoopContext"/> it is given, and the usage sum lives in that context's
/// <see cref="LoopContext.AdditionalProperties"/>, so concurrent runs of one
/// compiled agent cannot see each other's loop.
/// </para>
/// </remarks>
internal sealed class RecordingLoopEvaluator : LoopEvaluator
{
    /// <summary>The <see cref="LoopContext.AdditionalProperties"/> key the run's usage sum lives under.</summary>
    private const string UsageKey = "Tracon.RecordingLoopEvaluator.Usage";

    private readonly string _agentName;
    private readonly int _maxIterations;
    private readonly LoopOutcome _onExhausted;
    private readonly LoopOutcome _onCriterionError;
    private readonly ILogger _logger;

    /// <summary>Initializes the composite evaluator.</summary>
    /// <param name="agentName">The agent the loop belongs to, for the log message.</param>
    /// <param name="criteria">The criteria, in the order they are evaluated.</param>
    /// <param name="maxIterations">
    /// The definition's resolved iteration ceiling. In the default mode it is not
    /// enforced here — the framework owns that — but it is what lets the last
    /// event say that the ceiling, not a satisfied criterion, is about to end the
    /// loop. With <paramref name="onExhausted"/> set to
    /// <see cref="LoopOutcome.FailRun"/> the evaluator enforces it itself.
    /// </param>
    /// <param name="onExhausted">What happens when the ceiling is reached with a criterion still unsatisfied.</param>
    /// <param name="onCriterionError">What happens when a criterion throws.</param>
    /// <param name="logger">The logger a failing criterion is reported to.</param>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    public RecordingLoopEvaluator(
        string agentName,
        IReadOnlyList<LoopCriterionBinding> criteria,
        int maxIterations,
        LoopOutcome onExhausted,
        LoopOutcome onCriterionError,
        ILogger logger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentName);
        ArgumentNullException.ThrowIfNull(criteria);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxIterations);
        ArgumentNullException.ThrowIfNull(logger);

        _agentName = agentName;
        Criteria = criteria;
        _maxIterations = maxIterations;
        _onExhausted = onExhausted;
        _onCriterionError = onCriterionError;
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

        var usage = AccumulateUsage(context);

        LoopEvaluation? continuation = null;
        string? continuedBy = null;
        string? failedCriterion = null;
        ExceptionDispatchInfo? criterionError = null;

        foreach (var criterion in Criteria)
        {
            LoopEvaluation evaluation;

            try
            {
                evaluation = await criterion.Evaluator
                    .EvaluateAsync(context, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception ex) when (OperationCancellation.IsFailure(ex, cancellationToken))
            {
                // Our own cancellation passes the filter: the run itself is
                // ending, and that is not a criterion failure. A criterion's own
                // timeout (TaskCanceledException with our token intact) is one
                // (K-840).
                failedCriterion = criterion.Kind;

                if (_onCriterionError == LoopOutcome.FailRun)
                {
                    // Rethrown below, after the event: the run keeps the criterion's
                    // OWN error (an unreachable judge stays a provider error).
                    criterionError = ExceptionDispatchInfo.Capture(ex);

                    _logger.LogError(
                        ex,
                        "Tracon loop criterion '{Kind}' of agent '{Agent}' failed to evaluate on iteration " +
                        "{Iteration}. OnCriterionError is FailRun: the run fails with this error.",
                        criterion.Kind,
                        _agentName,
                        context.Iteration);
                }
                else
                {
                    _logger.LogError(
                        ex,
                        "Tracon loop criterion '{Kind}' of agent '{Agent}' failed to evaluate on iteration " +
                        "{Iteration}. The loop stops and the last response is returned.",
                        criterion.Kind,
                        _agentName,
                        context.Iteration);
                }

                break;
            }

            if (evaluation.ShouldReinvoke)
            {
                continuation = evaluation;
                continuedBy = criterion.Kind;
                break;
            }
        }

        // Default mode: the framework evaluates after every turn EXCEPT the one
        // that reaches the ceiling, so this is the last chance to record that the
        // ceiling — and not a satisfied criterion — is what ends the loop.
        // FailRun mode: the framework was given one iteration more, the turn at the
        // ceiling IS evaluated here, and an unsatisfied evaluation there ends the run.
        var failOnExhaustion = _onExhausted == LoopOutcome.FailRun;
        var ceilingReached = continuedBy is not null
            && context.Iteration >= (failOnExhaustion ? _maxIterations : _maxIterations - 1);
        var exhausted = failOnExhaustion && ceilingReached;
        var failsRun = exhausted || criterionError is not null;

        var recorded = await WriteIterationEventAsync(
                context, continuedBy, continuation, failedCriterion, ceilingReached, failsRun, cancellationToken)
            .ConfigureAwait(false);

        if (failsRun)
        {
            // The run ends in the exception below. Without this its usage, cost and
            // quota entry would say the loop spent nothing. 🚨 Correct only while the
            // recorder writes NO usage for an exception-ended run: if it starts
            // keeping the streamed usage there (F-317), this sum counts it twice.
            TraconRunContext.Current?.ExtraUsage?.Add(usage);
        }

        // After the usage above, so a failing callback cannot cost the run its sum, and
        // outside the criteria's try/catch: the callback writes to the caller's stream,
        // and its failure is no criterion failure (OnCriterionError would swallow it).
        if (recorded is not null
            && TraconRunContext.Current?.AfterLoopIterationRecorded is { } afterRecorded)
        {
            await afterRecorded(recorded, cancellationToken).ConfigureAwait(false);
        }

        criterionError?.Throw();

        if (exhausted)
        {
            // Only the agent, the count and the criterion KIND: the response and the
            // feedback are free-form text written for the model, and this message is
            // stored with the run and shown to clients.
            throw new TraconLoopUnsatisfiedException(string.Create(
                CultureInfo.InvariantCulture,
                $"The loop of agent '{_agentName}' reached its ceiling of {_maxIterations} iterations while " +
                $"criterion '{continuedBy}' still asked for another iteration, and OnExhausted is FailRun."))
            {
                AgentName = _agentName,
                Iterations = context.Iteration,
                UnsatisfiedCriterion = continuedBy,
            };
        }

        // The winning evaluation is returned UNCHANGED, never rebuilt from its
        // Feedback: an evaluator may have answered with ContinueWithMessages,
        // and rebuilding would drop those messages on the floor.
        return continuation ?? LoopEvaluation.Stop();
    }

    /// <summary>Adds the turn that was just evaluated to the run's usage sum.</summary>
    /// <param name="context">The loop run's context.</param>
    /// <returns>The sum so far, or <see langword="null"/> when no turn reported usage.</returns>
    private static UsageDetails? AccumulateUsage(LoopContext context)
    {
        var total = context.AdditionalProperties.TryGetValue(UsageKey, out var stored)
            ? stored as UsageDetails
            : null;

        if (context.LastResponse?.Usage is { } turn)
        {
            if (total is null)
            {
                total = new UsageDetails();
                context.AdditionalProperties[UsageKey] = total;
            }

            total.Add(turn);
        }

        return total;
    }

    /// <summary>Writes the iteration's event to the run's record.</summary>
    /// <returns>The recorded event, or <see langword="null"/> when the run has no event writer.</returns>
    private static async ValueTask<RunEvent?> WriteIterationEventAsync(
        LoopContext context,
        string? continuedBy,
        LoopEvaluation? continuation,
        string? failedCriterion,
        bool ceilingReached,
        bool failsRun,
        CancellationToken cancellationToken)
    {
        if (TraconRunContext.Current?.Writer is not { } writer)
        {
            return null;
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
                FailsRun = failsRun,
            },
            TraconCoreJsonContext.Default.LoopIterationCompletedEventPayload);

        var text = failedCriterion is not null
            ? failsRun
                ? string.Create(
                    CultureInfo.InvariantCulture,
                    $"iteration {context.Iteration} stopped: criterion '{failedCriterion}' failed and fails the run")
                : string.Create(
                    CultureInfo.InvariantCulture,
                    $"iteration {context.Iteration} stopped: criterion '{failedCriterion}' failed")
            : ceilingReached
                ? failsRun
                    ? string.Create(
                        CultureInfo.InvariantCulture,
                        $"iteration {context.Iteration} continued by '{continuedBy}'; the iteration ceiling ends the loop and fails the run")
                    : string.Create(
                        CultureInfo.InvariantCulture,
                        $"iteration {context.Iteration} continued by '{continuedBy}'; the iteration ceiling ends the loop")
                : continuedBy is not null
                    ? string.Create(
                        CultureInfo.InvariantCulture,
                        $"iteration {context.Iteration} continued by '{continuedBy}'")
                    : string.Create(CultureInfo.InvariantCulture, $"iteration {context.Iteration} stopped");

        return await writer.AppendAsync(
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
    /// is what ends the loop. In the default mode the turn that follows this
    /// event runs and then stops without being evaluated, so it produces no
    /// event of its own; when <see cref="FailsRun"/> is also set, no turn follows.
    /// </summary>
    public required bool CeilingReached { get; init; }

    /// <summary>
    /// Gets whether this evaluation fails the run: the ceiling was reached with
    /// <see cref="LoopSettings.OnExhausted"/> set to <see cref="LoopOutcome.FailRun"/>,
    /// or a criterion threw with <see cref="LoopSettings.OnCriterionError"/> set to it.
    /// </summary>
    public required bool FailsRun { get; init; }
}
