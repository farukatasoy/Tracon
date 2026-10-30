using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace Tracon;

// 144.1's two-layer wait limit. This mirrors the shape
// OnlineEvalJobHandler.JudgeOneAsync uses for JudgeTimeout — the
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
/// <para>
/// Tracon builds one wrapper for each entry of <see cref="AgentDefinition.CallableAgents"/>
/// when it compiles the calling agent; there is no other way to obtain one.
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
    private readonly ChildRefusalMode _refusalMode;
    private readonly ParameterizedAgentFactory? _parameterFactory;
    private readonly string _deadlineSetting;
    private readonly int? _pinnedVersion;
    private readonly string? _versionParameter;
    private readonly bool _hostsApproval;
    private readonly string _callKind;

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
    /// <param name="refusalMode">
    /// Whether a call that cannot deliver the sub-agent's answer comes back as
    /// a tool result (agent delegation, the default) or throws (a workflow step).
    /// </param>
    /// <param name="parameterFactory">
    /// Present only for a WORKFLOW STEP. When the running workflow carries
    /// parameter values (<see cref="AgentRunScope.ParameterValues"/>), the step's
    /// agent is compiled with the values its own schema declares, instead of
    /// being resolved from the catalog. An agent called as a tool never gets one:
    /// a sub-agent receives no values from the agent that calls it, exactly as in
    /// a direct run. This is a separate flag from <paramref name="refusalMode"/>
    /// on purpose - the two are different meanings that happen to coincide today.
    /// </param>
    /// <param name="deadlineSetting">
    /// What set <paramref name="childDeadline"/>, as the refusal names it: a workflow
    /// node's own timeout, or the installation's <c>'Tracon:AgentGraph:ChildDeadline'</c>
    /// (the default).
    /// </param>
    /// <param name="pinnedVersion">
    /// The version of the sub-agent every call runs (a workflow step's
    /// <see cref="WorkflowNodeReference.AgentVersion"/>, or the <see cref="CallableAgentReference.Version"/> of a
    /// callable agent). <see langword="null"/> (the default)
    /// resolves the current version on every call. The wrapper still resolves late, so a pinned
    /// step keeps its version across edits of the agent; the child run row records that version.
    /// </param>
    /// <param name="hostsApproval">
    /// Present only for a workflow step whose Microsoft Agent Framework host turns a tool approval into a workflow
    /// request and waits for the answer. The wrapper then passes the approval request on and its sub-run
    /// closes <see cref="RunStatus.AwaitingApproval"/>. <see langword="false"/> (the default) refuses the approval: an agent
    /// tree cannot wait in its middle, and neither can a step that is a plain function executor.
    /// </param>
    /// <param name="callKind">
    /// What kind of caller a refusal that fails names (<see cref="ChildRefusalMode.Fail"/>): a
    /// <c>"workflow step"</c> (the default) or the <c>"loop reviewer"</c> of an <c>agentReview</c> criterion.
    /// </param>
    /// <param name="versionParameter">
    /// The name of the run parameter whose value is the version of the sub-agent a call runs; only a graph
    /// built in code sets it. The value is read from the running workflow's scope on <em>every call</em>:
    /// empty or absent runs the current version, a whole number from 1 runs that version, anything else
    /// fails the call by name. <see langword="null"/> (the default) leaves the choice to
    /// <paramref name="pinnedVersion"/>; the two are never both set.
    /// </param>
    /// <exception cref="ArgumentNullException">One of the required dependencies is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="childDeadline"/> or <paramref name="waitTimeout"/> is outside one
    /// millisecond to about 49.7 days, or <paramref name="waitTimeout"/> is not greater
    /// than <paramref name="childDeadline"/>.
    /// </exception>
    internal ChildAgentInvoker(
        CallableAgentResolver resolver,
        ITenantContext tenantContext,
        ILogger logger,
        string callerName,
        CallableAgentInfo child,
        TimeSpan childDeadline,
        TimeSpan waitTimeout,
        TimeProvider? timeProvider = null,
        ChildRefusalMode refusalMode = ChildRefusalMode.ToolResult,
        ParameterizedAgentFactory? parameterFactory = null,
        string? deadlineSetting = null,
        int? pinnedVersion = null,
        bool hostsApproval = false,
        string? callKind = null,
        string? versionParameter = null)
    {
        ArgumentNullException.ThrowIfNull(resolver);
        ArgumentNullException.ThrowIfNull(tenantContext);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentException.ThrowIfNullOrWhiteSpace(callerName);
        ArgumentException.ThrowIfNullOrWhiteSpace(child.Name, nameof(child));

        if (!TimerPeriod.IsValid(childDeadline))
        {
            throw new ArgumentOutOfRangeException(
                nameof(childDeadline), childDeadline, TimerPeriod.Failure("The child deadline", childDeadline));
        }

        if (!TimerPeriod.IsValid(waitTimeout))
        {
            throw new ArgumentOutOfRangeException(
                nameof(waitTimeout), waitTimeout, TimerPeriod.Failure("The wait timeout", waitTimeout));
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
        _refusalMode = refusalMode;
        _parameterFactory = parameterFactory;
        _pinnedVersion = pinnedVersion;
        _versionParameter = versionParameter;
        _hostsApproval = hostsApproval;
        _callKind = callKind ?? "workflow step";
        _deadlineSetting = deadlineSetting ?? "'Tracon:AgentGraph:ChildDeadline'";
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

        if (await RefuseAsync(scope, cancellationToken).ConfigureAwait(false) is { } refusal)
        {
            return Refused(refusal);
        }

        var (agent, stepValues) = await ResolveForRunAsync(scope!, cancellationToken).ConfigureAwait(false);
        var childOptions = CreateChildOptions(scope!, stepValues, options);

        // Before the child starts: a call that times out or fails still names the run it opened.
        ChildRunReport.Fill(options, childOptions.RunId, agent);

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
            // result — success or failure — is discarded.
            // deadline is disposed by the observer once invocation settles,
            // never here: it is already canceled (childDeadline < waitTimeout),
            // but the child may still be reading its .Token.
            ObserveAbandonedChild(invocation, deadline);

            await WriteTimedOutAsync(scope!, childOptions, hardCutoff: true, CancellationToken.None).ConfigureAwait(false);
            return Refused(TimeoutRefusal());
        }

        try
        {
            var response = await invocation.ConfigureAwait(false);

            // A step whose host turns the approval into a workflow request passes it on and the workflow
            // waits for the answer. An agent tree and a plain function step refuse it.
            if (!_hostsApproval && ChildRunApproval.Describe(response.Messages) is { } pending)
            {
                return Refused(ApprovalRefusal(pending));
            }

            CountHandoffs(scope!, response.Messages.SelectMany(static message => message.Contents), cancellationToken);

            response.AgentId = Id;
            return response;
        }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            // Layer 1: our own deadline fired and the child honored it.
            await WriteTimedOutAsync(scope!, childOptions, hardCutoff: false, CancellationToken.None).ConfigureAwait(false);
            return Refused(TimeoutRefusal());
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && _refusalMode == ChildRefusalMode.ToolResult)
        {
            // NEITHER token was cancelled, so this is the provider
            // reporting its OWN request timeout - every provider SDK talks over
            // HttpClient, which raises exactly this exception. Calling it the
            // sub-agent's wait limit sends the operator to the wrong dial and
            // counts a provider outage on the wait-limit metric, so no
            // ChildRunTimedOut event is written here.
            //
            // A workflow step (ChildRefusalMode.Fail) does not reach this catch:
            // the provider's own exception fails the step as it is.
            return new AgentResponse(new ChatMessage(ChatRole.Assistant, ProviderFaultRefusal()));
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

        if (await RefuseAsync(scope, cancellationToken).ConfigureAwait(false) is { } refusal)
        {
            yield return RefusedUpdate(refusal);
            yield break;
        }

        var (agent, stepValues) = await ResolveForRunAsync(scope!, cancellationToken).ConfigureAwait(false);
        var childOptions = CreateChildOptions(scope!, stepValues, options);

        // Before the child starts: a call that times out or fails still names the run it opened.
        ChildRunReport.Fill(options, childOptions.RunId, agent);

        await WriteStartedAsync(scope!, childOptions, cancellationToken).ConfigureAwait(false);

        var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(_childDeadline);

        // A child's caller - a workflow step, the next participant - reads the
        // answer and acts on it, so a loop child hands over its final iteration
        // only (AgentAnswerStream). The limits below bound it the same way.
        var enumerator = AgentAnswerStream
            .RunAsync(agent, messages, session, childOptions, deadline.Token)
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
                var step = await AdvanceAsync(enumerator, deadline, waitDeadline, cancellationToken).ConfigureAwait(false);
                outcome = step.Outcome;

                if (step.Outcome == ChildStepOutcome.HardCutoff)
                {
                    // AdvanceAsync only ever returns HardCutoff when the
                    // caller's own token is NOT the one that fired.
                    ObserveAbandonedChild(step.AbandonedMoveNext!, deadline);

                    await WriteTimedOutAsync(scope!, childOptions, hardCutoff: true, CancellationToken.None).ConfigureAwait(false);
                    yield return RefusedUpdate(TimeoutRefusal());
                    yield break;
                }

                if (step.Outcome == ChildStepOutcome.CooperativeTimeout)
                {
                    await WriteTimedOutAsync(scope!, childOptions, hardCutoff: false, CancellationToken.None).ConfigureAwait(false);
                    yield return RefusedUpdate(TimeoutRefusal());
                    yield break;
                }

                if (step.Outcome == ChildStepOutcome.ProviderFault)
                {
                    // Same distinction as the non-streaming path: no
                    // wait limit fired, so no ChildRunTimedOut event. A
                    // workflow step fails with the provider's own exception.
                    if (_refusalMode == ChildRefusalMode.Fail)
                    {
                        step.Fault!.Throw();
                    }

                    yield return new AgentResponseUpdate(ChatRole.Assistant, ProviderFaultRefusal());
                    yield break;
                }

                if (!step.HasNext)
                {
                    yield break;
                }

                // The count is made BEFORE the update goes up: the framework acts on a handoff call when it
                // reads it, so the call that goes past the limit must not reach it.
                CountHandoffs(scope!, step.Update!.Contents, cancellationToken);

                yield return !_hostsApproval &&
                             ChildRunApproval.Describe(step.Update!.Contents) is { } pending
                    ? RefusedUpdate(ApprovalRefusal(pending))
                    : AsOwnUpdate(step.Update!);
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

    /// <summary>
    /// Adds the handoff calls in a child's answer to the run's count and fails the run when the count passes
    /// the limit (<see cref="WorkflowDefinition.MaxHandoffs"/>).
    /// </summary>
    /// <param name="scope">The scope of the run the step belongs to.</param>
    /// <param name="contents">The content of the update, or of the messages, the child produced.</param>
    /// <param name="cancellationToken">The caller's token: a caller that has cancelled wins over a limit the same update crosses.</param>
    /// <exception cref="TraconHandoffLimitExceededException">The run made more handoffs than it may.</exception>
    /// <remarks>
    /// Nothing happens for a run with no counter: the counter exists only on the scope of a Handoff
    /// workflow stored as a definition. It is read from the scope, never from an ambient value: the
    /// scope is the run's own object, and every step of the run adds to the same counter.
    /// </remarks>
    private void CountHandoffs(AgentRunScope scope, IEnumerable<AIContent> contents, CancellationToken cancellationToken)
    {
        // The update that crosses the limit can arrive in the same instant the caller cancels. The run is then
        // Canceled, as for any other step, not Failed with a limit nobody reached on purpose.
        if (cancellationToken.IsCancellationRequested ||
            scope.Handoffs is not { } limit ||
            !limit.RecordAndExceeds(contents))
        {
            return;
        }

        throw new TraconHandoffLimitExceededException(
            $"Agent '{_childName}' asked for handoff number {limit.Made}, but the workflow allows {limit.Max} " +
            "('maxHandoffs'), so the run stopped before it. Two agents may be handing the conversation back and forth: " +
            "change the workflow's handoffs so the loop ends, or raise 'maxHandoffs'.");
    }

    /// <summary>Marks a child's update as this wrapper's own.</summary>
    /// <param name="update">The update the child produced.</param>
    /// <returns>The same update, with this wrapper's id as its agent id.</returns>
    /// <remarks>
    /// A workflow step is this wrapper, and Microsoft Agent Framework's handoff step reads a handoff
    /// call only from an update whose agent id is the step's own agent. The child's agent is resolved
    /// for each call and has its own id, so the handoff call was taken for an outside tool call and
    /// the workflow stopped to wait for its result (measured 2026-10-20). Only the agent id changes;
    /// the child does not read the update again.
    /// </remarks>
    private AgentResponseUpdate AsOwnUpdate(AgentResponseUpdate update)
    {
        update.AgentId = Id;
        return update;
    }

    /// <summary>Advances the child's streaming enumerator under the same two-layer race as the non-streaming path.</summary>
    private async Task<ChildStep> AdvanceAsync(
        IAsyncEnumerator<AgentResponseUpdate> enumerator,
        CancellationTokenSource deadline,
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
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            // Only OUR deadline firing makes this a wait limit. An
            // OperationCanceledException raised while neither token was
            // cancelled is the provider's own request timeout.
            return deadline.IsCancellationRequested
                ? ChildStep.TimedOut()
                : ChildStep.Faulted(ExceptionDispatchInfo.Capture(exception));
        }
    }

    /// <summary>
    /// Produces the reason the call is refused; returns <see langword="null"/>
    /// when the call is allowed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// For agent delegation the refusal is returned as a <em>tool result</em>,
    /// not an exception. Throwing an exception would fail the calling agent's
    /// run; whereas "I could not make this sub-call" is a normal result the
    /// model can evaluate and try another approach for.
    /// </para>
    /// <para>
    /// A workflow step is the opposite case (<see cref="ChildRefusalMode.Fail"/>):
    /// no model reads the step's answer, a graph edge does. Returned as
    /// text, the refusal became the next node's input and the run ended
    /// <c>Completed</c>.
    /// </para>
    /// </remarks>
    private async ValueTask<ChildRefusal?> RefuseAsync(AgentRunScope? scope, CancellationToken cancellationToken)
    {
        if (scope is null)
        {
            _logger.LogWarning(
                "Agent '{Caller}' wanted to call agent '{Child}' but there is no run scope. " +
                "Sub-calls can only be made while Tracon's run recording is on.",
                _callerName,
                _childName);

            return new ChildRefusal(
                $"Could not call agent '{_childName}': sub-agent calls are disabled " +
                "because run recording is off.");
        }

        var maxDepth = scope.Budget?.MaxDepth ?? 0;

        if (scope.Depth + 1 > maxDepth)
        {
            return new ChildRefusal(
                $"Could not call agent '{_childName}': the call depth limit was exceeded " +
                $"(the maximum allowed depth is {maxDepth}). Finish the work yourself, or " +
                "build a call chain with fewer layers.");
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

            return new ChildRefusal($"Could not call agent '{_childName}': a sub-run cannot leave the caller's tenant.");
        }

        if (scope.Budget is { } budget)
        {
            // The tenant's quota is read where the work begins. A spent tree budget answers
            // first, because it is held in memory and a quota read is not; and the quota is read
            // BEFORE the run is reserved, because a refused step must not use up a run of the tree.
            if (!budget.IsExhausted &&
                await TreeQuotaGate.RefusalAsync(budget, TreeStartKind.AgentStep, cancellationToken).ConfigureAwait(false) is { } quota)
            {
                return new ChildRefusal(
                    $"Could not call agent '{_childName}': {quota.Reason} A new child run cannot be started.",
                    ChildRefusalCause.Quota,
                    quota);
            }

            if (!budget.TryReserveRun())
            {
                return new ChildRefusal(
                    $"Could not call agent '{_childName}': {budget.DescribeExhaustion()}",
                    ChildRefusalCause.Budget);
            }
        }

        return null;
    }

    private TraconRunOptions CreateChildOptions(
        AgentRunScope scope,
        IReadOnlyDictionary<string, string>? stepValues,
        AgentRunOptions? options)
    {
        // The caller's own options are not the child's: the tree fields come from the
        // scope. Only the model options for this run pass on - a handoff step gives
        // its agent the handoff tools and instructions this way, and dropping them
        // meant no workflow ever handed off. They pass the caller's run rule first.
        RunChatOptionsRule.Check(options);

        return new()
        {
            ChatOptions = (options as ChatClientAgentRunOptions)?.ChatOptions?.Clone(),
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

            // Inherited with the id: what names a session with no record below a workflow names one here too.
            SessionHasNoRecord = scope.SessionHasNoRecord,

            // What this step was bound with, so its own row records the values
            // (a replay of the step then repeats them). Null for a step that
            // bound none.
            ParameterValues = stepValues,

            // A step's workflow waits for the answer to a tool approval, so its row closes
            // AwaitingApproval. The sub-agent a step's agent calls builds its OWN options and never gets
            // this: below the step the approval is still refused.
            SuspendsWorkflowOnApproval = _hostsApproval,
        };
    }

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

    /// <summary>
    /// Resolves the agent for one call, binding the running workflow's parameter
    /// values when this wrapper is a workflow step.
    /// </summary>
    /// <param name="scope">The running run's scope.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>
    /// The agent, and the final values it was bound with (<see langword="null"/>
    /// when the step bound none).
    /// </returns>
    /// <exception cref="TraconException">
    /// The step's agent requires a parameter the workflow does not supply. The
    /// start-of-run coverage check normally refuses this first; it can only
    /// happen here when the agent changed after the run started.
    /// </exception>
    /// <remarks>
    /// <para>
    /// The step receives only the values its OWN schema declares: the workflow's
    /// contract is wider than any one step's, and a name the step does not
    /// declare is dropped for that step. A step whose agent declares no
    /// parameters, or a run whose workflow carries no values, resolves from the
    /// catalog as before - the common path pays nothing.
    /// </para>
    /// <para>
    /// A bound step bypasses <see cref="CompiledAgentCache"/> (its key has no
    /// notion of a value) and therefore the catalog's decorator chain,
    /// which <see cref="ParameterizedAgentFactory.CompileAsync"/> applies by hand.
    /// The agent is compiled on every call; <c>MAF</c> executor identity
    /// comes from THIS wrapper, not from the agent it resolves, so checkpoint
    /// compatibility is unaffected.
    /// </para>
    /// </remarks>
    private async ValueTask<(AIAgent Agent, IReadOnlyDictionary<string, string>? Values)> ResolveForRunAsync(
        AgentRunScope scope,
        CancellationToken cancellationToken)
    {
        // The version is read from the scope on EVERY call: the graph was built before the run's values were
        // bound, so nothing earlier could have known it.
        var version = VersionFor(scope);

        if (_parameterFactory is not { } factory ||
            scope.ParameterValues is not { Count: > 0 } workflowValues)
        {
            return (await ResolveAsync(version, cancellationToken).ConfigureAwait(false), null);
        }

        var (definition, origin) = await factory.FindAsync(_childName, version, cancellationToken)
            .ConfigureAwait(false);

        if (definition is not { Parameters.Count: > 0 })
        {
            return (await ResolveAsync(version, cancellationToken).ConfigureAwait(false), null);
        }

        var stepValues = ParameterValueSet.Intersect(definition.Parameters, workflowValues);
        var check = factory.Validate(definition.Parameters, stepValues);

        if (!check.IsValid)
        {
            throw new TraconException(
                $"Workflow step '{_childName}' cannot run with the workflow's parameters. " +
                ParameterValueSet.Describe(check));
        }

        var final = ParameterValueSet.Resolve(definition.Parameters, stepValues);

        // Nothing of the workflow's values reaches this step: the catalog binds
        // the step's own defaults and caches the result.
        if (stepValues.Count == 0)
        {
            return (await ResolveAsync(version, cancellationToken).ConfigureAwait(false), final);
        }

        var bound = await factory
            .CompileAsync(definition, origin, culture: null, stepValues, cancellationToken)
            .ConfigureAwait(false);

        return (bound, final);
    }

    /// <summary>
    /// The version of the sub-agent this call runs: the fixed pin, or the value of the run parameter the step
    /// names.
    /// </summary>
    /// <param name="scope">The running run's scope.</param>
    /// <returns>The version; <see langword="null"/> runs the current one.</returns>
    /// <exception cref="TraconException">
    /// The workflow does not declare the parameter, or its value is not a whole number from 1. The message
    /// names the parameter and never repeats the value: a value is the caller's content, and this text reaches
    /// a run's error and a log line.
    /// </exception>
    /// <remarks>
    /// An absent name is not enough to tell a mistyped parameter from a declared one nobody supplied (a
    /// parameter with no value and no default is not in the final set), so the scope carries the declared
    /// names: a name that is not declared would otherwise run the current version in silence, the very
    /// outcome a pin exists to prevent.
    /// </remarks>
    private int? VersionFor(AgentRunScope scope)
    {
        if (_versionParameter is not { } parameter)
        {
            return _pinnedVersion;
        }

        if (scope.ParameterNames?.Contains(parameter) != true)
        {
            throw new TraconException(
                $"Workflow step '{_childName}' reads its agent version from run parameter '{parameter}', " +
                $"but workflow '{_callerName}' does not declare it. Declare the parameter in the workflow's parameters.");
        }

        if (scope.ParameterValues is not { } values ||
            !values.TryGetValue(parameter, out var text) ||
            text.Length == 0)
        {
            return null;
        }

        // Digits only: no sign, no space, no decimal point. A number that overflows is as invalid as a word.
        if (!int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var version) || version < 1)
        {
            throw new TraconException(
                $"Workflow step '{_childName}' cannot run: run parameter '{parameter}' is not a version number. " +
                "A version is a whole number from 1; leave the parameter empty to run the agent's current version.");
        }

        return version;
    }

    private ValueTask<AIAgent> ResolveAsync(CancellationToken cancellationToken)
        => ResolveAsync(_pinnedVersion, cancellationToken);

    private async ValueTask<AIAgent> ResolveAsync(int? version, CancellationToken cancellationToken)
    {
        // A pinned step asks the catalog for ITS version: a version the agent does not
        // have, or an agent that keeps no history, throws here and fails the step by name
        // (the compiler checked both, but the agent can change after that).
        var agent = await _resolver.ResolveAsync(_childName, version, cancellationToken).ConfigureAwait(false);

        return agent ?? throw new TraconException(
            $"Agent '{_callerName}' wants to call agent '{_childName}', but no such agent " +
            "exists in the catalog. The call graph is validated at save time; the sub-agent " +
            "may have since been deleted.");
    }

    /// <summary>
    /// The answer for a call that cannot deliver the sub-agent's own: the
    /// refusal as text in <see cref="ChildRefusalMode.ToolResult"/>, thrown in
    /// <see cref="ChildRefusalMode.Fail"/>.
    /// </summary>
    private AgentResponse Refused(ChildRefusal refusal)
        => _refusalMode == ChildRefusalMode.Fail
            ? throw ToException(refusal)
            : new AgentResponse(new ChatMessage(ChatRole.Assistant, refusal.Text));

    /// <summary>The streaming form of <see cref="Refused"/>.</summary>
    private AgentResponseUpdate RefusedUpdate(ChildRefusal refusal)
        => _refusalMode == ChildRefusalMode.Fail
            ? throw ToException(refusal)
            : new AgentResponseUpdate(ChatRole.Assistant, refusal.Text);

    /// <summary>
    /// The exception a workflow step fails with. The budget keeps the class
    /// the same exhaustion has everywhere else (QuotaExceeded), the wait limit
    /// gets its own (Timeout), and the rest are plain Tracon errors.
    /// </summary>
    private TraconException ToException(ChildRefusal refusal)
        => refusal.Cause switch
        {
            ChildRefusalCause.Budget => new TraconRunBudgetExceededException(refusal.Text),
            ChildRefusalCause.Quota => TreeQuotaGate.ToException(refusal.Quota!, refusal.Text),
            ChildRefusalCause.Timeout => new TraconChildRunTimeoutException(refusal.Text)
            {
                AgentName = _childName,
                Timeout = _childDeadline,
            },
            _ => new TraconException(refusal.Text),
        };

    private ChildRefusal ApprovalRefusal(string toolNames)
        => _refusalMode == ChildRefusalMode.Fail
            ? new(
                $"Agent '{_childName}' could not complete: tool '{toolNames}' requires user approval. " +
                $"A {_callKind} cannot wait for a tool approval. Define an auto-approval rule for this " +
                "tool, or restrict the agent to tools that do not require approval.")
            : new(
                $"Agent '{_childName}' could not complete: tool '{toolNames}' requires user approval. " +
                "A sub-agent cannot request approval; approval is the input of the next turn and " +
                "cannot be awaited in the middle of the tree. Define an auto-approval rule for this " +
                "tool, or restrict the sub-agent to tools that do not require approval.");

    private ChildRefusal TimeoutRefusal()
        => _refusalMode == ChildRefusalMode.Fail
            ? new(
                $"Agent '{_childName}' did not respond in time (limit: {_childDeadline}, set by " +
                $"{_deadlineSetting}). The {_callKind} failed; its eventual result, if any, is discarded.",
                ChildRefusalCause.Timeout)
            : new(
                $"Agent '{_childName}' did not respond in time (limit: {_childDeadline}). The tree " +
                "continues; the sub-call's eventual result, if any, is discarded.",
                ChildRefusalCause.Timeout);

    /// <summary>
    /// The refusal for a sub-call the provider itself broke off, kept
    /// separate from <see cref="TimeoutRefusal"/> so the wait limit and a
    /// provider outage never read as the same event.
    /// </summary>
    private string ProviderFaultRefusal()
        => $"Agent '{_childName}' could not complete: its model provider did not answer. " +
           $"This is a provider fault, not the sub-agent's wait limit — that limit ({_childDeadline}) " +
           "never fired. The tree continues; the sub-call produced no result.";

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
        Task<bool>? AbandonedMoveNext,
        ExceptionDispatchInfo? Fault = null)
    {
        public static ChildStep Next(AgentResponseUpdate update) => new(ChildStepOutcome.Completed, true, update, null);

        public static ChildStep Done() => new(ChildStepOutcome.Completed, false, null, null);

        public static ChildStep TimedOut() => new(ChildStepOutcome.CooperativeTimeout, false, null, null);

        public static ChildStep Faulted(ExceptionDispatchInfo fault) => new(ChildStepOutcome.ProviderFault, false, null, null, fault);

        public static ChildStep Abandoned(Task<bool> moveNext) => new(ChildStepOutcome.HardCutoff, false, null, moveNext);
    }

    /// <summary>Why a call cannot deliver the sub-agent's answer, and the text that says so.</summary>
    private sealed record ChildRefusal(
        string Text,
        ChildRefusalCause Cause = ChildRefusalCause.Other,
        QuotaDecision? Quota = null);

    private enum ChildRefusalCause
    {
        Other,

        /// <summary>The tree's run, token, cost or time budget refused the call.</summary>
        Budget,

        /// <summary>The tenant's quota refused the call: its period is spent.</summary>
        Quota,

        /// <summary>The sub-agent's wait limit fired.</summary>
        Timeout,
    }

    private enum ChildStepOutcome
    {
        Completed,
        CooperativeTimeout,
        HardCutoff,

        /// <summary>The provider reported its own request timeout; no wait limit fired.</summary>
        ProviderFault,
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
/// approval); that package reaches it through <c>InternalsVisibleTo</c>.
/// </para>
/// </remarks>
internal static class ChildRunApproval
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
