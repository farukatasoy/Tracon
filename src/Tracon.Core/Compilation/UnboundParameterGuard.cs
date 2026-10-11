using System.Runtime.CompilerServices;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace Tracon;

/// <summary>
/// Marks an agent compiled without parameter values, and fails its run when a
/// required parameter has no default.
/// </summary>
/// <remarks>
/// <para>
/// Only <c>POST /api/agents/{name}/run</c> and eval used to bind parameter
/// values. Every other path — a queued run, a batch or schedule, replay, the
/// OpenAI-compatible endpoints, MCP, A2A, a workflow step, an agent called as
/// a tool, voice — compiled the definition as it was, and the model received
/// the literal <c>{{name}}</c> placeholders. The documented rule is that such
/// a run does not start.
/// </para>
/// <para>
/// The compiler puts this guard on every agent it compiles from a definition
/// that declares parameters without values for them
/// (<c>AgentDefinitionCompiler.BuildAgent</c>), so every path, including one
/// added later, is covered. When the defaults cover every required parameter
/// the guard only marks the agent; otherwise it throws before any provider
/// call, and the decorators outside it record the run as failed. A path that
/// can answer before starting a run looks for the guard with
/// <see cref="AIAgent.GetService{TService}(object?)"/> and refuses with
/// <c>400</c> when <see cref="RequiresValues"/> is set. Replay refuses any
/// marked agent: its recorded input has no parameter values, so even the
/// defaults would change the source run's instructions.
/// </para>
/// <para>
/// It never throws at compile time: saving, validating and session reads
/// compile agents too, and none of them starts a run.
/// </para>
/// </remarks>
internal sealed class UnboundParameterGuard : DelegatingAIAgent
{
    private readonly string _agentName;

    /// <summary>Creates the guard.</summary>
    /// <param name="innerAgent">The compiled agent.</param>
    /// <param name="agentName">The agent's name.</param>
    /// <param name="missing">The outcome that names the required parameters with no value.</param>
    public UnboundParameterGuard(AIAgent innerAgent, string agentName, AgentParameterValidationResult missing)
        : base(innerAgent)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentName);
        ArgumentNullException.ThrowIfNull(missing);

        _agentName = agentName;
        Missing = missing;
    }

    /// <summary>Gets the outcome that names the required parameters with no value.</summary>
    public AgentParameterValidationResult Missing { get; }

    /// <summary>
    /// Gets whether a required parameter has no default, so a run without values
    /// cannot start.
    /// </summary>
    public bool RequiresValues => !Missing.IsValid;

    /// <summary>The error a run that reaches this guard fails with.</summary>
    /// <returns>The error.</returns>
    public TraconException Failure()
    {
        var names = string.Join(", ", Missing.Errors.Select(static error => $"'{error.ParameterName}'"));

        return new TraconException(
            $"Agent '{_agentName}' requires parameter(s) {names}, which have no default value, and this run was " +
            "started without parameter values. Start it with 'POST /api/agents/{name}/run' and a 'parameters' " +
            "object, or give the parameters a default value.");
    }

    /// <inheritdoc />
    protected override Task<AgentResponse> RunCoreAsync(
        IEnumerable<ChatMessage> messages,
        AgentSession? session = null,
        AgentRunOptions? options = null,
        CancellationToken cancellationToken = default)
        => RequiresValues
            ? throw Failure()
            : base.RunCoreAsync(messages, session, options, cancellationToken);

    /// <inheritdoc />
    protected override async IAsyncEnumerable<AgentResponseUpdate> RunCoreStreamingAsync(
        IEnumerable<ChatMessage> messages,
        AgentSession? session = null,
        AgentRunOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (RequiresValues)
        {
            throw Failure();
        }

        await foreach (var update in base
            .RunCoreStreamingAsync(messages, session, options, cancellationToken)
            .ConfigureAwait(false))
        {
            yield return update;
        }
    }
}
