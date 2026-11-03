using System.Runtime.CompilerServices;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace Tracon;

/// <summary>
/// Writes the telemetry of each call through a lane lent for the length of the call.
/// </summary>
/// <remarks>
/// <para>
/// The agent keeps no state of its own, so one decorated agent can serve any number of runs in turn or at once. The lane
/// is taken in the body of the call and handed back in its <c>finally</c>: in a stream that is the iterator body, which
/// runs when the first update is asked for and ends when the consumer stops, however it stops.
/// </para>
/// <para>
/// A stream the consumer never disposes never hands its lane back. The lane stays out of the pool and the next run makes
/// another one.
/// </para>
/// </remarks>
internal sealed class PooledOpenTelemetryAgent : DelegatingAIAgent
{
    private readonly TelemetryLanePool _pool;
    private readonly string? _providerName;

    /// <summary>Initializes an agent that runs <paramref name="innerAgent"/> through the lanes of <paramref name="pool"/>.</summary>
    /// <param name="innerAgent">The agent to run.</param>
    /// <param name="pool">The lanes.</param>
    /// <exception cref="ArgumentNullException">A parameter is <see langword="null"/>.</exception>
    internal PooledOpenTelemetryAgent(AIAgent innerAgent, TelemetryLanePool pool)
        : base(innerAgent)
    {
        ArgumentNullException.ThrowIfNull(pool);
        _pool = pool;
        _providerName = innerAgent.GetService<AIAgentMetadata>()?.ProviderName;
    }

    /// <inheritdoc />
    protected override async Task<AgentResponse> RunCoreAsync(
        IEnumerable<ChatMessage> messages,
        AgentSession? session = null,
        AgentRunOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var lane = _pool.TryRent(_providerName, InnerAgent);

        if (lane is null)
        {
            return await base.RunCoreAsync(messages, session, options, cancellationToken).ConfigureAwait(false);
        }

        try
        {
            return await lane.Agent.RunAsync(messages, session, options, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _pool.Return(lane);
        }
    }

    /// <inheritdoc />
    protected override async IAsyncEnumerable<AgentResponseUpdate> RunCoreStreamingAsync(
        IEnumerable<ChatMessage> messages,
        AgentSession? session = null,
        AgentRunOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var lane = _pool.TryRent(_providerName, InnerAgent);

        try
        {
            var updates = lane is null
                ? base.RunCoreStreamingAsync(messages, session, options, cancellationToken)
                : lane.Agent.RunStreamingAsync(messages, session, options, cancellationToken);

            await foreach (var update in updates.ConfigureAwait(false))
            {
                yield return update;
            }
        }
        finally
        {
            if (lane is not null)
            {
                _pool.Return(lane);
            }
        }
    }
}
