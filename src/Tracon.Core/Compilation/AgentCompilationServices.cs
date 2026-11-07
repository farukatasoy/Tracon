using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Tracon;

/// <summary>
/// The optional services and settings an <see cref="AgentDefinitionCompiler"/> compiles with.
/// </summary>
/// <remarks>
/// <para>
/// A member that is <see langword="null"/> switches its feature off, and a definition that asks for the
/// feature then fails to compile with a message that names what is missing: no skill script runs, no agent
/// calls another agent, a definition that asks for file memory gets an error. An empty instance
/// (<see cref="Empty"/>) is the compiler of a test or a host with no container.
/// </para>
/// <para>
/// The five lookups at the end of this type (<see cref="ConversationMemoryOptions"/>, <see cref="VectorStore"/>,
/// <see cref="EmbeddingAccess"/>, <see cref="BuiltInToolGovernance"/> and <see cref="WorkspaceLimits"/>) read the
/// container when an agent is compiled, not when this object is made. The compiler is built inside the
/// catalog's own construction chain, and a service that needs the catalog would close a cycle if it were asked for
/// here.
/// </para>
/// </remarks>
internal sealed class AgentCompilationServices
{
    /// <summary>Gets a set with no service: nothing is registered, every optional feature is off.</summary>
    public static AgentCompilationServices Empty { get; } = new();

    /// <summary>Gets the logger factory passed to the produced agents.</summary>
    public ILoggerFactory? LoggerFactory { get; init; }

    /// <summary>
    /// Gets the container. It is passed to the produced agents and is where the five lookups at the end of this
    /// type read from. Without it, a feature that needs one of those services reports it as not registered.
    /// </summary>
    public IServiceProvider? Provider { get; init; }

    /// <summary>
    /// Gets the chat history provider attached to the produced agents. When <see langword="null"/>, the Microsoft
    /// Agent Framework's in-memory default is used and history travels in the session state.
    /// </summary>
    public ChatHistoryProvider? ChatHistoryProvider { get; init; }

    /// <summary>Gets the catalog that resolves skill sources.</summary>
    public AgentSkillCatalog? Skills { get; init; }

    /// <summary>Gets the skill script support. When <see langword="null"/>, no script can run.</summary>
    public SkillScriptSupport? Scripts { get; init; }

    /// <summary>Gets the resolver for callable sub-agents. When <see langword="null"/>, no agent can call another.</summary>
    public CallableAgentResolver? CallableAgents { get; init; }

    /// <summary>Gets the tenant context. Used to verify that a sub-call does not change tenant.</summary>
    public ITenantContext? TenantContext { get; init; }

    /// <summary>
    /// Gets the default model for the summaries of context compaction. When <see langword="null"/>, the agent's
    /// own model is used.
    /// </summary>
    public ModelBinding? UtilityModel { get; init; }

    // MAAI001: Microsoft.Agents.AI.AgentFileStore is marked "evaluation purposes only". The suppression sits where
    // the type is named, as it does in the compiler.
#pragma warning disable MAAI001
    /// <summary>
    /// Gets the file store of the memory providers. When <see langword="null"/>, a definition that asks for file memory,
    /// text search or a session workspace gets a compilation error.
    /// </summary>
    public AgentFileStore? FileStore { get; init; }
#pragma warning restore MAAI001

    /// <summary>
    /// Gets the factory of the context provider for <see cref="AgentDefinition.McpResourceUris"/>. When
    /// <see langword="null"/>, a definition that asks for an MCP resource gets a compilation error.
    /// </summary>
    public IMcpResourceContextProviderFactory? McpResources { get; init; }

    /// <summary>
    /// Gets the semantic search store. When <see langword="null"/>, a definition that asks for vector search gets a
    /// compilation error.
    /// </summary>
    public IVectorSearchStore? VectorSearchStore { get; init; }

    /// <summary>
    /// Gets the embedding generator. When <see langword="null"/>, a definition that asks for vector search gets a
    /// compilation error.
    /// </summary>
    public IEmbeddingGenerator<string, Embedding<float>>? EmbeddingGenerator { get; init; }

    /// <summary>Gets the most results the <c>search_knowledge</c> tool returns.</summary>
    public int KnowledgeMaxResults { get; init; } = 5;

    /// <summary>
    /// Gets the definition store, used to resolve <see cref="AgentDefinition.SharedInstructionsName"/>. When
    /// <see langword="null"/>, a definition that references a shared instructions block gets a compilation error.
    /// </summary>
    public IAgentDefinitionStore? DefinitionStore { get; init; }

    /// <summary>
    /// Gets the tree-wide defaults, including the sub-agent wait limits a definition's own sub-agent list may override.
    /// </summary>
    public TraconAgentGraphOptions AgentGraph { get; init; } = new();

    /// <summary>Gets the time source of the sub-agent wait race. When <see langword="null"/>, the system clock.</summary>
    public TimeProvider? TimeProvider { get; init; }

    /// <summary>
    /// Gets the loop stop criteria registered in code. A definition that names a kind that is neither built in nor
    /// registered here gets a compilation error.
    /// </summary>
    public IEnumerable<TraconLoopEvaluatorRegistration>? LoopCriteria { get; init; }

    /// <summary>
    /// Gets the judge binding an <c>aiJudge</c> loop criterion calls. When <see langword="null"/> or unconfigured,
    /// that criterion gets a compilation error instead of borrowing the agent's own model.
    /// </summary>
    public ModelRunJudgeOptions? JudgeOptions { get; init; }

    /// <summary>
    /// Gets the tenant egress policy store. Its policy for the current tenant enters the compiled-agent cache key, so a
    /// policy saved after an agent was cached reaches the next resolve.
    /// </summary>
    public ITenantEgressPolicyStore? TenantEgressPolicies { get; init; }

    /// <summary>
    /// Gets the agent extensions registered in code. A definition that names one that is not registered here gets a
    /// compilation error.
    /// </summary>
    public IEnumerable<IAgentExtension>? AgentExtensions { get; init; }

    /// <summary>Gets the hosted tools registered in code. When <see langword="null"/>, none is registered.</summary>
    public HostedToolRegistry? HostedTools { get; init; }

    /// <summary>Gets the most tools one agent may carry. An agent over it does not compile.</summary>
    public int MaxToolsPerAgent { get; init; } = TraconToolOptions.DefaultMaxToolsPerAgent;

    /// <summary>Gets the conversation memory settings, or <see langword="null"/> when no container is set.</summary>
    public IOptions<TraconConversationMemoryOptions>? ConversationMemoryOptions
        => Provider?.GetService<IOptions<TraconConversationMemoryOptions>>();

    /// <summary>Gets the vector store conversation memory writes to, or <see langword="null"/> when none is registered.</summary>
    public Microsoft.Extensions.VectorData.VectorStore? VectorStore
        => Provider?.GetService<Microsoft.Extensions.VectorData.VectorStore>();

    /// <summary>
    /// Gets the check that carries the egress policy and the provider bindings of the embedding call, or
    /// <see langword="null"/> when none is registered.
    /// </summary>
    public EmbeddingProviderAccess? EmbeddingAccess
        => Provider?.GetService<EmbeddingProviderAccess>();

    /// <summary>
    /// Gets the service that runs a tool Tracon adds to a run through the tool chain, or <see langword="null"/> when
    /// Tracon is not registered.
    /// </summary>
    public BuiltInToolGovernance? BuiltInToolGovernance
        => Provider?.GetService<BuiltInToolGovernance>();

    /// <summary>Gets the limits of a session workspace, or <see langword="null"/> when none is registered.</summary>
    public IOptionsMonitor<TraconWorkspaceOptions>? WorkspaceLimits
        => Provider?.GetService<IOptionsMonitor<TraconWorkspaceOptions>>();
}
