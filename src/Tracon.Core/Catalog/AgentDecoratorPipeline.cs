using System.Runtime.ExceptionServices;
using Microsoft.Agents.AI;

namespace Tracon;

/// <summary>
/// Applies every registered <see cref="IAgentDecorator"/> to an agent, in the
/// same order <see cref="CompositeAgentCatalog"/> uses.
/// </summary>
/// <remarks>
/// A caller that compiles an agent OUTSIDE the catalog (a parameterized run,
/// which bypasses <c>CompiledAgentCache</c> and therefore the catalog's own
/// <c>ResolveAsync</c>) must still apply this same pipeline by hand - otherwise
/// the agent runs undecorated: no run recording, no telemetry, no tool-approval
/// gate.
/// </remarks>
public static class AgentDecoratorPipeline
{
    /// <summary>Applies every decorator, ordered from innermost to outermost.</summary>
    /// <param name="agent">The compiled agent.</param>
    /// <param name="descriptor">The catalog summary of the agent.</param>
    /// <param name="decorators">The registered decorators.</param>
    /// <returns>The decorated agent.</returns>
    /// <exception cref="ArgumentNullException">A parameter is <see langword="null"/>.</exception>
    /// <exception cref="TraconAgentSourceException">A decorator threw while decorating the agent.</exception>
    public static AIAgent Apply(AIAgent agent, AgentDescriptor descriptor, IEnumerable<IAgentDecorator> decorators)
    {
        ArgumentNullException.ThrowIfNull(agent);
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentNullException.ThrowIfNull(decorators);

        foreach (var decorator in decorators.OrderByDescending(static decorator => decorator.Order))
        {
            try
            {
                agent = decorator.Decorate(agent, descriptor);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                throw Wrap(descriptor, exception);
            }
        }

        return agent;
    }

    /// <summary>
    /// Normalizes a decorator failure into the same exception shape
    /// <see cref="CompositeAgentCatalog"/> uses for a source failure - a
    /// packaged consumer reads one contract for "the catalog could not
    /// resolve this agent because of an extension", not two.
    /// </summary>
    private static TraconAgentSourceException Wrap(AgentDescriptor descriptor, Exception exception)
    {
        if (exception is TraconException)
        {
            // Already a safe, normalized error (see the identical rationale on
            // CompositeAgentCatalog.HandleSourceFailure) - re-wrapping it would
            // both lose its specific type and misreport it as a decorator
            // failure it is not.
            ExceptionDispatchInfo.Capture(exception).Throw();
        }

        return new TraconAgentSourceException(
            descriptor.SourceName,
            TraconAgentSourceException.SourceFailedErrorType,
            $"Agent source '{descriptor.SourceName}' failed during decorate ({TraconAgentSourceException.SourceFailedErrorType}).",
            exception);
    }
}
