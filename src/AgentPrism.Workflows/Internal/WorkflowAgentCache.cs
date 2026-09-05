using System.Collections.Concurrent;
using Microsoft.Agents.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

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
    private readonly AgentPrismAgentGraphOptions _agentGraph;
    private readonly TimeProvider? _timeProvider;

    /// <summary>Creates a new cache.</summary>
    /// <param name="resolver">The resolver that resolves agents from the catalog.</param>
    /// <param name="tenantContext">The tenant context.</param>
    /// <param name="loggerFactory">The logger factory.</param>
    /// <param name="options">
    /// Tree-wide defaults, including the sub-agent wait limits
    /// (<see cref="AgentPrismAgentGraphOptions.ChildDeadline"/>/<see cref="AgentPrismAgentGraphOptions.WaitTimeout"/>)
    /// applied to every workflow participant — a workflow participant has no
    /// <see cref="AgentDefinition.SubAgents"/> of its own to override them with.
    /// </param>
    /// <param name="timeProvider">Time source for the sub-agent wait race. Defaults to <see cref="TimeProvider.System"/>.</param>
    /// <exception cref="ArgumentNullException">One of the required dependencies is <see langword="null"/>.</exception>
    public WorkflowAgentCache(
        CallableAgentResolver resolver,
        ITenantContext tenantContext,
        ILoggerFactory loggerFactory,
        IOptions<AgentPrismOptions> options,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(resolver);
        ArgumentNullException.ThrowIfNull(tenantContext);
        ArgumentNullException.ThrowIfNull(loggerFactory);
        ArgumentNullException.ThrowIfNull(options);

        _resolver = resolver;
        _tenantContext = tenantContext;
        _loggerFactory = loggerFactory;
        _agentGraph = options.Value.AgentGraph;
        _timeProvider = timeProvider;
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
            new AgentKey(_tenantContext.TenantId, workflowName, agentName),
            static (key, state) => Create(key, state),
            (
                Resolver: _resolver,
                TenantContext: _tenantContext,
                LoggerFactory: _loggerFactory,
                Description: description,
                AgentGraph: _agentGraph,
                TimeProvider: _timeProvider));

    private static ChildAgentInvoker Create(
        AgentKey key,
        (CallableAgentResolver Resolver,
         ITenantContext TenantContext,
         ILoggerFactory LoggerFactory,
         string? Description,
         AgentPrismAgentGraphOptions AgentGraph,
         TimeProvider? TimeProvider) state)
    {
        var invoker = new ChildAgentInvoker(
            state.Resolver,
            state.TenantContext,
            state.LoggerFactory.CreateLogger<ChildAgentInvoker>(),
            key.WorkflowName,
            new CallableAgentInfo(key.AgentName, state.Description, Version: 0),
            state.AgentGraph.ChildDeadline,
            state.AgentGraph.WaitTimeout,
            state.TimeProvider);

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

    // 🚨 TenantId is part of the key and must stay part of it. A workflow name
    // and an agent name are unique only WITHIN a tenant (UNIQUE (tenant_id,
    // name)); without the tenant the second tenant received the first tenant's
    // wrapper and its captured Description, and that description reaches the
    // participant list sent to the model in the GroupChat and Magentic patterns.
    // Same defect class as K-380 (CompiledAgentCache) and K-381 (file memory).
    //
    // The tenant deliberately does NOT reach WorkflowAgentIdentity: the executor
    // id stays derived from the (workflow, agent) pair, so checkpoints written
    // by phase 16 onward stay readable.
    private readonly record struct AgentKey(string TenantId, string WorkflowName, string AgentName);
}
