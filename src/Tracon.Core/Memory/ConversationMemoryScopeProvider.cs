using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.VectorData;

namespace Tracon;

/// <summary>
/// Gives an agent the conversation memory of the user it is running for, and nobody else's.
/// </summary>
/// <remarks>
/// <para>
/// Wraps <see cref="ChatHistoryMemoryProvider"/>, which is <see langword="sealed"/> and keeps its scope
/// (tenant, agent, user, session) in the session state the first time it runs. That freeze is wrong here:
/// with session ownership off, two users can share a session id, and the first user's scope would
/// then read and write for the second. So the wrapper hands the inner provider NO session. The inner
/// provider builds its scope again on every call, from the run that is calling (<see cref="TraconRunContext"/>),
/// and keeps no state between calls.
/// </para>
/// <para>
/// Memory is off for a run that has no user or no session, for a run whose session has no record (a workflow
/// run and everything below it: nothing could delete what it kept), and for a run of another tenant than the
/// one the agent was compiled for: the call goes through unchanged. The search scope is tenant, agent
/// and user, so a user is remembered across sessions; the write scope also carries the session, which is
/// what an erasure of the session deletes by.
/// </para>
/// <para>
/// The search tool of <see cref="ConversationMemorySearch.OnDemand"/> is made by the inner provider and is not in the
/// tool registry, so the wrapper runs it through the tool chain (<see cref="BuiltInToolGovernance"/>) before the model
/// sees it. The inner provider makes a new function on every call, so the chain is built on every call too. A tool
/// the inner provider adds that the wrapper does not know is refused: it would run outside the chain.
/// </para>
/// </remarks>
// MAAI001: the context provider contexts are marked "evaluation purposes only" - the rationale is the
// same as in AgentDefinitionCompiler.Memory.cs.
#pragma warning disable MAAI001
internal sealed class ConversationMemoryScopeProvider : AIContextProvider, IDisposable
{
    private readonly ChatHistoryMemoryProvider _inner;
    private readonly string _tenantId;
    private readonly string _agentName;
    private readonly ConversationMemorySearch _search;
    private readonly BuiltInToolGovernance? _governance;
    private readonly ILogger? _logger;

    /// <summary>Creates the provider for one compiled agent.</summary>
    /// <param name="store">The vector store the host registered.</param>
    /// <param name="settings">The collection settings set with <c>UseConversationMemory</c>.</param>
    /// <param name="tenantId">The tenant the agent is compiled for.</param>
    /// <param name="agentName">The agent's name.</param>
    /// <param name="search">When the agent searches.</param>
    /// <param name="maxResults">The most records a search returns; <see langword="null"/> takes the framework's default.</param>
    /// <param name="governance">
    /// Runs the search tool through the tool chain. Required for <see cref="ConversationMemorySearch.OnDemand"/>,
    /// the only mode that has a tool; <see langword="null"/> is allowed otherwise.
    /// </param>
    /// <param name="loggerFactory">The logger factory.</param>
    /// <exception cref="ArgumentNullException"><paramref name="governance"/> is <see langword="null"/> for <see cref="ConversationMemorySearch.OnDemand"/>.</exception>
    public ConversationMemoryScopeProvider(
        VectorStore store,
        TraconConversationMemoryOptions settings,
        string tenantId,
        string agentName,
        ConversationMemorySearch search,
        int? maxResults,
        BuiltInToolGovernance? governance,
        ILoggerFactory? loggerFactory)
    {
        // A search tool with no chain to run through is not offered: the provider does not build.
        if (search == ConversationMemorySearch.OnDemand)
        {
            ArgumentNullException.ThrowIfNull(governance);
        }

        _tenantId = AmbientTenantScope.Normalize(tenantId);
        _agentName = agentName;
        _search = search;
        _governance = governance;
        _logger = loggerFactory?.CreateLogger<ConversationMemoryScopeProvider>();

        _inner = new ChatHistoryMemoryProvider(
            store,
            settings.CollectionName,
            settings.VectorDimensions,
            _ => new ChatHistoryMemoryProvider.State(
                CurrentScope(withSession: true) ?? throw new InvalidOperationException("Conversation memory was called outside a run with a user."),
                CurrentScope(withSession: false)),
            new ChatHistoryMemoryProviderOptions
            {
                SearchTime = search == ConversationMemorySearch.OnDemand
                    ? ChatHistoryMemoryProviderOptions.SearchBehavior.OnDemandFunctionCalling
                    : ChatHistoryMemoryProviderOptions.SearchBehavior.BeforeAIInvoke,
                MaxResults = maxResults,
                FunctionToolName = SearchToolName,
                FunctionToolDescription = "Searches what this user said to this agent in earlier conversations.",

                // What is stored is what a person and the agent said, as text. A tool result is large and can
                // carry data the user never saw; a message another context provider added (this one's own
                // memories included) would be stored back and grow on every turn.
                StorageInputRequestMessageFilter = static messages => messages.Where(static message =>
                    message.GetAgentRequestMessageSourceType() == AgentRequestMessageSourceType.External
                    && message.Role == ChatRole.User
                    && !string.IsNullOrWhiteSpace(message.Text)),
                StorageInputResponseMessageFilter = static messages => messages.Where(static message =>
                    message.Role == ChatRole.Assistant && !string.IsNullOrWhiteSpace(message.Text)),
            },
            loggerFactory);
    }

    /// <summary>The name of the search tool of <see cref="ConversationMemorySearch.OnDemand"/>.</summary>
    public const string SearchToolName = "search_conversation_memory";

    /// <summary>Gets no state keys: the provider keeps nothing in the session.</summary>
    public override IReadOnlyList<string> StateKeys => [];

    /// <inheritdoc />
    protected override async ValueTask<AIContext> InvokingCoreAsync(InvokingContext context, CancellationToken cancellationToken = default)
    {
        if (CurrentScope(withSession: true) is null)
        {
            return context.AIContext;
        }

        // The tools the caller has are read BEFORE the inner provider runs: a provider that changed the list it was
        // given in place would otherwise make its own tool look like one the caller already had.
        var before = _search == ConversationMemorySearch.OnDemand
            ? new AIContext { Tools = [.. context.AIContext.Tools ?? []] }
            : null;

        var provided = await _inner
            .InvokingAsync(new InvokingContext(context.Agent, session: null, context.AIContext), cancellationToken)
            .ConfigureAwait(false);

        return before is null
            ? provided
            : GovernAddedTools(before, provided, _governance!);
    }

    /// <summary>
    /// Runs the tool the inner provider added through the tool chain, and refuses a tool it does not know.
    /// </summary>
    /// <param name="before">The context the inner provider was given.</param>
    /// <param name="after">The context the inner provider returned; its tools are replaced.</param>
    /// <param name="governance">Runs a tool through the tool chain.</param>
    /// <returns><paramref name="after"/>, with the search tool wrapped.</returns>
    /// <exception cref="InvalidOperationException">
    /// The inner provider added a tool that is not <see cref="SearchToolName"/>. It would reach the model outside the
    /// tool chain (no authorization handler, no budget gate, no timeout), so the run stops instead of offering it.
    /// </exception>
    internal static AIContext GovernAddedTools(AIContext before, AIContext after, BuiltInToolGovernance governance)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);
        ArgumentNullException.ThrowIfNull(governance);

        if (after.Tools is null)
        {
            return after;
        }

        // The tools the caller already had are the framework's and the host's; they keep their identity. A tool
        // that is not among them was added by the inner provider.
        var known = new HashSet<AITool>(before.Tools ?? [], ReferenceEqualityComparer.Instance);
        var tools = new List<AITool>();

        foreach (var tool in after.Tools)
        {
            if (known.Contains(tool))
            {
                tools.Add(tool);

                continue;
            }

            if (tool is not AIFunction { Name: SearchToolName } search)
            {
                throw new InvalidOperationException(
                    $"Conversation memory added the tool '{tool.Name}', which Tracon does not know. It would run outside the tool " +
                    $"chain, so it is not offered. Tracon runs only '{SearchToolName}' through the chain; update Tracon with the framework.");
            }

            tools.Add(governance.Govern(search, ToolEffect.External, requiresApproval: false));
        }

        after.Tools = tools;

        return after;
    }

    /// <inheritdoc />
    protected override async ValueTask InvokedCoreAsync(InvokedContext context, CancellationToken cancellationToken = default)
    {
        if (context.InvokeException is not null || context.ResponseMessages is null || CurrentScope(withSession: true) is null)
        {
            return;
        }

        await _inner
            .InvokedAsync(new InvokedContext(context.Agent, session: null, context.RequestMessages, context.ResponseMessages), cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public void Dispose() => _inner.Dispose();

    /// <summary>Reads the scope of the run that is calling.</summary>
    /// <param name="withSession">Whether the scope names the session: the write scope does, the search scope does not.</param>
    /// <returns>The scope, or <see langword="null"/> when this run has no memory.</returns>
    private ChatHistoryMemoryProviderScope? CurrentScope(bool withSession)
    {
        var run = TraconRunContext.Current;

        if (run is not { UserId: { Length: > 0 } userId, SessionId: { Length: > 0 } sessionId, SessionHasNoRecord: false })
        {
            return null;
        }

        // The compiled agent is tenant-specific and is cached per tenant; this guards a compiler that
        // was handed to a second tenant by hand (the synchronous Compile path resolves no tenant).
        if (run.TenantId is { } runTenant && !string.Equals(AmbientTenantScope.Normalize(runTenant), _tenantId, StringComparison.Ordinal))
        {
            if (_logger is not null && _logger.IsEnabled(LogLevel.Warning))
            {
                _logger.LogWarning(
                    "Conversation memory of agent {Agent} skipped: the run belongs to another tenant than the agent was compiled for.",
                    _agentName);
            }

            return null;
        }

        return new ChatHistoryMemoryProviderScope
        {
            ApplicationId = _tenantId,
            AgentId = _agentName,
            UserId = userId,
            SessionId = withSession ? sessionId : null,
        };
    }
}
#pragma warning restore MAAI001
