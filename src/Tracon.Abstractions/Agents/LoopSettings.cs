namespace Tracon;

/// <summary>
/// Turns the harness loop on: the agent is re-invoked until a stop criterion
/// says the work is finished.
/// </summary>
/// <remarks>
/// <para>
/// The loop is <strong>off</strong> while <c>HarnessSettings.Loop</c> is
/// <see langword="null"/>, and the harness behaves exactly as it did before this
/// setting existed. Nothing turns it on implicitly.
/// </para>
/// <para>
/// <strong>This is not <c>HarnessSettings.MaximumIterationsPerRequest</c>.</strong>
/// The two numbers count different things and are deliberately kept in separate
/// types so they cannot be read as one:
/// </para>
/// <list type="table">
///   <listheader>
///     <term>Setting</term>
///     <description>What it bounds</description>
///   </listheader>
///   <item>
///     <term><c>HarnessSettings.MaximumIterationsPerRequest</c></term>
///     <description>
///     The harness's <em>inner</em> tool-calling loop inside ONE agent
///     invocation. It is a runaway-safety ceiling, not a stop criterion.
///     </description>
///   </item>
///   <item>
///     <term><c>MaxIterations</c></term>
///     <description>
///     The <em>outer</em> loop that re-invokes the whole agent after a criterion
///     says the work is not finished. It bounds how many times that may happen.
///     </description>
///   </item>
/// </list>
/// <para>
/// Criteria are evaluated <strong>in order, and the first one that asks for
/// another iteration wins</strong> — the remaining criteria are not evaluated
/// that iteration. The loop therefore stops only when EVERY criterion is
/// satisfied. Put the cheapest criterion first: an <c>aiJudge</c> placed after a
/// <c>completionMarker</c> costs nothing on the iterations the marker already
/// keeps going.
/// </para>
/// <para>
/// Every completed iteration writes a
/// <c>RunEventType.LoopIterationCompleted</c> event to the run, so the
/// loop is visible in the run record instead of hidden inside a caller's own
/// code.
/// </para>
/// </remarks>
/// <example>
/// An agent that keeps working until it writes <c>ALL DONE</c>, for at most
/// five iterations:
/// <code language="csharp">
/// new AgentDefinition
/// {
///     Name = "researcher",
///     Model = new ModelBinding { Provider = "openai", Model = "gpt-5" },
///     Harness = new HarnessSettings
///     {
///         Loop = new LoopSettings
///         {
///             Criteria = [new LoopCriterion { Kind = "completionMarker", Marker = "ALL DONE" }],
///             MaxIterations = 5,
///         },
///     },
/// };
/// </code>
/// </example>
public sealed record LoopSettings
{
    /// <summary>
    /// The iteration ceiling applied when <c>MaxIterations</c> is
    /// <see langword="null"/>.
    /// </summary>
    /// <remarks>
    /// The ceiling is never left open. A criterion that can never be satisfied —
    /// a marker the model does not write, a judge that is never convinced — turns
    /// an open loop into a bill the consumer only learns about from an invoice,
    /// so Tracon applies its own ceiling instead of leaving the number to the
    /// framework's default.
    /// </remarks>
    public const int DefaultMaxIterations = 10;

    /// <summary>
    /// Gets the stop criteria, evaluated in order. At least one is required.
    /// </summary>
    public required IReadOnlyList<LoopCriterion> Criteria { get; init; }

    /// <summary>
    /// Gets the upper number of loop iterations. <see langword="null"/> takes
    /// <c>DefaultMaxIterations</c>. Must be greater than zero.
    /// </summary>
    public int? MaxIterations { get; init; }

    /// <summary>
    /// Gets a value that starts every iteration from a fresh context instead of
    /// carrying the previous iterations' messages forward.
    /// </summary>
    /// <remarks>
    /// Off by default. Turning it on changes what the agent remembers between
    /// iterations: the agent no longer sees its own earlier answers, only the
    /// original request and the criterion's feedback. Use it when repeated
    /// context is what makes the agent repeat itself; leave it off when the work
    /// builds on the previous iteration.
    /// </remarks>
    public bool FreshContextPerIteration { get; init; }
}
