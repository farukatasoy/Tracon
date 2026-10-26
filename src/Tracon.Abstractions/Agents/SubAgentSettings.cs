namespace Tracon;

/// <summary>
/// Determines how an agent calls the agents it may call, and how long it waits for them.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="CallMode"/> picks the surface the model sees: the Microsoft Agent Framework
/// background task tools (<see cref="SubAgentCallMode.Background"/>, the default), or one tool
/// per callable agent (<see cref="SubAgentCallMode.Tool"/>). The wait limits below, the depth
/// limit, the tree budget and the refusal of a sub-agent that needs tool approval apply in
/// both modes. In <see cref="SubAgentCallMode.Tool"/> mode the agent tool does not go through
/// the tool registry: the tool authorization, approval rules and per-tool timeout of the
/// registry do not apply to the call itself, only to the tools the sub-agent runs.
/// </para>
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
///     CallableAgents = ["researcher"],
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
    /// Gets how the agent calls every agent in <c>CallableAgents</c>.
    /// Defaults to <c>Background</c>.
    /// </summary>
    /// <remarks>
    /// With <see cref="SubAgentCallMode.Tool"/>, the tool of a callable agent is named after the
    /// agent, with each run of characters other than letters and digits replaced by one underscore
    /// (<c>support-agent</c> becomes <c>support_agent</c>). A name that equals another tool of
    /// the agent, or that another callable agent also gets, fails validation and compilation.
    /// </remarks>
    public SubAgentCallMode CallMode { get; init; }

    /// <summary>
    /// Gets the deadline applied to a single sub-agent run (the cooperative layer).
    /// </summary>
    /// <remarks>Must be from one millisecond to about 49.7 days; any other value fails to compile.</remarks>
    public TimeSpan? ChildDeadline { get; init; }

    /// <summary>
    /// Gets the hard wait cutoff handed to the framework (the hard-cutoff layer).
    /// Must be greater than <c>ChildDeadline</c>.
    /// </summary>
    /// <remarks>Must be from one millisecond to about 49.7 days; any other value fails to compile.</remarks>
    public TimeSpan? WaitTimeout { get; init; }
}
