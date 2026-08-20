using System.Collections.Concurrent;
using Microsoft.Agents.AI;
using Microsoft.Extensions.Logging;

namespace AgentPrism;

/// <summary>
/// Holds the agent wrappers bound to a workflow for the lifetime of the process.
/// </summary>
/// <remarks>
/// <para>
/// <strong>This cache exists for CORRECTNESS, not performance.</strong>
/// Microsoft Agent Framework derives executor ids from the agent
/// <em>instance</em>: the id has the form <c>{Name}_{AIAgent.Id}</c>, and
/// <c>AIAgent.Id</c> is generated randomly for each instance - it is not
/// virtual, and a derived class cannot override it.
/// </para>
/// <para>
/// Consequence: if agents were rebuilt every time the graph is rebuilt, their
/// executor ids would change every time, and resuming from a checkpoint would
/// fail with <c>InvalidDataException: The specified checkpoint is
/// not compatible with the workflow</c>. Measured: a graph rebuilt
/// with the same agent instances was compatible; a graph built with new
/// instances was not.
/// </para>
/// <para>
/// The wrapper (<see cref="ChildAgentInvoker"/>) resolves the real agent from
/// the catalog on <em>every call</em>; a cached wrapper therefore never goes
/// stale. Only the name, description, and id stay fixed in the cache.
/// </para>
/// <para>
/// <strong>The identity also survives across process
/// lifetimes.</strong> Every wrapper gets a permanent id derived from the
/// <c>(workflow, agent)</c> pair via <see cref="WorkflowAgentIdentity"/>; this
/// way old checkpoints stay compatible even after the application restarts,
/// and a run awaiting human input is not lost on a deployment. The cache is
/// still kept: reusing the same instance is both cheap and keeps the identity
/// write path in one place.
/// </para>
/// </remarks>
internal sealed class WorkflowAgentCache
{
    private readonly ConcurrentDictionary<AgentKey, ChildAgentInvoker> _agents = new();
    private readonly CallableAgentResolver _resolver;
    private readonly ITenantContext _tenantContext;
    private readonly ILoggerFactory _loggerFactory;

    /// <summary>Creates a new cache.</summary>
    /// <param name="resolver">The resolver that resolves agents from the catalog.</param>
    /// <param name="tenantContext">The tenant context.</param>
    /// <param name="loggerFactory">The logger factory.</param>
    /// <exception cref="ArgumentNullException">One of the dependencies is <see langword="null"/>.</exception>
    public WorkflowAgentCache(
        CallableAgentResolver resolver,
        ITenantContext tenantContext,
        ILoggerFactory loggerFactory)
    {
        ArgumentNullException.ThrowIfNull(resolver);
        ArgumentNullException.ThrowIfNull(tenantContext);
        ArgumentNullException.ThrowIfNull(loggerFactory);

        _resolver = resolver;
        _tenantContext = tenantContext;
        _loggerFactory = loggerFactory;
    }

    /// <summary>
    /// Returns the agent wrapper a workflow should use; creates and stores it
    /// on the first call.
    /// </summary>
    /// <param name="workflowName">The name of the enclosing workflow.</param>
    /// <param name="agentName">The name of the agent to bind.</param>
    /// <param name="description">
    /// The participant description. <strong>It is captured on the first
    /// binding</strong> and stays fixed for the lifetime of the process;
    /// identity stability requires this. The description only reaches the
    /// participant list sent to the model in the <c>GroupChat</c> and
    /// <c>Magentic</c> patterns - it does not affect the agent's behavior.
    /// </param>
    /// <returns>An agent with a stable identity, ready to be placed in the graph.</returns>
    public ChildAgentInvoker Get(string workflowName, string agentName, string? description)
        => _agents.GetOrAdd(
            new AgentKey(workflowName, agentName),
            static (key, state) => Create(key, state),
            (Resolver: _resolver, TenantContext: _tenantContext, LoggerFactory: _loggerFactory, Description: description));

    private static ChildAgentInvoker Create(
        AgentKey key,
        (CallableAgentResolver Resolver, ITenantContext TenantContext, ILoggerFactory LoggerFactory, string? Description) state)
    {
        var invoker = new ChildAgentInvoker(
            state.Resolver,
            state.TenantContext,
            state.LoggerFactory.CreateLogger<ChildAgentInvoker>(),
            key.WorkflowName,
            new CallableAgentInfo(key.AgentName, state.Description, Version: 0));

        // The identity is written on the instance BEFORE it enters the graph:
        // MAF reads the executor id at binding time, and changing it afterward
        // would split the graph in two.
        WorkflowAgentIdentity.TryApply(
            invoker,
            key.WorkflowName,
            key.AgentName,
            state.LoggerFactory.CreateLogger(typeof(WorkflowAgentIdentity).FullName!));

        return invoker;
    }

    private readonly record struct AgentKey(string WorkflowName, string AgentName);
}
