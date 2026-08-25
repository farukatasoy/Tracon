using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace AgentPrism;

/// <summary>
/// Converts an <see cref="AgentDefinition"/> definition into an executable
/// <see cref="AIAgent"/> instance.
/// </summary>
/// <remarks>
/// <para>The conversion goes through these steps:</para>
/// <list type="number">
/// <item>
/// <description><see cref="ModelBinding"/> → <see cref="IModelProviderRegistry"/> → <see cref="IChatClient"/></description>
/// </item>
/// <item>
/// <description><see cref="AgentDefinition.ToolNames"/> → <see cref="IToolRegistry"/> → list of <see cref="AIFunction"/></description>
/// </item>
/// <item>
/// <description><c>AsHarnessAgent</c> when <see cref="AgentDefinition.Harness"/> is set, otherwise <c>AsAIAgent</c></description>
/// </item>
/// </list>
/// <para>
/// An unknown tool name results in an <see cref="AgentPrismCompilationException"/>.
/// It is not silently skipped: an agent running with a missing tool is an
/// agent that does not do the work the user expects.
/// </para>
/// </remarks>
/// <remarks>
/// This is the public orchestration entry point. Dependency resolution, chat
/// option/tool building, compaction/memory setup, chat/harness agent
/// production, and skill source production each live in their own
/// <c>partial</c> file under this same type - see
/// <c>AgentDefinitionCompiler.Dependencies.cs</c>,
/// <c>AgentDefinitionCompiler.ChatOptions.cs</c>,
/// <c>AgentDefinitionCompiler.Compaction.cs</c>,
/// <c>AgentDefinitionCompiler.Memory.cs</c>,
/// <c>AgentDefinitionCompiler.Agents.cs</c>, and
/// <c>AgentDefinitionCompiler.Skills.cs</c>.
/// </remarks>
public sealed partial class AgentDefinitionCompiler
{
    private readonly IModelProviderRegistry _models;
    private readonly IToolRegistry _tools;
    private readonly ILoggerFactory? _loggerFactory;
    private readonly IServiceProvider? _services;
    private readonly ChatHistoryProvider? _chatHistoryProvider;
    private readonly AgentSkillCatalog? _skills;
    private readonly SkillScriptSupport? _scripts;
    private readonly CallableAgentResolver? _callableAgents;
    private readonly ITenantContext? _tenantContext;
    private readonly ModelBinding? _utilityModel;
    private readonly IMcpResourceContextProviderFactory? _mcpResources;
    private readonly IVectorSearchStore? _vectorSearchStore;
    private readonly IEmbeddingGenerator<string, Embedding<float>>? _embeddingGenerator;
    private readonly int _knowledgeMaxResults;
    private readonly IAgentDefinitionStore? _definitionStore;

    // MAAI001: Microsoft.Agents.AI.AgentFileStore is marked "evaluation
    // purposes only". The suppression is kept in a single file (this file,
    // continuing the K-020 pattern); only this file is updated if MAF changes this API.
#pragma warning disable MAAI001
    private readonly AgentFileStore? _fileStore;
#pragma warning restore MAAI001

    /// <summary>Creates a new compiler.</summary>
    /// <param name="models">Model provider registry.</param>
    /// <param name="tools">Tool registry.</param>
    /// <param name="loggerFactory">Logger factory passed to the produced agents.</param>
    /// <param name="services">Service provider passed to the produced agents.</param>
    /// <param name="chatHistoryProvider">
    /// Chat history provider attached to the produced agents. When <see langword="null"/>,
    /// Microsoft Agent Framework's in-memory default is used and history is
    /// carried inside the session state.
    /// </param>
    /// <param name="skills">Catalog that resolves skill sources.</param>
    /// <param name="scripts">
    /// Skill script support. When <see langword="null"/>, no script can run;
    /// the feature is turned on with <c>UseSkillScripts</c>.
    /// </param>
    /// <param name="callableAgents">
    /// Resolver for callable sub-agents. When <see langword="null"/>, no agent
    /// can call another agent.
    /// </param>
    /// <param name="tenantContext">
    /// Tenant context. Used to verify that sub-calls do not change tenant.
    /// </param>
    /// <param name="utilityModel">
    /// Default model for summarization in context compaction. Used when an
    /// agent does not supply its own <c>CompactionSettings.SummarizationModel</c>;
    /// when <see langword="null"/>, falls back to the agent's own model.
    /// </param>
    /// <param name="fileStore">
    /// File store used by memory providers. When <see langword="null"/>, a
    /// definition requesting <c>MemorySettings.EnableFileMemory</c>/<c>EnableTextSearch</c>
    /// gets a compilation error.
    /// </param>
    /// <param name="mcpResources">
    /// Factory that builds the context provider for
    /// <see cref="AgentDefinition.McpResourceUris"/> (Mode A). When <see langword="null"/>,
    /// a definition requesting an MCP resource gets a compilation error.
    /// </param>
    /// <param name="vectorSearchStore">
    /// Semantic search store. When <see langword="null"/>, a
    /// definition requesting <c>MemorySettings.EnableVectorSearch</c> gets a
    /// compilation error.
    /// </param>
    /// <param name="embeddingGenerator">
    /// Embedding generator. When <see langword="null"/>, a
    /// definition requesting <c>MemorySettings.EnableVectorSearch</c> gets a
    /// compilation error.
    /// </param>
    /// <param name="knowledgeMaxResults">
    /// Maximum number of results the <c>search_knowledge</c> tool returns.
    /// </param>
    /// <param name="definitionStore">
    /// The definition store, used to resolve <see cref="AgentDefinition.SharedInstructionsName"/>.
    /// When <see langword="null"/>, a definition that references a shared
    /// instructions block gets a compilation error.
    /// </param>
    /// <exception cref="ArgumentNullException">One of the required dependencies is <see langword="null"/>.</exception>
#pragma warning disable MAAI001 // AgentFileStore — see the rationale on the _fileStore field.
    public AgentDefinitionCompiler(
        IModelProviderRegistry models,
        IToolRegistry tools,
        ILoggerFactory? loggerFactory = null,
        IServiceProvider? services = null,
        ChatHistoryProvider? chatHistoryProvider = null,
        AgentSkillCatalog? skills = null,
        SkillScriptSupport? scripts = null,
        CallableAgentResolver? callableAgents = null,
        ITenantContext? tenantContext = null,
        ModelBinding? utilityModel = null,
        AgentFileStore? fileStore = null,
        IMcpResourceContextProviderFactory? mcpResources = null,
        IVectorSearchStore? vectorSearchStore = null,
        IEmbeddingGenerator<string, Embedding<float>>? embeddingGenerator = null,
        int knowledgeMaxResults = 5,
        IAgentDefinitionStore? definitionStore = null)
#pragma warning restore MAAI001
    {
        ArgumentNullException.ThrowIfNull(models);
        ArgumentNullException.ThrowIfNull(tools);

        _models = models;
        _tools = tools;
        _loggerFactory = loggerFactory;
        _services = services;
        _chatHistoryProvider = chatHistoryProvider;
        _skills = skills;
        _scripts = scripts;
        _callableAgents = callableAgents;
        _tenantContext = tenantContext;
        _utilityModel = utilityModel;
        _fileStore = fileStore;
        _mcpResources = mcpResources;
        _vectorSearchStore = vectorSearchStore;
        _embeddingGenerator = embeddingGenerator;
        _knowledgeMaxResults = knowledgeMaxResults;
        _definitionStore = definitionStore;
    }

    /// <summary>Converts a definition into an executable agent.</summary>
    /// <param name="definition">The definition to compile.</param>
    /// <returns>The executable agent.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="definition"/> is <see langword="null"/>.</exception>
    /// <exception cref="AgentPrismCompilationException">
    /// The model provider cannot be found, or the definition references a tool name that is not registered.
    /// </exception>
    public AIAgent Compile(AgentDefinition definition) => Compile(definition, ResolvedCallableAgents.Empty);

    /// <summary>Converts a definition, together with its resolved sub-agents, into an executable agent.</summary>
    /// <param name="definition">The definition to compile.</param>
    /// <param name="callableAgents">
    /// Sub-agent summaries resolved beforehand via <see cref="ResolveCallableAgentsAsync"/>.
    /// </param>
    /// <returns>The executable agent.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="definition"/> is <see langword="null"/>.</exception>
    /// <exception cref="AgentPrismCompilationException">
    /// The model provider cannot be found, or the definition references a tool name that is not registered.
    /// </exception>
    public AIAgent Compile(AgentDefinition definition, ResolvedCallableAgents callableAgents)
        => Compile(definition, callableAgents, toolTransform: null, culture: null);

    /// <summary>
    /// Compiles a definition together with its resolved sub-agents, transforming its tools.
    /// </summary>
    /// <param name="definition">The definition to compile.</param>
    /// <param name="callableAgents">
    /// Sub-agent summaries resolved beforehand via <see cref="ResolveCallableAgentsAsync"/>.
    /// </param>
    /// <param name="toolTransform">
    /// Transform applied to every tool resolved from the registry. When <see langword="null"/>,
    /// tools are bound as-is.
    /// </param>
    /// <param name="culture">
    /// The requested culture, resolved against <see cref="AgentDefinition.InstructionsByCulture"/>
    /// (see <see cref="InstructionCultureResolver"/>). <see langword="null"/> uses
    /// <see cref="AgentDefinition.Instructions"/> unconditionally.
    /// </param>
    /// <returns>The executable agent.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="definition"/> is <see langword="null"/>.</exception>
    /// <exception cref="AgentPrismCompilationException">
    /// The model provider cannot be found, or the definition references a tool name that is not registered.
    /// </exception>
    /// <remarks>
    /// <para>
    /// The transform exists for replay
    /// (<see cref="ReplayToolMode.ReplayTools"/>): a <c>DelegatingAIFunction</c>
    /// that replays recorded tool results preserves the wrapped tool's name,
    /// description, and JSON schema - the model sees the tools
    /// <em>exactly as before</em> but no body actually runs.
    /// </para>
    /// <para>
    /// The transform applies only to tools resolved from the
    /// <see cref="IToolRegistry"/> registry. Tools opened by skills and
    /// callable sub-agents come through an <c>AIContextProvider</c> and do not
    /// pass through here; the caller must disable them <em>at the definition
    /// level</em> instead.
    /// </para>
    /// <para>
    /// The compiled agent cache (<see cref="CompiledAgentCache"/>) is
    /// <strong>not used</strong> for this path: the transform changes per
    /// call, and caching a replay agent would also break normal runs.
    /// </para>
    /// </remarks>
    public AIAgent Compile(
        AgentDefinition definition,
        ResolvedCallableAgents callableAgents,
        Func<AIFunction, AIFunction>? toolTransform,
        string? culture = null)
    {
        ArgumentNullException.ThrowIfNull(definition);

        return BuildAgent(definition, callableAgents, toolTransform, culture, CreateChatClient(definition), sharedInstructions: null);
    }

    /// <summary>Converts a definition into an executable agent (BYOK).</summary>
    /// <param name="definition">The definition to compile.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The executable agent.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="definition"/> is <see langword="null"/>.</exception>
    /// <exception cref="AgentPrismCompilationException">
    /// The model provider cannot be found, the definition references a tool name that is not
    /// registered, the current tenant's egress policy forbids the provider, or the tenant's
    /// provider binding cannot be resolved to a credential value.
    /// </exception>
    /// <remarks>
    /// The async counterpart of <see cref="Compile(AgentDefinition)"/>: resolves the
    /// requesting tenant's own provider credential and egress policy
    /// (<see cref="IModelProviderRegistry.CreateChatClientAsync"/>) before building the client.
    /// When no tenant context or provider binding is registered, behavior is identical
    /// to the sync overload.
    /// </remarks>
    public ValueTask<AIAgent> CompileAsync(AgentDefinition definition, CancellationToken cancellationToken)
        => CompileAsync(definition, ResolvedCallableAgents.Empty, culture: null, cancellationToken);

    /// <summary>
    /// Converts a definition, together with its resolved sub-agents, into an executable agent
    /// (BYOK).
    /// </summary>
    /// <param name="definition">The definition to compile.</param>
    /// <param name="callableAgents">
    /// Sub-agent summaries resolved beforehand via <see cref="ResolveCallableAgentsAsync"/>.
    /// </param>
    /// <param name="culture">
    /// See <see cref="Compile(AgentDefinition, ResolvedCallableAgents, Func{AIFunction,
    /// AIFunction}, string)"/>.
    /// </param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The executable agent.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="definition"/> is <see langword="null"/>.</exception>
    /// <exception cref="AgentPrismCompilationException">
    /// See <see cref="CompileAsync(AgentDefinition, CancellationToken)"/>.
    /// </exception>
    public ValueTask<AIAgent> CompileAsync(
        AgentDefinition definition,
        ResolvedCallableAgents callableAgents,
        string? culture,
        CancellationToken cancellationToken)
        => CompileAsync(definition, callableAgents, toolTransform: null, culture, cancellationToken);

    /// <summary>
    /// Compiles a definition together with its resolved sub-agents, transforming its tools
    /// (BYOK).
    /// </summary>
    /// <param name="definition">The definition to compile.</param>
    /// <param name="callableAgents">
    /// Sub-agent summaries resolved beforehand via <see cref="ResolveCallableAgentsAsync"/>.
    /// </param>
    /// <param name="toolTransform">
    /// Transform applied to every tool resolved from the registry. See
    /// <see cref="Compile(AgentDefinition, ResolvedCallableAgents, Func{AIFunction, AIFunction}, string)"/>.
    /// </param>
    /// <param name="culture">
    /// See <see cref="Compile(AgentDefinition, ResolvedCallableAgents, Func{AIFunction, AIFunction}, string)"/>.
    /// </param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The executable agent.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="definition"/> is <see langword="null"/>.</exception>
    /// <exception cref="AgentPrismCompilationException">
    /// See <see cref="CompileAsync(AgentDefinition, CancellationToken)"/>.
    /// </exception>
    public async ValueTask<AIAgent> CompileAsync(
        AgentDefinition definition,
        ResolvedCallableAgents callableAgents,
        Func<AIFunction, AIFunction>? toolTransform,
        string? culture,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(definition);

        var chatClient = await CreateChatClientAsync(definition, cancellationToken).ConfigureAwait(false);
        var sharedInstructions = await ResolveSharedInstructionsAsync(definition, cancellationToken).ConfigureAwait(false);

        return BuildAgent(definition, callableAgents, toolTransform, culture, chatClient, sharedInstructions.Text);
    }

    /// <summary>
    /// Binds a parameterized definition's instructions for a single set of
    /// values and compiles it, bypassing <see cref="CompiledAgentCache"/>.
    /// </summary>
    /// <param name="definition">
    /// The source definition. Its <see cref="AgentDefinition.Parameters"/>
    /// schema is validated against <paramref name="values"/> the same way
    /// <c>AgentParameterValidator.ValidateValues</c> validates a run request -
    /// callers that already ran that check (an HTTP endpoint) do not need to
    /// repeat it; a caller that has not (an eval case) should call it first,
    /// since this method does not itself report which parameter was missing.
    /// </param>
    /// <param name="culture">The culture to resolve instructions with before binding.</param>
    /// <param name="values">The parameter values for this one compilation.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The compiled agent.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="definition"/> is <see langword="null"/>.</exception>
    /// <exception cref="AgentPrismCompilationException">
    /// See <see cref="CompileAsync(AgentDefinition, CancellationToken)"/>.
    /// </exception>
    /// <remarks>
    /// <para>
    /// The bound text is written into a definition <em>copy</em>:
    /// <see cref="AgentDefinition.InstructionsByCulture"/> is cleared (culture
    /// resolution already happened, once, right here) and
    /// <see cref="AgentDefinition.Parameters"/> is cleared (the copy's text no
    /// longer contains template placeholders, so there is nothing left for
    /// the compiler's schema check to validate).
    /// </para>
    /// <para>
    /// Never cached: two compilations of the same definition with different
    /// parameter values must never share one compiled instance, and
    /// <see cref="CompiledAgentCache"/> has no notion of "value" in its key -
    /// the same reason a tenant-specific provider credential (BYOK) bypasses it.
    /// </para>
    /// </remarks>
    public async ValueTask<AIAgent> CompileParameterizedAsync(
        AgentDefinition definition,
        string? culture,
        IReadOnlyDictionary<string, string>? values,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(definition);

        var boundInstructions = InstructionParameterBinder.Bind(
            InstructionCultureResolver.Resolve(definition, culture),
            definition.Parameters,
            values);

        var boundDefinition = definition with
        {
            Instructions = boundInstructions,
            InstructionsByCulture = null,
            Parameters = [],
        };

        var callable = await ResolveCallableAgentsAsync(boundDefinition, cancellationToken).ConfigureAwait(false);

        return await CompileAsync(boundDefinition, callable, culture: null, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Reports whether compiling <paramref name="binding"/> for the current
    /// tenant would bake a tenant-specific provider credential into the
    /// resulting chat client (BYOK).
    /// </summary>
    /// <param name="binding">The model binding to check — usually an <see cref="AgentDefinition.Model"/>.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>
    /// <see langword="true"/> if the caller must not hand the compiled agent
    /// to <see cref="CompiledAgentCache"/>: see
    /// <see cref="IModelProviderRegistry.HasTenantProviderOverrideAsync"/> for
    /// the full rationale.
    /// </returns>
    public ValueTask<bool> UsesTenantProviderOverrideAsync(ModelBinding binding, CancellationToken cancellationToken = default)
        => _models.HasTenantProviderOverrideAsync(binding, cancellationToken);

    /// <summary>Compiles a definition and stores it in the supplied cache when it is safe to do so.</summary>
    /// <param name="definition">The definition to compile.</param>
    /// <param name="cache">The cache that owns compiled agents for this source.</param>
    /// <param name="tenantId">The tenant whose compilation cache is used.</param>
    /// <param name="culture">The requested instruction culture.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The executable agent.</returns>
    public async ValueTask<AIAgent> CompileCachedAsync(
        AgentDefinition definition,
        CompiledAgentCache cache,
        string tenantId,
        string? culture = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);

        var dependencies = await ResolveDependenciesAsync(definition, cancellationToken).ConfigureAwait(false);

        if (dependencies.BypassCache)
        {
            return await CompileAsync(definition, dependencies.CallableAgents, culture, cancellationToken).ConfigureAwait(false);
        }

        return await cache.GetOrAddAsync(
            tenantId,
            definition.Name,
            definition.Version,
            dependencies.CacheFingerprint,
            culture ?? string.Empty,
            () => CompileAsync(definition, dependencies.CallableAgents, culture, cancellationToken)).ConfigureAwait(false);
    }

    private AIAgent BuildAgent(
        AgentDefinition definition,
        ResolvedCallableAgents callableAgents,
        Func<AIFunction, AIFunction>? toolTransform,
        string? culture,
        IChatClient chatClient,
        string? sharedInstructions)
    {
        // 🚨 The synchronous Compile() overloads never resolve
        // SharedInstructionsName (resolving it needs an async store read) — a
        // definition that references a block through them fails loudly here
        // instead of silently compiling with the block's content missing.
        if (definition.SharedInstructionsName is not null && sharedInstructions is null)
        {
            throw new AgentPrismCompilationException(
                $"Agent '{definition.Name}' references shared instructions '{definition.SharedInstructionsName}', " +
                "but the synchronous Compile() path does not resolve shared instructions blocks. Use CompileAsync().")
            {
                AgentName = definition.Name,
            };
        }

        if (AgentParameterValidator.ValidateSchema(definition) is { Count: > 0 } schemaErrors)
        {
            throw new AgentPrismCompilationException(
                $"Agent '{definition.Name}' has an invalid parameter schema: {string.Join(" ", schemaErrors)}")
            {
                AgentName = definition.Name,
            };
        }

        var tools = ResolveTools(definition);
        AddVectorSearchTool(definition, tools);

        if (toolTransform is not null)
        {
            for (var index = 0; index < tools.Count; index++)
            {
                // IToolRegistry.TryGet returns AIFunctionDeclaration (Phase 61):
                // a client-side tool (AddClientTool) is a declaration only, not
                // an AIFunction, and this filter deliberately skips it — there is
                // no server-side body to transform.
                if (tools[index] is AIFunction function)
                {
                    tools[index] = toolTransform(function);
                }
            }
        }

        var chatOptions = BuildChatOptions(definition, tools, culture, sharedInstructions);

        return definition.Harness is null
            ? CompileChatAgent(definition, chatClient, chatOptions, callableAgents)
            : CompileHarnessAgent(definition, chatClient, chatOptions, callableAgents);
    }
}

/// <summary>
/// The resolved set of sub-agents a definition may call, and its cache fingerprint.
/// </summary>
/// <param name="Agents">Sub-agent summaries.</param>
/// <param name="Fingerprint">
/// Fingerprint derived from the sub-agent names and versions. Enters the
/// compiled agent cache's key.
/// </param>
public readonly record struct ResolvedCallableAgents(
    IReadOnlyList<CallableAgentInfo> Agents,
    string Fingerprint)
{
    /// <summary>The result for a definition that calls no sub-agent.</summary>
    public static ResolvedCallableAgents Empty { get; } = new([], string.Empty);
}

/// <summary>
/// The resolved text of a definition's <see cref="AgentDefinition.SharedInstructionsName"/>
/// reference, and its cache fingerprint.
/// </summary>
/// <param name="Text">
/// The referenced block's <see cref="AgentDefinition.Instructions"/> text.
/// <see langword="null"/> when the definition references no block.
/// </param>
/// <param name="Fingerprint">
/// Fingerprint derived from the block's name and version. Enters the compiled
/// agent cache's key: the block can change version independently of the
/// referencing definition.
/// </param>
internal readonly record struct ResolvedSharedInstructions(string? Text, string Fingerprint)
{
    /// <summary>The result for a definition that references no shared instructions block.</summary>
    public static ResolvedSharedInstructions Empty { get; } = new(null, string.Empty);
}
