using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace AgentPrism;

/// <summary>
/// Makes a recorded run re-runnable with the same input under modified
/// conditions.
/// </summary>
/// <remarks>
/// <para>
/// The service does <strong>not start</strong> the run; it only prepares it.
/// This lets the caller use the same plan on a streaming, non-streaming, or
/// queued (Phase 46) path, and every failure condition is known before the
/// response starts.
/// </para>
/// <para>
/// 🚨 <strong>Replay is sessionless.</strong> If the source run belongs to a
/// session, its input is only the messages from <em>that turn</em>; history
/// is injected by <c>ChatHistoryProvider</c> and is not part of the recorded
/// input. Running the replay in the same session would write into the
/// source's conversation (append-only, K-014); that is why a replay is
/// always a new, sessionless run. To restart a multi-turn conversation from
/// the beginning, use branching (<see cref="IConversationBranchStore"/>).
/// </para>
/// </remarks>
public sealed class RunReplayService
{
    private readonly IRunStore _runs;
    private readonly IRunInputStore _inputs;
    private readonly IAgentDefinitionStore _definitions;
    private readonly IAgentCatalog _catalog;
    private readonly AgentDefinitionCompiler _compiler;
    private readonly IToolRegistry _tools;
    private readonly IAgentDecorator[] _decorators;
    private readonly ITenantContext _tenantContext;

    /// <summary>Creates a new replay service.</summary>
    /// <param name="runs">The run store.</param>
    /// <param name="inputs">The input store.</param>
    /// <param name="definitions">The agent definition store.</param>
    /// <param name="catalog">The agent catalog.</param>
    /// <param name="compiler">The definition compiler.</param>
    /// <param name="tools">The tool registry. The approval-required tool check is read from here.</param>
    /// <param name="decorators">The wrappers to apply to the resolved agent.</param>
    /// <param name="tenantContext">The tenant context.</param>
    /// <exception cref="ArgumentNullException">One of the dependencies is <see langword="null"/>.</exception>
    public RunReplayService(
        IRunStore runs,
        IRunInputStore inputs,
        IAgentDefinitionStore definitions,
        IAgentCatalog catalog,
        AgentDefinitionCompiler compiler,
        IToolRegistry tools,
        IEnumerable<IAgentDecorator> decorators,
        ITenantContext tenantContext)
    {
        ArgumentNullException.ThrowIfNull(runs);
        ArgumentNullException.ThrowIfNull(inputs);
        ArgumentNullException.ThrowIfNull(definitions);
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(compiler);
        ArgumentNullException.ThrowIfNull(tools);
        ArgumentNullException.ThrowIfNull(decorators);
        ArgumentNullException.ThrowIfNull(tenantContext);

        _runs = runs;
        _inputs = inputs;
        _definitions = definitions;
        _catalog = catalog;
        _compiler = compiler;
        _tools = tools;

        // The order is the SAME as CompositeAgentCatalog: the decorator with
        // the larger Order is applied first, so the recording wrapper
        // (Order = 0) ends up outermost.
        _decorators = [.. decorators.OrderByDescending(static decorator => decorator.Order)];
        _tenantContext = tenantContext;
    }

    /// <summary>Prepares a replay.</summary>
    /// <param name="runId">The identity of the source run.</param>
    /// <param name="request">The conditions to modify.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The preparation result.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
    public async ValueTask<RunReplayPreparation> PrepareAsync(
        Guid runId,
        RunReplayRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var source = await _runs.GetRunAsync(runId, cancellationToken).ConfigureAwait(false);

        // "Not found" and "belongs to a different tenant" give the SAME
        // result; a distinct response would leak existence.
        if (source is null || !string.Equals(source.TenantId, _tenantContext.TenantId, StringComparison.Ordinal))
        {
            return RunReplayPreparation.Failed(
                RunReplayOutcome.RunNotFound,
                $"There is no run with id '{runId}'.");
        }

        var input = await _inputs
            .GetAsync(_tenantContext.TenantId, runId, cancellationToken)
            .ConfigureAwait(false);

        if (input is null || input.Messages.Count == 0)
        {
            return RunReplayPreparation.Failed(
                RunReplayOutcome.InputNotFound,
                $"Run '{runId}' has no recorded input. It may have started while input recording " +
                "was disabled (AgentPrism:RunRecording:RecordRunInput = false), or been deleted by " +
                "a retention policy; this run cannot be replayed.");
        }

        var definition = await ResolveDefinitionAsync(source.AgentName, request.AgentVersion, cancellationToken)
            .ConfigureAwait(false);

        if (definition is null)
        {
            return await PrepareFromCatalogAsync(source, request, input, cancellationToken).ConfigureAwait(false);
        }

        if (request.ToolMode == ReplayToolMode.LiveTools && FindApprovalTool(definition.ToolNames) is { } approvalTool)
        {
            return RunReplayPreparation.Failed(
                RunReplayOutcome.ApprovalRequired,
                $"Agent '{definition.Name}' carries the tool '{approvalTool}', which requires approval, " +
                "and cannot run in 'LiveTools' mode. A replay may start in the background; the approval " +
                "request would find no client to answer it at that moment (the same limit applies to " +
                "sub-agents). Use 'ReplayTools' or 'NoTools'.");
        }

        var effective = ApplyOverrides(definition, request);
        var playback = request.ToolMode == ReplayToolMode.ReplayTools
            ? new RecordedToolPlayback(
                await _runs.ListToolInvocationsAsync(runId, cancellationToken).ConfigureAwait(false))
            : null;

        var callable = await _compiler
            .ResolveCallableAgentsAsync(effective, cancellationToken)
            .ConfigureAwait(false);

        // 🚨 The cache (CompiledAgentCache) is DELIBERATELY skipped: the
        // overridden model and played-back tools are specific to this call;
        // letting them enter the cache would also corrupt later normal runs.
        // The async overload is used so a replay honors the run's own
        // tenant's provider credential and egress policy (phase 65) the same
        // way the original run did, instead of silently falling back to the
        // global setup-time credential.
        var agent = await _compiler
            .CompileAsync(effective, callable, playback is null ? null : playback.Wrap, culture: null, cancellationToken)
            .ConfigureAwait(false);

        // 🚨 The guard sits INSIDE the decorators; the rationale is in the
        // note on ReplayMismatchGuard (the exception must land in the
        // recording wrapper's catch block).
        if (playback is not null)
        {
            agent = new ReplayMismatchGuard(agent, playback);
        }

        return RunReplayPreparation.Ready(
            Decorate(agent, ToDescriptor(effective)),
            input.Messages,
            source,
            effective.Version,
            effective.Model.Model);
    }

    /// <summary>
    /// Builds a plan for an agent (code-defined) that has no counterpart in
    /// the definition store.
    /// </summary>
    /// <remarks>
    /// 🚨 A code-defined agent has no <see cref="AgentDefinition"/>
    /// counterpart; there is also no definition to recompile for model
    /// override or tool substitution. Silently falling back to
    /// <see cref="ReplayToolMode.LiveTools"/> would violate K1 — the user
    /// would believe no side effect was produced. So the request is
    /// explicitly rejected instead.
    /// </remarks>
    private async ValueTask<RunReplayPreparation> PrepareFromCatalogAsync(
        RunRecord source,
        RunReplayRequest request,
        RunInputRecord input,
        CancellationToken cancellationToken)
    {
        if (request.AgentVersion is not null)
        {
            return RunReplayPreparation.Failed(
                RunReplayOutcome.NotSupported,
                $"Agent '{source.AgentName}' has no version {request.AgentVersion}. " +
                "Code-defined agents have no version history.");
        }

        if (request.ModelId is not null || request.ToolMode != ReplayToolMode.LiveTools)
        {
            return RunReplayPreparation.Failed(
                RunReplayOutcome.NotSupported,
                $"Agent '{source.AgentName}' has no persistent definition (code-defined or deleted). " +
                "Model overrides and 'NoTools'/'ReplayTools' modes require recompiling the definition. " +
                "This agent can only be replayed with 'toolMode: LiveTools' and no override — tools " +
                "ACTUALLY run and produce side effects.");
        }

        var agent = await _catalog.ResolveAsync(source.AgentName, culture: null, cancellationToken).ConfigureAwait(false);

        if (agent is null)
        {
            return RunReplayPreparation.Failed(
                RunReplayOutcome.NotSupported,
                $"There is no longer an agent named '{source.AgentName}'; the run cannot be replayed.");
        }

        // A code-defined agent carries no AgentDefinition (the check above
        // already rejected every mode except LiveTools), but the same
        // approval-tool protection is still needed here: otherwise a
        // code-defined agent carrying a tool that requires approval would,
        // unlike a DB-defined one, silently end in "nothing happened" with
        // no 409 (HATA-S4-014). Tool names are read from the catalog
        // descriptor (the same data that also feeds the UI's agent list),
        // not from AgentDefinition.
        var descriptors = await _catalog.ListAsync(cancellationToken).ConfigureAwait(false);
        var descriptor = descriptors.FirstOrDefault(
            candidate => string.Equals(candidate.Name, source.AgentName, StringComparison.Ordinal));

        if (descriptor is not null && FindApprovalTool(descriptor.ToolNames) is { } approvalTool)
        {
            return RunReplayPreparation.Failed(
                RunReplayOutcome.ApprovalRequired,
                $"Agent '{source.AgentName}' carries the tool '{approvalTool}', which requires approval, " +
                "and cannot run in 'LiveTools' mode. A replay may start in the background; the approval " +
                "request would find no client to answer it at that moment (the same limit applies to " +
                "sub-agents). This agent has no persistent definition (code-defined or deleted), so " +
                "'ReplayTools'/'NoTools' are not available either — this run cannot be replayed.");
        }

        // The catalog applies its own wrappers; wrapping a second time would
        // record the run twice.
        return RunReplayPreparation.Ready(agent, input.Messages, source, agentVersion: null, modelId: source.ModelId);
    }

    private async ValueTask<AgentDefinition?> ResolveDefinitionAsync(
        string agentName,
        int? version,
        CancellationToken cancellationToken)
        => version is { } requested
            ? await _definitions.GetVersionAsync(agentName, requested, cancellationToken).ConfigureAwait(false)
            : await _definitions.GetAsync(agentName, cancellationToken).ConfigureAwait(false);

    /// <summary>
    /// Applies the request's overrides to a definition.
    /// </summary>
    /// <remarks>
    /// 🚨 In <see cref="ReplayToolMode.NoTools"/> and
    /// <see cref="ReplayToolMode.ReplayTools"/> modes, the skill and
    /// callable-sub-agent surfaces are also <strong>disabled</strong>. Both
    /// expose their tools through an <c>AIContextProvider</c> and do not go
    /// through the tool wrapping; if left open, a skill script would run and
    /// a sub-agent would spend a real model call (and real money) — breaking
    /// the "no tool actually runs" promise.
    /// </remarks>
    private static AgentDefinition ApplyOverrides(AgentDefinition definition, RunReplayRequest request)
    {
        var effective = definition;

        if (request.ModelId is { Length: > 0 } modelId)
        {
            effective = effective with { Model = effective.Model with { Model = modelId } };
        }

        if (request.ToolMode != ReplayToolMode.LiveTools)
        {
            effective = effective with { SkillNames = [], CallableAgentNames = [] };
        }

        if (request.ToolMode == ReplayToolMode.NoTools)
        {
            effective = effective with { ToolNames = [] };
        }

        return effective;
    }

    private string? FindApprovalTool(IReadOnlyList<string> toolNames)
    {
        if (toolNames.Count == 0)
        {
            return null;
        }

        foreach (var descriptor in _tools.List())
        {
            if (descriptor.RequiresApproval &&
                toolNames.Contains(descriptor.Name, StringComparer.Ordinal))
            {
                return descriptor.Name;
            }
        }

        return null;
    }

    private AIAgent Decorate(AIAgent agent, AgentDescriptor descriptor)
    {
        foreach (var decorator in _decorators)
        {
            agent = decorator.Decorate(agent, descriptor);
        }

        return agent;
    }

    private static AgentDescriptor ToDescriptor(AgentDefinition definition)
        => new()
        {
            Name = definition.Name,
            DisplayName = definition.DisplayName,
            Description = definition.Description,
            Origin = AgentDefinitionOrigin.Database,
            SourceName = "replay",
            Version = definition.Version,
            Model = definition.Model,
            ToolNames = definition.ToolNames,
            SkillNames = definition.SkillNames,
            CallableAgentNames = definition.CallableAgentNames,
            UsesHarness = definition.Harness is not null,
            UpdatedAt = definition.UpdatedAt,
        };
}

/// <summary>Represents the outcome of a replay preparation.</summary>
public enum RunReplayOutcome
{
    /// <summary>The plan is ready.</summary>
    Ready = 0,

    /// <summary>The source run does not exist or belongs to a different tenant.</summary>
    RunNotFound = 1,

    /// <summary>The source run has no recorded input.</summary>
    InputNotFound = 2,

    /// <summary>The requested conditions cannot be applied to this agent.</summary>
    NotSupported = 3,

    /// <summary>A tool that requires approval cannot run with <see cref="ReplayToolMode.LiveTools"/>.</summary>
    ApprovalRequired = 4,
}

/// <summary>Represents a prepared replay plan.</summary>
public sealed record RunReplayPreparation
{
    private RunReplayPreparation()
    {
    }

    /// <summary>Gets the outcome of the preparation.</summary>
    public required RunReplayOutcome Outcome { get; init; }

    /// <summary>Gets the human-readable reason for a failure. Empty when <see cref="RunReplayOutcome.Ready"/>.</summary>
    public string? Detail { get; init; }

    /// <summary>Gets the agent to run.</summary>
    public AIAgent? Agent { get; init; }

    /// <summary>Gets the recorded input of the source run.</summary>
    public IReadOnlyList<ChatMessage> Messages { get; init; } = [];

    /// <summary>Gets the source run.</summary>
    public RunRecord? SourceRun { get; init; }

    /// <summary>Gets the definition version to use in the replay. <see langword="null"/> for a code-defined agent.</summary>
    public int? AgentVersion { get; init; }

    /// <summary>Gets the model to use in the replay. <see langword="null"/> if unknown.</summary>
    public string? ModelId { get; init; }

    /// <summary>Produces a failed preparation.</summary>
    /// <param name="outcome">The outcome.</param>
    /// <param name="detail">The reason.</param>
    /// <returns>The preparation.</returns>
    public static RunReplayPreparation Failed(RunReplayOutcome outcome, string detail)
        => new() { Outcome = outcome, Detail = detail };

    /// <summary>Produces a ready plan.</summary>
    /// <param name="agent">The agent to run.</param>
    /// <param name="messages">The recorded input.</param>
    /// <param name="sourceRun">The source run.</param>
    /// <param name="agentVersion">The definition version to use.</param>
    /// <param name="modelId">The model to use.</param>
    /// <returns>The preparation.</returns>
    public static RunReplayPreparation Ready(
        AIAgent agent,
        IReadOnlyList<ChatMessage> messages,
        RunRecord sourceRun,
        int? agentVersion,
        string? modelId)
        => new()
        {
            Outcome = RunReplayOutcome.Ready,
            Agent = agent,
            Messages = messages,
            SourceRun = sourceRun,
            AgentVersion = agentVersion,
            ModelId = modelId,
        };
}
