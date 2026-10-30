using Microsoft.Agents.AI.Hosting;
using Microsoft.Agents.AI.Hosting.A2A;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Tracon;

/// <summary>
/// Extensions that register the services needed to publish Tracon agents
/// over A2A.
/// </summary>
public static class TraconA2ABuilderExtensions
{
    /// <summary>
    /// Registers an A2A server for each agent in
    /// <see cref="TraconA2AOptions.ExposedAgents"/>. The HTTP endpoint must
    /// still be connected separately with <c>app.MapTraconA2A(...)</c>.
    /// </summary>
    /// <param name="builder">The Tracon configuration chain.</param>
    /// <param name="configure">Options mutator.</param>
    /// <returns>The chain, for further configuration.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// <c>ExposedAgents</c> is read and FROZEN HERE, at registration time.
    /// <c>AddA2AServer</c> requires an agent INSTANCE; because Tracon's
    /// catalog can change at runtime (MAF's own registry is NOT used), a
    /// lazily-resolved <see cref="ExternalAgentProxy"/> is registered for each
    /// name — the real agent is resolved from the catalog on EVERY call, but
    /// WHICH names exist in A2A is frozen at registration time.
    /// </para>
    /// <para>By default no agent is exposed.</para>
    /// <para>
    /// A message that carries a <c>contextId</c> continues a conversation: the
    /// session is stored, and the agent sees the earlier turns. A conversation
    /// belongs to the caller (the signed-in user, the API key, or, with no
    /// identity at all, the local operator); another caller that sends the same
    /// <c>contextId</c> starts a new one. A consumer replaces the storage by
    /// registering an <c>AgentSessionStore</c>, keyed by the agent name or
    /// without a key, and the caller identification by registering an
    /// <c>AgentIsolationKeyProvider</c>.
    /// </para>
    /// <example>
    /// <code>
    /// builder.AddTracon()
    ///        .UseA2A(o => o.ExposedAgents.Add("support"));
    /// </code>
    /// </example>
    /// </remarks>
    public static ITraconBuilder UseA2A(
        this ITraconBuilder builder,
        Action<TraconA2AOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var services = builder.Services;

        var options = new TraconA2AOptions();
        configure?.Invoke(options);

        // The value is put into DI HERE (at registration time); MapTraconA2A()
        // reads it back after Build(). Unlike TraconMcpServerOptions this is
        // a plain singleton, NOT an IOptionsMonitor: A2A's list is already
        // frozen, so IOptionsMonitor's "can change at runtime" promise has no
        // counterpart here.
        services.AddSingleton(options);

        // A2A conversations are scoped to the caller. The provider
        // is a TryAdd so a consumer's own AgentIsolationKeyProvider wins; it
        // reads the request, so the accessor is registered with it.
        services.AddHttpContextAccessor();
        services.TryAddSingleton<AgentIsolationKeyProvider, TraconCallerIsolationKeyProvider>();

        foreach (var agentName in options.ExposedAgents)
        {
            var proxy = new ExternalAgentProxy(agentName) { BudgetTemplate = options.Budget };

            services.AddKeyedSingleton(agentName, proxy);
            A2ASessionStore.Register(services, agentName);

            services.AddA2AServer(proxy, register =>
            {
                // MAF has no mid-run approval suspension. A task
                // response is another form of that same suspension; every run
                // answers with a single message to avoid the same boundary.
                // (MAF 1.22 renamed DisallowBackground to ReturnMessage.)
                //
                // MEAI001: AgentRunMode is marked "for evaluation purposes".
                // Usage is consolidated in this ONE file (same pattern as MAAI001).
#pragma warning disable MEAI001
                register.AgentRunMode = AgentRunMode.ReturnMessage;
#pragma warning restore MEAI001
            });
        }

        return builder;
    }
}
