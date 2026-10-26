using System.Collections.Concurrent;
using Microsoft.Agents.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Tracon;

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
    private readonly TraconAgentGraphOptions _agentGraph;
    private readonly TimeProvider? _timeProvider;
    private readonly ParameterizedAgentFactory? _parameterFactory;

    /// <summary>Creates a new cache.</summary>
    /// <param name="resolver">The resolver that resolves agents from the catalog.</param>
    /// <param name="tenantContext">The tenant context.</param>
    /// <param name="loggerFactory">The logger factory.</param>
    /// <param name="options">
    /// Tree-wide defaults, including the sub-agent wait limits
    /// (<see cref="TraconAgentGraphOptions.ChildDeadline"/>/<see cref="TraconAgentGraphOptions.WaitTimeout"/>)
    /// applied to every workflow participant — a workflow participant has no
    /// <see cref="AgentDefinition.SubAgents"/> of its own to override them with.
    /// </param>
    /// <param name="timeProvider">Time source for the sub-agent wait race. Defaults to <see cref="TimeProvider.System"/>.</param>
    /// <param name="parameterFactory">
    /// Lets a step bind the running workflow's parameter values. When
    /// <see langword="null"/> a workflow's values never reach its steps.
    /// </param>
    /// <exception cref="ArgumentNullException">One of the required dependencies is <see langword="null"/>.</exception>
    public WorkflowAgentCache(
        CallableAgentResolver resolver,
        ITenantContext tenantContext,
        ILoggerFactory loggerFactory,
        IOptions<TraconOptions> options,
        TimeProvider? timeProvider = null,
        ParameterizedAgentFactory? parameterFactory = null)
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
        _parameterFactory = parameterFactory;
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
    /// <param name="timeout">
    /// The node's own timeout (<see cref="WorkflowNodeReference.Timeout"/>), which
    /// replaces <see cref="TraconAgentGraphOptions.ChildDeadline"/> for this step; the
    /// hard cutoff is the timeout plus 30 seconds. <see langword="null"/> uses the
    /// installation's limits.
    /// </param>
    /// <param name="agentVersion">
    /// The version of the agent the step runs (<see cref="WorkflowNodeReference.AgentVersion"/>).
    /// <see langword="null"/> runs the agent's current version on every call.
    /// </param>
    /// <param name="hostsApproval">
    /// Whether a Microsoft Agent Framework host runs the agent and turns its tool approval into a workflow
    /// request: the five ready-made patterns' participants and <c>GetWorkflowAgent</c>. A plain function
    /// step (a mixed chain's agent node, <c>GetWorkflowAgentStep</c>) and a Magentic manager do not, and
    /// their wrapper refuses the approval.
    /// </param>
    /// <param name="agentVersionParameter">
    /// The name of the run parameter that carries the version the step runs
    /// (<see cref="WorkflowAgentStepOptions.AgentVersionParameter"/>), read on every call.
    /// <see langword="null"/> leaves the choice to <paramref name="agentVersion"/>.
    /// </param>
    /// <returns>An agent with a stable identity, ready to be placed in the graph.</returns>
    /// <remarks>
    /// The timeout, the version and the name of the version parameter are part of the cache key, because
    /// <see cref="ChildAgentInvoker"/> takes all three in its constructor; none is part of the
    /// identity. The key holds the parameter's <em>name</em>, never a run's value: the value is read when
    /// the step runs, so every run, whatever version it asks for, shares one wrapper and the cache stays
    /// the size of the graph. Every wrapper of the same <c>(workflow, agent)</c> pair gets the same executor
    /// id, so a changed timeout or a changed pin does not make a waiting run's checkpoint
    /// incompatible.
    /// </remarks>
    public ChildAgentInvoker Get(
        string workflowName,
        string agentName,
        string? description,
        TimeSpan? timeout = null,
        int? agentVersion = null,
        bool hostsApproval = false,
        string? agentVersionParameter = null)
        => _agents.GetOrAdd(
            new AgentKey(_tenantContext.TenantId, workflowName, agentName, timeout, agentVersion, hostsApproval, agentVersionParameter),
            static (key, state) => Create(key, state),
            (
                Resolver: _resolver,
                TenantContext: _tenantContext,
                LoggerFactory: _loggerFactory,
                Description: description,
                AgentGraph: _agentGraph,
                TimeProvider: _timeProvider,
                ParameterFactory: _parameterFactory));

    /// <summary>
    /// Finds the real name of the agent a Microsoft Agent Framework executor id stands for.
    /// </summary>
    /// <param name="identity">
    /// The 32 hexadecimal digits that follow the agent's name in an executor id
    /// (<see cref="WorkflowAgentIdentity"/>).
    /// </param>
    /// <returns>The agent's catalog name; <see langword="null"/> when no wrapper has that identity.</returns>
    /// <remarks>
    /// The framework builds the executor id from the agent's name with every character it dislikes replaced
    /// (<c>approval-agent</c> becomes <c>approval_agent</c>), so the name cannot be read back from the id. The
    /// identity is the key: every wrapper of one <c>(workflow, agent)</c> pair carries the same one, and it is
    /// derived from the two names.
    /// </remarks>
    public string? NameOf(string identity)
    {
        foreach (var wrapper in _agents.Values)
        {
            if (string.Equals(wrapper.Id, identity, StringComparison.Ordinal))
            {
                return wrapper.Name;
            }
        }

        return null;
    }

    private static ChildAgentInvoker Create(
        AgentKey key,
        (CallableAgentResolver Resolver,
         ITenantContext TenantContext,
         ILoggerFactory LoggerFactory,
         string? Description,
         TraconAgentGraphOptions AgentGraph,
         TimeProvider? TimeProvider,
         ParameterizedAgentFactory? ParameterFactory) state)
    {
        // F-405: a node's timeout replaces the installation's ChildDeadline, and its
        // hard cutoff follows the same rule as the installation's default (deadline +
        // 30 s), not a WaitTimeout pinned for every agent. The save checked the sum fits.
        var (childDeadline, waitTimeout, limitName) = key.Timeout is { } nodeTimeout
            ? (nodeTimeout, nodeTimeout + TraconAgentGraphOptions.WaitTimeoutPad, $"the 'timeout' of node '{key.AgentName}' in workflow '{key.WorkflowName}'")
            : (state.AgentGraph.ChildDeadline, state.AgentGraph.WaitTimeout, "'Tracon:AgentGraph:ChildDeadline'");

        var invoker = new ChildAgentInvoker(
            state.Resolver,
            state.TenantContext,
            state.LoggerFactory.CreateLogger<ChildAgentInvoker>(),
            key.WorkflowName,
            new CallableAgentInfo(key.AgentName, state.Description, Version: 0),
            childDeadline,
            waitTimeout,
            state.TimeProvider,

            // 🚨 A workflow step FAILS when it cannot deliver an answer (F-328).
            // The delegation default returns the refusal as text for the calling
            // model; here no model reads it, the next node takes it as content.
            ChildRefusalMode.Fail,

            // Only a workflow step binds the workflow's parameter values (F-294);
            // an agent called as a tool never does.
            state.ParameterFactory,
            limitName,
            key.AgentVersion,
            key.HostsApproval,
            versionParameter: key.AgentVersionParameter);

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
    //
    // The timeout is part of the key (F-405) for the same reason the tenant is: the
    // wrapper takes it in its constructor. Like the tenant, it never reaches the id. The
    // pinned agent version (phase 200) follows the same rule: two steps of the same
    // (workflow, agent) pair that pin different versions are two wrappers with ONE id.
    //
    // The version PARAMETER enters as its name and never as a run's value. A value in the key would make one
    // wrapper per version a caller ever asked for - a number the caller controls, so the cache would grow
    // without a bound - and the wrapper reads the value from the running scope anyway.
    private readonly record struct AgentKey(
        string TenantId,
        string WorkflowName,
        string AgentName,
        TimeSpan? Timeout,
        int? AgentVersion,
        bool HostsApproval,
        string? AgentVersionParameter);
}
