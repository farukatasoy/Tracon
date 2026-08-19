using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Compaction;
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
///   <item><description><see cref="ModelBinding"/> → <see cref="IModelProviderRegistry"/> → <see cref="IChatClient"/></description></item>
///   <item><description><see cref="AgentDefinition.ToolNames"/> → <see cref="IToolRegistry"/> → list of <see cref="AIFunction"/></description></item>
///   <item><description><c>AsHarnessAgent</c> when <see cref="AgentDefinition.Harness"/> is set, otherwise <c>AsAIAgent</c></description></item>
/// </list>
/// <para>
/// An unknown tool name results in an <see cref="AgentPrismCompilationException"/>.
/// It is not silently skipped: an agent running with a missing tool is an
/// agent that does not do the work the user expects.
/// </para>
/// </remarks>
public sealed class AgentDefinitionCompiler
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
    /// Semantic search store (Phase 51). When <see langword="null"/>, a
    /// definition requesting <c>MemorySettings.EnableVectorSearch</c> gets a
    /// compilation error.
    /// </param>
    /// <param name="embeddingGenerator">
    /// Embedding generator (Phase 51). When <see langword="null"/>, a
    /// definition requesting <c>MemorySettings.EnableVectorSearch</c> gets a
    /// compilation error.
    /// </param>
    /// <param name="knowledgeMaxResults">
    /// Maximum number of results the <c>search_knowledge</c> tool returns (Phase 51).
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
        int knowledgeMaxResults = 5)
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
        => Compile(definition, callableAgents, toolTransform: null);

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
    /// <returns>The executable agent.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="definition"/> is <see langword="null"/>.</exception>
    /// <exception cref="AgentPrismCompilationException">
    /// The model provider cannot be found, or the definition references a tool name that is not registered.
    /// </exception>
    /// <remarks>
    /// <para>
    /// The transform exists for replay (Phase 47,
    /// <see cref="ReplayToolMode.ReplayTools"/>): a <c>DelegatingAIFunction</c>
    /// that replays recorded tool results preserves the wrapped tool's name,
    /// description, and JSON schema - the model sees the tools
    /// <em>exactly as before</em> but no body actually runs.
    /// </para>
    /// <para>
    /// 🚨 The transform applies only to tools resolved from the
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
        Func<AIFunction, AIFunction>? toolTransform)
    {
        ArgumentNullException.ThrowIfNull(definition);

        return BuildAgent(definition, callableAgents, toolTransform, CreateChatClient(definition));
    }

    /// <summary>Converts a definition into an executable agent (phase 65, BYOK).</summary>
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
    /// to the sync overload (K1).
    /// </remarks>
    public ValueTask<AIAgent> CompileAsync(AgentDefinition definition, CancellationToken cancellationToken)
        => CompileAsync(definition, ResolvedCallableAgents.Empty, cancellationToken);

    /// <summary>
    /// Converts a definition, together with its resolved sub-agents, into an executable agent
    /// (phase 65, BYOK).
    /// </summary>
    /// <param name="definition">The definition to compile.</param>
    /// <param name="callableAgents">
    /// Sub-agent summaries resolved beforehand via <see cref="ResolveCallableAgentsAsync"/>.
    /// </param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The executable agent.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="definition"/> is <see langword="null"/>.</exception>
    /// <exception cref="AgentPrismCompilationException">See <see cref="CompileAsync(AgentDefinition, CancellationToken)"/>.</exception>
    public ValueTask<AIAgent> CompileAsync(
        AgentDefinition definition,
        ResolvedCallableAgents callableAgents,
        CancellationToken cancellationToken)
        => CompileAsync(definition, callableAgents, toolTransform: null, cancellationToken);

    /// <summary>
    /// Compiles a definition together with its resolved sub-agents, transforming its tools
    /// (phase 65, BYOK).
    /// </summary>
    /// <param name="definition">The definition to compile.</param>
    /// <param name="callableAgents">
    /// Sub-agent summaries resolved beforehand via <see cref="ResolveCallableAgentsAsync"/>.
    /// </param>
    /// <param name="toolTransform">
    /// Transform applied to every tool resolved from the registry. See
    /// <see cref="Compile(AgentDefinition, ResolvedCallableAgents, Func{AIFunction, AIFunction})"/>.
    /// </param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The executable agent.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="definition"/> is <see langword="null"/>.</exception>
    /// <exception cref="AgentPrismCompilationException">See <see cref="CompileAsync(AgentDefinition, CancellationToken)"/>.</exception>
    public async ValueTask<AIAgent> CompileAsync(
        AgentDefinition definition,
        ResolvedCallableAgents callableAgents,
        Func<AIFunction, AIFunction>? toolTransform,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(definition);

        var chatClient = await CreateChatClientAsync(definition, cancellationToken).ConfigureAwait(false);

        return BuildAgent(definition, callableAgents, toolTransform, chatClient);
    }

    /// <summary>
    /// Reports whether compiling <paramref name="binding"/> for the current
    /// tenant would bake a tenant-specific provider credential into the
    /// resulting chat client (phase 65, BYOK).
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

    private AIAgent BuildAgent(
        AgentDefinition definition,
        ResolvedCallableAgents callableAgents,
        Func<AIFunction, AIFunction>? toolTransform,
        IChatClient chatClient)
    {
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

        var chatOptions = BuildChatOptions(definition, tools);

        return definition.Harness is null
            ? CompileChatAgent(definition, chatClient, chatOptions, callableAgents)
            : CompileHarnessAgent(definition, chatClient, chatOptions, callableAgents);
    }

    /// <summary>
    /// Resolves the sub-agents a definition may call and produces the cache fingerprint.
    /// </summary>
    /// <param name="definition">The agent definition being resolved.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Sub-agent summaries and the fingerprint.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="definition"/> is <see langword="null"/>.</exception>
    /// <exception cref="AgentPrismCompilationException">
    /// The definition wants to call a sub-agent but the feature is not registered.
    /// </exception>
    internal async ValueTask<ResolvedCallableAgents> ResolveCallableAgentsAsync(
        AgentDefinition definition,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(definition);

        if (definition.CallableAgentNames.Count == 0)
        {
            return ResolvedCallableAgents.Empty;
        }

        if (_callableAgents is null)
        {
            throw new AgentPrismCompilationException(
                $"Agent '{definition.Name}' wants to call other agents, but the sub-agent " +
                "resolver is not registered.")
            {
                AgentName = definition.Name,
            };
        }

        var infos = await _callableAgents
            .DescribeAsync(definition.CallableAgentNames, cancellationToken)
            .ConfigureAwait(false);

        return new ResolvedCallableAgents(infos, CreateCallableFingerprint(infos));
    }

    /// <summary>
    /// Produces a cache fingerprint from the sub-agent list.
    /// </summary>
    /// <remarks>
    /// A sub-agent's <em>description</em> is embedded in the instruction text
    /// sent to the model. If the fingerprint did not carry the version, the
    /// calling agent would stay in the cache with the old text when a
    /// sub-agent's description was updated, and the change would never take effect.
    /// </remarks>
    private static string CreateCallableFingerprint(IReadOnlyList<CallableAgentInfo> infos)
    {
        var content = new StringBuilder();

        foreach (var info in infos)
        {
            content.Append('|').Append(info.Name).Append(':').Append(info.Version);
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content.ToString())));
    }

    /// <summary>Validates a definition's skills and produces the cache key.</summary>
    /// <param name="definition">The agent definition being validated.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Skill fingerprint.</returns>
    internal ValueTask<ResolvedAgentSkills> ResolveSkillsAsync(
        AgentDefinition definition,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(definition);

        if (definition.SkillNames.Count == 0)
        {
            return ValueTask.FromResult(ResolvedAgentSkills.Empty);
        }

        if (_skills is null)
        {
            throw new AgentPrismCompilationException(
                $"Agent '{definition.Name}' uses skills, but the skill catalog is not registered.")
            {
                AgentName = definition.Name,
            };
        }

        return _skills.ResolveAsync(definition, cancellationToken);
    }

    private IChatClient CreateChatClient(AgentDefinition definition) => CreateChatClient(definition, definition.Model);

    private IChatClient CreateChatClient(AgentDefinition definition, ModelBinding binding)
    {
        try
        {
            return _models.CreateChatClient(binding);
        }
        catch (AgentPrismException ex)
        {
            throw new AgentPrismCompilationException(
                $"Agent '{definition.Name}' could not be compiled: {ex.Message}",
                ex)
            {
                AgentName = definition.Name,
            };
        }
    }

    private ValueTask<IChatClient> CreateChatClientAsync(AgentDefinition definition, CancellationToken cancellationToken)
        => CreateChatClientAsync(definition, definition.Model, cancellationToken);

    private async ValueTask<IChatClient> CreateChatClientAsync(AgentDefinition definition, ModelBinding binding, CancellationToken cancellationToken)
    {
        try
        {
            return await _models.CreateChatClientAsync(binding, cancellationToken).ConfigureAwait(false);
        }
        catch (AgentPrismException ex)
        {
            throw new AgentPrismCompilationException(
                $"Agent '{definition.Name}' could not be compiled: {ex.Message}",
                ex)
            {
                AgentName = definition.Name,
            };
        }
    }

    private List<AITool> ResolveTools(AgentDefinition definition)
    {
        var tools = new List<AITool>(definition.ToolNames.Count);
        List<string>? missing = null;

        foreach (var toolName in definition.ToolNames)
        {
            if (_tools.TryGet(toolName, out var tool))
            {
                tools.Add(tool);
            }
            else
            {
                (missing ??= []).Add(toolName);
            }
        }

        if (missing is not null)
        {
            var registered = _tools.List();
            var available = registered.Count == 0
                ? "no tools are registered"
                : string.Join(", ", registered.Select(static descriptor => descriptor.Name));

            throw new AgentPrismCompilationException(
                $"Agent '{definition.Name}' references the following tools, but they are not registered in code: " +
                $"{string.Join(", ", missing)}. Registered tools: {available}. " +
                "Tools are defined only in code; register them with `builder.AddAgentPrism().AddTool(...)`.")
            {
                AgentName = definition.Name,
            };
        }

        return tools;
    }

    private ChatOptions BuildChatOptions(AgentDefinition definition, List<AITool> tools)
    {
        var options = new ChatOptions
        {
            Instructions = definition.Instructions,
            ModelId = definition.Model.Model,
            Temperature = definition.Model.Temperature,
            TopP = definition.Model.TopP,
            MaxOutputTokens = definition.Model.MaxOutputTokens,
        };

        if (tools.Count > 0)
        {
            options.Tools = tools;
        }

        if (ParseReasoningEffort(definition) is { } effort)
        {
            options.Reasoning = new ReasoningOptions { Effort = effort };
        }

        options.ResponseFormat = BuildResponseFormat(definition);

        return options;
    }

    /// <summary>
    /// Converts <see cref="ModelBinding.ResponseFormat"/> into a <see cref="ChatResponseFormat"/>.
    /// </summary>
    /// <remarks>
    /// An invalid combination is not silently ignored (the K-034 pattern):
    /// compilation stops when the mode is <see cref="AgentResponseFormatKind.JsonSchema"/>
    /// and the schema is missing, when the schema is set for other modes, or
    /// when the schema is not a JSON object. Only the
    /// <c>ForJsonSchema(JsonElement, ...)</c> overload is used - the overloads
    /// taking <c>Type</c> or <c>JsonSerializerOptions</c> rely on reflection
    /// and break the AOT stance (see docs/38-YAPILANDIRILMIS-CIKTI.md, 38.4).
    /// </remarks>
    private ChatResponseFormat? BuildResponseFormat(AgentDefinition definition)
    {
        var format = definition.Model.ResponseFormat;

        if (format is null)
        {
            return null;
        }

        if (format.Kind == AgentResponseFormatKind.JsonSchema)
        {
            if (format.Schema is not { } schema)
            {
                throw new AgentPrismCompilationException(
                    $"Agent '{definition.Name}' selected the JsonSchema output mode but did not " +
                    $"supply {nameof(AgentResponseFormat.Schema)}.")
                {
                    AgentName = definition.Name,
                };
            }

            if (schema.ValueKind != JsonValueKind.Object)
            {
                throw new AgentPrismCompilationException(
                    $"Agent '{definition.Name}''s {nameof(AgentResponseFormat.Schema)} field must be a JSON " +
                    "object.")
                {
                    AgentName = definition.Name,
                };
            }

            CheckStructuredOutputCapability(definition);

            return ChatResponseFormat.ForJsonSchema(schema, format.SchemaName, format.SchemaDescription);
        }

        if (format.Schema is not null)
        {
            throw new AgentPrismCompilationException(
                $"Agent '{definition.Name}' selected the '{format.Kind}' output mode but also supplied " +
                $"{nameof(AgentResponseFormat.Schema)}. The schema is only used in JsonSchema mode.")
            {
                AgentName = definition.Name,
            };
        }

        if (format.Kind == AgentResponseFormatKind.Json)
        {
            CheckStructuredOutputCapability(definition);

            return ChatResponseFormat.Json;
        }

        return ChatResponseFormat.Text;
    }

    /// <summary>
    /// Checks whether the selected model supports structured output.
    /// </summary>
    /// <remarks>
    /// The check is SKIPPED for a model not found in the model catalog
    /// (K-032): model names may come from configuration and the catalog is
    /// not a validation list. Compilation stops only for a model that IS
    /// FOUND in the catalog and whose <see cref="ModelDescriptor.SupportsStructuredOutput"/>
    /// value is explicitly <see langword="false"/>.
    /// </remarks>
    private void CheckStructuredOutputCapability(AgentDefinition definition)
    {
        var descriptor = FindModelDescriptor(definition.Model.Provider, definition.Model.Model);

        if (descriptor is { SupportsStructuredOutput: false })
        {
            throw new AgentPrismCompilationException(
                $"Agent '{definition.Name}''s model ('{definition.Model.Provider}/{definition.Model.Model}') " +
                "does not support structured output.")
            {
                AgentName = definition.Name,
            };
        }
    }

    private ModelDescriptor? FindModelDescriptor(string provider, string model)
        => ModelCatalogLookup.Find(_models, provider, model);

    /// <summary>
    /// Converts a <see cref="ModelBinding.ReasoningEffort"/> value into a
    /// <see cref="Microsoft.Extensions.AI.ReasoningEffort"/> value.
    /// </summary>
    /// <remarks>
    /// An invalid value is not silently ignored. Reasoning effort changes both
    /// cost and latency; if a mistyped value ran unnoticed, the user would not
    /// get the behavior they expect and would not see why. The provider
    /// decides whether the model supports this setting.
    /// </remarks>
    private static ReasoningEffort? ParseReasoningEffort(AgentDefinition definition)
    {
        var value = definition.Model.ReasoningEffort;

        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (Enum.TryParse<ReasoningEffort>(value, ignoreCase: true, out var effort)
            && Enum.IsDefined(effort))
        {
            return effort;
        }

        throw new AgentPrismCompilationException(
            $"Agent '{definition.Name}''s reasoning effort value is not recognized: '{value}'. " +
            $"Valid values: {string.Join(", ", Enum.GetNames<ReasoningEffort>())}.")
        {
            AgentName = definition.Name,
        };
    }

    // MAAI001: Microsoft.Agents.AI.Compaction.* and AgentFileStore are marked
    // "evaluation purposes only". Context compaction/memory setup is kept in
    // a single block; only this file is updated if MAF changes these APIs.
    // Rationale: docs/KARARLAR.md (same pattern as K-020).
#pragma warning disable MAAI001
    private const int DefaultMinimumPreservedTurns = 2;
    private const int DefaultMinimumPreservedGroups = 4;
    private const int DefaultContextWindowMaxOutputTokens = 4096;

    /// <summary>
    /// The minimum length a token must have for <see cref="BuildKeywordPattern"/>
    /// to count it as "meaningful" - excludes short filler words from the pattern.
    /// </summary>
    private const int MinKeywordLength = 3;

    // The ContextWindow and Pipeline strategies do not expose a
    // CompactionTrigger parameter (they set up/carry their own internal
    // triggers). In these cases, ObservedCompactionStrategy's own trigger is
    // used as a stand-in: the actual gating is left entirely to the internal strategy/strategies.
    private static readonly CompactionTrigger AlwaysTrigger = static _ => true;

    /// <summary>
    /// Builds an executable <see cref="CompactionStrategy"/> from a
    /// definition's compaction settings.
    /// </summary>
    /// <returns>
    /// <see langword="null"/> - the definition does not want compaction
    /// (<see cref="AgentDefinition.Compaction"/> is empty or
    /// <see cref="CompactionStrategyKind.None"/>).
    /// </returns>
    /// <exception cref="AgentPrismCompilationException">
    /// The selected strategy requires a trigger but none was given, or
    /// <see cref="CompactionSettings.MaxContextWindowTokens"/> is missing for
    /// <see cref="CompactionStrategyKind.ContextWindow"/>.
    /// </exception>
    // internal (not private): so structural decisions such as the Pipeline's
    // fixed order can be tested directly. CompactionProvider does not leak
    // this strategy outward, so tests cannot reach it another way.
    internal ObservedCompactionStrategy? BuildCompactionStrategy(AgentDefinition definition)
    {
        var settings = definition.Compaction;

        if (settings is null || settings.Strategy == CompactionStrategyKind.None)
        {
            return null;
        }

        var trigger = BuildTrigger(settings);
        var requiresTrigger = settings.Strategy is not (CompactionStrategyKind.ContextWindow);

        if (requiresTrigger && trigger is null)
        {
            throw new AgentPrismCompilationException(
                $"Agent '{definition.Name}' selected the '{settings.Strategy}' compaction strategy " +
                "but gave no trigger (at least one of TriggerTokens/TriggerMessages/TriggerTurns is required).")
            {
                AgentName = definition.Name,
            };
        }

        CompactionStrategy inner = settings.Strategy switch
        {
            CompactionStrategyKind.SlidingWindow => new SlidingWindowCompactionStrategy(
                trigger!, settings.MinimumPreservedTurns ?? DefaultMinimumPreservedTurns, target: null),

            CompactionStrategyKind.Truncation => new TruncationCompactionStrategy(
                trigger!, settings.MinimumPreservedGroups ?? DefaultMinimumPreservedGroups, target: null),

            CompactionStrategyKind.ToolResult => new ToolResultCompactionStrategy(
                trigger!, settings.MinimumPreservedGroups ?? DefaultMinimumPreservedGroups, target: null),

            CompactionStrategyKind.Summarization => new SummarizationCompactionStrategy(
                ResolveSummarizationChatClient(definition),
                trigger!,
                settings.MinimumPreservedGroups ?? DefaultMinimumPreservedGroups,
                settings.SummarizationPrompt,
                target: null),

            CompactionStrategyKind.ContextWindow => BuildContextWindowStrategy(definition, settings),

            // The order is fixed and documented: ToolResult -> SlidingWindow -> Summarization.
            // A free order produces a configuration surface that is hard to
            // understand in the UI.
            CompactionStrategyKind.Pipeline => new PipelineCompactionStrategy(
            [
                new ToolResultCompactionStrategy(
                    trigger!, settings.MinimumPreservedGroups ?? DefaultMinimumPreservedGroups, target: null),
                new SlidingWindowCompactionStrategy(
                    trigger!, settings.MinimumPreservedTurns ?? DefaultMinimumPreservedTurns, target: null),
                new SummarizationCompactionStrategy(
                    ResolveSummarizationChatClient(definition),
                    trigger!,
                    settings.MinimumPreservedGroups ?? DefaultMinimumPreservedGroups,
                    settings.SummarizationPrompt,
                    target: null),
            ]),

            _ => throw new AgentPrismCompilationException(
                $"Agent '{definition.Name}' selected an unknown compaction strategy: '{settings.Strategy}'.")
            {
                AgentName = definition.Name,
            },
        };

        return new ObservedCompactionStrategy(inner, trigger ?? AlwaysTrigger, target: null);
    }

    /// <summary>
    /// Builds a single <see cref="CompactionTrigger"/> from the configured
    /// trigger fields. When more than one is set, they are combined so that
    /// any one of them firing triggers compaction.
    /// </summary>
    /// <returns><see langword="null"/> when no trigger field is set.</returns>
    private static CompactionTrigger? BuildTrigger(CompactionSettings settings)
    {
        List<CompactionTrigger>? triggers = null;

        if (settings.TriggerTokens is { } tokens)
        {
            (triggers ??= []).Add(CompactionTriggers.TokensExceed(tokens));
        }

        if (settings.TriggerMessages is { } messages)
        {
            (triggers ??= []).Add(CompactionTriggers.MessagesExceed(messages));
        }

        if (settings.TriggerTurns is { } turns)
        {
            (triggers ??= []).Add(CompactionTriggers.TurnsExceed(turns));
        }

        return triggers switch
        {
            null => null,
            { Count: 1 } single => single[0],
            _ => CompactionTriggers.Any([.. triggers]),
        };
    }

    /// <remarks>
    /// 🚨 Phase 62, F-59: when <see cref="CompactionSettings.MaxContextWindowTokens"/>
    /// is not given, it is DERIVED from <see cref="ModelDescriptor.ContextWindowTokens"/>
    /// in the catalog instead of failing compilation outright — the value the
    /// user would otherwise have to copy in by hand already sits on the model
    /// binding. Compilation fails only when NEITHER source has a value, and
    /// the message names both fields so the user knows which one to fill in.
    /// </remarks>
    private ContextWindowCompactionStrategy BuildContextWindowStrategy(AgentDefinition definition, CompactionSettings settings)
    {
        var maxContextWindowTokens = settings.MaxContextWindowTokens
            ?? FindModelDescriptor(definition.Model.Provider, definition.Model.Model)?.ContextWindowTokens;

        if (maxContextWindowTokens is not { } resolvedMaxContextWindowTokens)
        {
            throw new AgentPrismCompilationException(
                $"Agent '{definition.Name}' selected the ContextWindow compaction strategy but did not " +
                $"supply {nameof(CompactionSettings.MaxContextWindowTokens)}, and its model " +
                $"('{definition.Model.Provider}/{definition.Model.Model}') has no context window size in " +
                $"the catalog either. Set {nameof(CompactionSettings.MaxContextWindowTokens)} explicitly.")
            {
                AgentName = definition.Name,
            };
        }

        var maxOutputTokens = settings.MaxOutputTokens
            ?? definition.Model.MaxOutputTokens
            ?? DefaultContextWindowMaxOutputTokens;

        // toolEvictionThreshold/truncationThreshold are not exposed in
        // CompactionSettings in this phase (the doc's §13.1 shape does not
        // include them) - reasonable constant values are used. If needed, add
        // them as a separate field later.
        return new ContextWindowCompactionStrategy(
            resolvedMaxContextWindowTokens,
            maxOutputTokens,
            toolEvictionThreshold: 0.5,
            truncationThreshold: 0.7);
    }

    /// <summary>
    /// Resolves the model used for the summarization call and wraps it with a token tracker.
    /// </summary>
    /// <remarks>
    /// Order: the agent's own <see cref="CompactionSettings.SummarizationModel"/>
    /// → the application-wide utility model → the agent's own model.
    /// </remarks>
    private CompactionUsageTrackingChatClient ResolveSummarizationChatClient(AgentDefinition definition)
    {
        var binding = definition.Compaction?.SummarizationModel ?? _utilityModel ?? definition.Model;

        return new CompactionUsageTrackingChatClient(CreateChatClient(definition, binding));
    }

    /// <summary>Builds a list of <see cref="AIContextProvider"/> from a definition's memory settings.</summary>
    private List<AIContextProvider> CreateMemoryProviders(AgentDefinition definition)
    {
        var memory = definition.Memory;

        if (memory is null)
        {
            return [];
        }

        var providers = new List<AIContextProvider>();

        if (memory.EnableFileMemory)
        {
            providers.Add(new FileMemoryProvider(RequireFileStore(definition), stateInitializer: null, options: null));
        }

        if (memory.EnableTodo)
        {
            providers.Add(new TodoProvider(new TodoProviderOptions()));
        }

        if (CreateTextSearchProvider(definition) is { } textSearch)
        {
            providers.Add(textSearch);
        }

        return providers;
    }

    private TextSearchProvider? CreateTextSearchProvider(AgentDefinition definition)
    {
        if (definition.Memory is not { EnableTextSearch: true })
        {
            return null;
        }

        var fileStore = RequireFileStore(definition);

        return new TextSearchProvider(
            (query, cancellationToken) => SearchFileStoreAsync(fileStore, query, cancellationToken),
            new TextSearchProviderOptions(),
            _loggerFactory);
    }

    /// <summary>
    /// Adds the <c>search_knowledge</c> tool to <paramref name="tools"/> when
    /// <see cref="MemorySettings.EnableVectorSearch"/> is set (Phase 51).
    /// </summary>
    /// <exception cref="AgentPrismCompilationException">
    /// Semantic search is requested but <see cref="IVectorSearchStore"/>, the
    /// embedding generator, or the tenant cannot be resolved.
    /// </exception>
    private void AddVectorSearchTool(AgentDefinition definition, List<AITool> tools)
    {
        if (definition.Memory is not { EnableVectorSearch: true } memory)
        {
            return;
        }

        if (_vectorSearchStore is null)
        {
            throw new AgentPrismCompilationException(
                $"Agent '{definition.Name}' wants semantic search, but IVectorSearchStore is not " +
                "registered (today only PostgreSQL: UsePostgreSql()).")
            {
                AgentName = definition.Name,
            };
        }

        if (_embeddingGenerator is null)
        {
            throw new AgentPrismCompilationException(
                $"Agent '{definition.Name}' wants semantic search, but " +
                "IEmbeddingGenerator<string, Embedding<float>> is not registered.")
            {
                AgentName = definition.Name,
            };
        }

        var tenantId = _tenantContext?.TenantId ?? definition.TenantId
            ?? throw new AgentPrismCompilationException(
                $"Agent '{definition.Name}' wants semantic search, but the tenant could not be resolved.")
            {
                AgentName = definition.Name,
            };

        var collection = memory.VectorCollection is { Length: > 0 } named ? named : definition.Name;

        tools.Add(VectorSearchToolFactory.Create(
            _vectorSearchStore, _embeddingGenerator, tenantId, collection, _knowledgeMaxResults));
    }

    /// <remarks>
    /// 🚨 Wraps with <see cref="TenantPrefixingAgentFileStore"/>: <see cref="_fileStore"/>
    /// is, by default, a SINGLE store shared process-wide. Without the wrapper,
    /// different tenants' file memory/text search would mix at the root "/"
    /// directory - a breach of tenant isolation.
    /// </remarks>
    private TenantPrefixingAgentFileStore RequireFileStore(AgentDefinition definition)
    {
        if (_fileStore is null)
        {
            throw new AgentPrismCompilationException(
                $"Agent '{definition.Name}' wants a memory provider, but the file store is not registered.")
            {
                AgentName = definition.Name,
            };
        }

        var tenantId = _tenantContext?.TenantId ?? definition.TenantId
            ?? throw new AgentPrismCompilationException(
                $"Agent '{definition.Name}' wants a memory provider, but the tenant could not be resolved.")
            {
                AgentName = definition.Name,
            };

        // 🚨 The agent name is part of the prefix as well (defect F-105): the
        // tenant boundary alone let one agent's private file be found by
        // another agent's text search inside the SAME tenant. The name is known
        // here, at compile time, and is already part of the CompiledAgentCache
        // key, so the boundary needs no ambient state.
        return new TenantPrefixingAgentFileStore(_fileStore, tenantId, definition.Name);
    }

    /// <summary>
    /// Maps <see cref="AgentFileStore.SearchAsync"/> results to
    /// <see cref="TextSearchProvider.TextSearchResult"/>.
    /// </summary>
    /// <remarks>
    /// Searches over whichever concrete <see cref="AgentFileStore"/> is
    /// registered (this phase: in-memory); if a persistent store is
    /// registered, this falls through to persistent search without code changes.
    /// </remarks>
    private static async Task<IEnumerable<TextSearchProvider.TextSearchResult>> SearchFileStoreAsync(
        AgentFileStore fileStore,
        string query,
        CancellationToken cancellationToken)
    {
        var matches = await fileStore
            .SearchAsync("/", regexPattern: BuildKeywordPattern(query), globPattern: null, recursive: true, cancellationToken)
            .ConfigureAwait(false);

        return matches.Select(static match => new TextSearchProvider.TextSearchResult
        {
            Text = match.Snippet,
            SourceName = match.FileName,
            SourceLink = match.FileName,
        });
    }

    /// <summary>
    /// Converts the natural-language query <see cref="TextSearchProvider"/>
    /// has the model write into a regex pattern as expected by
    /// <see cref="AgentFileStore.SearchAsync"/>.
    /// </summary>
    /// <remarks>
    /// HATA-S1-009: <c>SearchAsync</c> expects a REGEX (via <c>~</c> in
    /// PostgreSQL, via <see cref="Regex"/> elsewhere), but
    /// <see cref="TextSearchProvider"/> has the model write a natural-language
    /// query (e.g. "is there a record about FILE-7841?"). Running this raw
    /// text as-is as a regex almost never matches (spaces and punctuation
    /// impose narrow constraints in regex terms) - instead, the query is split
    /// on whitespace into meaningful tokens (excluding short filler words),
    /// each is escaped and joined with "OR"; a line matches if it contains ANY
    /// of these tokens (e.g. "FILE-7841"). If zero tokens remain (the whole
    /// query consists of short words), the raw query is escaped and used
    /// as-is - this never makes the behavior worse than it is today.
    /// </remarks>
    private static string BuildKeywordPattern(string query)
    {
        var tokens = query
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Where(static token => token.Length >= MinKeywordLength)
            .Select(Regex.Escape)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return tokens.Length == 0 ? Regex.Escape(query) : string.Join('|', tokens);
    }
#pragma warning restore MAAI001

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
    /// harness defect documented in K-053 would have hit this feature too.
    /// </para>
    /// <para>
    /// Every sub-agent is wrapped with <see cref="ChildAgentInvoker"/>. The
    /// provider calls the sub-agent with <c>options = null</c> (measured in
    /// Phase 12); tree information can only be added by the wrapper.
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
        => CreateChildAgents(definition, callableAgents) is { } children
            ? new BackgroundAgentsProvider(children, new BackgroundAgentsProviderOptions())
            : null;
#pragma warning restore MAAI001

    /// <summary>Builds callable sub-agents together with their wrappers.</summary>
    /// <returns>The wrapped sub-agents; <see langword="null"/> when the definition calls no sub-agent.</returns>
    private List<AIAgent>? CreateChildAgents(AgentDefinition definition, ResolvedCallableAgents callableAgents)
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

        var logger = _loggerFactory?.CreateLogger<ChildAgentInvoker>()
            ?? (ILogger)Microsoft.Extensions.Logging.Abstractions.NullLogger<ChildAgentInvoker>.Instance;

        var children = new List<AIAgent>(callableAgents.Agents.Count);

        foreach (var info in callableAgents.Agents)
        {
            children.Add(new ChildAgentInvoker(_callableAgents, _tenantContext, logger, definition.Name, info));
        }

        return children;
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

        if (CreateChildAgents(definition, callableAgents) is { } children)
        {
            options.BackgroundAgents = children;
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

        return chatClient.AsHarnessAgent(options, _loggerFactory, _services);
#pragma warning restore MAAI001
    }

    private AgentSkillsProvider CreateSkillsProvider(AgentDefinition definition)
        => new(CreateSkillsSource(definition), new AgentSkillsProviderOptions(), _loggerFactory, ownsSource: true);

    private DeduplicatingAgentSkillsSource CreateSkillsSource(AgentDefinition definition)
    {
        if (_skills is null)
        {
            throw new AgentPrismCompilationException(
                $"Agent '{definition.Name}' uses skills, but the skill catalog is not registered.")
            {
                AgentName = definition.Name,
            };
        }

        AgentSkillsSource source = new AggregatingAgentSkillsSource(CreateInnerSources(definition));
        source = new FilteringAgentSkillsSource(
            source,
            (skill, _) => definition.SkillNames.Contains(skill.Frontmatter.Name, StringComparer.Ordinal),
            _loggerFactory);
        source = new CachingAgentSkillsSource(
            source,
            new CachingAgentSkillsSourceOptions
            {
                CacheIsolationKeySelector = _ => _skills.TenantId,
            });
        return new DeduplicatingAgentSkillsSource(source, _loggerFactory);
    }

    /// <summary>Combines the database and disk sources.</summary>
    /// <remarks>
    /// The disk source is added only when a root is defined via
    /// <c>UseSkillScripts</c>. Order matters: the database source comes
    /// first, so if a skill with the same name exists,
    /// <c>DeduplicatingAgentSkillsSource</c> keeps the database record and
    /// tenant isolation is not broken.
    /// </remarks>
    private List<AgentSkillsSource> CreateInnerSources(AgentDefinition definition)
    {
        var sources = new List<AgentSkillsSource>(2)
        {
            new AgentPrismSkillsSource(_skills!, definition, _scripts),
        };

        if (_scripts?.CreateFileSource() is { } fileSource)
        {
            sources.Add(fileSource);
        }

        return sources;
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
