using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace Tracon;

// Phase 144: 144.1's two-layer wait limit. This mirrors the shape
// OnlineEvalJobHandler.JudgeOneAsync uses for JudgeTimeout (K-621) — the
// same semantic is deliberately not reinvented for sub-agents.

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
/// incoming options; the wrapper reads it from the <see cref="TraconRunContext"/>
/// scope and builds the <see cref="TraconRunOptions"/> object itself.
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
    private readonly TimeSpan _childDeadline;
    private readonly TimeSpan _waitTimeout;
    private readonly TimeProvider _timeProvider;

    /// <summary>Creates a new sub-agent wrapper.</summary>
    /// <param name="resolver">Resolver that resolves the sub-agent from the catalog.</param>
    /// <param name="tenantContext">Tenant context.</param>
    /// <param name="logger">Logger.</param>
    /// <param name="callerName">Name of the calling agent.</param>
    /// <param name="child">Summary of the sub-agent being called.</param>
    /// <param name="childDeadline">
    /// The cooperative wait limit (144.1 layer 1), resolved beforehand from
    /// <see cref="SubAgentSettings.ChildDeadline"/>/<see cref="TraconAgentGraphOptions.ChildDeadline"/>.
    /// Must be greater than zero.
    /// </param>
    /// <param name="waitTimeout">
    /// The hard wait cutoff (144.1 layer 2), resolved beforehand from
    /// <see cref="SubAgentSettings.WaitTimeout"/>/<see cref="TraconAgentGraphOptions.WaitTimeout"/>.
    /// Must be greater than <paramref name="childDeadline"/>.
    /// </param>
    /// <param name="timeProvider">Time source for the wait race. Defaults to <see cref="TimeProvider.System"/>.</param>
    /// <exception cref="ArgumentNullException">One of the required dependencies is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="childDeadline"/> is not greater than zero, or
    /// <paramref name="waitTimeout"/> is not greater than <paramref name="childDeadline"/>.
    /// </exception>
    public ChildAgentInvoker(
        CallableAgentResolver resolver,
        ITenantContext tenantContext,
        ILogger logger,
        string callerName,
        CallableAgentInfo child,
        TimeSpan childDeadline,
        TimeSpan waitTimeout,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(resolver);
        ArgumentNullException.ThrowIfNull(tenantContext);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentException.ThrowIfNullOrWhiteSpace(callerName);
        ArgumentException.ThrowIfNullOrWhiteSpace(child.Name, nameof(child));

        if (childDeadline <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(childDeadline), childDeadline, "The child deadline must be greater than zero.");
        }

        if (waitTimeout <= childDeadline)
        {
            throw new ArgumentOutOfRangeException(
                nameof(waitTimeout), waitTimeout, "The wait timeout must be greater than the child deadline.");
        }

        _resolver = resolver;
        _tenantContext = tenantContext;
        _logger = logger;
        _callerName = callerName;
        _childName = child.Name;
        _childDescription = child.Description;
        _childDeadline = childDeadline;
        _waitTimeout = waitTimeout;
        _timeProvider = timeProvider ?? TimeProvider.System;
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
        var scope = TraconRunContext.Current;

        if (Refuse(scope) is { } refusal)
        {
            return new AgentResponse(new ChatMessage(ChatRole.Assistant, refusal));
        }

        var childOptions = CreateChildOptions(scope!);
        var agent = await ResolveAsync(cancellationToken).ConfigureAwait(false);

        await WriteStartedAsync(scope!, childOptions, cancellationToken).ConfigureAwait(false);

        // 144.1 layer 1: the caller's token and Tracon's own deadline are
        // combined. A child that reads the token genuinely cancels here.
        var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(_childDeadline);

        var invocation = agent.RunAsync(messages, session, childOptions, deadline.Token);

        // 144.1 layer 2: a hard cutoff for the child that ignores the token
        // above. Raced with CancellationToken.None so a real elapsed timeout
        // is never confused with the caller's own cancellation below.
        var waitTimeoutTask = Task.Delay(_waitTimeout, _timeProvider, CancellationToken.None);
        var callerCancellation = Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);

        var winner = await Task.WhenAny(invocation, waitTimeoutTask, callerCancellation).ConfigureAwait(false);

        // A real caller cancellation racing callerCancellation ahead of
        // invocation is NOT a hard cutoff - invocation is about to cancel for
        // real too (its token is linked to the same cancellationToken). Falling
        // through lets the try/catch/finally below handle it exactly like any
        // other caller-canceled call, ChildRunCompleted included.
        if (winner != invocation && !cancellationToken.IsCancellationRequested)
        {
            // The child is abandoned running in the background; its eventual
            // result — success or failure — is discarded (K-621 semantics).
            // deadline is disposed by the observer once invocation settles,
            // never here: it is already canceled (childDeadline < waitTimeout),
            // but the child may still be reading its .Token.
            ObserveAbandonedChild(invocation, deadline);

            await WriteTimedOutAsync(scope!, childOptions, hardCutoff: true, CancellationToken.None).ConfigureAwait(false);
            return new AgentResponse(new ChatMessage(ChatRole.Assistant, TimeoutRefusal()));
        }

        try
        {
            var response = await invocation.ConfigureAwait(false);

            return ChildRunApproval.Describe(response.Messages) is { } pending
                ? new AgentResponse(new ChatMessage(ChatRole.Assistant, ApprovalRefusal(pending)))
                : response;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Layer 1: our own deadline fired and the child honored it.
            await WriteTimedOutAsync(scope!, childOptions, hardCutoff: false, CancellationToken.None).ConfigureAwait(false);
            return new AgentResponse(new ChatMessage(ChatRole.Assistant, TimeoutRefusal()));
        }
        finally
        {
            deadline.Dispose();
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
        var scope = TraconRunContext.Current;

        if (Refuse(scope) is { } refusal)
        {
            yield return new AgentResponseUpdate(ChatRole.Assistant, refusal);
            yield break;
        }

        var childOptions = CreateChildOptions(scope!);
        var agent = await ResolveAsync(cancellationToken).ConfigureAwait(false);

        await WriteStartedAsync(scope!, childOptions, cancellationToken).ConfigureAwait(false);

        var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(_childDeadline);

        var enumerator = agent
            .RunStreamingAsync(messages, session, childOptions, deadline.Token)
            .GetAsyncEnumerator(CancellationToken.None);

        // The hard cutoff bounds the WHOLE call, not the gap between two
        // updates - restarting a fresh Task.Delay(_waitTimeout) on every
        // update would let a chatty child that never pauses run forever.
        var waitDeadline = _timeProvider.GetUtcNow() + _waitTimeout;
        var outcome = ChildStepOutcome.Completed;

        try
        {
            while (true)
            {
                var step = await AdvanceAsync(enumerator, waitDeadline, cancellationToken).ConfigureAwait(false);
                outcome = step.Outcome;

                if (step.Outcome == ChildStepOutcome.HardCutoff)
                {
                    // AdvanceAsync only ever returns HardCutoff when the
                    // caller's own token is NOT the one that fired.
                    ObserveAbandonedChild(step.AbandonedMoveNext!, deadline);

                    await WriteTimedOutAsync(scope!, childOptions, hardCutoff: true, CancellationToken.None).ConfigureAwait(false);
                    yield return new AgentResponseUpdate(ChatRole.Assistant, TimeoutRefusal());
                    yield break;
                }

                if (step.Outcome == ChildStepOutcome.CooperativeTimeout)
                {
                    await WriteTimedOutAsync(scope!, childOptions, hardCutoff: false, CancellationToken.None).ConfigureAwait(false);
                    yield return new AgentResponseUpdate(ChatRole.Assistant, TimeoutRefusal());
                    yield break;
                }

                if (!step.HasNext)
                {
                    yield break;
                }

                yield return ChildRunApproval.Describe(step.Update!.Contents) is { } pending
                    ? new AgentResponseUpdate(ChatRole.Assistant, ApprovalRefusal(pending))
                    : step.Update;
            }
        }
        finally
        {
            if (outcome != ChildStepOutcome.HardCutoff)
            {
                await enumerator.DisposeAsync().ConfigureAwait(false);
                deadline.Dispose();
                await WriteCompletedAsync(scope!, childOptions, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    /// <summary>Advances the child's streaming enumerator under the same two-layer race as the non-streaming path.</summary>
    private async Task<ChildStep> AdvanceAsync(
        IAsyncEnumerator<AgentResponseUpdate> enumerator,
        DateTimeOffset waitDeadline,
        CancellationToken cancellationToken)
    {
        var moveNext = enumerator.MoveNextAsync().AsTask();
        var remaining = waitDeadline - _timeProvider.GetUtcNow();
        var waitTimeoutTask = Task.Delay(remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero, _timeProvider, CancellationToken.None);
        var callerCancellation = Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);

        var winner = await Task.WhenAny(moveNext, waitTimeoutTask, callerCancellation).ConfigureAwait(false);

        // Same distinction as the non-streaming path: a real caller
        // cancellation racing ahead of moveNext is not a hard cutoff -
        // moveNext is about to cancel for real too. Falling through lets the
        // catch below (and the caller's normal finally) handle it like any
        // other caller-canceled call.
        if (winner != moveNext && !cancellationToken.IsCancellationRequested)
        {
            return ChildStep.Abandoned(moveNext);
        }

        try
        {
            return await moveNext.ConfigureAwait(false)
                ? ChildStep.Next(enumerator.Current)
                : ChildStep.Done();
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return ChildStep.TimedOut();
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
                "Sub-calls can only be made while Tracon's run recording is on.",
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

    private static TraconRunOptions CreateChildOptions(AgentRunScope scope)
        => new()
        {
            RunId = TraconId.NewId(),
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
        TraconRunOptions childOptions,
        CancellationToken cancellationToken)
        => WriteAsync(scope, RunEventType.ChildRunStarted, childOptions, cancellationToken);

    private ValueTask WriteCompletedAsync(
        AgentRunScope scope,
        TraconRunOptions childOptions,
        CancellationToken cancellationToken)
        => WriteAsync(scope, RunEventType.ChildRunCompleted, childOptions, CancellationToken.None);

    private async ValueTask WriteAsync(
        AgentRunScope scope,
        RunEventType type,
        TraconRunOptions childOptions,
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

        return agent ?? throw new TraconException(
            $"Agent '{_callerName}' wants to call agent '{_childName}', but no such agent " +
            "exists in the catalog. The call graph is validated at save time; the sub-agent " +
            "may have since been deleted.");
    }

    private string ApprovalRefusal(string toolNames)
        => $"Agent '{_childName}' could not complete: tool '{toolNames}' requires user approval. " +
           "A sub-agent cannot request approval; approval is the input of the next turn and " +
           "cannot be awaited in the middle of the tree. Define an auto-approval rule for this " +
           "tool, or restrict the sub-agent to tools that do not require approval.";

    private string TimeoutRefusal()
        => $"Agent '{_childName}' did not respond in time (limit: {_childDeadline}). The tree " +
           "continues; the sub-call's eventual result, if any, is discarded.";

    /// <summary>Writes 144's <see cref="RunEventType.ChildRunTimedOut"/> event.</summary>
    private async ValueTask WriteTimedOutAsync(
        AgentRunScope scope,
        TraconRunOptions childOptions,
        bool hardCutoff,
        CancellationToken cancellationToken)
    {
        if (scope.Writer is not { } writer)
        {
            return;
        }

        var payload = JsonSerializer.Serialize(
            new ChildRunTimedOutEventPayload
            {
                ChildRunId = childOptions.RunId,
                HardCutoff = hardCutoff,
            },
            TraconCoreJsonContext.Default.ChildRunTimedOutEventPayload);

        await writer.AppendAsync(
            new RunEventDraft(RunEventType.ChildRunTimedOut)
            {
                Text = _childName,
                Payload = payload,
            },
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Observes a sub-agent call abandoned by the hard-cutoff layer so its
    /// eventual completion never produces an unobserved task exception, and
    /// disposes the deadline source it was carrying once it settles.
    /// </summary>
    private void ObserveAbandonedChild(Task invocation, CancellationTokenSource deadline)
    {
        _ = invocation.ContinueWith(
            task =>
            {
                deadline.Dispose();

                if (task.IsFaulted)
                {
                    _logger.LogWarning(
                        task.Exception,
                        "Agent '{Caller}''s call to '{Child}' faulted after its wait limit was reported.",
                        _callerName,
                        _childName);
                }
                else if (_logger.IsEnabled(LogLevel.Debug))
                {
                    _logger.LogDebug(
                        "Agent '{Caller}''s call to '{Child}' finished after its wait limit was reported.",
                        _callerName,
                        _childName);
                }
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    /// <summary>One step of the streaming race: the next update, or a timeout outcome (144.1).</summary>
    private readonly record struct ChildStep(
        ChildStepOutcome Outcome,
        bool HasNext,
        AgentResponseUpdate? Update,
        Task<bool>? AbandonedMoveNext)
    {
        public static ChildStep Next(AgentResponseUpdate update) => new(ChildStepOutcome.Completed, true, update, null);

        public static ChildStep Done() => new(ChildStepOutcome.Completed, false, null, null);

        public static ChildStep TimedOut() => new(ChildStepOutcome.CooperativeTimeout, false, null, null);

        public static ChildStep Abandoned(Task<bool> moveNext) => new(ChildStepOutcome.HardCutoff, false, null, moveNext);
    }

    private enum ChildStepOutcome
    {
        Completed,
        CooperativeTimeout,
        HardCutoff,
    }
}

/// <summary>The <see cref="RunEventType.ChildRunTimedOut"/> event payload.</summary>
internal sealed record ChildRunTimedOutEventPayload
{
    /// <summary>Gets the sub-run's own identity.</summary>
    public required Guid? ChildRunId { get; init; }

    /// <summary>
    /// Gets whether the hard-cutoff layer fired (<see langword="true"/>, the
    /// sub-agent ignored cancellation and keeps running in the background) or
    /// the cooperative layer did (<see langword="false"/>, the sub-agent
    /// honored cancellation and its resources were released).
    /// </summary>
    public required bool HardCutoff { get; init; }
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
/// <c>Tracon.AspNetCore</c> use the same detection too (the second
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

    /// <summary>Collects the tool-approval requests found in a response's messages.</summary>
    /// <param name="messages">Response messages.</param>
    /// <returns>The requests, in the order they appear. Empty when there are none.</returns>
    public static IReadOnlyList<ToolApprovalRequestContent> CollectRequests(IEnumerable<ChatMessage> messages)
    {
        List<ToolApprovalRequestContent>? requests = null;

        foreach (var message in messages)
        {
            foreach (var content in message.Contents)
            {
                if (content is ToolApprovalRequestContent request)
                {
                    (requests ??= []).Add(request);
                }
            }
        }

        return requests ?? [];
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
