using Microsoft.Agents.AI.Workflows;

namespace Tracon;

/// <summary>The recipe for a single run.</summary>
internal sealed record WorkflowExecution
{
    public required string WorkflowName { get; init; }

    public required Guid RunId { get; init; }

    public required string SessionId { get; init; }

    public required string? Message { get; init; }

    /// <summary>The checkpoint to resume from. <see langword="null"/> for a new run.</summary>
    public required string? ResumeFrom { get; init; }

    /// <summary>
    /// The answers to give to pending requests. Only populated on the <c>/respond</c> path.
    /// </summary>
    public IReadOnlyList<WorkflowAnswer> Answers { get; init; } = [];

    /// <summary>The run and the request this execution answers; <see langword="null"/> for a run or a resume.</summary>
    public AnsweredRequest? AnsweredRequest { get; init; }

    /// <summary>
    /// The parameter values: what the caller supplied to a new run, or the
    /// source run's recorded values on a resume or a response.
    /// <see langword="null"/> means none.
    /// </summary>
    public IReadOnlyDictionary<string, string>? Parameters { get; init; }

    /// <summary>
    /// The stored definition version the run is compiled from. <see langword="null"/>
    /// runs the current definition (a new run, or a source that recorded no version).
    /// </summary>
    public int? DefinitionVersion { get; init; }

    /// <summary>
    /// The run this execution continues from its checkpoint: the one a resume or a response names.
    /// <see langword="null"/> for a new run.
    /// </summary>
    public Guid? SourceRunId { get; init; }

    /// <summary>
    /// Whether the row of this execution records <see cref="SourceRunId"/> as the run it continues
    /// (<see cref="RunRecord.ContinuedFromRunId"/>): a resume does, a response does not.
    /// </summary>
    public bool RecordsContinuation { get; init; }

    /// <summary>
    /// Whether the caller chose the run's id, so a row may already be open under it: a queued run's, or
    /// the continuation a recovery opened. That row may name the run it continues already.
    /// </summary>
    public bool RowMayBeOpen { get; init; }
}

/// <summary>
/// The run's deadline: the source the runner cancels at the run timeout, when it
/// was armed, and which setting set the value it carries.
/// </summary>
/// <param name="source">The source; owned by the caller.</param>
/// <param name="armedAt">The monotonic timestamp taken when the run started.</param>
/// <param name="installation">The installation's run timeout, the value the source was armed with.</param>
/// <param name="callerToken">The token the run was started with.</param>
internal sealed class RunDeadline(CancellationTokenSource source, long armedAt, TimeSpan installation, CancellationToken callerToken)
{
    private TimeSpan _limit = installation;
    private bool _setByDefinition;
    private bool _setByRegistration;

    /// <summary>Gets whether the deadline has passed.</summary>
    public bool Fired => source.IsCancellationRequested;

    /// <summary>Gets whether the run's caller cancelled the token the run was started with.</summary>
    public bool CallerStopped => callerToken.IsCancellationRequested;

    /// <summary>
    /// Moves the deadline to <paramref name="limit"/> after the start of the run; a
    /// limit that has already passed cancels at once.
    /// </summary>
    /// <param name="limit">The run timeout the workflow sets.</param>
    /// <param name="clock">The runner's time source.</param>
    /// <param name="byRegistration">Whether a registration in code, not a stored definition, sets it.</param>
    /// <returns>A task that completes when the source is re-armed.</returns>
    public async ValueTask RearmAsync(TimeSpan limit, TimeProvider clock, bool byRegistration)
    {
        _limit = limit;
        _setByDefinition = true;
        _setByRegistration = byRegistration;

        if (source.IsCancellationRequested)
        {
            return;
        }

        var remaining = limit - clock.GetElapsedTime(armedAt);

        // The source was created with the runner's TimeProvider, so CancelAfter
        // moves that provider's timer: a test clock still decides when it fires.
        if (remaining > TimeSpan.Zero)
        {
            source.CancelAfter(remaining);
        }
        else
        {
            await source.CancelAsync().ConfigureAwait(false);
        }
    }

    /// <summary>The reason a run that reached this deadline carries.</summary>
    /// <returns>The run error, naming the setting that set the value.</returns>
    public RunError ToRunError()
        => new()
        {
            Type = nameof(TimeoutException),
            Message = _setByRegistration
                ? $"Workflow timed out and was stopped after {_limit}, the run timeout its registration sets. " +
                  "Raise WorkflowRegistrationOptions.RunTimeout (up to 'Tracon:Workflows:MaxRunTimeout'), or shorten the graph."
                : _setByDefinition
                ? $"Workflow timed out and was stopped after {_limit}, the run timeout its definition sets. " +
                  "Raise the workflow's 'runTimeout' (up to 'Tracon:Workflows:MaxRunTimeout'), or shorten the graph."
                : "Workflow timed out and was stopped. " +
                  "Raise the 'Tracon:Workflows:RunTimeout' value, or " +
                  "shorten the graph.",
        };
}

/// <summary>The result of one attempt to send a response.</summary>
/// <param name="Delivered">Whether the response was sent to execution.</param>
/// <param name="Failure">The error, if the response could not be converted.</param>
internal readonly record struct ResponseDelivery(bool Delivered, RunError? Failure);

/// <summary>The request a run answers, and the run that waited for it.</summary>
/// <param name="SourceRunId">The run that waited.</param>
/// <param name="RequestId">The request.</param>
internal readonly record struct AnsweredRequest(Guid SourceRunId, string RequestId);

/// <summary>A single result coming out of the pump: an event, an error, a cancellation, or a wait.</summary>
internal readonly record struct PumpedEvent(RunEvent? Event, RunError? Failure, bool Canceled, bool Awaiting)
{
    public static PumpedEvent FromEvent(RunEvent runEvent) => new(runEvent, null, false, false);

    public static PumpedEvent FromFailure(RunError error) => new(null, error, false, false);

    /// <summary>Execution is waiting for a human answer.</summary>
    public static PumpedEvent FromAwaiting() => new(null, null, false, true);

    /// <summary>Execution stopped because a token was cancelled.</summary>
    /// <remarks>
    /// The reason is deliberately not built here. A deadline can end a
    /// run through three separate doors - startup, a super-step boundary,
    /// a cancelled MoveNextAsync - and MAF can also end its stream quietly,
    /// through no door at all. While each door built its own reason, the
    /// quiet path had none: the runner produced a RunError naming the
    /// setting and then dropped it, and the operator saw a bare
    /// cancellation at full cost. The reason is attached once now, where
    /// the run is closed, so no path can be added that forgets it.
    /// </remarks>
    public static PumpedEvent FromCancellation() => new(null, null, true, false);
}
