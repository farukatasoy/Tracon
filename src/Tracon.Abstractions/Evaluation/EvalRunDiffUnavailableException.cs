namespace Tracon;

/// <summary>
/// Thrown by <see cref="IEvalStore.DiffRunsAsync"/> when two eval runs exist
/// but cannot be compared.
/// </summary>
/// <remarks>
/// <para>
/// This is thrown rather than answered with an empty
/// <see cref="EvalRunDiff"/> on purpose. An empty diff reads as "nothing
/// changed", which turns a CI gate green — the worst possible failure mode for
/// a regression check. A caller that cannot compare must be forced to say so.
/// </para>
/// <para>
/// The message is written for an operator and is not translated (server
/// responses carry one language).
/// </para>
/// </remarks>
public sealed class EvalRunDiffUnavailableException : TraconException
{
    /// <summary>
    /// The stable value written to <see cref="TraconException.ErrorType"/>.
    /// </summary>
    public const string EvalRunDiffUnavailableErrorType = "eval_run_diff_unavailable";

    /// <summary>Creates a new error.</summary>
    public EvalRunDiffUnavailableException()
    {
    }

    /// <summary>Creates a new error.</summary>
    /// <param name="message">The error message.</param>
    public EvalRunDiffUnavailableException(string message)
        : base(message)
    {
    }

    /// <summary>Creates a new error.</summary>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The underlying error.</param>
    public EvalRunDiffUnavailableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>Creates a new error that states why the comparison is impossible.</summary>
    /// <param name="reason">Why the two runs cannot be compared.</param>
    /// <param name="message">The error message.</param>
    public EvalRunDiffUnavailableException(EvalRunDiffUnavailableReason reason, string message)
        : base(message)
    {
        Reason = reason;
    }

    /// <summary>Why the two runs cannot be compared.</summary>
    public EvalRunDiffUnavailableReason Reason { get; }

    /// <inheritdoc />
    public override string ErrorType => EvalRunDiffUnavailableErrorType;
}

/// <summary>Why two eval runs cannot be compared.</summary>
public enum EvalRunDiffUnavailableReason
{
    /// <summary>
    /// No reason was stated. Treated as "cannot compare" by every caller; the
    /// value a custom store gets when it throws through one of the standard
    /// constructors.
    /// </summary>
    Unspecified = 0,

    /// <summary>The two runs measure different suites, so their cases do not align.</summary>
    DifferentSuites = 1,

    /// <summary>
    /// One of the runs has not completed. A half-finished run has cases that
    /// simply have not run yet, and comparing against it reports them as
    /// missing rather than as unmeasured.
    /// </summary>
    RunNotCompleted = 2,

    /// <summary>
    /// One of the runs kept its summary but its per-case results are gone,
    /// removed by the <c>eval_case_results</c> retention target.
    /// </summary>
    DetailsRemoved = 3,
}
