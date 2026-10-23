using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Tracon;

/// <summary>
/// The turn of an agent run that every run surface shares: the gates a run passes before it starts, the agent resolved
/// for the request, the pending answers of the session matched, and the options the run starts with.
/// </summary>
/// <remarks>
/// <para>
/// The management API's run endpoint and the AG-UI endpoint (<c>Tracon.AGUI</c>) both call this. A turn is set up
/// in one place, so a gate or a match added here reaches every surface that runs an agent, and a second copy cannot
/// fall behind the first. What stays with the surface is the wire: the SSE frames, the JSON body and the way a refusal
/// is written.
/// </para>
/// <para>
/// 🚨 Everything in <see cref="PrepareAsync"/> happens BEFORE the response begins. Once the status line and the headers are
/// on the wire a problem can no longer be a <c>ProblemDetails</c> the caller can read (same physical constraint as K-324).
/// </para>
/// </remarks>
internal static class AgentTurnService
{
    /// <summary>
    /// Runs the gates a run passes after the request body is read: attribution, authorization, session ownership,
    /// quota and the context-window pre-flight, in that order.
    /// </summary>
    /// <param name="name">The agent name.</param>
    /// <param name="request">The run request.</param>
    /// <param name="httpContext">The request; the gates read their services from it.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The refusal to answer with, or <see langword="null"/> when the run may start.</returns>
    /// <remarks>
    /// <para>
    /// The order is part of the contract. Authorization runs AFTER attribution (it needs the resolved user) and BEFORE the
    /// quota check (an unauthorized call must not consume the tenant's quota). The quota check happens BEFORE the run starts:
    /// an in-progress run is not cut off when the quota is exceeded (K-162), only a new run gets a 429.
    /// </para>
    /// <para>
    /// Ownership guards the run surface too, not only the session endpoints (phase 148): continuing a conversation reads its
    /// whole history back into the model, so leaving this door open would make gating <c>GET /api/sessions/{id}</c> decorative.
    /// Inert while ownership is off, which is the default.
    /// </para>
    /// </remarks>
    public static async ValueTask<IResult?> CheckRunGatesAsync(
        string name,
        AgentRunRequest request,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var services = httpContext.RequestServices;
        var tenantContext = services.GetRequiredService<ITenantContext>();
        var attributionContext = services.GetService<IRunAttributionContext>();

        // 🚨 Attribution is checked BEFORE the run starts, so an oversized label set is REJECTED rather than trimmed.
        // The run request body is deliberately NOT a source of attribution: a userId field there would let any client
        // write spend against another user's name.
        if (RunAttributionGate.Check(attributionContext) is { } attributionProblem)
        {
            return attributionProblem;
        }

        if (await RunAuthorizationGate
                .CheckRunAsync(
                    services.GetService<IRunAuthorizationHandler>(),
                    tenantContext,
                    name,
                    request.SessionId,
                    attributionContext,
                    httpContext,
                    cancellationToken)
                .ConfigureAwait(false) is { } authorizationProblem)
        {
            return authorizationProblem;
        }

        if (await SessionOwnershipGate
                .CheckRunSessionAsync(
                    services.GetService<IOptionsMonitor<TraconSessionOwnershipOptions>>(),
                    attributionContext,
                    services.GetRequiredService<ISessionStore>(),
                    request.SessionId,
                    httpContext,
                    cancellationToken)
                .ConfigureAwait(false) is { } ownershipProblem)
        {
            return ownershipProblem;
        }

        if (await QuotaGate
                .CheckAsync(services.GetService<QuotaEnforcer>(), tenantContext, name, httpContext, cancellationToken)
                .ConfigureAwait(false) is { } quotaProblem)
        {
            return quotaProblem;
        }

        // Pre-flight context-window check (phase 62, F-59): disabled by default, and even when enabled it never touches
        // a provider - only PreflightGate/ContextWindowEstimator run.
        if (await PreflightGate
                .CheckAsync(
                    services.GetRequiredService<IOptionsMonitor<TraconOptions>>(),
                    services.GetRequiredService<ContextWindowEstimator>(),
                    services.GetRequiredService<IAgentCatalog>(),
                    name,
                    request.Message,
                    cancellationToken)
                .ConfigureAwait(false) is { } preflightProblem)
        {
            return preflightProblem;
        }

        return null;
    }

    /// <summary>
    /// Resolves the agent of a run request and checks everything about the request that can still be refused with a
    /// proper status code: the attachments, the parameters, the client-side tool results and the tool input answers.
    /// </summary>
    /// <param name="name">The agent name.</param>
    /// <param name="request">The run request.</param>
    /// <param name="prefix">The path prefix attachment references are built with.</param>
    /// <param name="httpContext">The request.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The turn that is ready to run, or the refusal to answer with.</returns>
    public static async ValueTask<TurnPreparation> PrepareAsync(
        string name,
        AgentRunRequest request,
        string prefix,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var services = httpContext.RequestServices;
        var catalog = services.GetRequiredService<IAgentCatalog>();
        var sessions = services.GetRequiredService<AgentSessionManager>();
        var attachmentStore = services.GetRequiredService<IAttachmentStore>();
        var tenantContext = services.GetRequiredService<ITenantContext>();
        var experimentAssignment = services.GetRequiredService<ExperimentAssignmentResolver>();
        var definitionStore = services.GetRequiredService<IAgentDefinitionStore>();
        var compiler = services.GetRequiredService<AgentDefinitionCompiler>();
        var decorators = services.GetServices<IAgentDecorator>();
        var optionsMonitor = services.GetRequiredService<IOptionsMonitor<TraconOptions>>();

        // Approval decisions and tool results are valid requests on their own:
        // a user approving a pending call or answering a client-side tool call
        // does not write a new message.
        if (string.IsNullOrWhiteSpace(request.Message) &&
            request.Approvals.Count == 0 &&
            request.ToolResults.Count == 0 &&
            request.InputResponses.Count == 0 &&
            request.AttachmentIds.Count == 0)
        {
            return TurnPreparation.Refused(Results.Problem(
                title: "Empty request",
                detail: "One of 'message', 'attachmentIds', 'approvals', 'toolResults', or 'inputResponses' is required.",
                statusCode: StatusCodes.Status400BadRequest));
        }

        if (request.Approvals.Count > 0 && string.IsNullOrWhiteSpace(request.SessionId))
        {
            return TurnPreparation.Refused(Results.Problem(
                title: "Session required for approval",
                detail: "A pending approval request lives in session history; 'sessionId' is required.",
                statusCode: StatusCodes.Status400BadRequest));
        }

        if (request.ToolResults.Count > 0 && string.IsNullOrWhiteSpace(request.SessionId))
        {
            return TurnPreparation.Refused(Results.Problem(
                title: "Session required for tool result",
                detail: "A pending client-side tool call lives in session history; 'sessionId' is required.",
                statusCode: StatusCodes.Status400BadRequest));
        }

        if (request.InputResponses.Count > 0 && string.IsNullOrWhiteSpace(request.SessionId))
        {
            return TurnPreparation.Refused(Results.Problem(
                title: "Session required for tool input",
                detail: "A pending tool input request lives in session history; 'sessionId' is required.",
                statusCode: StatusCodes.Status400BadRequest));
        }

        // Attachment ownership is validated before the stream starts: once the
        // response has begun, we cannot return a proper ProblemDetails.
        var attachments = new List<AttachmentDescriptor>(request.AttachmentIds.Count);

        foreach (var attachmentId in request.AttachmentIds)
        {
            if (await attachmentStore.GetAsync(tenantContext.TenantId, attachmentId, cancellationToken)
                    .ConfigureAwait(false) is not { } descriptor)
            {
                return TurnPreparation.Refused(Results.Problem(
                    title: "Attachment not found",
                    detail: $"There is no attachment with id '{attachmentId}', or it does not belong to this tenant.",
                    statusCode: StatusCodes.Status400BadRequest));
            }

            attachments.Add(descriptor);
        }

        // The id is generated here (not inside AgentRunStream.ExecuteAsync): the
        // experiment assignment key (session id ?? run id) must be known before
        // the stream starts.
        var runId = TraconId.NewId();
        var assignmentKey = request.SessionId ?? runId.ToString("D");

        var assignment = await experimentAssignment
            .ResolveAsync(tenantContext.TenantId, name, assignmentKey, cancellationToken)
            .ConfigureAwait(false);

        // 🚨 Missing/unknown parameters are checked BEFORE the run starts, so
        // the response is a proper ProblemDetails - this is not possible once
        // the stream has started. The gate is a no-op (Definition null,
        // Problem null) for the overwhelming majority of agents that declare
        // no parameter schema at all.
        var parameterGate = await AgentParameterGate
            .CheckAsync(optionsMonitor, definitionStore, httpContext.RequestServices, name, assignment?.Version, request.Parameters, cancellationToken)
            .ConfigureAwait(false);

        if (parameterGate.Problem is { } parameterProblem)
        {
            return TurnPreparation.Refused(parameterProblem);
        }

        Microsoft.Agents.AI.AIAgent? agent;

        // Resolution compiles a declarative definition; an unknown tool or
        // provider fails here. The response has not started yet, so we can
        // still return a proper ProblemDetails - this is not possible once the
        // stream has started.
        try
        {
            if (parameterGate.Definition is { Parameters.Count: > 0 } sourceDefinition)
            {
                // 🚨 A parameterized run bypasses CompiledAgentCache entirely:
                // two runs of the same agent with different parameter values
                // must never share one compiled instance, and the cache has
                // no notion of "value" in its key. Unlike a tenant-specific
                // provider credential (BYOK), which only bypasses the cache
                // while staying inside IAgentCatalog.ResolveAsync, THIS bypass
                // skips the catalog entirely - so it must apply the SAME
                // AgentDecoratorPipeline by hand, or the run goes out
                // undecorated: no recording, no telemetry, no tool-approval gate.
                agent = await compiler
                    .CompileParameterizedAsync(sourceDefinition, request.Culture, request.Parameters, cancellationToken)
                    .ConfigureAwait(false);

                agent = AgentDecoratorPipeline.Apply(
                    agent,
                    AgentDescriptorProjection.FromDefinition(
                        sourceDefinition,
                        parameterGate.Origin,
                        AgentDefinitionLookup.SourceName(parameterGate.Origin)),
                    decorators);
            }
            else
            {
                agent = assignment is null
                    ? await catalog.ResolveAsync(name, request.Culture, cancellationToken).ConfigureAwait(false)
                    : await catalog.ResolveAsync(name, assignment.Version, request.Culture, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (TraconException ex)
        {
            // 🚨 A parameterized run bypasses IAgentCatalog entirely (see the
            // remark above), so a decorator or source failure here never
            // passes through CompositeAgentCatalog.HandleSourceFailure - the
            // one place that already logs and records the
            // tracon.agent_source.failures metric for the same
            // exception type. Without this, the failure is silently
            // swallowed into a 400 with no server-side trace at all.
            if (ex is TraconAgentSourceException)
            {
                httpContext.RequestServices.GetRequiredService<ILoggerFactory>()
                    .CreateLogger("Tracon.AgentEndpoints")
                    .LogError(ex, "Decorating a parameterized run of agent '{AgentName}' failed.", name);
            }

            return TurnPreparation.Refused(Results.Problem(
                title: "Agent compilation failed",
                detail: ex.Message,
                statusCode: StatusCodes.Status400BadRequest));
        }

        if (agent is null)
        {
            return TurnPreparation.Refused(Results.Problem(
                title: "Agent not found",
                detail: $"There is no agent named '{name}'.",
                statusCode: StatusCodes.Status404NotFound));
        }

        // 🚨 Client-side tool results are matched against pending calls BEFORE
        // the stream starts: the default streaming path sends the SSE headers
        // (200, text/event-stream) before BuildMessagesAsync runs, so a 400/409
        // ProblemDetails is no longer possible once execution reaches there
        // (same physical constraint as decision K-324). BuildMessagesAsync
        // matches again to build the actual message; matching has no side
        // effects, so repeating it is safe.
        if (request.ToolResults.Count > 0)
        {
            if (request.ToolResults.Any(static result =>
                    (result.Result?.Length ?? 0) > ClientToolResultResolver.MaxResultLength ||
                    (result.ErrorMessage?.Length ?? 0) > ClientToolResultResolver.MaxResultLength))
            {
                return TurnPreparation.Refused(Results.Problem(
                    title: "Tool result too large",
                    detail: $"'result' and 'errorMessage' cannot exceed {ClientToolResultResolver.MaxResultLength} " +
                            "characters; an unbounded tool result would consume the model's context window.",
                    statusCode: StatusCodes.Status400BadRequest));
            }

            var toolResultsSession = await sessions
                .GetOrCreateSessionAsync(agent, request.SessionId!, cancellationToken)
                .ConfigureAwait(false);

            var chatHistory = httpContext.RequestServices.GetRequiredService<Microsoft.Agents.AI.ChatHistoryProvider>();

            var match = await ClientToolResultResolver
                .MatchAsync(
                    request.ToolResults,
                    agent,
                    toolResultsSession,
                    chatHistory,
                    httpContext.RequestServices.GetRequiredService<IToolRegistry>(),
                    cancellationToken)
                .ConfigureAwait(false);

            switch (match.Kind)
            {
                case ClientToolResultMatchKind.UnknownCallId:
                    return TurnPreparation.Refused(Results.Problem(
                        title: "Unknown tool call",
                        detail: $"There is no pending client-side tool call with id '{match.CallId}' in this session.",
                        statusCode: StatusCodes.Status400BadRequest));

                case ClientToolResultMatchKind.AlreadyAnswered:
                    return TurnPreparation.Refused(Results.Problem(
                        title: "Tool call already answered",
                        detail: $"The client-side tool call with id '{match.CallId}' already has a result.",
                        statusCode: StatusCodes.Status409Conflict));

                case ClientToolResultMatchKind.Success:
                default:
                    break;
            }
        }

        // 🚨 Answers to a tool's request for input are matched BEFORE the stream starts, for the same
        // reason as the client-side tool results above: a wrong answer is a 400/409 the caller can read
        // only while the response has not begun. Running the tool again under a wrong request would repeat
        // whatever the tool does, so no answer is skipped silently. BuildMessagesAsync matches again to
        // build the message; matching has no side effects.
        if (request.InputResponses.Count > 0)
        {
            var inputSession = await sessions
                .GetOrCreateSessionAsync(agent, request.SessionId!, cancellationToken)
                .ConfigureAwait(false);

            var inputMatch = await ToolInputResolver
                .MatchAsync(
                    request.InputResponses,
                    agent,
                    inputSession,
                    httpContext.RequestServices.GetRequiredService<Microsoft.Agents.AI.ChatHistoryProvider>(),
                    cancellationToken)
                .ConfigureAwait(false);

            switch (inputMatch.Kind)
            {
                case ToolInputMatchKind.UnknownRequestId:
                    return TurnPreparation.Refused(Results.Problem(
                        title: "Unknown tool input request",
                        detail: $"There is no pending tool input request with id '{inputMatch.RequestId}' in this session.",
                        statusCode: StatusCodes.Status400BadRequest));

                case ToolInputMatchKind.DuplicateRequestId:
                    return TurnPreparation.Refused(Results.Problem(
                        title: "Duplicate tool input answer",
                        detail: $"The tool input request '{inputMatch.RequestId}' is answered more than once in this request.",
                        statusCode: StatusCodes.Status400BadRequest));

                case ToolInputMatchKind.InvalidContent:
                    return TurnPreparation.Refused(Results.Problem(
                        title: "Invalid tool input answer",
                        detail: $"The answer to the tool input request '{inputMatch.RequestId}' is not valid: {inputMatch.Detail}",
                        statusCode: StatusCodes.Status400BadRequest));

                case ToolInputMatchKind.AlreadyAnswered:
                    return TurnPreparation.Refused(Results.Problem(
                        title: "Tool input request already answered",
                        detail: $"The tool input request '{inputMatch.RequestId}' already has an answer.",
                        statusCode: StatusCodes.Status409Conflict));

                case ToolInputMatchKind.Success:
                default:
                    break;
            }
        }

        // F-294: the run records the values it was bound with, defaults
        // included, so a replay and a continuation repeat them. Only an agent
        // that declares a schema has anything to record.
        var recordedValues = parameterGate.Definition is { Parameters.Count: > 0 } declared
            ? ParameterValueSet.Resolve(declared.Parameters, request.Parameters)
            : null;

        return TurnPreparation.Ready(new PreparedTurn(agent, name, request, attachments, prefix, runId, assignment, recordedValues));
    }

    /// <summary>
    /// Saves the session and, when that write loses a race, records the
    /// conflict on the run before letting the exception continue.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The run itself stays <see cref="RunStatus.Completed"/>, which is the
    /// truthful status: the model was called, tokens were spent, an answer
    /// was produced. What lost the race is the session write that happens
    /// AFTER the run, and the caller is told about that with a 409.
    /// </para>
    /// <para>
    /// Before this event the conflict left no trace in the
    /// record at all. The operator saw two successful runs for the same
    /// session, and the losing side existed only in a response the caller
    /// had already consumed - so "my message went missing" could not be
    /// answered from the control plane's own record.
    /// </para>
    /// <para>
    /// The write is best effort and never replaces the 409: observability
    /// does not break functionality. A store that cannot take the event
    /// leaves the caller's answer unchanged.
    /// </para>
    /// </remarks>
    public static async ValueTask SaveSessionRecordingConflictAsync(
        HttpContext httpContext,
        AgentSessionManager sessions,
        Microsoft.Agents.AI.AIAgent agent,
        Microsoft.Agents.AI.AgentSession session,
        Guid runId,
        CancellationToken cancellationToken)
    {
        try
        {
            await sessions.SaveSessionAsync(agent, session, cancellationToken).ConfigureAwait(false);
        }
        catch (TraconSessionConflictException conflict)
        {
            var runs = httpContext.RequestServices.GetRequiredService<IRunStore>();

            try
            {
                var lastSequence = await runs
                    .GetLastEventSequenceAsync(runId, cancellationToken)
                    .ConfigureAwait(false);

                await runs.AppendEventAsync(
                    new RunEvent
                    {
                        RunId = runId,
                        Sequence = (lastSequence ?? -1) + 1,
                        Type = RunEventType.SessionWriteConflicted,
                        Timestamp = DateTimeOffset.UtcNow,
                        Text = conflict.SessionId,
                        TenantId = httpContext.RequestServices.GetRequiredService<ITenantContext>().TenantId,
                    },
                    cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (OperationCancellation.IsFailure(ex, cancellationToken))
            {
                httpContext.RequestServices
                    .GetRequiredService<ILoggerFactory>()
                    .CreateLogger(typeof(AgentEndpoints))
                    .LogWarning(ex, "The session conflict on run {RunId} could not be recorded.", runId);
            }

            throw;
        }
    }

}

/// <summary>The outcome of <see cref="AgentTurnService.PrepareAsync"/>.</summary>
internal readonly struct TurnPreparation
{
    private TurnPreparation(PreparedTurn? turn, IResult? problem)
    {
        Turn = turn;
        Problem = problem;
    }

    /// <summary>Gets the turn that is ready to run; <see langword="null"/> when the request was refused.</summary>
    public PreparedTurn? Turn { get; }

    /// <summary>Gets the refusal to answer with; <see langword="null"/> when the turn is ready.</summary>
    public IResult? Problem { get; }

    /// <summary>Creates the outcome of a turn that is ready to run.</summary>
    /// <param name="turn">The turn.</param>
    /// <returns>The outcome.</returns>
    public static TurnPreparation Ready(PreparedTurn turn) => new(turn, null);

    /// <summary>Creates the outcome of a refused request.</summary>
    /// <param name="problem">The refusal.</param>
    /// <returns>The outcome.</returns>
    public static TurnPreparation Refused(IResult problem) => new(null, problem);
}

/// <summary>
/// A pending tool approval, as the <c>approvals</c> SSE frame announces it: the request and how to show it.
/// </summary>
/// <param name="RequestId">The approval request's identity.</param>
/// <param name="ToolName">The tool the call waits to run.</param>
/// <param name="EntityType">The kind of entity the call acts on, when the tool names one.</param>
/// <param name="EntityId">The entity's identity.</param>
/// <param name="EntityName">The entity's display name.</param>
/// <param name="Message">The text the approval is shown with.</param>
internal sealed record PendingApprovalAnnouncement(
    string RequestId,
    string ToolName,
    string? EntityType,
    string? EntityId,
    string? EntityName,
    string? Message);

/// <summary>
/// The <c>inputs</c> SSE frame (and the <c>inputs</c> member of the non-streaming body): one request for user input that
/// suspended a tool call, sent right before <c>done</c>.
/// </summary>
/// <param name="RequestId">The request's identity.</param>
/// <param name="ToolName">The tool whose call is suspended.</param>
/// <param name="ServerName">The MCP server that asked.</param>
/// <param name="Message">What the server asks the user.</param>
/// <param name="RequestedSchema">The flat schema of the fields the server asked for.</param>
internal sealed record PendingToolInputAnnouncement(
    string RequestId,
    string ToolName,
    string ServerName,
    string Message,
    JsonElement RequestedSchema)
{
    /// <summary>Converts the pending requests of a run.</summary>
    /// <param name="pending">The requests.</param>
    /// <returns>The announcements, in order.</returns>
    public static PendingToolInputAnnouncement[] From(IReadOnlyList<PendingToolInput> pending)
        => [.. pending.Select(static input => new PendingToolInputAnnouncement(
            input.RequestId,
            input.ToolName,
            input.ServerName,
            input.Message,
            input.RequestedSchema))];
}

/// <summary>
/// A run request whose agent is resolved and whose answers are matched: everything needed to open the session, build the
/// messages and start the run.
/// </summary>
/// <param name="agent">The resolved agent.</param>
/// <param name="agentName">The agent name.</param>
/// <param name="request">The run request.</param>
/// <param name="attachments">The request's attachments, ownership checked.</param>
/// <param name="prefix">The path prefix attachment references are built with.</param>
/// <param name="runId">The identity the run is recorded under.</param>
/// <param name="assignment">The experiment arm the run was assigned to, if any.</param>
/// <param name="parameterValues">The agent parameter values the run records, defaults included.</param>
internal sealed class PreparedTurn(
    Microsoft.Agents.AI.AIAgent agent,
    string agentName,
    AgentRunRequest request,
    IReadOnlyList<AttachmentDescriptor> attachments,
    string prefix,
    Guid runId,
    ExperimentAssignment? assignment,
    IReadOnlyDictionary<string, string>? parameterValues)
{
    /// <summary>Gets the resolved agent.</summary>
    public Microsoft.Agents.AI.AIAgent Agent => agent;

    /// <summary>Gets the agent name.</summary>
    public string AgentName => agentName;

    /// <summary>Gets the run request.</summary>
    public AgentRunRequest Request => request;

    /// <summary>Gets the identity the run is recorded under.</summary>
    public Guid RunId => runId;

    /// <summary>
    /// Creates the options the run starts with: the run's identity, its experiment arm, its parameter values, and the
    /// hooks that publish what the run waits for.
    /// </summary>
    /// <param name="session">The open session; a run without one cannot be answered later, so it offers no tool input.</param>
    /// <param name="inputAnswers">The answers the replayed tool calls read, from <see cref="BuildMessagesAsync"/>.</param>
    /// <param name="onPendingApprovals">
    /// Called right before the run publishes the tool approvals it waits for; <see langword="null"/> when the surface does
    /// not announce them. A run without a session has nothing to announce: nobody can answer it.
    /// </param>
    /// <param name="onPendingInputs">Called right before the run publishes the tool inputs it waits for.</param>
    /// <returns>The run options.</returns>
    public TraconRunOptions CreateRunOptions(
        Microsoft.Agents.AI.AgentSession? session,
        IReadOnlyDictionary<string, IReadOnlyList<ToolInputAnswer>>? inputAnswers,
        Func<IReadOnlyList<PendingApprovalAnnouncement>, CancellationToken, ValueTask>? onPendingApprovals,
        Func<IReadOnlyList<PendingToolInputAnnouncement>, CancellationToken, ValueTask>? onPendingInputs)
        => new()
        {
            RunId = runId,
            AgentVersion = assignment?.Version,
            ExperimentId = assignment?.ExperimentId,
            Variant = assignment?.Variant,
            ParameterValues = parameterValues,

            // 🚨 Phase 142: this is the ONLY delivery path for a synchronous run's tool-approval presentation - the queue
            // path's mailbox (GET /api/approvals/pending) does not exist for a run started without 'Prefer: respond-async',
            // and the updates carry MAF's own ToolApprovalRequestContent verbatim, which has no field to carry a
            // presentation. The hook runs right before the stream's own end, which is how the console's approval card
            // learns an entity's name.
            BeforePendingApprovalIsPublished = onPendingApprovals is null
                ? null
                : async (producedMessages, presentations, hookCancellation) =>
                {
                    var requests = ChildRunApproval.CollectRequests(producedMessages);

                    // F-448: without a session nobody can answer the approval; the run ends with an error instead of an
                    // approval card.
                    if (requests.Count == 0 || session is null)
                    {
                        return;
                    }

                    var announcements = requests
                        .Select(approval =>
                        {
                            var presentation = presentations.GetValueOrDefault(approval.RequestId);

                            return new PendingApprovalAnnouncement(
                                approval.RequestId,
                                approval.ToolCall is FunctionCallContent call ? call.Name : approval.ToolCall.CallId,
                                presentation?.EntityType,
                                presentation?.EntityId,
                                presentation?.EntityName,
                                presentation?.Message);
                        })
                        .ToArray();

                    await onPendingApprovals(announcements, hookCancellation).ConfigureAwait(false);
                },

            // Phase 235: a tool's MCP server asked the user for input. Only a run with a session can be answered (the
            // answer is the next turn of that session), so only such a run offers it; every other run's request is declined.
            AcceptsToolInput = session is not null,
            ToolInputAnswers = inputAnswers,
            BeforePendingToolInputIsPublished = onPendingInputs is null
                ? null
                : async (pendingInputs, hookCancellation) =>
                {
                    await onPendingInputs(PendingToolInputAnnouncement.From(pendingInputs), hookCancellation)
                        .ConfigureAwait(false);
                },
        };
    /// <summary>
    /// Builds the messages to send: approval responses, then client-side
    /// tool results, then the user message — whichever of these apply.
    /// </summary>
    /// <remarks>
    /// Both approval responses and tool results come BEFORE the user
    /// message. Microsoft Agent Framework cannot process a new user
    /// message without answering a pending call first; the reverse order
    /// would leave the model facing an unanswered call.
    /// </remarks>
    public async ValueTask<(List<ChatMessage> Messages, IReadOnlyDictionary<string, IReadOnlyList<ToolInputAnswer>>? InputAnswers)> BuildMessagesAsync(
        HttpContext httpContext,
        Microsoft.Agents.AI.AgentSession? session,
        CancellationToken cancellationToken)
    {
        var messages = new List<ChatMessage>(2);
        var services = httpContext.RequestServices;
        IReadOnlyDictionary<string, IReadOnlyList<ToolInputAnswer>>? inputAnswers = null;

        if (request.Approvals.Count > 0 && session is not null)
        {
            var loggerFactory = services.GetRequiredService<ILoggerFactory>();

            var approvalMessage = await ToolApprovalResolver.BuildResponseMessageAsync(
                request.Approvals,
                agent,
                agentName,
                session,
                services.GetRequiredService<Microsoft.Agents.AI.ChatHistoryProvider>(),
                services.GetRequiredService<IToolApprovalRuleStore>(),
                services.GetRequiredService<ITenantContext>(),
                services.GetRequiredService<IToolRegistry>(),
                services.GetRequiredService<IAuditLog>(),
                services.GetRequiredService<IAuditActorResolver>(),
                loggerFactory.CreateLogger(typeof(ToolApprovalResolver).FullName!),
                services.GetRequiredService<TraconMetrics>(),
                cancellationToken).ConfigureAwait(false);

            if (approvalMessage is not null)
            {
                messages.Add(approvalMessage);
            }
        }

        // Re-matches results against pending calls (RunAsync already
        // validated this before the stream started — see the comment
        // there). A mismatch here means the pending call was answered or
        // removed between the two matches (a rare concurrent request);
        // the result is silently dropped rather than failing a run whose
        // headers may already be on the wire.
        if (request.ToolResults.Count > 0 && session is not null)
        {
            var loggerFactory = services.GetRequiredService<ILoggerFactory>();

            var match = await ClientToolResultResolver
                .MatchAsync(
                    request.ToolResults,
                    agent,
                    session,
                    services.GetRequiredService<Microsoft.Agents.AI.ChatHistoryProvider>(),
                    services.GetRequiredService<IToolRegistry>(),
                    cancellationToken)
                .ConfigureAwait(false);

            if (match.Kind == ClientToolResultMatchKind.Success)
            {
                var toolResultsMessage = await ClientToolResultResolver.BuildResponseMessageAsync(
                    match.Matched,
                    services.GetRequiredService<ITenantContext>(),
                    services.GetRequiredService<IAuditLog>(),
                    services.GetRequiredService<IAuditActorResolver>(),
                    loggerFactory.CreateLogger(typeof(ClientToolResultResolver).FullName!),
                    services.GetRequiredService<TraconMetrics>(),
                    cancellationToken).ConfigureAwait(false);

                if (toolResultsMessage is not null)
                {
                    messages.Add(toolResultsMessage);
                }
            }
        }

        // Answers to a tool's request for input (Phase 235) run the suspended tool call again; like
        // the two blocks above they come BEFORE the user message. Matching again here, after the
        // endpoint did, is side-effect free; a request answered in between is dropped like a tool
        // result is, because the headers may already be on the wire.
        if (request.InputResponses.Count > 0 && session is not null)
        {
            var loggerFactory = services.GetRequiredService<ILoggerFactory>();

            var inputMatch = await ToolInputResolver
                .MatchAsync(
                    request.InputResponses,
                    agent,
                    session,
                    services.GetRequiredService<Microsoft.Agents.AI.ChatHistoryProvider>(),
                    cancellationToken)
                .ConfigureAwait(false);

            if (inputMatch.Kind == ToolInputMatchKind.Success)
            {
                var continuation = await ToolInputResolver.BuildContinuationAsync(
                    inputMatch.Matched,
                    services.GetRequiredService<ITenantContext>(),
                    services.GetRequiredService<IAuditLog>(),
                    services.GetRequiredService<IAuditActorResolver>(),
                    loggerFactory.CreateLogger(typeof(ToolInputResolver).FullName!),
                    services.GetRequiredService<TraconMetrics>(),
                    cancellationToken).ConfigureAwait(false);

                messages.AddRange(continuation.Messages);

                if (continuation.Answers.Count > 0)
                {
                    inputAnswers = continuation.Answers;
                }
            }
        }

        // 🚨 Documents are their own messages, added BEFORE the user's
        // message: each is wrapped in a delimiter and marked through
        // AIContent.AdditionalProperties (DocumentChannelMessageBuilder)
        // so the model - and the run record - can tell reference text
        // apart from the instructions and from the actual request.
        foreach (var document in request.Documents)
        {
            messages.Add(DocumentChannelMessageBuilder.Build(document));
        }

        var contents = new List<AIContent>();

        if (!string.IsNullOrWhiteSpace(request.Message))
        {
            contents.Add(new TextContent(request.Message));
        }

        // Binary content is NOT carried here: only a small UriContent reference
        // is added. The actual bytes are resolved right before the model
        // call, BEFORE being sent to the provider (see AttachmentResolvingChatClient).
        foreach (var attachment in attachments)
        {
            // The file's name travels with the reference; resolution hands it
            // to the model as DataContent.Name (F-580 class scan).
            contents.Add(new UriContent(AttachmentUriReference.Create(prefix, attachment.Id), attachment.MediaType)
            {
                AdditionalProperties = new() { [AttachmentUriReference.NameProperty] = attachment.FileName },
            });
        }

        if (contents.Count > 0)
        {
            messages.Add(new ChatMessage(ChatRole.User, contents));
        }

        return (messages, inputAnswers);
    }
}
