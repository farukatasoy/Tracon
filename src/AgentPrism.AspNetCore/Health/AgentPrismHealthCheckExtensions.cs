using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace AgentPrism;

/// <summary>Extensions that connect AgentPrism to the standard .NET health check system (Phase 33, F-38).</summary>
public static class AgentPrismHealthCheckExtensions
{
    /// <summary>
    /// Registers the AgentPrism health check. The consumer sets up its own <c>/health</c> path.
    /// </summary>
    /// <param name="builder">The health checks builder.</param>
    /// <param name="name">The name of the check. Default is <c>agentprism</c>.</param>
    /// <param name="tags">Tags to add to the check. Example: readiness/liveness separation.</param>
    /// <returns>The builder, for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="name"/> is empty.</exception>
    /// <remarks>
    /// <para>
    /// AgentPrism does not call <c>MapHealthChecks</c> — seizing the consumer's path
    /// choice would force K1. Setup:
    /// </para>
    /// <example>
    /// <code>
    /// builder.Services.AddHealthChecks().AddAgentPrismHealthChecks();
    /// app.MapHealthChecks("/health");
    /// </code>
    /// </example>
    /// <para>
    /// The check reads <see cref="AgentPrismDiagnosticsCollector"/>; it produces no
    /// model call. For the three states, see <see cref="AgentPrismHealthCheck"/>.
    /// </para>
    /// <para>
    /// This call only adds a service registration; whether <c>AddAgentPrism()</c> is
    /// called before or after it does not matter (DI registration is order-independent).
    /// However, if <c>AddAgentPrism()</c> is never called, <see cref="AgentPrismDiagnosticsCollector"/>
    /// cannot be resolved on the first <c>/health</c> probe and DI raises a clear error.
    /// </para>
    /// </remarks>
    public static IHealthChecksBuilder AddAgentPrismHealthChecks(
        this IHealthChecksBuilder builder,
        string name = "agentprism",
        IEnumerable<string>? tags = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        return builder.AddCheck<AgentPrismHealthCheck>(name, tags: tags);
    }
}
