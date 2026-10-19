using Microsoft.Agents.AI;

namespace Tracon.Samples.MafFeatures;

/// <summary>
/// Adds Microsoft Agent Framework function middleware to every agent the catalog resolves.
/// </summary>
/// <remarks>
/// <para>
/// This is the lightest door into the framework. The decorator wraps the agent the
/// catalog already built, so the agent keeps every Tracon ring - the model rings, the
/// tenant key, the egress policy, run recording and cost - and gains the middleware.
/// </para>
/// <para>
/// <see cref="Order"/> must be above <c>0</c>. Run recording sits at <c>0</c> and hands
/// the agents inside it a plain <see cref="AgentRunOptions"/>, which the framework's
/// function middleware accepts. A decorator outside it receives Tracon's own run options,
/// and the middleware rejects that type.
/// </para>
/// </remarks>
/// <param name="log">The log the middleware writes to.</param>
public sealed class ToolCallLogDecorator(ToolCallLog log) : IAgentDecorator
{
    /// <inheritdoc />
    public int Order => 100;

    /// <inheritdoc />
    public AIAgent Decorate(AIAgent agent, AgentDescriptor descriptor)
        => agent.AsBuilder()
            .Use(async (innerAgent, context, next, cancellationToken) =>
            {
                log.Record(descriptor.Name, context.Function.Name);
                return await next(context, cancellationToken).ConfigureAwait(false);
            })
            .Build();
}
