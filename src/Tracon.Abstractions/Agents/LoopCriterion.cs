namespace Tracon;

/// <summary>
/// One declarative stop criterion of the harness loop (see <c>LoopSettings</c>).
/// </summary>
/// <remarks>
/// <para>
/// A criterion carries <strong>data only</strong>, never code. That is the same
/// boundary tools and eval checks live behind: a definition can arrive from the
/// management API, and an interface-authored definition must never be able to
/// define behavior. A criterion whose logic is written in code is registered
/// with <c>ITraconBuilder.AddLoopEvaluator(kind, evaluator)</c> and is
/// referenced here by its registered <c>Kind</c> name alone.
/// </para>
/// <para>
/// Four kinds are built in. Each reads only the fields listed for it, and
/// ignores the rest:
/// </para>
/// <list type="table">
///   <listheader><term>Kind</term><description>Fields it reads</description></listheader>
///   <item>
///     <term><c>completionMarker</c></term>
///     <description>
///     <c>Marker</c> (required). The loop stops once the agent's answer
///     contains that text.
///     </description>
///   </item>
///   <item>
///     <term><c>todoCompletion</c></term>
///     <description>
///     <c>Modes</c> (optional). The loop stops once the agent's todo list
///     has no open item left.
///     </description>
///   </item>
///   <item>
///     <term><c>aiJudge</c></term>
///     <description>
///     <c>JudgeCriteria</c> (required) and <c>JudgeInstructions</c>
///     (optional). A judge model decides whether the work is finished.
///     </description>
///   </item>
///   <item>
///     <term><c>backgroundTaskCompletion</c></term>
///     <description>
///     None. The loop stops once no background task is still running.
///     </description>
///   </item>
/// </list>
/// <para>
/// An unknown <c>Kind</c> is <strong>rejected</strong> when the agent is
/// built, with <c>TraconCompilationException</c>; it is never ignored.
/// A silently ignored stop criterion is a loop with no stop criterion.
/// </para>
/// </remarks>
public sealed record LoopCriterion
{
    /// <summary>
    /// Gets the criterion kind: <c>completionMarker</c>, <c>todoCompletion</c>,
    /// <c>aiJudge</c>, <c>backgroundTaskCompletion</c>, or a kind registered in
    /// code with <c>AddLoopEvaluator</c>.
    /// </summary>
    public required string Kind { get; init; }

    /// <summary>
    /// Gets the completion marker text. Read by <c>completionMarker</c> only,
    /// where it is required.
    /// </summary>
    public string? Marker { get; init; }

    /// <summary>
    /// Gets the criteria the judge model scores the answer against. Read by
    /// <c>aiJudge</c> only, where at least one entry is required.
    /// </summary>
    public IReadOnlyList<string> JudgeCriteria { get; init; } = [];

    /// <summary>
    /// Gets the extra instructions handed to the judge model. Read by
    /// <c>aiJudge</c> only.
    /// </summary>
    public string? JudgeInstructions { get; init; }

    /// <summary>
    /// Gets the agent modes the criterion applies to. Read by
    /// <c>todoCompletion</c> only.
    /// </summary>
    /// <remarks>
    /// Tracon does not configure the harness mode provider today, so a
    /// definition has no mode names of its own to match. Leave this empty
    /// unless the agent's own instructions establish the modes.
    /// </remarks>
    public IReadOnlyList<string> Modes { get; init; } = [];
}
