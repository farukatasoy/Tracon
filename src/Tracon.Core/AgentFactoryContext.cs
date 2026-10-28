using Microsoft.Extensions.AI;

namespace Tracon;

/// <summary>
/// What the factory of an agent registered with a declared model receives: the agent's name, the
/// declared model and the chat client Tracon built for it.
/// </summary>
/// <remarks>
/// <para>
/// The factory builds the agent around <see cref="ChatClient"/>. That client comes from the model
/// registry: it carries the model rings (run budget, circuit breaker, fallback, response cache,
/// content guards) and, for the tenant of the run, the tenant's own provider key and egress policy.
/// Because the declared model is also the model of the agent in the catalog, the run record prices the
/// run at that model, the cost quota applies, and <c>POST /api/agents/{name}/estimate</c> answers.
/// </para>
/// <para>
/// Tracon calls the factory on every resolve and keeps neither the agent nor the client. The client
/// holds the tenant's key, so do not keep it, or the agent that holds it, in a field or a cache that
/// outlives the call: another tenant would use the first tenant's key.
/// </para>
/// <para>
/// A factory builds the agent. It does not call the model: the detail view of the agent
/// (<c>GET /api/agents/{name}</c>) calls the factory to read the instructions and builds the client
/// without the tenant's key. A model call made while the factory runs uses that client.
/// </para>
/// </remarks>
public sealed class AgentFactoryContext
{
    /// <summary>Gets the name the agent is registered under.</summary>
    public required string AgentName { get; init; }

    /// <summary>Gets the host's service provider.</summary>
    /// <remarks>
    /// Resolve the services you need from it while the factory runs, and do not keep it. When Tracon
    /// resolves the agent for a run it is the root provider; when the detail view of the agent reads the
    /// instructions it is the provider of that request.
    /// </remarks>
    public required IServiceProvider Services { get; init; }

    /// <summary>Gets the model the agent declared.</summary>
    public required ModelBinding Model { get; init; }

    /// <summary>
    /// Gets the chat client for <see cref="Model"/>, built for the tenant of the current run.
    /// </summary>
    public required IChatClient ChatClient { get; init; }
}
