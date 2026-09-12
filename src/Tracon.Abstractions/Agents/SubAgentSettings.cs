namespace Tracon;

/// <summary>
/// Determines how long an agent waits for the agents it calls.
/// </summary>
/// <remarks>
/// <para>
/// The limit is enforced in two independent layers, the same pattern
/// <c>OnlineEvaluationOptions.JudgeTimeout</c> uses for a judge call:
/// <see cref="ChildDeadline"/> is a <strong>cooperative</strong> cutoff — the
/// sub-agent's own cancellation token is canceled, and a sub-agent that reads
/// it genuinely stops and releases its resources. <see cref="WaitTimeout"/> is
/// a <strong>hard</strong> cutoff for the one case the cooperative layer
/// cannot catch: a sub-agent that ignores cancellation. Past
/// <see cref="WaitTimeout"/> the caller stops waiting; the sub-agent keeps
/// running in the background and its eventual result — success or failure —
/// is discarded without writing a further event, a metric, or producing an
/// unobserved exception.
/// </para>
/// <para>
/// When left <see langword="null"/>, both fields fall back to
/// <c>TraconAgentGraphOptions.ChildDeadline</c> and
/// <c>TraconAgentGraphOptions.WaitTimeout</c>. An invalid combination
/// (zero, negative, or <see cref="WaitTimeout"/> not greater than
/// <see cref="ChildDeadline"/>) is rejected while the agent is built, with
/// <see cref="TraconCompilationException"/>; it is not ignored silently.
/// </para>
/// </remarks>
/// <example>
/// A router that hands work to a sub-agent that must answer within 10 seconds,
/// with a 20-second hard cutoff if it hangs:
/// <code language="csharp">
/// new AgentDefinition
/// {
///     Name = "router",
///     Model = new ModelBinding { Provider = "openai", Model = "gpt-5" },
///     CallableAgentNames = ["researcher"],
///     SubAgents = new SubAgentSettings
///     {
///         ChildDeadline = TimeSpan.FromSeconds(10),
///         WaitTimeout = TimeSpan.FromSeconds(20),
///     },
/// };
/// </code>
/// </example>
public sealed record SubAgentSettings
{
    /// <summary>
    /// Gets the deadline applied to a single sub-agent run (the cooperative layer).
    /// </summary>
    public TimeSpan? ChildDeadline { get; init; }

    /// <summary>
    /// Gets the hard wait cutoff handed to the framework (the hard-cutoff layer).
    /// Must be greater than <c>ChildDeadline</c>.
    /// </summary>
    public TimeSpan? WaitTimeout { get; init; }
}
