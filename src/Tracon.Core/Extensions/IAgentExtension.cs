namespace Tracon;

/// <summary>
/// A named piece of code that reaches the Microsoft Agent Framework options the
/// compiler builds for an agent. A definition selects it by
/// <see cref="IAgentExtension.Name"/> in <see cref="AgentDefinition.ExtensionNames"/>.
/// </summary>
/// <remarks>
/// <para>
/// This is the escape door for a Microsoft Agent Framework option Tracon does not
/// model, for an <c>AIContextProvider</c> of your own, or for a chat client that must
/// see every model call (<see cref="AgentExtensionContext.UseChatClient"/>). The agent still goes
/// through the compiler, so it keeps the model ring, the tenant's provider
/// credential, egress and cost; a hand-built agent in
/// <c>AddAgent(name, factory)</c> loses them.
/// </para>
/// <para>
/// The same boundary as tools applies: code defines, data selects. The name is all
/// a stored definition carries; the user interface cannot write the code behind it.
/// </para>
/// <para>
/// <see cref="Configure"/> runs when an agent is compiled, not on every run. The
/// compiled agent is cached per tenant, agent name and definition, so the result
/// serves the runs of that agent that follow. Two runs that start before the cache
/// holds the agent can each compile it. An agent compiled with parameter values, and
/// an agent of a tenant that holds its own provider key, is not cached and runs the
/// extension each time. Keep the call cheap, keep the extension free of state that
/// belongs to one run, and take services from
/// <see cref="AgentExtensionContext.Services"/> (the root provider; a request-scoped
/// service is not available).
/// </para>
/// <para>
/// <strong>Lifetime.</strong> One <c>singleton</c> instance serves every agent and every
/// tenant. An implementation must be thread-safe: two tenants compile the same
/// extension at the same time. A registration by type resolves it from the container once.
/// </para>
/// <para>
/// <strong>Tenant.</strong> The mode is <c>expected tenant</c>: the tenant the agent is
/// compiled for arrives in <see cref="AgentExtensionContext.TenantId"/>, and the extension
/// does not read an ambient tenant. The compiled-agent cache key carries the tenant, so a
/// result built for one tenant never serves another.
/// </para>
/// <para>
/// <strong>Delivery.</strong> <c>No delivery guarantee applies</c>: <see cref="Configure"/>
/// is a compile-time callback, not a message. It runs once per compilation of an agent
/// that names the extension, and a failure fails that compilation; nothing is retried or
/// queued. After a deployment that changes an extension, an agent already compiled keeps
/// the old result until its definition changes or the process restarts.
/// </para>
/// </remarks>
public interface IAgentExtension
{
    /// <summary>
    /// Gets the name a definition uses to select this extension. It is 1 to 128
    /// characters of lowercase ASCII letters, digits, <c>.</c>, <c>_</c> or <c>-</c>,
    /// and unique among the registered extensions.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Gets what the extension does, in one sentence, or <see langword="null"/>. The
    /// console shows it next to the name and <c>GET /api/agent-extensions</c>
    /// returns it to every reader, so it must not carry a secret.
    /// </summary>
    string? Description { get; }

    /// <summary>
    /// Changes the options of the agent that is being compiled, adds a wrapper
    /// around it, or adds a chat client ring to its model pipeline.
    /// </summary>
    /// <param name="context">The agent being compiled.</param>
    /// <remarks>
    /// The compiler checks the options after the call. A member the definition or a
    /// Tracon tenant and security decision owns must come back unchanged; if it does
    /// not, the compilation fails with a <see cref="TraconCompilationException"/> that
    /// names this extension and the member. An exception thrown here fails the
    /// compilation the same way.
    /// </remarks>
    void Configure(AgentExtensionContext context);
}
