using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Compaction;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace Tracon;

/// <summary>
/// Chat agent, child agent (sub-agent), and harness agent production.
/// </summary>
public sealed partial class AgentDefinitionCompiler
{
    private AIAgent CompileChatAgent(
        AgentDefinition definition,
        PendingChatClient pendingClient,
        SecondaryChatClients secondaryClients,
        ChatOptions chatOptions,
        ChildAgentsBuild? backgroundAgents)
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

        if (CreateBackgroundAgentsProvider(backgroundAgents) is { } backgroundAgentsProvider)
        {
            providers.Add(backgroundAgentsProvider);
        }

        if (BuildCompactionStrategy(definition, secondaryClients.Summarization) is { } compactionStrategy)
        {
            // MAAI001: CompactionProvider is marked "evaluation purposes only" -
            // the rationale is the same as the block above BuildCompactionStrategy.
#pragma warning disable MAAI001
            providers.Add(new CompactionProvider(compactionStrategy, stateKey: null, _loggerFactory));
#pragma warning restore MAAI001
        }

        providers.AddRange(CreateMemoryProviders(definition));

        if (CreateWorkspaceProvider(definition) is { } workspace)
        {
            providers.Add(workspace);
        }

        if (CreateMcpResourceProvider(definition) is { } mcpResources)
        {
            providers.Add(mcpResources);
        }

        if (providers.Count > 0)
        {
            options.AIContextProviders = providers;
        }

        // Phase 222: the one point where an extension reaches the options, after
        // Tracon has set every member the definition owns and before MAF reads them.
        // Phase 232: the model client is built after it, with the extensions' rings.
        var extensions = _agentExtensions.Configure(
            definition, ResolveTenantId(definition), _services ?? NoServices.Instance, chatOptions: options, harnessOptions: null);
        var client = BuildChatClient(definition, pendingClient, extensions.Rings);

        return AgentExtensionRegistry.Wrap(
            AgentExtensionRegistry.Build(() => client.AsAIAgent(options, _loggerFactory, _services), definition),
            extensions.Wrappers,
            definition.Name);
    }

    /// <summary>The tenant an agent is compiled for: the current tenant, then the definition's own, then the default.</summary>
    private string ResolveTenantId(AgentDefinition definition)
        => _tenantContext?.TenantId ?? definition.TenantId ?? FixedTenantContext.Default.TenantId;

    /// <summary>
    /// Builds the context provider for <see cref="AgentDefinition.McpResourceUris"/> (Mode A).
    /// </summary>
    /// <exception cref="TraconCompilationException">
    /// The definition wants an MCP resource, but the <c>Tracon.Mcp</c> package is not registered.
    /// </exception>
    private AIContextProvider? CreateMcpResourceProvider(AgentDefinition definition)
    {
        if (definition.McpResourceUris.Count == 0)
        {
            return null;
        }

        if (_mcpResources is null)
        {
            throw new TraconCompilationException(
                $"Agent '{definition.Name}' uses an MCP resource, but the Tracon.Mcp package " +
                "is not registered (UseMcp() was not called).")
            {
                AgentName = definition.Name,
            };
        }

        var tenantId = _tenantContext?.TenantId ?? definition.TenantId
            ?? throw new TraconCompilationException(
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
    private static BackgroundAgentsProvider? CreateBackgroundAgentsProvider(ChildAgentsBuild? build)
        => build is not null
            ? new BackgroundAgentsProvider(build.Children, new BackgroundAgentsProviderOptions { WaitTimeout = build.WaitTimeout })
            : null;
#pragma warning restore MAAI001

    /// <summary>Throws when two tools of a Tool mode agent would share a name.</summary>
    /// <exception cref="TraconCompilationException">
    /// A callable agent becomes a tool whose name another tool of the agent already has.
    /// </exception>
    private static void ThrowOnSubAgentToolConflict(AgentDefinition definition)
    {
        // The rule the validator reports with a path: a definition saved over HTTP passed it,
        // and a definition written in code never met the validator.
        if (SubAgentToolNames.FindProblems(definition) is { Count: > 0 } problems)
        {
            throw new TraconCompilationException(
                $"Agent '{definition.Name}' has callable agents whose tool names collide: " +
                string.Join(" ", problems.Select(static problem => problem.Message)))
            {
                AgentName = definition.Name,
            };
        }
    }

    /// <summary>Builds callable sub-agents together with their wrappers.</summary>
    /// <returns>
    /// The wrapped sub-agents and the resolved hard wait cutoff; <see langword="null"/>
    /// when the definition calls no sub-agent.
    /// </returns>
    /// <exception cref="TraconCompilationException">
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
            throw new TraconCompilationException(
                $"Agent '{definition.Name}' wants to call other agents, but the sub-agent " +
                "resolver is not registered.")
            {
                AgentName = definition.Name,
            };
        }

        var (childDeadline, waitTimeout) = ResolveSubAgentTimeouts(definition);

        var logger = _loggerFactory?.CreateLogger<ChildAgentInvoker>()
            ?? (ILogger)Microsoft.Extensions.Logging.Abstractions.NullLogger<ChildAgentInvoker>.Instance;

        // The pin is the definition's own: the infos were described from it, and an info list handed to a
        // Compile overload by a caller is matched to the definition by name.
        var pins = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var reference in definition.CallableAgents)
        {
            if (reference.Version is { } version)
            {
                pins[reference.Name] = version;
            }
        }

        var children = new List<AIAgent>(callableAgents.Agents.Count);

        foreach (var info in callableAgents.Agents)
        {
            // Every call mode builds its children here, so the version the callee runs is the same in all of
            // them. The wrapper resolves late and the version is passed on every call: a pinned callee keeps its
            // version across edits of the callee, and an unpinned one runs the current version.
            children.Add(new ChildAgentInvoker(
                _callableAgents,
                _tenantContext,
                logger,
                definition.Name,
                info,
                childDeadline,
                waitTimeout,
                _timeProvider,
                pinnedVersion: pins.TryGetValue(info.Name, out var pinned) ? pinned : null));
        }

        return new ChildAgentsBuild(children, waitTimeout);
    }

    /// <summary>
    /// Gives the loop the way to call an <c>agentReview</c> criterion's reviewer: through the same
    /// <see cref="ChildAgentInvoker"/> every sub-agent call uses, so the reviewer is a child run bound by
    /// the tree's tenant, depth, budget, quota and wait limits.
    /// </summary>
    /// <param name="definition">The definition being compiled.</param>
    /// <returns>
    /// The factory. Nothing is built until a criterion asks: a definition with no <c>agentReview</c>
    /// criterion never calls it, and a host without a sub-agent resolver fails only for one that has.
    /// </returns>
    /// <remarks>
    /// The reviewer shares the wait limits of the agent's sub-agent calls
    /// (<see cref="ResolveSubAgentTimeouts"/>), and a refusal fails the call
    /// (<see cref="ChildRefusalMode.Fail"/>): no model reads the reviewer's answer as a tool result, the
    /// loop does, and a refusal read as a verdict would approve or reject the draft by accident.
    /// </remarks>
    private ReviewerInvokerFactory CreateReviewerInvokerFactory(AgentDefinition definition)
        => (reviewerName, reviewerVersion) =>
        {
            if (_callableAgents is null || _tenantContext is null)
            {
                throw new TraconCompilationException(
                    $"Agent '{definition.Name}' has an 'agentReview' loop criterion, but the sub-agent " +
                    "resolver is not registered.")
                {
                    AgentName = definition.Name,
                };
            }

            var (childDeadline, waitTimeout) = ResolveSubAgentTimeouts(definition);

            return new ChildAgentInvoker(
                _callableAgents,
                _tenantContext,
                _loggerFactory?.CreateLogger<ChildAgentInvoker>()
                    ?? (ILogger)Microsoft.Extensions.Logging.Abstractions.NullLogger<ChildAgentInvoker>.Instance,
                definition.Name,
                new CallableAgentInfo(reviewerName, Description: null, Version: 0),
                childDeadline,
                waitTimeout,
                _timeProvider,
                ChildRefusalMode.Fail,
                deadlineSetting: definition.SubAgents?.ChildDeadline is null
                    ? null
                    : "the agent's own 'SubAgents.ChildDeadline'",
                pinnedVersion: reviewerVersion,
                callKind: "loop reviewer");
        };

    /// <summary>
    /// Resolves the sub-agent wait limits for a definition: its own
    /// <see cref="AgentDefinition.SubAgents"/> override the tree-wide
    /// <see cref="TraconAgentGraphOptions.ChildDeadline"/>/<see cref="TraconAgentGraphOptions.WaitTimeout"/>
    /// defaults field by field, then the resolved pair is validated together
    /// — the same "resolve, then validate the whole" shape as
    /// <see cref="BuildCompactionStrategy"/>.
    /// </summary>
    /// <exception cref="TraconCompilationException">
    /// A resolved value is outside the timer range (one millisecond to about 49.7
    /// days), or the resolved wait timeout is not strictly greater than the
    /// resolved deadline.
    /// </exception>
    // internal (not private): so the resolution order (agent override wins
    // field by field over the tree-wide default) can be tested directly, the
    // same reason BuildCompactionStrategy is internal.
    internal (TimeSpan ChildDeadline, TimeSpan WaitTimeout) ResolveSubAgentTimeouts(AgentDefinition definition)
    {
        var childDeadline = definition.SubAgents?.ChildDeadline ?? _agentGraph.ChildDeadline;
        var waitTimeout = definition.SubAgents?.WaitTimeout ?? _agentGraph.WaitTimeout;

        // F-280: both values arm CancelAfter in every delegation, which refuses more
        // than about 49.7 days there - after ChildRunStarted was already written. A
        // definition saved over HTTP carries its own values, so the check lives here
        // and not only in the startup validator.
        foreach (var (field, value) in new[]
                 {
                     (nameof(SubAgentSettings.ChildDeadline), childDeadline),
                     (nameof(SubAgentSettings.WaitTimeout), waitTimeout),
                 })
        {
            if (!TimerPeriod.IsValid(value))
            {
                throw new TraconCompilationException(
                    $"Agent '{definition.Name}' has an invalid sub-agent wait limit: " +
                    TimerPeriod.Failure(field, value))
                {
                    AgentName = definition.Name,
                };
            }
        }

        if (waitTimeout <= childDeadline)
        {
            throw new TraconCompilationException(
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

    private AIAgent CompileHarnessAgent(
        AgentDefinition definition,
        PendingChatClient pendingClient,
        SecondaryChatClients secondaryClients,
        ChatOptions chatOptions,
        ChildAgentsBuild? backgroundAgents)
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

            // F-339 · F-315 (K-892): MAF turns these three on by itself, and
            // each one falls back to something no definition asked for. With
            // no FileMemoryStore it writes model content under the process's
            // working directory (./agent-file-memory) — outside the tenant
            // stores, retention and erasure; with no AgentSkillsSource it reads
            // SKILL.md files from the working directory, past the SkillNames
            // filter; and it adds the provider's paid hosted web search, which
            // the Chat Completions surface refuses with a 400. File memory and
            // web search are on only when the definition turns them on.
            //
            // F-578: MAF's own skill provider is never built. Tracon builds the
            // provider for the named skills (CreateSkillsProvider) and passes it
            // in AIContextProviders, as it does for a plain agent, so one chain
            // serves both and the working directory is never scanned.
            DisableFileMemory = harness.DisableFileMemory || definition.Memory is not { EnableFileMemory: true },
            DisableWebSearch = !harness.EnableWebSearch,
            DisableAgentSkillsProvider = true,
            DisableToolAutoApproval = harness.DisableToolAutoApproval,
            DisableAgentModeProvider = harness.DisableAgentModeProvider,

            // The harness produces its own internal spans. Without a source
            // name, these go to MAF's own source and Tracon's span store
            // never sees them; harness steps would be missing from the
            // waterfall view.
            OpenTelemetrySourceName = TraconDiagnostics.ActivitySourceName,

            // FileAccessStore is INTENTIONALLY left unassigned: it activates
            // only when a value is assigned, and the file tools it builds would
            // run outside the Tracon tool chain. The session workspace (Phase
            // 234) adds its own wrapped provider below instead.
            // Rationale: docs/KARARLAR.md, decisions K-062 and K-1069.
            //
            // BackgroundAgents was opened in Phase 12 and follows the same
            // rule: no value is assigned when the definition carries no agent
            // name, and the feature is off.
        };

        AgentSkillsProvider? skillsProvider = null;

        if (definition.SkillNames.Count > 0)
        {
            // The setting turns the skills off, so the named skills would be
            // dropped without a word (K1 zero surprise).
            if (harness.DisableAgentSkillsProvider)
            {
                throw new TraconCompilationException(
                    $"Agent '{definition.Name}' names skills, but " +
                    $"{nameof(HarnessSettings)}.{nameof(HarnessSettings.DisableAgentSkillsProvider)} is turned off.")
                {
                    AgentName = definition.Name,
                };
            }

            skillsProvider = CreateSkillsProvider(definition);
        }

        if (harness is { EnableWebSearch: true, DisableWebSearch: true })
        {
            throw new TraconCompilationException(
                $"Agent '{definition.Name}' turns web search on with " +
                $"{nameof(HarnessSettings)}.{nameof(HarnessSettings.EnableWebSearch)} and off with " +
                $"{nameof(HarnessSettings)}.{nameof(HarnessSettings.DisableWebSearch)}.")
            {
                AgentName = definition.Name,
            };
        }

        if (backgroundAgents is { } build)
        {
            // 144.4: the option object is built by Tracon on BOTH compile
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
                throw new TraconCompilationException(
                    $"Agent '{definition.Name}' wants compaction, but " +
                    $"{nameof(HarnessSettings)}.{nameof(HarnessSettings.DisableCompaction)} is turned off.")
                {
                    AgentName = definition.Name,
                };
            }

            options.CompactionStrategy = BuildCompactionStrategy(definition, secondaryClients.Summarization);
        }

        if (definition.Memory is { EnableFileMemory: true })
        {
            if (harness.DisableFileMemory)
            {
                throw new TraconCompilationException(
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
            throw new TraconCompilationException(
                $"Agent '{definition.Name}' wants todo tracking, but " +
                $"{nameof(HarnessSettings)}.{nameof(HarnessSettings.DisableTodoProvider)} is turned off.")
            {
                AgentName = definition.Name,
            };
        }

        // When EnableTodo == true and DisableTodoProvider == false, nothing
        // extra is DONE: the harness already keeps todo tracking on by default.

        var harnessProviders = new List<AIContextProvider>(3);

        if (skillsProvider is not null)
        {
            harnessProviders.Add(skillsProvider);
        }

        if (CreateTextSearchProvider(definition) is { } textSearch)
        {
            harnessProviders.Add(textSearch);
        }

        // Phase 234: the harness builds its own FileAccessProvider only when FileAccessStore is assigned
        // (measured, MAF 1.25). Tracon leaves it unassigned and adds its own wrapped provider here, as
        // it does for skills, so the file tools run through the Tracon tool chain.
        if (CreateWorkspaceProvider(definition) is { } workspace)
        {
            harnessProviders.Add(workspace);
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
        // Tracon hands MAF ONE evaluator, not the definition's list:
        // RecordingLoopEvaluator runs the criteria in MAF's own order and
        // writes a single LoopIterationCompleted event per iteration. Handing
        // MAF the list instead would leave the run record with no evidence of
        // the loop at all.
        if (harness.Loop is not null)
        {
            var (loopEvaluator, loopOptions) = _loopEvaluators.Build(
                definition, secondaryClients.LoopJudge, CreateReviewerInvokerFactory(definition));

            options.LoopEvaluators = [loopEvaluator];
            options.LoopAgentOptions = loopOptions;
        }

        // Phase 222: the one point where an extension reaches the options, after
        // Tracon has set every member the definition owns and before MAF reads them.
        // Phase 232: the model client is built after it, with the extensions' rings.
        var extensions = _agentExtensions.Configure(
            definition, ResolveTenantId(definition), _services ?? NoServices.Instance, chatOptions: null, harnessOptions: options);
        var client = BuildChatClient(definition, pendingClient, extensions.Rings);

        return AgentExtensionRegistry.Wrap(
            AgentExtensionRegistry.Build(() => new HarnessLoopBoundaryChatClient(client).AsHarnessAgent(options, _loggerFactory, _services), definition),
            extensions.Wrappers,
            definition.Name);
#pragma warning restore MAAI001
    }
}

/// <summary>The sub-agent wrappers built for a definition, together with their resolved hard wait cutoff.</summary>
internal sealed record ChildAgentsBuild(List<AIAgent> Children, TimeSpan WaitTimeout);
