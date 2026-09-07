using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Compaction;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace AgentPrism;

/// <summary>
/// Chat agent, child agent (sub-agent), and harness agent production.
/// </summary>
public sealed partial class AgentDefinitionCompiler
{
    private ChatClientAgent CompileChatAgent(
        AgentDefinition definition,
        IChatClient chatClient,
        ChatOptions chatOptions,
        ResolvedCallableAgents callableAgents)
    {
        var options = new ChatClientAgentOptions
        {
            Id = definition.Name,
            Name = definition.Name,
            Description = definition.Description,
            ChatOptions = chatOptions,
            ChatHistoryProvider = _chatHistoryProvider,
        };

        var providers = new List<AIContextProvider>(2);

        if (definition.SkillNames.Count > 0)
        {
            providers.Add(CreateSkillsProvider(definition));
        }

        if (CreateBackgroundAgentsProvider(definition, callableAgents) is { } backgroundAgents)
        {
            providers.Add(backgroundAgents);
        }

        if (BuildCompactionStrategy(definition) is { } compactionStrategy)
        {
            // MAAI001: CompactionProvider is marked "evaluation purposes only" -
            // the rationale is the same as the block above BuildCompactionStrategy.
#pragma warning disable MAAI001
            providers.Add(new CompactionProvider(compactionStrategy, stateKey: null, _loggerFactory));
#pragma warning restore MAAI001
        }

        providers.AddRange(CreateMemoryProviders(definition));

        if (CreateMcpResourceProvider(definition) is { } mcpResources)
        {
            providers.Add(mcpResources);
        }

        if (providers.Count > 0)
        {
            options.AIContextProviders = providers;
        }

        return chatClient.AsAIAgent(options, _loggerFactory, _services);
    }

    /// <summary>
    /// Builds the context provider for <see cref="AgentDefinition.McpResourceUris"/> (Mode A).
    /// </summary>
    /// <exception cref="AgentPrismCompilationException">
    /// The definition wants an MCP resource, but the <c>AgentPrism.Mcp</c> package is not registered.
    /// </exception>
    private AIContextProvider? CreateMcpResourceProvider(AgentDefinition definition)
    {
        if (definition.McpResourceUris.Count == 0)
        {
            return null;
        }

        if (_mcpResources is null)
        {
            throw new AgentPrismCompilationException(
                $"Agent '{definition.Name}' uses an MCP resource, but the AgentPrism.Mcp package " +
                "is not registered (UseMcp() was not called).")
            {
                AgentName = definition.Name,
            };
        }

        var tenantId = _tenantContext?.TenantId ?? definition.TenantId
            ?? throw new AgentPrismCompilationException(
                $"Agent '{definition.Name}' uses an MCP resource, but the tenant could not be resolved.")
            {
                AgentName = definition.Name,
            };

        return _mcpResources.Create(definition.McpResourceUris, tenantId);
    }

    /// <summary>
    /// Builds the context provider that enables sub-agent calls.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Microsoft Agent Framework's <see cref="BackgroundAgentsProvider"/> type
    /// is an <see cref="AIContextProvider"/>; it does not require a harness.
    /// The plain agent path therefore has first-class support for this - the
    /// harness defect recorded earlier would have hit this feature too.
    /// </para>
    /// <para>
    /// Every sub-agent is wrapped with <see cref="ChildAgentInvoker"/>. The
    /// provider calls the sub-agent with <c>options = null</c> (measured);
    /// tree information can only be added by the wrapper.
    /// </para>
    /// </remarks>
    // MAAI001: BackgroundAgentsProvider is marked "evaluation purposes only".
    // The suppression is a deliberate decision resting on the same rationale
    // as K-020: sub-agent setup is kept in a single method, only this file is
    // updated if MAF changes this API. Rationale: docs/KARARLAR.md, decision K-097.
#pragma warning disable MAAI001
    private BackgroundAgentsProvider? CreateBackgroundAgentsProvider(
        AgentDefinition definition,
        ResolvedCallableAgents callableAgents)
        => CreateChildAgents(definition, callableAgents) is { } build
            ? new BackgroundAgentsProvider(build.Children, new BackgroundAgentsProviderOptions { WaitTimeout = build.WaitTimeout })
            : null;
#pragma warning restore MAAI001

    /// <summary>Builds callable sub-agents together with their wrappers.</summary>
    /// <returns>
    /// The wrapped sub-agents and the resolved hard wait cutoff; <see langword="null"/>
    /// when the definition calls no sub-agent.
    /// </returns>
    /// <exception cref="AgentPrismCompilationException">
    /// The sub-agent resolver is not registered, or the resolved
    /// <see cref="SubAgentSettings.ChildDeadline"/>/<see cref="SubAgentSettings.WaitTimeout"/>
    /// combination is invalid (144.2).
    /// </exception>
    private ChildAgentsBuild? CreateChildAgents(AgentDefinition definition, ResolvedCallableAgents callableAgents)
    {
        if (callableAgents.Agents.Count == 0)
        {
            return null;
        }

        if (_callableAgents is null || _tenantContext is null)
        {
            throw new AgentPrismCompilationException(
                $"Agent '{definition.Name}' wants to call other agents, but the sub-agent " +
                "resolver is not registered.")
            {
                AgentName = definition.Name,
            };
        }

        var (childDeadline, waitTimeout) = ResolveSubAgentTimeouts(definition);

        var logger = _loggerFactory?.CreateLogger<ChildAgentInvoker>()
            ?? (ILogger)Microsoft.Extensions.Logging.Abstractions.NullLogger<ChildAgentInvoker>.Instance;

        var children = new List<AIAgent>(callableAgents.Agents.Count);

        foreach (var info in callableAgents.Agents)
        {
            children.Add(new ChildAgentInvoker(
                _callableAgents,
                _tenantContext,
                logger,
                definition.Name,
                info,
                childDeadline,
                waitTimeout,
                _timeProvider));
        }

        return new ChildAgentsBuild(children, waitTimeout);
    }

    /// <summary>
    /// Resolves the sub-agent wait limits for a definition: its own
    /// <see cref="AgentDefinition.SubAgents"/> override the tree-wide
    /// <see cref="AgentPrismAgentGraphOptions.ChildDeadline"/>/<see cref="AgentPrismAgentGraphOptions.WaitTimeout"/>
    /// defaults field by field, then the resolved pair is validated together
    /// — the same "resolve, then validate the whole" shape as
    /// <see cref="BuildCompactionStrategy"/>.
    /// </summary>
    /// <exception cref="AgentPrismCompilationException">
    /// The resolved deadline is not positive, or the resolved wait timeout is
    /// not strictly greater than the resolved deadline.
    /// </exception>
    // internal (not private): so the resolution order (agent override wins
    // field by field over the tree-wide default) can be tested directly, the
    // same reason BuildCompactionStrategy is internal.
    internal (TimeSpan ChildDeadline, TimeSpan WaitTimeout) ResolveSubAgentTimeouts(AgentDefinition definition)
    {
        var childDeadline = definition.SubAgents?.ChildDeadline ?? _agentGraph.ChildDeadline;
        var waitTimeout = definition.SubAgents?.WaitTimeout ?? _agentGraph.WaitTimeout;

        if (childDeadline <= TimeSpan.Zero)
        {
            throw new AgentPrismCompilationException(
                $"Agent '{definition.Name}' has an invalid sub-agent wait limit: " +
                $"{nameof(SubAgentSettings.ChildDeadline)} must be greater than zero. " +
                $"Actual value: {childDeadline}.")
            {
                AgentName = definition.Name,
            };
        }

        if (waitTimeout <= childDeadline)
        {
            throw new AgentPrismCompilationException(
                $"Agent '{definition.Name}' has an invalid sub-agent wait limit: " +
                $"{nameof(SubAgentSettings.WaitTimeout)} ({waitTimeout}) must be greater than " +
                $"{nameof(SubAgentSettings.ChildDeadline)} ({childDeadline}); otherwise the hard cutoff " +
                "would fire before the cooperative one ever gets a chance to take effect.")
            {
                AgentName = definition.Name,
            };
        }

        return (childDeadline, waitTimeout);
    }

    private HarnessAgent CompileHarnessAgent(
        AgentDefinition definition,
        IChatClient chatClient,
        ChatOptions chatOptions,
        ResolvedCallableAgents callableAgents)
    {
        var harness = definition.Harness!;

        // MAAI001: Microsoft Agent Framework's harness options are marked
        // "evaluation purposes only" and may change in the future. The
        // suppression is a deliberate decision: harness usage is kept in a
        // single file, so only this file is updated if MAF changes this API.
        // Rationale: docs/KARARLAR.md, decision K-020.
#pragma warning disable MAAI001
        var options = new HarnessAgentOptions
        {
            Id = definition.Name,
            Name = definition.Name,
            Description = definition.Description,
            ChatOptions = chatOptions,
            ChatHistoryProvider = _chatHistoryProvider,
            HarnessInstructions = harness.HarnessInstructions,
            MaxContextWindowTokens = harness.MaxContextWindowTokens,
            MaxOutputTokens = harness.MaxOutputTokens,
            MaximumIterationsPerRequest = harness.MaximumIterationsPerRequest,
            DisableCompaction = harness.DisableCompaction,
            DisableTodoProvider = harness.DisableTodoProvider,
            DisableFileMemory = harness.DisableFileMemory,
            DisableWebSearch = harness.DisableWebSearch,
            DisableToolAutoApproval = harness.DisableToolAutoApproval,
            DisableAgentSkillsProvider = harness.DisableAgentSkillsProvider,
            DisableAgentModeProvider = harness.DisableAgentModeProvider,

            // The harness produces its own internal spans. Without a source
            // name, these go to MAF's own source and AgentPrism's span store
            // never sees them; harness steps would be missing from the
            // waterfall view.
            OpenTelemetrySourceName = AgentPrismDiagnostics.ActivitySourceName,

            // FileAccessStore is INTENTIONALLY left unassigned: it activates
            // only when a value is assigned; leaving it unassigned means file
            // access is disabled. Rationale: docs/KARARLAR.md, decision K-062.
            //
            // BackgroundAgents was opened in Phase 12 and follows the same
            // rule: no value is assigned when the definition carries no agent
            // name, and the feature is off.
        };

        if (definition.SkillNames.Count > 0)
        {
            options.AgentSkillsSource = CreateSkillsSource(definition);
        }

        if (CreateChildAgents(definition, callableAgents) is { } build)
        {
            // 144.4: the option object is built by AgentPrism on BOTH compile
            // paths, so behavior never depends on what MAF's own null
            // semantics happen to be for BackgroundAgentsProviderOptions
            // (measured unassigned before this phase — see 144's plan).
            options.BackgroundAgents = build.Children;
            options.BackgroundAgentsProviderOptions = new BackgroundAgentsProviderOptions { WaitTimeout = build.WaitTimeout };
        }

        // Conflict check: if the user has both requested compaction/memory and
        // disabled the same capability in the harness, which one wins must not
        // silently stay ambiguous (K1 zero surprise).
        if (definition.Compaction is { Strategy: not CompactionStrategyKind.None })
        {
            if (harness.DisableCompaction)
            {
                throw new AgentPrismCompilationException(
                    $"Agent '{definition.Name}' wants compaction, but " +
                    $"{nameof(HarnessSettings)}.{nameof(HarnessSettings.DisableCompaction)} is turned off.")
                {
                    AgentName = definition.Name,
                };
            }

            options.CompactionStrategy = BuildCompactionStrategy(definition);
        }

        if (definition.Memory is { EnableFileMemory: true })
        {
            if (harness.DisableFileMemory)
            {
                throw new AgentPrismCompilationException(
                    $"Agent '{definition.Name}' wants file memory, but " +
                    $"{nameof(HarnessSettings)}.{nameof(HarnessSettings.DisableFileMemory)} is turned off.")
                {
                    AgentName = definition.Name,
                };
            }

            options.FileMemoryStore = RequireFileStore(definition);
        }

        if (definition.Memory is { EnableTodo: true } && harness.DisableTodoProvider)
        {
            throw new AgentPrismCompilationException(
                $"Agent '{definition.Name}' wants todo tracking, but " +
                $"{nameof(HarnessSettings)}.{nameof(HarnessSettings.DisableTodoProvider)} is turned off.")
            {
                AgentName = definition.Name,
            };
        }

        // When EnableTodo == true and DisableTodoProvider == false, nothing
        // extra is DONE: the harness already keeps todo tracking on by default.

        var harnessProviders = new List<AIContextProvider>(2);

        if (CreateTextSearchProvider(definition) is { } textSearch)
        {
            harnessProviders.Add(textSearch);
        }

        if (CreateMcpResourceProvider(definition) is { } mcpResources)
        {
            harnessProviders.Add(mcpResources);
        }

        if (harnessProviders.Count > 0)
        {
            options.AIContextProviders = harnessProviders;
        }

        // The loop (Phase 151) is OFF unless the definition asks for it. While
        // Harness.Loop is null neither option is assigned, and MAF builds the
        // agent exactly as it did before this member existed — the same
        // opt-in-by-assignment rule FileAccessStore follows (K-062).
        //
        // AgentPrism hands MAF ONE evaluator, not the definition's list:
        // RecordingLoopEvaluator runs the criteria in MAF's own order and
        // writes a single LoopIterationCompleted event per iteration. Handing
        // MAF the list instead would leave the run record with no evidence of
        // the loop at all.
        if (harness.Loop is not null)
        {
            var (loopEvaluator, loopOptions) = _loopEvaluators.Build(definition);

            options.LoopEvaluators = [loopEvaluator];
            options.LoopAgentOptions = loopOptions;
        }

        return chatClient.AsHarnessAgent(options, _loggerFactory, _services);
#pragma warning restore MAAI001
    }
}

/// <summary>The sub-agent wrappers built for a definition, together with their resolved hard wait cutoff.</summary>
internal sealed record ChildAgentsBuild(List<AIAgent> Children, TimeSpan WaitTimeout);
