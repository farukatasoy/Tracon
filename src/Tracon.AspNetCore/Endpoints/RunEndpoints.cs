using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;

namespace Tracon;

/// <summary>
/// Run record and event stream endpoints.
/// </summary>
internal static class RunEndpoints
{
    /// <summary>
    /// The maximum number of runs returned in a single tree.
    /// </summary>
    /// <remarks>
    /// The upper bound is deliberately much larger than the budget's
    /// <c>MaxTotalRuns</c> default (25): the tree must not look truncated on
    /// an installation with a raised budget. It is still not unbounded,
    /// though; this endpoint is not paginated.
    /// </remarks>
    private const int MaxTreeSize = 200;

    /// <summary>Maps the run endpoints.</summary>
    /// <param name="builder">The endpoint route builder.</param>
    /// <param name="options">The access and streaming settings.</param>
    /// <param name="roles">The resolved role policies.</param>
    /// <param name="prefix">
    /// The normalized path prefix. Needed to build the comparison location in
    /// the replay response.
    /// </param>
    public static void Map(
        IEndpointRouteBuilder builder,
        TraconEndpointOptions options,
        TraconRolePolicies roles,
        string prefix)
    {
        builder.MapGet("/api/runs", async Task<Results<Ok<IReadOnlyList<RunRecord>>, ProblemHttpResult>> (
                IRunStore runs,
                [FromServices] IRunAuthorizationHandler? runAuthorizationHandler,
                [FromServices] IRunAttributionContext? attributionContext,
                ITenantContext tenants,
                [FromQuery] string? agentName,
                [FromQuery] RunStatus? status,
                [FromQuery] RunKind? kind,
                [FromQuery] string? sessionId,
                [FromQuery] string? errorType,
                [FromQuery] string? userId,
                [FromQuery] string? label,
                [FromQuery] DateTimeOffset? startedAfter,
                [FromQuery] bool? includeChildren,
                [FromQuery] Guid? parentRunId,
                [FromQuery] Guid? rootRunId,
                [FromQuery] int? skip,
                [FromQuery] int? take,
                HttpContext httpContext,
                CancellationToken cancellationToken) =>
            {
                // 🚨 A denied list is REJECTED (403), never silently filtered:
                // there is no single run identity to hide, and filtering rows
                // out server-side would break the skip/take paging contract —
                // the same rule the session list follows (K-671).
                if (await RunAuthorizationGate
                        .CheckRunResourceAsync(
                            runAuthorizationHandler,
                            tenants,
                            runId: null,
                            agentName: null,
                            sessionId: null,
                            attributionContext,
                            RunAccess.Read,
                            NotAuthorized(),
                            httpContext,
                            cancellationToken)
                        .ConfigureAwait(false) is { } authorizationProblem)
                {
                    return authorizationProblem;
                }

                var (labelKey, labelValue) = RunAttributionGate.ParseLabelFilter(label);

                var records = await runs.QueryRunsAsync(
                    new RunQuery
                    {
                        AgentName = agentName,
                        Status = status,
                        Kind = kind,
                        SessionId = sessionId,
                        ErrorType = errorType,
                        UserId = userId,
                        LabelKey = labelKey,
                        LabelValue = labelValue,
                        StartedAfter = startedAfter,

                        // The default is root runs only: when an agent calls other
                        // agents, the list fills with rows the user did not start.
                        OnlyRootRuns = includeChildren is not true,
                        ParentRunId = parentRunId,
                        RootRunId = rootRunId,
                        Skip = Math.Max(skip ?? 0, 0),
                        Take = Math.Clamp(take ?? 50, 1, 200),
                    },
                    cancellationToken).ConfigureAwait(false);

                return TypedResults.Ok(records);
            })
            .RequireRole(roles.Reader)
            .RequireApiKeyScope(ApiKeyScope.RunsRead)
            .WithName("TraconListRuns")
            .WithTags("Tracon", "Runs")
            .WithSummary("Lists runs from newest to oldest.")
            .WithDescription(
                "By default, ONLY root runs are returned. To also see child runs, use " +
                "'includeChildren=true'; pass 'rootRunId' for an entire tree, or 'parentRunId' for " +
                "the direct children of a run. 'userId' narrows the list to one user's runs, and " +
                "'label' takes a 'key:value' pair ('label=team:payments'); a bare 'label=team' " +
                "matches any value of that key. Both dimensions are recorded from the server-side " +
                "IRunAttributionContext, never from the run request body. If a registered " +
                "IRunAuthorizationHandler denies the caller, the response is 403 — the list is " +
                "REJECTED, never silently filtered.")
            .ProducesProblem(StatusCodes.Status403Forbidden);

        builder.MapGet("/api/runs/{runId:guid}/tree", async Task<Results<Ok<IReadOnlyList<RunRecord>>, ProblemHttpResult>> (
                Guid runId,
                IRunStore runs,
                [FromServices] IRunAuthorizationHandler? runAuthorizationHandler,
                [FromServices] IRunAttributionContext? attributionContext,
                ITenantContext tenants,
                HttpContext httpContext,
                CancellationToken cancellationToken) =>
            {
                if (await runs.GetRunAsync(runId, cancellationToken).ConfigureAwait(false) is not { } record)
                {
                    return NotFound(runId);
                }

                if (await RunAuthorizationGate
                        .CheckRunResourceAsync(
                            runAuthorizationHandler,
                            tenants,
                            runId,
                            record.AgentName,
                            record.SessionId,
                            attributionContext,
                            RunAccess.Read,
                            NotFound(runId),
                            httpContext,
                            cancellationToken)
                        .ConfigureAwait(false) is { } authorizationProblem)
                {
                    return authorizationProblem;
                }

                // The tree is always fetched from the ROOT. A request from a child
                // run's detail also returns the whole tree; without seeing sibling
                // branches, the user cannot tell where in the tree they are.
                var rootRunId = record.RootRunId ?? record.Id;

                var tree = await runs.QueryRunsAsync(
                    new RunQuery
                    {
                        RootRunId = rootRunId,
                        OnlyRootRuns = false,
                        Take = MaxTreeSize,
                    },
                    cancellationToken).ConfigureAwait(false);

                return TypedResults.Ok(tree);
            })
            .RequireRole(roles.Reader)
            .RequireApiKeyScope(ApiKeyScope.RunsRead)
            .WithName("TraconGetRunTree")
            .WithTags("Tracon", "Runs")
            .WithSummary("Returns the entire tree a run belongs to, starting from the root.")
            .WithDescription(
                "The tree is always resolved from the ROOT, whichever member is asked for: a " +
                "request naming a child run still returns the whole tree, because without the " +
                "sibling branches a client cannot tell where in the tree that run sits. Each " +
                "entry carries its parent, so the shape is rebuilt on the client. At most 200 " +
                "runs are returned; a tree larger than that is truncated rather than paged.");

        builder.MapGet("/api/runs/{runId:guid}", async Task<Results<Ok<RunRecord>, ProblemHttpResult>> (
                Guid runId,
                IRunStore runs,
                [FromServices] IRunAuthorizationHandler? runAuthorizationHandler,
                [FromServices] IRunAttributionContext? attributionContext,
                ITenantContext tenants,
                HttpContext httpContext,
                CancellationToken cancellationToken) =>
            {
                if (await runs.GetRunAsync(runId, cancellationToken).ConfigureAwait(false) is not { } record)
                {
                    return NotFound(runId);
                }

                if (await RunAuthorizationGate
                        .CheckRunResourceAsync(
                            runAuthorizationHandler,
                            tenants,
                            runId,
                            record.AgentName,
                            record.SessionId,
                            attributionContext,
                            RunAccess.Read,
                            NotFound(runId),
                            httpContext,
                            cancellationToken)
                        .ConfigureAwait(false) is { } authorizationProblem)
                {
                    return authorizationProblem;
                }

                return TypedResults.Ok(record);
            })
            .RequireRole(roles.Reader)
            .RequireApiKeyScope(ApiKeyScope.RunsRead)
            .WithName("TraconGetRun")
            .WithTags("Tracon", "Runs")
            .WithSummary("Returns the summary of a single run.")
            .WithDescription(
                "The summary carries status, timings, token counts, and — when pricing is " +
                "configured — cost; it does not carry the conversation. Read the messages from " +
                "the events endpoint, and the recorded input from the input endpoint. A run row " +
                "is written when the run starts, so a run that is still going is readable here " +
                "with a non-terminal status.");

        builder.MapGet("/api/runs/{runId:guid}/events", async Task<Results<ProblemHttpResult, IResult>> (
                Guid runId,
                IRunStore runs,
                [FromServices] IRunAuthorizationHandler? runAuthorizationHandler,
                [FromServices] IRunAttributionContext? attributionContext,
                ITenantContext tenants,
                HttpContext httpContext,
                CancellationToken cancellationToken) =>
            {
                // The existence check happens before the stream starts; the status
                // code cannot be changed once the response has started. The
                // authorization check has to clear the same bar: a denial must
                // arrive as a status code, never as a stream that opens and
                // then stops.
                if (await runs.GetRunAsync(runId, cancellationToken).ConfigureAwait(false) is not { } record)
                {
                    return NotFound(runId);
                }

                if (await RunAuthorizationGate
                        .CheckRunResourceAsync(
                            runAuthorizationHandler,
                            tenants,
                            runId,
                            record.AgentName,
                            record.SessionId,
                            attributionContext,
                            RunAccess.Read,
                            NotFound(runId),
                            httpContext,
                            cancellationToken)
                        .ConfigureAwait(false) is { } authorizationProblem)
                {
                    return authorizationProblem;
                }

                return new RunEventStream(runId, runs, options.RunEventPollInterval);
            })
            .RequireRole(roles.Reader)
            .RequireApiKeyScope(ApiKeyScope.RunsRead)
            .WithName("TraconStreamRunEvents")
            .WithTags("Tracon", "Runs")
            .WithSummary("Streams a run's events over SSE; live and historical use the same path.")
            .WithDescription(
                "If the connection drops, the client resumes from its last sequence number using the " +
                "'Last-Event-ID' header. If the run is still in progress, the stream stays open until it completes. " +
                "This stream's frame names ('run.started', 'tool.invoking', ...) are a different, larger set than " +
                "the direct run-agent stream's ('run', 'update', 'approvals', 'done', 'error') — the two are " +
                "separate contracts, not one seen through two content types.")
            .Produces<string>(StatusCodes.Status200OK, contentType: "text/event-stream")
            .ProducesProblem(StatusCodes.Status404NotFound);

        builder.MapPost("/api/runs/{runId:guid}/cancel", CancelRunAsync)
            .RequireRole(roles.Operator)
            .RequireApiKeyScope(ApiKeyScope.RunsWrite)
            .WithName("TraconCancelRun")
            .WithTags("Tracon", "Runs")
            .WithSummary("Requests cancellation of a running run.")
            .WithDescription(
                "202 only reports that cancellation was REQUESTED; the final status is read from " +
                "'GET /api/runs/{id}'. Returns 409 if the run is not executing on this instance (a different " +
                "instance, or a restarted process). Canceling a root run also stops every child run in the " +
                "tree; canceling a child run on its own does not affect the root.");

        builder.MapPost("/api/runs/{runId:guid}/feedback", SaveFeedbackAsync)
            .RequireRole(roles.Operator)
            .RequireApiKeyScope(ApiKeyScope.RunsWrite)
            .WithName("TraconSaveRunFeedback")
            .WithTags("Tracon", "Runs")
            .WithSummary("Writes a score for a run or for a single message.")
            .Accepts<RunFeedbackRequest>("application/json")
            .WithDescription(
                "When the same author writes the same 'name' onto the same target (run or message) a second " +
                "time, the row is UPDATED, not a new row opened; a different name opens a new row, so one " +
                "reviewer can score a run for both 'helpfulness' and 'accuracy'. A blank 'name' becomes " +
                "'overall'. If 'messageId' is left blank, the score applies to the whole run. A 'categorical' " +
                "score carries 'textValue' instead of 'value'; every other kind carries 'value'.");

        builder.MapGet("/api/runs/{runId:guid}/feedback", ListFeedbackAsync)
            .RequireRole(roles.Reader)
            .RequireApiKeyScope(ApiKeyScope.RunsRead)
            .WithName("TraconListRunFeedback")
            .WithTags("Tracon", "Runs")
            .WithSummary("Lists all scores for a run.")
            .WithDescription(
                "Both human scores and scores written by automatic evaluators appear in one " +
                "list; the source is a field on each entry, not a separate endpoint. A run " +
                "belonging to another tenant is reported as 404 rather than 403, so the API " +
                "does not confirm that the run exists. A run with no scores returns an empty " +
                "list, not 404.");

        builder.MapDelete("/api/runs/{runId:guid}/feedback/{scoreId:guid}", DeleteFeedbackAsync)
            .RequireRole(roles.Operator)
            .RequireApiKeyScope(ApiKeyScope.RunsWrite)
            .WithName("TraconDeleteRunFeedback")
            .WithTags("Tracon", "Runs")
            .WithSummary("Deletes a score.")
            .WithDescription(
                "The deletion is recorded in the audit trail, so removing a score is itself " +
                "traceable. The run must belong to the calling tenant; otherwise the response " +
                "is 404. An unknown score id also returns 404, so repeating the call is not " +
                "idempotent. Aggregate statistics computed from scores are recalculated on the " +
                "next read rather than adjusted here.");

        builder.MapGet("/api/runs/{runId:guid}/input", GetRunInputAsync)
            .RequireRole(roles.Reader)
            .RequireApiKeyScope(ApiKeyScope.RunsRead)
            .WithName("TraconGetRunInput")
            .WithTags("Tracon", "Runs")
            .WithSummary("Returns the recorded input messages for a run.")
            .WithDescription(
                "Returns 404 for a run that started while input recording was disabled " +
                "(Tracon:RunRecording:RecordRunInput = false), or that was deleted by a retention " +
                "policy; such a run cannot be replayed.");

        builder.MapGet("/api/runs/{a:guid}/compare/{b:guid}", CompareRunsAsync)
            .RequireRole(roles.Reader)
            .RequireApiKeyScope(ApiKeyScope.RunsRead)
            .WithName("TraconCompareRuns")
            .WithTags("Tracon", "Runs")
            .WithSummary("Returns the summaries of two runs side by side.")
            .WithDescription(
                "The diff is NOT computed on the server; the endpoint returns the two summaries and the UI " +
                "shows the comparison — the same pattern as the agent definition version diff.");

        builder.MapPost("/api/runs/{runId:guid}/replay", async (
                Guid runId,
                [FromServices] RunReplayService replays,
                [FromServices] IAuditLog auditLog,
                [FromServices] IAuditActorResolver actorResolver,
                [FromServices] ITenantContext tenants,
                [FromServices] ILoggerFactory loggerFactory,
                [FromServices] IAuthorizationService? authorization,
                [FromServices] IRunAuthorizationHandler? runAuthorizationHandler,
                [FromServices] IRunAttributionContext? attributionContext,
                [FromServices] TraconMetrics metrics,
                HttpContext httpContext,
                CancellationToken cancellationToken) => await ReplayRunAsync(
                    runId,
                    replays,
                    auditLog,
                    actorResolver,
                    tenants,
                    loggerFactory,
                    roles,
                    authorization,
                    runAuthorizationHandler,
                    attributionContext,
                    httpContext,
                    prefix,
                    metrics,
                    cancellationToken).ConfigureAwait(false))
            .RequireRole(roles.Operator)
            .RequireApiKeyScope(ApiKeyScope.RunsWrite)
            .WithName("TraconReplayRun")
            .WithTags("Tracon", "Runs")
            .WithSummary("Starts a new run with recorded input.")
            .Accepts<RunReplayRequest>("application/json")
            .WithDescription(
                "The input is preserved, the conditions change: 'agentVersion', 'modelId', and " +
                "'toolMode'. The default 'toolMode' value is 'ReplayTools', and NO tool actually runs — " +
                "recorded results are replayed. Replaying a call with no recorded result STOPS the " +
                "replay and returns 422. 'LiveTools' ACTUALLY runs tools, produces side effects, " +
                "requires the Admin role, and returns 409 if a tool requires approval. An agent carrying " +
                "a client-side tool (AddClientTool) cannot be replayed in ANY tool mode and also returns " +
                "409 — its body runs in the caller's browser and no call to it was recorded. " +
                "Replay is sessionless: if the source run belongs to a session, only that TURN's " +
                "input is replayed; the conversation history is not carried over. Replay STARTS a " +
                "run, so a registered IRunAuthorizationHandler is asked with the SOURCE run's id; a " +
                "denial returns 403 before any run row is opened.")
            .Produces<RunReplayResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status502BadGateway);
    }

    private static async Task<Results<Ok<RunInputResponse>, ProblemHttpResult>> GetRunInputAsync(
        Guid runId,
        [FromServices] IRunStore runs,
        [FromServices] IRunInputStore inputs,
        [FromServices] ITenantContext tenants,
        [FromServices] IRunAuthorizationHandler? runAuthorizationHandler,
        [FromServices] IRunAttributionContext? attributionContext,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var run = await runs.GetRunAsync(runId, cancellationToken).ConfigureAwait(false);

        if (run is null || !string.Equals(run.TenantId, tenants.TenantId, StringComparison.Ordinal))
        {
            return NotFound(runId);
        }

        if (await RunAuthorizationGate
                .CheckRunResourceAsync(
                    runAuthorizationHandler,
                    tenants,
                    runId,
                    run.AgentName,
                    run.SessionId,
                    attributionContext,
                    RunAccess.Read,
                    NotFound(runId),
                    httpContext,
                    cancellationToken)
                .ConfigureAwait(false) is { } authorizationProblem)
        {
            return authorizationProblem;
        }

        var input = await inputs
            .GetAsync(tenants.TenantId, runId, cancellationToken)
            .ConfigureAwait(false);

        if (input is null)
        {
            return TypedResults.Problem(
                title: "No recorded input",
                detail: $"Run '{runId}' has no recorded input. It may have started while input " +
                        "recording was disabled, or been deleted by a retention policy.",
                statusCode: StatusCodes.Status404NotFound);
        }

        return TypedResults.Ok(new RunInputResponse
        {
            RunId = input.RunId,
            CreatedAt = input.CreatedAt,
            Messages = input.Messages,
        });
    }

    private static async Task<Results<Ok<RunComparisonResponse>, ProblemHttpResult>> CompareRunsAsync(
        Guid a,
        Guid b,
        [FromServices] IRunStore runs,
        [FromServices] IRunScoreStore scores,
        [FromServices] ITenantContext tenants,
        [FromServices] IRunAuthorizationHandler? runAuthorizationHandler,
        [FromServices] IRunAttributionContext? attributionContext,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var left = await runs.GetRunAsync(a, cancellationToken).ConfigureAwait(false);
        var right = await runs.GetRunAsync(b, cancellationToken).ConfigureAwait(false);

        // The tenant boundary is checked separately for each side; "does not
        // exist" and "belongs to another tenant" return the same 404.
        if (left is null || !string.Equals(left.TenantId, tenants.TenantId, StringComparison.Ordinal))
        {
            return NotFound(a);
        }

        if (right is null || !string.Equals(right.TenantId, tenants.TenantId, StringComparison.Ordinal))
        {
            return NotFound(b);
        }

        // 🚨 A comparison reads TWO runs, so it passes the gate TWICE. Either
        // denial denies the whole comparison; asking once would let a caller
        // read a run they may not see by pairing it with one they may.
        if (await RunAuthorizationGate
                .CheckRunResourceAsync(
                    runAuthorizationHandler, tenants, a, left.AgentName, left.SessionId,
                    attributionContext, RunAccess.Read, NotFound(a), httpContext, cancellationToken)
                .ConfigureAwait(false) is { } leftProblem)
        {
            return leftProblem;
        }

        if (await RunAuthorizationGate
                .CheckRunResourceAsync(
                    runAuthorizationHandler, tenants, b, right.AgentName, right.SessionId,
                    attributionContext, RunAccess.Read, NotFound(b), httpContext, cancellationToken)
                .ConfigureAwait(false) is { } rightProblem)
        {
            return rightProblem;
        }

        return TypedResults.Ok(new RunComparisonResponse
        {
            Left = await BuildSideAsync(left, runs, scores, tenants, cancellationToken).ConfigureAwait(false),
            Right = await BuildSideAsync(right, runs, scores, tenants, cancellationToken).ConfigureAwait(false),
        });
    }

    private static async ValueTask<RunComparisonSide> BuildSideAsync(
        RunRecord run,
        IRunStore runs,
        IRunScoreStore scores,
        ITenantContext tenants,
        CancellationToken cancellationToken)
    {
        var tools = await runs.ListToolInvocationsAsync(run.Id, cancellationToken).ConfigureAwait(false);
        var runScores = await scores.ListAsync(tenants.TenantId, run.Id, cancellationToken).ConfigureAwait(false);

        return new RunComparisonSide
        {
            RunId = run.Id,
            AgentName = run.AgentName,
            AgentVersion = run.AgentVersion,
            ModelId = run.ModelId,
            Status = run.Status,
            DurationMs = run.CompletedAt is { } completed
                ? (long)(completed - run.StartedAt).TotalMilliseconds
                : null,
            Usage = run.Usage,
            Cost = run.Cost,
            ToolCallCount = tools.Count,
            ErrorClass = run.Error?.Class,
            ErrorMessage = run.Error?.Message,
            ReplayOfRunId = run.ReplayOfRunId,
            Output = await ReadOutputAsync(runs, run.Id, cancellationToken).ConfigureAwait(false),
            Scores = runScores,
        };
    }

    /// <summary>
    /// Reads the text a run produced from its event stream.
    /// </summary>
    /// <remarks>
    /// The non-streaming path (<c>RunCoreAsync</c>) writes both a
    /// <c>MessageDelta</c> for every <c>TextContent</c> and a final
    /// <c>MessageCompleted</c>; the streaming path produces only
    /// <c>MessageDelta</c> events and NEVER writes an equivalent "completed"
    /// event. Summing both would DOUBLE-COUNT the text on the non-streaming
    /// path; that is why <c>MessageCompleted</c> wins when it is present.
    /// </remarks>
    private static async ValueTask<string?> ReadOutputAsync(
        IRunStore runs,
        Guid runId,
        CancellationToken cancellationToken)
    {
        var completed = new StringBuilder();
        var deltas = new StringBuilder();

        await foreach (var runEvent in runs.ReadEventsAsync(runId, 0, cancellationToken).ConfigureAwait(false))
        {
            switch (runEvent.Type)
            {
                case RunEventType.MessageCompleted when runEvent.Text is { Length: > 0 } text:
                    completed.Append(text);
                    break;

                case RunEventType.MessageDelta when runEvent.Text is { Length: > 0 } delta:
                    deltas.Append(delta);
                    break;

                default:
                    break;
            }
        }

        var result = completed.Length > 0 ? completed.ToString() : deltas.ToString();

        return result.Length == 0 ? null : result;
    }

    /// <summary>
    /// Reruns a recorded run with the same input under changed conditions.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The run is <strong>non-streaming</strong> and returns a single JSON body.
    /// A streaming replay would add nothing to the design: what the comparison
    /// cares about is the final output, and the event stream is already
    /// readable via <c>GET /api/runs/{id}/events</c>.
    /// </para>
    /// <para>
    /// <see cref="ReplayToolMode.LiveTools"/> requires the <c>Admin</c> role.
    /// The endpoint is bound to <c>Operator</c>; the difference is enforced
    /// HERE, at runtime, because the role depends on the mode itself. If role
    /// policies are not registered at all (authorization disabled), no
    /// additional check is performed — the deployment is already explicitly
    /// unprotected.
    /// </para>
    /// </remarks>
    private static async Task<IResult> ReplayRunAsync(
        Guid runId,
        [FromServices] RunReplayService replays,
        [FromServices] IAuditLog auditLog,
        [FromServices] IAuditActorResolver actorResolver,
        [FromServices] ITenantContext tenants,
        [FromServices] ILoggerFactory loggerFactory,
        [FromServices] TraconRolePolicies roles,
        [FromServices] IAuthorizationService? authorization,
        [FromServices] IRunAuthorizationHandler? runAuthorizationHandler,
        [FromServices] IRunAttributionContext? attributionContext,
        HttpContext httpContext,
        string prefix,
        [FromServices] TraconMetrics metrics,
        CancellationToken cancellationToken)
    {
        var (bound, bindError) = await RequestBodyBinding
            .ReadAsync<RunReplayRequest>(httpContext, cancellationToken)
            .ConfigureAwait(false);

        if (bindError is not null)
        {
            return bindError;
        }

        var request = bound!;

        if (request.ToolMode == ReplayToolMode.LiveTools &&
            roles.Admin is { } adminPolicy &&
            authorization is not null)
        {
            var authorized = await authorization
                .AuthorizeAsync(httpContext.User, resource: null, adminPolicy)
                .ConfigureAwait(false);

            if (!authorized.Succeeded)
            {
                return Results.Problem(
                    title: "Insufficient permission",
                    detail: "'LiveTools' mode ACTUALLY runs tools and produces side effects; " +
                            "the Admin role is required. Use 'ReplayTools' or 'NoTools' for a " +
                            "side-effect-free replay.",
                    statusCode: StatusCodes.Status403Forbidden);
            }
        }

        var preparation = await replays.PrepareAsync(runId, request, cancellationToken).ConfigureAwait(false);

        if (preparation.Outcome != RunReplayOutcome.Ready)
        {
            return preparation.Outcome switch
            {
                RunReplayOutcome.RunNotFound => Results.Problem(
                    title: "Run not found",
                    detail: preparation.Detail,
                    statusCode: StatusCodes.Status404NotFound),
                RunReplayOutcome.InputNotFound => Results.Problem(
                    title: "No recorded input",
                    detail: preparation.Detail,
                    statusCode: StatusCodes.Status404NotFound),
                RunReplayOutcome.ApprovalRequired => Results.Problem(
                    title: "A tool requiring approval cannot run live",
                    detail: preparation.Detail,
                    statusCode: StatusCodes.Status409Conflict),
                RunReplayOutcome.ClientToolNotReplayable => Results.Problem(
                    title: "A client-side tool cannot be replayed",
                    detail: preparation.Detail,
                    statusCode: StatusCodes.Status409Conflict),
                _ => Results.Problem(
                    title: "Replay not supported",
                    detail: preparation.Detail,
                    statusCode: StatusCodes.Status400BadRequest),
            };
        }

        // 🚨 Replay is the SIXTH run-starting surface, and phase 139 missed it:
        // this endpoint's own summary says "Starts a new run with recorded
        // input". The gate is asked with RunAccess.Start, but through
        // CheckRunResourceAsync rather than CheckRunAsync, because the request
        // must carry the SOURCE run's id - without it a handler cannot tell
        // "start a run of agent X" apart from "replay somebody else's recorded
        // conversation", which is the whole reason replay needed covering.
        // It runs after PrepareAsync (where the source run's tenant ownership
        // is settled) and BEFORE any run row is opened, so a denied replay
        // leaves no row behind and consumes no quota.
        var sourceRun = preparation.SourceRun!;

        if (await RunAuthorizationGate
                .CheckRunResourceAsync(
                    runAuthorizationHandler,
                    tenants,
                    runId,
                    sourceRun.AgentName,
                    sessionId: null,
                    attributionContext,
                    RunAccess.Start,
                    NotAuthorized(),
                    httpContext,
                    cancellationToken)
                .ConfigureAwait(false) is { } authorizationProblem)
        {
            return authorizationProblem;
        }

        var newRunId = TraconId.NewId();

        try
        {
            var response = await preparation.Agent!.RunAsync(
                preparation.Messages,
                session: null,
                new TraconRunOptions
                {
                    RunId = newRunId,
                    AgentVersion = preparation.AgentVersion,
                    ReplayOfRunId = runId,
                },
                cancellationToken).ConfigureAwait(false);

            await AuditRecorder.WriteAsync(
                auditLog,
                actorResolver,
                loggerFactory.CreateLogger("Tracon.RunEndpoints"),
                metrics,
                tenants.TenantId,
                action: "run.replay",
                entity: $"run:{newRunId}",
                before: null,
                after: AuditPayload.Write(writer =>
                {
                    writer.WriteString("sourceRunId", runId.ToString());
                    writer.WriteString("toolMode", request.ToolMode.ToString());
                }),
                cancellationToken).ConfigureAwait(false);

            return TypedResults.Ok(new RunReplayResponse
            {
                RunId = newRunId,
                SourceRunId = runId,
                ToolMode = request.ToolMode,
                AgentVersion = preparation.AgentVersion,
                ModelId = preparation.ModelId,
                Output = response.Text,
                CompareLocation = $"{prefix}/api/runs/{runId}/compare/{newRunId}",
            });
        }
        catch (ReplayToolMismatchException ex)
        {
            // 🚨 422: a mismatched tool call is not an ERROR, it is a FINDING —
            // it means the new version calls a different tool. Silently
            // skipping it would leave the model with a gap it cannot see, and
            // running it live would produce an unwanted side effect (Phase 47,
            // Open Question 3).
            return Results.Problem(
                title: "Recorded tool result not found",
                detail: ex.Message,
                statusCode: StatusCodes.Status422UnprocessableEntity,
                extensions: new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["toolName"] = ex.ToolName,
                    ["arguments"] = ex.Arguments,
                    ["runId"] = newRunId,
                });
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // 🚨 The exception type is deliberately NOT caught with a narrow
            // list, a direct consequence of K-296: official provider SDKs do
            // NOT throw `HttpRequestException` (OpenAI throws
            // `System.ClientModel.ClientResultException`, Azure throws
            // `RequestFailedException`), and a narrow list would turn a real
            // model error into an unhandled 500. Measured: this endpoint was
            // first written with the list `TraconException or
            // InvalidOperationException or HttpRequestException`, and in the
            // sample application a `403 model_not_found` slipped through
            // exactly this way.
            //
            // The run record is already closed at this point (RunRecordingAgent
            // catches the error and marks the row Failed); the only remaining
            // work here is translating the error into a status code the
            // client can understand.
            var correlationId = SafeErrorText.NewCorrelationId();
            loggerFactory.CreateLogger("Tracon.RunEndpoints")
                .LogError(ex, "Run replay {RunId} failed. (ref: {CorrelationId})", newRunId, correlationId);

            return Results.Problem(
                title: "Replay failed",
                detail: SafeErrorText.ForPersistence(ex, correlationId),
                statusCode: StatusCodes.Status502BadGateway);
        }
    }

    private static async Task<Results<Ok<RunScore>, ProblemHttpResult>> SaveFeedbackAsync(
        Guid runId,
        HttpContext httpContext,
        [FromServices] IRunStore runs,
        [FromServices] IRunScoreStore scores,
        [FromServices] ITenantContext tenants,
        [FromServices] IAuditLog auditLog,
        [FromServices] IAuditActorResolver actorResolver,
        [FromServices] ILoggerFactory loggerFactory,
        [FromServices] IRunAuthorizationHandler? runAuthorizationHandler,
        [FromServices] IRunAttributionContext? attributionContext,
        [FromServices] TimeProvider? timeProvider,
        [FromServices] TraconMetrics metrics,
        CancellationToken cancellationToken)
    {
        var (bound, bindError) = await RequestBodyBinding
            .ReadAsync<RunFeedbackRequest>(httpContext, cancellationToken)
            .ConfigureAwait(false);

        if (bindError is not null)
        {
            return bindError;
        }

        var request = bound!;

        // A caller that sends no name keeps today's behavior: one score per
        // author per target, named 'overall' — the same name the migration
        // gave every row written before scores had names.
        var name = string.IsNullOrWhiteSpace(request.Name) ? RunScoreRules.DefaultName : request.Name;

        if (!RunScoreRules.IsValidName(name))
        {
            return InvalidFeedback(RunScoreRules.NameDescription);
        }

        if (request.Kind == RunScoreKind.Categorical)
        {
            if (string.IsNullOrWhiteSpace(request.TextValue))
            {
                return InvalidFeedback("A categorical score ('categorical') needs a 'textValue'.");
            }

            if (request.TextValue.Length > RunScoreRules.MaxTextValueLength)
            {
                return InvalidFeedback(
                    $"'textValue' can be at most {RunScoreRules.MaxTextValueLength} characters.");
            }

            if (request.Value is not null)
            {
                return InvalidFeedback("A categorical score ('categorical') carries no numeric 'value'.");
            }
        }
        else
        {
            if (request.TextValue is not null)
            {
                return InvalidFeedback("Only a categorical score ('categorical') carries a 'textValue'.");
            }

            if (request.Value is null)
            {
                return InvalidFeedback("A numeric score needs a 'value'.");
            }

            if (request.Kind == RunScoreKind.Binary && request.Value is not (0 or 1))
            {
                return InvalidFeedback("A binary score ('binary') can only be 0 or 1.");
            }

            if (request.Kind == RunScoreKind.Stars && request.Value is < 1 or > 5)
            {
                return InvalidFeedback("A star score ('stars') must be between 1 and 5.");
            }
        }

        var run = await runs.GetRunAsync(runId, cancellationToken).ConfigureAwait(false);

        // "Does not exist" and "belongs to another tenant" return the SAME
        // 404; a separate message would leak existence
        // (docs/arsiv/fazlar/31-GERI-BILDIRIM-VE-PUANLAMA.md, section 31.3).
        if (run is null || !string.Equals(run.TenantId, tenants.TenantId, StringComparison.Ordinal))
        {
            return NotFoundFeedback(runId);
        }

        if (await RunAuthorizationGate
                .CheckRunResourceAsync(
                    runAuthorizationHandler, tenants, runId, run.AgentName, run.SessionId,
                    attributionContext, RunAccess.Feedback, NotFoundFeedback(runId), httpContext, cancellationToken)
                .ConfigureAwait(false) is { } authorizationProblem)
        {
            return authorizationProblem;
        }

        var now = (timeProvider ?? TimeProvider.System).GetUtcNow();

        var saved = await scores.UpsertAsync(
            new RunScore
            {
                TenantId = tenants.TenantId,
                RunId = runId,
                MessageId = string.IsNullOrWhiteSpace(request.MessageId) ? null : request.MessageId,
                Name = name,
                Kind = request.Kind,
                Value = request.Value,
                TextValue = request.TextValue,
                Comment = string.IsNullOrWhiteSpace(request.Comment) ? null : request.Comment,
                Source = "human",
                Author = actorResolver.Resolve(),
                CreatedAt = now,
            },
            cancellationToken).ConfigureAwait(false);

        await AuditRecorder.WriteAsync(
            auditLog,
            actorResolver,
            loggerFactory.CreateLogger("Tracon.RunEndpoints"),
            metrics,
            tenants.TenantId,
            action: "run.feedback.save",
            entity: $"run_score:{saved.Id}",
            before: null,
            after: AuditPayload.Write(writer =>
            {
                writer.WriteString("runId", runId.ToString());
                writer.WriteString("name", saved.Name);
                writer.WriteString("kind", saved.Kind.ToString());

                if (saved.Value is { } value)
                {
                    writer.WriteNumber("value", value);
                }
                else
                {
                    writer.WriteNull("value");
                }

                if (saved.TextValue is { } textValue)
                {
                    writer.WriteString("textValue", textValue);
                }
            }),
            cancellationToken).ConfigureAwait(false);

        return TypedResults.Ok(saved);
    }

    /// <summary>
    /// Requests cancellation of a running run.
    /// </summary>
    /// <remarks>
    /// Endpoint behavior:
    /// <list type="bullet">
    /// <item>
    /// If the run does not exist or belongs to another tenant, <c>404</c> (both are the
    /// same, to avoid leaking existence).
    /// </item>
    /// <item>If the run is registered, its source is canceled and <c>202</c> is returned.</item>
    /// <item>
    /// If the run is <see cref="RunStatus.Running"/> in <c>runs</c> but not registered,
    /// this instance is not executing it; <c>409</c> is returned.
    /// </item>
    /// <item>If the run has already ended, <c>409</c> is returned along with the current status.</item>
    /// <item>
    /// A run in the <see cref="RunStatus.Queued"/> status is NOT
    /// executing YET; it cannot be registered in <see cref="IRunCancellationRegistry"/>.
    /// In this case cancellation happens FROM THE QUEUE, via
    /// <c>IJobStore.CancelAsync</c> (Job.Id == RunId), and the
    /// <c>runs</c> row is closed directly to <see cref="RunStatus.Canceled"/>
    /// right here — since the worker never picked up the job,
    /// <c>RunRecordingAgent</c> will never close this row.
    /// </item>
    /// </list>
    /// </remarks>
    private static async Task<Results<Accepted<RunRecord>, ProblemHttpResult>> CancelRunAsync(
        Guid runId,
        [FromServices] IRunStore runs,
        [FromServices] IJobStore jobs,
        [FromServices] IRunCancellationRegistry cancellations,
        [FromServices] ITenantContext tenants,
        [FromServices] IAuditLog auditLog,
        [FromServices] IAuditActorResolver actorResolver,
        [FromServices] ILoggerFactory loggerFactory,
        [FromServices] IRunAuthorizationHandler? runAuthorizationHandler,
        [FromServices] IRunAttributionContext? attributionContext,
        [FromServices] TimeProvider? timeProvider,
        [FromServices] TraconMetrics metrics,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var run = await runs.GetRunAsync(runId, cancellationToken).ConfigureAwait(false);

        // "Does not exist" and "belongs to another tenant" return the SAME
        // 404; a separate message would leak existence (the same rationale
        // applies in SaveFeedbackAsync).
        if (run is null || !string.Equals(run.TenantId, tenants.TenantId, StringComparison.Ordinal))
        {
            return NotFound(runId);
        }

        // Checked before the status is read: a denied caller must not learn
        // whether the run is still executing.
        if (await RunAuthorizationGate
                .CheckRunResourceAsync(
                    runAuthorizationHandler, tenants, runId, run.AgentName, run.SessionId,
                    attributionContext, RunAccess.Cancel, NotFound(runId), httpContext, cancellationToken)
                .ConfigureAwait(false) is { } authorizationProblem)
        {
            return authorizationProblem;
        }

        if (run.Status == RunStatus.Queued)
        {
            if (!await jobs.CancelAsync(tenants.TenantId, runId, cancellationToken).ConfigureAwait(false))
            {
                return TypedResults.Problem(
                    title: "Run already ended",
                    detail: $"Run '{runId}' is already in status '{run.Status}'.",
                    statusCode: StatusCodes.Status409Conflict);
            }

            var now = (timeProvider ?? TimeProvider.System).GetUtcNow();

            await runs.CompleteRunAsync(
                // `run` came from a read already FILTERED by tenant; the
                // expected tenant is its own record (K-355).
                new RunCompletion
                {
                    RunId = runId,
                    Status = RunStatus.Canceled,
                    CompletedAt = now,
                    TenantId = run.TenantId,
                },
                cancellationToken).ConfigureAwait(false);

            await AuditRecorder.WriteAsync(
                auditLog,
                actorResolver,
                loggerFactory.CreateLogger("Tracon.RunEndpoints"),
                metrics,
                tenants.TenantId,
                action: "run.cancel",
                entity: $"run:{runId}",
                before: null,
                after: null,
                cancellationToken).ConfigureAwait(false);

            return TypedResults.Accepted($"/api/runs/{runId}", run with { Status = RunStatus.Canceled, CompletedAt = now });
        }

        if (!cancellations.TryCancel(runId, tenants.TenantId))
        {
            return run.Status == RunStatus.Running
                ? TypedResults.Problem(
                    title: "Run is not executing on this instance",
                    detail: $"Run '{runId}' shows as 'Running' but is not registered in this process. " +
                            "It may be running on a different instance, or the process may have restarted mid-run.",
                    statusCode: StatusCodes.Status409Conflict)
                : TypedResults.Problem(
                    title: "Run already ended",
                    detail: $"Run '{runId}' is already in status '{run.Status}'.",
                    statusCode: StatusCodes.Status409Conflict);
        }

        await AuditRecorder.WriteAsync(
            auditLog,
            actorResolver,
            loggerFactory.CreateLogger("Tracon.RunEndpoints"),
            metrics,
            tenants.TenantId,
            action: "run.cancel",
            entity: $"run:{runId}",
            before: null,
            after: null,
            cancellationToken).ConfigureAwait(false);

        // 202: cancellation was only REQUESTED. cts.Cancel() is not a
        // guarantee; the agent sees the token at its next check point. The
        // final status is read from 'GET /api/runs/{id}'.
        return TypedResults.Accepted($"/api/runs/{runId}", run);
    }

    private static async Task<Results<Ok<IReadOnlyList<RunScore>>, ProblemHttpResult>> ListFeedbackAsync(
        Guid runId,
        [FromServices] IRunStore runs,
        [FromServices] IRunScoreStore scores,
        [FromServices] ITenantContext tenants,
        [FromServices] IRunAuthorizationHandler? runAuthorizationHandler,
        [FromServices] IRunAttributionContext? attributionContext,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var run = await runs.GetRunAsync(runId, cancellationToken).ConfigureAwait(false);

        if (run is null || !string.Equals(run.TenantId, tenants.TenantId, StringComparison.Ordinal))
        {
            return NotFoundFeedback(runId);
        }

        // Reading scores is Read, not Feedback: the scores belong to the run,
        // and a reader who may see the run may see what was scored on it.
        if (await RunAuthorizationGate
                .CheckRunResourceAsync(
                    runAuthorizationHandler, tenants, runId, run.AgentName, run.SessionId,
                    attributionContext, RunAccess.Read, NotFoundFeedback(runId), httpContext, cancellationToken)
                .ConfigureAwait(false) is { } authorizationProblem)
        {
            return authorizationProblem;
        }

        var list = await scores.ListAsync(tenants.TenantId, runId, cancellationToken).ConfigureAwait(false);

        return TypedResults.Ok(list);
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> DeleteFeedbackAsync(
        Guid runId,
        Guid scoreId,
        [FromServices] IRunStore runs,
        [FromServices] IRunScoreStore scores,
        [FromServices] ITenantContext tenants,
        [FromServices] IAuditLog auditLog,
        [FromServices] IAuditActorResolver actorResolver,
        [FromServices] ILoggerFactory loggerFactory,
        [FromServices] IRunAuthorizationHandler? runAuthorizationHandler,
        [FromServices] IRunAttributionContext? attributionContext,
        [FromServices] TraconMetrics metrics,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var run = await runs.GetRunAsync(runId, cancellationToken).ConfigureAwait(false);

        if (run is null || !string.Equals(run.TenantId, tenants.TenantId, StringComparison.Ordinal))
        {
            return NotFoundFeedback(runId);
        }

        if (await RunAuthorizationGate
                .CheckRunResourceAsync(
                    runAuthorizationHandler, tenants, runId, run.AgentName, run.SessionId,
                    attributionContext, RunAccess.Feedback, NotFoundFeedback(runId), httpContext, cancellationToken)
                .ConfigureAwait(false) is { } authorizationProblem)
        {
            return authorizationProblem;
        }

        // 🚨 The gate above was asked about `runId`, but the store deletes by
        // `scoreId` alone - IRunScoreStore.DeleteAsync takes no run id. Without
        // this check the two identities are never tied together, so a caller
        // allowed to score run A could delete a score belonging to run B by
        // naming A in the route: the gate would answer about A and the store
        // would delete B's row. The route's run must actually own the score.
        var runScores = await scores.ListAsync(tenants.TenantId, runId, cancellationToken).ConfigureAwait(false);

        if (!runScores.Any(score => score.Id == scoreId))
        {
            return ScoreNotFound(scoreId);
        }

        if (!await scores.DeleteAsync(tenants.TenantId, scoreId, cancellationToken).ConfigureAwait(false))
        {
            return ScoreNotFound(scoreId);
        }

        await AuditRecorder.WriteAsync(
            auditLog,
            actorResolver,
            loggerFactory.CreateLogger("Tracon.RunEndpoints"),
            metrics,
            tenants.TenantId,
            action: "run.feedback.delete",
            entity: $"run_score:{scoreId}",
            before: null,
            after: null,
            cancellationToken).ConfigureAwait(false);

        return TypedResults.NoContent();
    }

    private static ProblemHttpResult InvalidFeedback(string detail)
        => TypedResults.Problem(
            title: "Score invalid",
            detail: detail,
            statusCode: StatusCodes.Status400BadRequest);

    private static ProblemHttpResult NotFoundFeedback(Guid runId) => NotFound(runId);

    private static ProblemHttpResult NotFound(Guid runId)
        => TypedResults.Problem(
            title: "Run not found",
            detail: $"There is no run with id '{runId}'.",
            statusCode: StatusCodes.Status404NotFound);

    private static ProblemHttpResult ScoreNotFound(Guid scoreId)
        => TypedResults.Problem(
            title: "Score not found",
            detail: $"There is no score with id '{scoreId}'.",
            statusCode: StatusCodes.Status404NotFound);

    /// <summary>The response a denied LIST gets: 403, with no run identity in it.</summary>
    private static ProblemHttpResult NotAuthorized()
        => TypedResults.Problem(
            title: "Run not authorized",
            detail: "The registered IRunAuthorizationHandler denied this request.",
            statusCode: StatusCodes.Status403Forbidden);

    /// <summary>
    /// Writes events as SSE; polls for new events while the run is in progress.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The event store does not offer a notification channel, so the live
    /// stream is served by polling. Order: <em>the status is read first</em>,
    /// then events are drained. The other way around, a run that completes
    /// between the two steps could have its final events left unwritten when
    /// the loop ends.
    /// </para>
    /// <para>
    /// Because events are append-only, replay and live
    /// streaming go through the same code path; the client sees no difference.
    /// </para>
    /// </remarks>
    private sealed class RunEventStream(Guid runId, IRunStore runs, TimeSpan pollInterval) : IResult
    {
        public async Task ExecuteAsync(HttpContext httpContext)
        {
            ArgumentNullException.ThrowIfNull(httpContext);

            var cancellationToken = httpContext.RequestAborted;
            var next = SseWriter.ReadResumeSequence(httpContext.Request);
            var writer = await SseWriter.StartAsync(httpContext.Response, cancellationToken).ConfigureAwait(false);

            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    var snapshot = await runs.GetRunAsync(runId, cancellationToken).ConfigureAwait(false);
                    var wroteAny = false;

                    await foreach (var runEvent in runs
                        .ReadEventsAsync(runId, next, cancellationToken)
                        .ConfigureAwait(false))
                    {
                        await writer.WriteEventAsync(
                            runEvent.Sequence,
                            EventName(runEvent.Type),
                            JsonSerializer.Serialize(runEvent, JsonOptions),
                            cancellationToken).ConfigureAwait(false);

                        next = runEvent.Sequence + 1;
                        wroteAny = true;
                    }

                    // If the run has been deleted or has ended, all events have
                    // been written. 🚨 Phase 46: 'Queued' is also an EXPECTED
                    // intermediate status — the worker may not have picked up
                    // the job yet. Only a status other than Running/Queued (or
                    // the record's own absence) closes the stream; otherwise a
                    // client connecting right after the 202 would see the
                    // stream close before the job ever started.
                    if (snapshot is null || snapshot.Status is not (RunStatus.Running or RunStatus.Queued))
                    {
                        break;
                    }

                    if (!wroteAny)
                    {
                        await writer.WriteKeepAliveAsync("waiting", cancellationToken).ConfigureAwait(false);
                    }

                    await Task.Delay(pollInterval, cancellationToken).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                // The client disconnected; there is no one left to write to.
            }
        }

        private static JsonSerializerOptions JsonOptions { get; } = new(JsonSerializerDefaults.Web);

        /// <summary>
        /// Translates the event type to an SSE event name. These names are a
        /// <strong>stable</strong> contract; changing them breaks clients. The
        /// first ten are shipped and their strings are FIXED
        /// (<c>RunEventFrameNameContractTests</c> pins them); the rest complete
        /// the table using the names the console's <c>EVENT_STYLE</c>
        /// (<c>run-detail.tsx</c>) already carried.
        /// </summary>
        private static string EventName(RunEventType type) => type switch
        {
            RunEventType.RunStarted => "run.started",
            RunEventType.MessageDelta => "message.delta",
            RunEventType.MessageCompleted => "message.completed",
            RunEventType.ToolInvoking => "tool.invoking",
            RunEventType.ToolInvoked => "tool.invoked",
            RunEventType.ToolFailed => "tool.failed",
            RunEventType.RunCompleted => "run.completed",
            RunEventType.RunFailed => "run.failed",
            RunEventType.ChildRunStarted => "child.started",
            RunEventType.ChildRunCompleted => "child.completed",
            RunEventType.HistoryCompacted => "history.compacted",
            RunEventType.WorkflowStarted => "workflow.started",
            RunEventType.SuperStepStarted => "superstep.started",
            RunEventType.SuperStepCompleted => "superstep.completed",
            RunEventType.ExecutorInvoked => "executor.invoked",
            RunEventType.ExecutorCompleted => "executor.completed",
            RunEventType.ExecutorFailed => "executor.failed",
            RunEventType.WorkflowOutput => "workflow.output",
            RunEventType.WorkflowRequest => "workflow.request",
            RunEventType.RunAwaitingInput => "run.awaiting-input",
            RunEventType.ContentMasked => "content.masked",
            RunEventType.ContentBlocked => "content.blocked",
            RunEventType.ModelFallbackUsed => "model.fallback-used",
            RunEventType.ReasoningDelta => "reasoning.delta",
            RunEventType.DocumentAttached => "document.attached",
            RunEventType.ToolOutputTruncated => "tool.output-truncated",
            RunEventType.RunContinuationBlocked => "run.continuation-blocked",
            RunEventType.StructuredResponseRejected => "structured-response.rejected",
            RunEventType.StructuredResponseRepairAttempted => "structured-response.repair-attempted",
            RunEventType.Custom => "custom",
            RunEventType.ChildRunTimedOut => "child.timed-out",
            RunEventType.LoopIterationCompleted => "loop.iteration-completed",
            RunEventType.SessionWriteConflicted => "session.write-conflicted",
            _ => "unknown",
        };
    }
}

/// <summary>Request body for writing a run/message score.</summary>
public sealed record RunFeedbackRequest
{
    /// <summary>
    /// The score's stable, low-cardinality name. Left blank it becomes
    /// <c>overall</c>.
    /// </summary>
    /// <remarks>
    /// Must match <c>[A-Za-z0-9._-]{1,64}</c>, the rule
    /// <see cref="RunScoreRules"/> carries. The same author can write more than
    /// one name onto the same run; writing the same name twice updates the
    /// existing row.
    /// </remarks>
    public string? Name { get; init; }

    /// <summary>The format of the score.</summary>
    public required RunScoreKind Kind { get; init; }

    /// <summary>
    /// 0/1 for <see cref="RunScoreKind.Binary"/>, 1 to 5 for
    /// <see cref="RunScoreKind.Stars"/>, 0 to 100 for
    /// <see cref="RunScoreKind.Numeric"/>. Left out for
    /// <see cref="RunScoreKind.Categorical"/>, required otherwise.
    /// </summary>
    public double? Value { get; init; }

    /// <summary>
    /// The categorical label. Required for <see cref="RunScoreKind.Categorical"/>
    /// and rejected for every other kind.
    /// </summary>
    public string? TextValue { get; init; }

    /// <summary>The id of the scored message. If left blank, the score applies to the whole run.</summary>
    public string? MessageId { get; init; }

    /// <summary>Free-text comment.</summary>
    public string? Comment { get; init; }
}
