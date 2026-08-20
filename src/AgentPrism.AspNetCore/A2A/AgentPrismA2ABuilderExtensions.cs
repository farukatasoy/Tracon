using Microsoft.Agents.AI.Hosting.A2A;
using Microsoft.Extensions.DependencyInjection;

namespace AgentPrism;

/// <summary>
/// Extensions that register the services needed to publish AgentPrism agents
/// over A2A.
/// </summary>
public static class AgentPrismA2ABuilderExtensions
{
    /// <summary>
    /// Registers an A2A server for each agent in
    /// <see cref="AgentPrismA2AOptions.ExposedAgents"/>. The HTTP endpoint must
    /// still be connected separately with <c>app.MapAgentPrismA2A(...)</c>.
    /// </summary>
    /// <param name="builder">The AgentPrism configuration chain.</param>
    /// <param name="configure">Options mutator.</param>
    /// <returns>The chain, for further configuration.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// <c>ExposedAgents</c> is read and FROZEN HERE, at registration time.
    /// <c>AddA2AServer</c> requires an agent INSTANCE; because AgentPrism's
    /// catalog can change at runtime (MAF's own registry is NOT used), a
    /// lazily-resolved <see cref="ExternalAgentProxy"/> is registered for each
    /// name — the real agent is resolved from the catalog on EVERY call, but
    /// WHICH names exist in A2A is frozen at registration time.
    /// </para>
    /// <para>By default no agent is exposed.</para>
    /// <example>
    /// <code>
    /// builder.AddAgentPrism()
    ///        .UseA2A(o => o.ExposedAgents.Add("support"));
    /// </code>
    /// </example>
    /// </remarks>
    public static IAgentPrismBuilder UseA2A(
        this IAgentPrismBuilder builder,
        Action<AgentPrismA2AOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var services = builder.Services;

        var options = new AgentPrismA2AOptions();
        configure?.Invoke(options);

        // The value is put into DI HERE (at registration time); MapAgentPrismA2A()
        // reads it back after Build(). Unlike AgentPrismMcpServerOptions this is
        // a plain singleton, NOT an IOptionsMonitor: A2A's list is already
        // frozen, so IOptionsMonitor's "can change at runtime" promise has no
        // counterpart here.
        services.AddSingleton(options);

        foreach (var agentName in options.ExposedAgents)
        {
            var proxy = new ExternalAgentProxy(agentName) { BudgetTemplate = options.Budget };

            services.AddKeyedSingleton(agentName, proxy);

            services.AddA2AServer(proxy, register =>
            {
                // MAF has no mid-run approval suspension (K-103). Background
                // mode is another form of that same suspension; it is turned
                // off explicitly to avoid the same boundary.
                //
                // MEAI001: AgentRunMode is marked "for evaluation purposes".
                // Usage is consolidated in this ONE file (same pattern as MAAI001).
#pragma warning disable MEAI001
                register.AgentRunMode = AgentRunMode.DisallowBackground;
#pragma warning restore MEAI001
            });
        }

        return builder;
    }
}
