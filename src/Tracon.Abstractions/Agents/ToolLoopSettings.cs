namespace Tracon;

/// <summary>Tool-call loop settings for a <see cref="ModelBinding"/>.</summary>
/// <remarks>
/// <para>
/// The loop is the one the model pipeline builds around the provider client; it
/// runs the tools the model asks for and sends their results back. Every member
/// keeps the loop's own default when it is left unset, so a binding without
/// <see cref="ModelBinding.ToolLoop"/> behaves exactly as it did before the
/// settings existed.
/// </para>
/// <para>
/// A fallback link runs the same agent's tools, so it takes the primary binding's
/// loop settings as one object. The tools' thread-safety and the limits of the
/// agent do not change with the model.
/// </para>
/// </remarks>
public sealed record ToolLoopSettings
{
    /// <summary>
    /// Gets whether independent tool calls within one turn may run at the
    /// same time.
    /// </summary>
    /// <remarks>
    /// <see langword="false"/> by default: with no change, tool calls within a
    /// turn run one after another exactly as they do today. A tool body that
    /// is not written to be thread-safe (a non-thread-safe field it shares
    /// across calls, for example) is only safe to run concurrently with
    /// itself once this is turned on for its agent. It is not the same as
    /// <see cref="ModelBinding.AllowMultipleToolCalls"/>: that one lets the model
    /// ask for several calls in one turn, this one decides whether Tracon runs
    /// them at the same time.
    /// </remarks>
    public bool AllowConcurrentToolCalls { get; init; }

    /// <summary>
    /// Gets how many tool-call iterations in a row may fail before the loop
    /// rethrows the error; the loop's default (3) is used when it is
    /// <see langword="null"/>.
    /// </summary>
    /// <remarks>
    /// <c>0</c> rethrows the first failure. A negative value is rejected while the
    /// agent is built, with <c>TraconCompilationException</c>.
    /// </remarks>
    public int? MaximumConsecutiveErrors { get; init; }

    /// <summary>
    /// Gets whether the loop ends when the model asks for a tool the agent does
    /// not have; the loop's default (<see langword="false"/>) is used when it is
    /// <see langword="null"/>.
    /// </summary>
    /// <remarks>
    /// With the default, the model gets an error result for the unknown call and
    /// the loop goes on. Set it to <see langword="true"/> to hand the unknown call
    /// back to the caller instead.
    /// </remarks>
    public bool? TerminateOnUnknownCalls { get; init; }

    /// <summary>
    /// Gets the iteration limit of the tool-call loop the model pipeline builds
    /// for this binding; <see langword="null"/> keeps the loop's own default.
    /// </summary>
    /// <remarks>
    /// Not part of the stored or wire shape. The compiler sets it on a copy of
    /// a harness agent's binding: the harness hands its model client to its
    /// own tool-call loop, but the loop that runs the tools is the one the
    /// model pipeline builds under it, so the harness's
    /// <c>MaximumIterationsPerRequest</c> has to reach that loop — and every
    /// fallback link's loop — through the binding.
    /// </remarks>
    internal int? IterationLimit { get; init; }
}
