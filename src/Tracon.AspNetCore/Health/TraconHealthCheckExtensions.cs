using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Tracon;

/// <summary>Extensions that connect Tracon to the standard.NET health check system.</summary>
public static class TraconHealthCheckExtensions
{
    /// <summary>
    /// Registers the Tracon health check. The consumer sets up its own <c>/health</c> path.
    /// </summary>
    /// <param name="builder">The health checks builder.</param>
    /// <param name="name">The name of the check. Default is <c>tracon</c>.</param>
    /// <param name="tags">Tags to add to the check. Example: readiness/liveness separation.</param>
    /// <returns>The builder, for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="name"/> is empty.</exception>
    /// <remarks>
    /// <para>
    /// Tracon does not call <c>MapHealthChecks</c> — taking over the consumer's path
    /// choice would break the no-surprises rule. Setup:
    /// </para>
    /// <example>
    /// <code>
    /// builder.Services.AddHealthChecks().AddTraconHealthChecks();
    /// app.MapHealthChecks("/health");
    /// </code>
    /// </example>
    /// <para>
    /// The check reads <see cref="TraconDiagnosticsCollector"/>; it produces no
    /// model call. For the three states, see <see cref="TraconHealthCheck"/>.
    /// </para>
    /// <para>
    /// This call only adds a service registration; whether <c>AddTracon()</c> is
    /// called before or after it does not matter (DI registration is order-independent).
    /// However, if <c>AddTracon()</c> is never called, <see cref="TraconDiagnosticsCollector"/>
    /// cannot be resolved on the first <c>/health</c> probe and DI raises a clear error.
    /// </para>
    /// </remarks>
    public static IHealthChecksBuilder AddTraconHealthChecks(
        this IHealthChecksBuilder builder,
        string name = "tracon",
        IEnumerable<string>? tags = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        return builder.AddCheck<TraconHealthCheck>(name, tags: tags);
    }
}
