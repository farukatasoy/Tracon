using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace AgentPrism;

/// <summary>
/// Wraps a sub-agent an agent may call; enforces depth, budget, and tenant
/// limits and links the sub-run into the tree.
/// </summary>
/// <remarks>
/// <para>
/// This is <strong>not</strong> an <see cref="IAgentDecorator"/>. Decorators
/// apply to <em>every</em> agent resolved from the catalog; this wrapper only
/// intervenes when one agent appears in another agent's view. It does not
/// touch the decorator order (recording 0 → telemetry 10 → approval 20).
/// </para>
/// <para>
/// <strong>The sub-agent is resolved late.</strong> This way, the calling
/// agent's compiled copy does not go stale when the sub-agent's definition changes.
/// </para>
/// <para>
/// Microsoft Agent Framework calls the sub-agent with <c>options = null</c>
/// (). Tree information therefore cannot be read from the
/// incoming options; the wrapper reads it from the <see cref="AgentPrismRunContext"/>
/// scope and builds the <see cref="AgentPrismRunOptions"/> object itself.
/// </para>
/// </remarks>
public sealed class ChildAgentInvoker : AIAgent
{
    private readonly CallableAgentResolver _resolver;
    private readonly ITenantContext _tenantContext;
    private readonly ILogger _logger;
    private readonly string _callerName;
    private readonly string _childName;
    private readonly string? _childDescription;

    /// <summary>Creates a new sub-agent wrapper.</summary>
    /// <param name="resolver">Resolver that resolves the sub-agent from the catalog.</param>
    /// <param name="tenantContext">Tenant context.</param>
    /// <param name="logger">Logger.</param>
    /// <param name="callerName">Name of the calling agent.</param>
    /// <param name="child">Summary of the sub-agent being called.</param>
    /// <exception cref="ArgumentNullException">One of the required dependencies is <see langword="null"/>.</exception>
    public ChildAgentInvoker(
        CallableAgentResolver resolver,
        ITenantContext tenantContext,
        ILogger logger,
        string callerName,
        CallableAgentInfo child)
    {
        ArgumentNullException.ThrowIfNull(resolver);
        ArgumentNullException.ThrowIfNull(tenantContext);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentException.ThrowIfNullOrWhiteSpace(callerName);
        ArgumentException.ThrowIfNullOrWhiteSpace(child.Name, nameof(child));

        _resolver = resolver;
        _tenantContext = tenantContext;
        _logger = logger;
        _callerName = callerName;
        _childName = child.Name;
        _childDescription = child.Description;
    }

    /// <inheritdoc />
    public override string Name => _childName;

    /// <inheritdoc />
    public override string? Description => _childDescription;

    /// <inheritdoc />
    protected override async ValueTask<AgentSession> CreateSessionCoreAsync(CancellationToken cancellationToken = default)
    {
        var agent = await ResolveAsync(cancellationToken).ConfigureAwait(false);

        return await agent.CreateSessionAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    protected override async ValueTask<AgentSession> DeserializeSessionCoreAsync(
        JsonElement serializedState,
        JsonSerializerOptions? jsonSerializerOptions = null,
        CancellationToken cancellationToken = default)
    {
        var agent = await ResolveAsync(cancellationToken).ConfigureAwait(false);

        return await agent
            .DeserializeSessionAsync(serializedState, jsonSerializerOptions, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    protected override async ValueTask<JsonElement> SerializeSessionCoreAsync(
        AgentSession session,
        JsonSerializerOptions? jsonSerializerOptions = null,
        CancellationToken cancellationToken = default)
    {
        var agent = await ResolveAsync(cancellationToken).ConfigureAwait(false);

        return await agent
            .SerializeSessionAsync(session, jsonSerializerOptions, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    protected override async Task<AgentResponse> RunCoreAsync(
        IEnumerable<ChatMessage> messages,
        AgentSession? session = null,
        AgentRunOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var scope = AgentPrismRunContext.Current;

        if (Refuse(scope) is { } refusal)
        {
            return new AgentResponse(new ChatMessage(ChatRole.Assistant, refusal));
        }

        var childOptions = CreateChildOptions(scope!);
        var agent = await ResolveAsync(cancellationToken).ConfigureAwait(false);

        await WriteStartedAsync(scope!, childOptions, cancellationToken).ConfigureAwait(false);

        try
        {
            var response = await agent
                .RunAsync(messages, session, childOptions, cancellationToken)
                .ConfigureAwait(false);

            return ChildRunApproval.Describe(response.Messages) is { } pending
                ? new AgentResponse(new ChatMessage(ChatRole.Assistant, ApprovalRefusal(pending)))
                : response;
        }
        finally
        {
            await WriteCompletedAsync(scope!, childOptions, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    protected override async IAsyncEnumerable<AgentResponseUpdate> RunCoreStreamingAsync(
        IEnumerable<ChatMessage> messages,
        AgentSession? session = null,
        AgentRunOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var scope = AgentPrismRunContext.Current;

        if (Refuse(scope) is { } refusal)
        {
            yield return new AgentResponseUpdate(ChatRole.Assistant, refusal);
            yield break;
        }

        var childOptions = CreateChildOptions(scope!);
        var agent = await ResolveAsync(cancellationToken).ConfigureAwait(false);

        await WriteStartedAsync(scope!, childOptions, cancellationToken).ConfigureAwait(false);

        try
        {
            var updates = agent.RunStreamingAsync(messages, session, childOptions, cancellationToken);

            await foreach (var update in updates.ConfigureAwait(false))
            {
                yield return ChildRunApproval.Describe(update.Contents) is { } pending
                    ? new AgentResponseUpdate(ChatRole.Assistant, ApprovalRefusal(pending))
                    : update;
            }
        }
        finally
        {
            await WriteCompletedAsync(scope!, childOptions, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Produces the reason the call is refused; returns <see langword="null"/>
    /// when the call is allowed.
    /// </summary>
    /// <remarks>
    /// The refusal is returned as a <em>tool result</em>, not an exception.
    /// Throwing an exception would fail the calling agent's run; whereas
    /// "I could not make this sub-call" is a normal result the model can
    /// evaluate and try another approach for.
    /// </remarks>
    private string? Refuse(AgentRunScope? scope)
    {
        if (scope is null)
        {
            _logger.LogWarning(
                "Agent '{Caller}' wanted to call agent '{Child}' but there is no run scope. " +
                "Sub-calls can only be made while AgentPrism's run recording is on.",
                _callerName,
                _childName);

            return $"Could not call agent '{_childName}': sub-agent calls are disabled " +
                   "because run recording is off.";
        }

        var maxDepth = scope.Budget?.MaxDepth ?? 0;

        if (scope.Depth + 1 > maxDepth)
        {
            return $"Could not call agent '{_childName}': the call depth limit was exceeded " +
                   $"(the maximum allowed depth is {maxDepth}). Finish the work yourself, or " +
                   "build a call chain with fewer layers.";
        }

        // Tenant leakage happens exactly here. The sub-call runs on another
        // thread; if the tenant context somehow got lost, it would fall back
        // to the default tenant and one tenant's agent would work with
        // another tenant's data.
        if (!string.Equals(scope.TenantId, _tenantContext.TenantId, StringComparison.Ordinal))
        {
            _logger.LogError(
                "Agent '{Caller}''s call to '{Child}' was refused: the tenant changed " +
                "('{Expected}' -> '{Actual}').",
                _callerName,
                _childName,
                scope.TenantId,
                _tenantContext.TenantId);

            return $"Could not call agent '{_childName}': a sub-run cannot leave the caller's tenant.";
        }

        if (scope.Budget is { } budget && !budget.TryReserveRun())
        {
            return $"Could not call agent '{_childName}': {budget.DescribeExhaustion()}";
        }

        return null;
    }

    private static AgentPrismRunOptions CreateChildOptions(AgentRunScope scope)
        => new()
        {
            RunId = AgentPrismId.NewId(),
            ParentRunId = scope.RunId,
            RootRunId = scope.RootRunId,
            Depth = scope.Depth + 1,

            // The SAME instance is carried over. If copied, each branch would
            // get its own budget.
            Budget = scope.Budget,

            // MAF does not pass a session to the sub-agent. The session id is
            // still carried: a tool running in the sub-run may produce an
            // additional root session, and if written without a session, the
            // retention policy would treat it as orphaned and delete it.
            SessionId = scope.SessionId,
        };

    private ValueTask WriteStartedAsync(
        AgentRunScope scope,
        AgentPrismRunOptions childOptions,
        CancellationToken cancellationToken)
        => WriteAsync(scope, RunEventType.ChildRunStarted, childOptions, cancellationToken);

    private ValueTask WriteCompletedAsync(
        AgentRunScope scope,
        AgentPrismRunOptions childOptions,
        CancellationToken cancellationToken)
        => WriteAsync(scope, RunEventType.ChildRunCompleted, childOptions, CancellationToken.None);

    private async ValueTask WriteAsync(
        AgentRunScope scope,
        RunEventType type,
        AgentPrismRunOptions childOptions,
        CancellationToken cancellationToken)
    {
        if (scope.Writer is not { } writer)
        {
            return;
        }

        await writer.AppendAsync(
            new RunEventDraft(type)
            {
                Text = _childName,
                Payload = childOptions.RunId?.ToString(),
            },
            cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask<AIAgent> ResolveAsync(CancellationToken cancellationToken)
    {
        var agent = await _resolver.ResolveAsync(_childName, cancellationToken).ConfigureAwait(false);

        return agent ?? throw new AgentPrismException(
            $"Agent '{_callerName}' wants to call agent '{_childName}', but no such agent " +
            "exists in the catalog. The call graph is validated at save time; the sub-agent " +
            "may have since been deleted.");
    }

    private string ApprovalRefusal(string toolNames)
        => $"Agent '{_childName}' could not complete: tool '{toolNames}' requires user approval. " +
           "A sub-agent cannot request approval; approval is the input of the next turn and " +
           "cannot be awaited in the middle of the tree. Define an auto-approval rule for this " +
           "tool, or restrict the sub-agent to tools that do not require approval.";
}

/// <summary>Determines whether a response carries a tool call pending approval.</summary>
/// <remarks>
/// <para>
/// The detection is kept in one place because it has more than one consumer:
/// <see cref="ChildAgentInvoker"/> returns an understandable error message to
/// the caller, <see cref="RunRecordingAgent"/> closes the sub-run as
/// <c>Failed</c>. If written separately in two places, one would change and
/// the other would fall behind.
/// </para>
/// <para>
/// The MCP and A2A external call handlers in
/// <c>AgentPrism.AspNetCore</c> use the same detection too (the second
/// application of the same rule: it is not an external caller, it cannot give
/// approval). This is why the type is <strong>public</strong>.
/// </para>
/// </remarks>
public static class ChildRunApproval
{
    /// <summary>Searches the messages for a tool call pending approval.</summary>
    /// <param name="messages">Response messages.</param>
    /// <returns>Names of tools pending approval; <see langword="null"/> when there are none.</returns>
    public static string? Describe(IEnumerable<ChatMessage> messages)
    {
        List<string>? names = null;

        foreach (var message in messages)
        {
            Collect(message.Contents, ref names);
        }

        return names is null ? null : string.Join(", ", names);
    }

    /// <summary>Searches the contents for a tool call pending approval.</summary>
    /// <param name="contents">Response contents.</param>
    /// <returns>Names of tools pending approval; <see langword="null"/> when there are none.</returns>
    public static string? Describe(IEnumerable<AIContent> contents)
    {
        List<string>? names = null;

        Collect(contents, ref names);

        return names is null ? null : string.Join(", ", names);
    }

    private static void Collect(IEnumerable<AIContent> contents, ref List<string>? names)
    {
        foreach (var content in contents)
        {
            if (content is not ToolApprovalRequestContent request)
            {
                continue;
            }

            // An approval request may not always carry a function call; when
            // there is no tool name, the call id is written instead. Leaving
            // the message empty would mean the user never learns which tool
            // is requesting approval.
            (names ??= []).Add(request.ToolCall is FunctionCallContent call
                ? call.Name
                : request.ToolCall.CallId);
        }
    }
}
