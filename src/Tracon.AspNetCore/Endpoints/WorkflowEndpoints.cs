using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Tracon;

/// <summary>
/// Workflow catalog, definition management, execution, and checkpoint endpoints.
/// </summary>
/// <remarks>
/// <para>
/// The <see cref="IWorkflowRunner"/> and <c>IWorkflowCheckpointStore</c>
/// parameters are <strong>explicitly</strong> marked with <c>[FromServices]</c>.
/// Minimal API, seeing a type that may not be registered, falls back to body
/// inference, and this mistake on a single endpoint breaks <em>all</em>
/// endpoints - (112 of 119 tests failed at once, and the
/// error message was disconnected from the cause).
/// </para>
/// <para>
/// If the execution engine is not registered, the run endpoints return
/// <c>501</c>; definition management continues to work. The same pattern was
/// established for the MCP refresh endpoint.
/// </para>
/// </remarks>
internal static class WorkflowEndpoints
{
    private static JsonSerializerOptions JsonOptions { get; } = new(JsonSerializerDefaults.Web);

    /// <summary>Maps the workflow endpoints.</summary>
    /// <param name="builder">The endpoint group.</param>
    /// <param name="roles">The resolved role policies.</param>
    /// <param name="prefix">The path prefix a queued run's <c>Location</c> starts with.</param>
    /// <param name="idempotencyFilter">
    /// The <c>Idempotency-Key</c> filter of the three run-starting endpoints. It accepts a
    /// key only together with <c>Prefer: respond-async</c>: their synchronous answer is a
    /// stream, which cannot be stored and replayed.
    /// </param>
    /// <param name="runEventPollInterval">
    /// How often a repeated answer that follows a run reads that run's new events from the store.
    /// </param>
    public static void Map(
        IEndpointRouteBuilder builder,
        TraconRolePolicies roles,
        string prefix,
        IdempotencyFilter idempotencyFilter,
        TimeSpan runEventPollInterval)
    {
        builder.MapGet("/api/workflows", ListAsync)
            .RequireRole(roles.Reader)
            .RequireApiKeyScope(ApiKeyScope.WorkflowsRead)
            .WithName("TraconListWorkflows")
            .WithTags("Tracon", "Workflows")
            .WithSummary("Lists workflows defined in code and stored in the database.")
            .WithDescription(
                "What the list contains depends on whether the workflow engine is registered. " +
                "With the engine, both code-defined and stored workflows appear, because only " +
                "the engine can see the ones built in code. Without it, only stored definitions " +
                "are listed — managing definitions does not require the engine, but running them " +
                "does.");

        builder.MapGet("/api/workflows/functions", ListFunctionsAsync)
            .RequireRole(roles.Reader)
            .RequireApiKeyScope(ApiKeyScope.WorkflowsRead)
            .WithName("TraconListWorkflowFunctions")
            .WithTags("Tracon", "Workflows")
            .WithSummary("Lists function nodes registered in code.")
            .WithDescription(
                "Function nodes are defined only in code, with AddWorkflowFunction() - " +
                "the same code-only boundary AddTool draws for tools. This endpoint does not offer a write " +
                "path; the console picks from this list when an operator builds a Sequential workflow's " +
                "node list. A function that takes settings carries settingsSchema, a JSON schema of " +
                "the object a node of it may carry in 'settings'; a function that takes none has " +
                "settingsSchema null and its nodes must carry no settings. 'timeout' is the time limit " +
                "the function was registered with, which a node's own 'timeout' replaces; null means " +
                "none. The schema is for display: " +
                "saving the workflow does the checking. An empty list means no function was registered, " +
                "or the workflow engine was never turned on with UseWorkflows() - either way, existing " +
                "agent-only workflows are unaffected.");

        builder.MapGet("/api/workflows/{name}", GetAsync)
            .RequireRole(roles.Reader)
            .RequireApiKeyScope(ApiKeyScope.WorkflowsRead)
            .WithName("TraconGetWorkflow")
            .WithTags("Tracon", "Workflows")
            .WithSummary("Returns a single workflow definition.")
            .WithDescription(
                "Only stored definitions are editable and only they are returned here. A " +
                "code-defined workflow is listed and can be run but has no stored definition, so " +
                "it answers 404 with a distinct 'No editable definition' title — different from " +
                "the plain not-found title used for a name that does not exist at all. Read the " +
                "structure of a code-defined workflow from the graph endpoint instead.");

        builder.MapGet("/api/workflows/{name}/graph", GetGraphAsync)
            .RequireRole(roles.Reader)
            .RequireApiKeyScope(ApiKeyScope.WorkflowsRead)
            .WithName("TraconGetWorkflowGraph")
            .WithTags("Tracon", "Workflows")
            .WithSummary("Returns the compiled graph of the workflow.")
            .WithDescription(
                "Node IDs are identical to the executor IDs in run events; this is how the UI " +
                "colors nodes live. The response also carries the Mermaid text generated by " +
                "Microsoft Agent Framework.");

        builder.MapPut("/api/workflows/{name}", SaveAsync)
            .RequireRole(roles.Admin)
            .RequireApiKeyScope(ApiKeyScope.WorkflowsAdmin)
            .WithName("TraconSaveWorkflow")
            .WithTags("Tracon", "Workflows")
            .WithSummary("Creates or updates a workflow definition.")
            .WithDescription(
                "The definition is validated at save time with the same rules the compiler " +
                "applies, so a shape that could not run is rejected with 400 instead of failing " +
                "on the first run. Which fields are required depends on the kind — a manager " +
                "driven workflow needs its manager agent, for example. A function node's " +
                "'settings' must be a JSON object that fits the settings type its function " +
                "declares (see settingsSchema on GET /api/workflows/functions): an unknown field, " +
                "a missing required member and a value of the wrong type are all refused with a " +
                "message that names the node and the member, and so is any settings on an agent " +
                "node or on a function that declares none. Settings are stored in the definition: " +
                "never put a secret in them, name its configuration key instead. A node's " +
                "'timeout' (function or agent node, hh:mm:ss) must be from 1 ms to about 49.7 days, " +
                "30 seconds less on an agent node; it is not checked against the run's timeout. " +
                "An agent node's 'agentVersion' pins the step to that version of the agent; it must be " +
                "1 or more, only an agent node may carry one, and the agent must have that version (an " +
                "agent defined in code keeps no versions): anything else is refused with 400. " +
                "'runTimeout' bounds one run of this workflow: it may only shorten " +
                "Tracon:Workflows:RunTimeout unless the host sets Tracon:Workflows:MaxRunTimeout, " +
                "and a value above that ceiling is refused with 400. The call " +
                "replaces the whole definition: omitted fields are cleared, not merged. The name " +
                "comes from the path and is not taken from the body. The name 'functions' is " +
                "reserved (400): GET /api/workflows/functions uses it. Every save appends a " +
                "version rather than overwriting; the previous content stays readable through " +
                "GET /api/workflows/{name}/versions.")
            .Accepts<WorkflowSaveRequest>("application/json");

        builder.MapGet("/api/workflows/{name}/versions", ListVersionsAsync)
            .RequireRole(roles.Reader)
            .RequireApiKeyScope(ApiKeyScope.WorkflowsRead)
            .WithName("TraconListWorkflowVersions")
            .WithTags("Tracon", "Workflows")
            .WithSummary("Lists a workflow definition's version history, newest first.")
            .WithDescription(
                "Every entry is a full definition snapshot, not a delta, so a single entry is " +
                "enough to inspect or restore a past state. The workflow must have a current " +
                "stored definition; a deleted name returns 404 even though its versions are kept " +
                "(a version number is never reused). A workflow defined in code also returns 404, " +
                "with a reason that says so: it is not stored by Tracon and has no version history. " +
                "The 'agentVersion' of a run's row is the workflow version it was compiled from.")
            .ProducesProblem(StatusCodes.Status404NotFound);

        builder.MapPost("/api/workflows/{name}/rollback", RollbackAsync)
            .RequireRole(roles.Admin)
            .RequireApiKeyScope(ApiKeyScope.WorkflowsAdmin)
            .WithName("TraconRollbackWorkflow")
            .WithTags("Tracon", "Workflows")
            .WithSummary("Writes a workflow definition as a new version with the content of a previous version.")
            .WithDescription(
                "A rollback moves forward, not backward: the old content is appended as a NEW " +
                "version and the history is never rewritten, so the rollback itself stays " +
                "auditable ('workflow.rollback') and can be rolled back in turn. The content is " +
                "checked the way a save is: a version that names a function that is no longer " +
                "registered, or an agent version that no longer exists, is refused with 400 and " +
                "nothing is written. An unknown version number returns 404; a workflow defined " +
                "in code returns 409. A run that is waiting keeps the version it recorded, so a " +
                "rollback does not change what it resumes with.")
            .Accepts<WorkflowRollbackRequest>("application/json")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        builder.MapGet("/api/workflows/{name}/versions/{a:int}/diff/{b:int}", GetVersionDiffAsync)
            .RequireRole(roles.Reader)
            .RequireApiKeyScope(ApiKeyScope.WorkflowsRead)
            .WithName("TraconGetWorkflowVersionDiff")
            .WithTags("Tracon", "Workflows")
            .WithSummary("Returns two definition versions as raw JSON; the diff is computed in the UI.")
            .WithDescription(
                "The server does no diffing and takes no position on how a change should be " +
                "displayed; it returns both snapshots verbatim as 'left' and 'right' so the client " +
                "chooses the presentation. The two version numbers may be given in any order. When " +
                "either version is missing the response is 404 and names the one that was not found; " +
                "a workflow defined in code returns 404 saying it has no version history.")
            .ProducesProblem(StatusCodes.Status404NotFound);

        builder.MapDelete("/api/workflows/{name}", DeleteAsync)
            .RequireRole(roles.Admin)
            .RequireApiKeyScope(ApiKeyScope.WorkflowsAdmin)
            .WithName("TraconDeleteWorkflow")
            .WithTags("Tracon", "Workflows")
            .WithSummary("Deletes a workflow definition.")
            .WithDescription(
                "Only a stored definition can be deleted; a code-defined workflow is removed by " +
                "changing the application, and asking for one here returns 404. Runs and " +
                "checkpoints already recorded are kept, so past executions stay readable, but a " +
                "checkpoint cannot be resumed once the definition it needs is gone. The version " +
                "history is kept too, so a version number is never given to a second content: " +
                "saving the name again continues the numbering.");

        builder.MapPost("/api/workflows/{name}/run", (
                    string name,
                    [FromServices] IWorkflowRunner? runner,
                    [FromServices] QuotaEnforcer? quotaEnforcer,
                    [FromServices] IRunAuthorizationHandler? runAuthorizationHandler,
                    [FromServices] IRunAttributionContext? attributionContext,
                    [FromServices] ISessionStore sessionStore,
                    [FromServices] IOptionsMonitor<TraconSessionOwnershipOptions>? sessionOwnershipOptions,
                    [FromServices] IOptionsMonitor<TraconOptions> traconOptions,
                    [FromServices] ITenantContext tenantContext,
                    [FromServices] ITraconDrainState drainState,
                    HttpContext httpContext,
                    CancellationToken cancellationToken)
                => RunAsync(
                    name, runner, quotaEnforcer, runAuthorizationHandler, attributionContext, sessionStore,
                    sessionOwnershipOptions, traconOptions, tenantContext, drainState, prefix, httpContext, cancellationToken))
            .RequireRole(roles.Operator)
            .RequireApiKeyScope(ApiKeyScope.RunsWrite)
            .AddEndpointFilter(idempotencyFilter)
            .WithName("TraconRunWorkflow")
            .WithTags("Tracon", "Workflows")
            .WithSummary("Runs the workflow and streams its events over SSE, or queues it with 'Prefer: respond-async'.")
            .Accepts<WorkflowRunHttpRequest>(true, "application/json")
            .WithDescription(
                "Each frame carries a RunEvent. The first frame reports the run ID; every " +
                "agent invoked within the workflow opens its own runs row, viewable as a tree " +
                "via GET /api/runs/{runId}/tree. If a registered IRunAuthorizationHandler denies " +
                "the caller, the run does not start and a 403 is returned, before the quota check. " +
                "When session ownership is turned on, naming another user's session in 'sessionId' " +
                "is refused the same way, and so is opening a NEW session when no authenticated " +
                "identity can be resolved to own it. A streamed run belongs to the connection: if the " +
                "client stops reading, the run closes as Canceled. A request carrying " +
                "'Prefer: respond-async' is checked the same way and then queued: the answer is " +
                "'202 Accepted' with a 'Location' header and the run id, the run executes in the " +
                "background worker, and its events are read from GET /api/runs/{runId}/events. A queued " +
                "run does not depend on any connection, and with ResumeInterruptedRuns on it is resumed " +
                "from its latest checkpoint after a restart. 'Idempotency-Key' is accepted only together " +
                "with 'Prefer: respond-async' (400 otherwise); a repeated request returns the same 202 " +
                "and queues nothing. With queuing disabled (Tracon:AsyncRun:Enabled = false) the " +
                "preference is answered with 501.")
            // The success response is SSE by default, a 202 body with 'Prefer: respond-async';
            // if the engine is not registered, it returns 501.
            .Produces<string>(StatusCodes.Status200OK, contentType: "text/event-stream")
            .Produces<AcceptedRunResponse>(StatusCodes.Status202Accepted)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status429TooManyRequests)
            .ProducesProblem(StatusCodes.Status501NotImplemented)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        builder.MapGet("/api/workflows/runs/{runId:guid}/checkpoints", ListCheckpointsAsync)
            .RequireRole(roles.Reader)
            .RequireApiKeyScope(ApiKeyScope.RunsRead)
            .WithName("TraconListWorkflowCheckpoints")
            .WithTags("Tracon", "Workflows")
            .WithSummary("Lists the checkpoints of a workflow run.")
            .WithDescription(
                "Checkpoints are the points a run can be resumed from; each entry's id is what " +
                "the resume endpoint takes. A run belonging to another tenant is reported as " +
                "404 rather than 403, so the API does not confirm that it exists. An empty list " +
                "means the run wrote no checkpoint — checkpointing is a property of how the " +
                "workflow was built, not something this endpoint can turn on. Checkpoints are " +
                "subject to retention, so an old run may have none left. A denial by a " +
                "registered IRunAuthorizationHandler produces the same 404.");

        builder.MapPost("/api/workflows/runs/{runId:guid}/resume", (
                    Guid runId,
                    HttpContext httpContext,
                    [FromServices] IWorkflowRunner? runner,
                    [FromServices] IRunStore runs,
                    [FromServices] IWorkflowDefinitionStore definitions,
                    [FromServices] ITenantContext tenants,
                    [FromServices] IRunAuthorizationHandler? runAuthorizationHandler,
                    [FromServices] IRunAttributionContext? attributionContext,
                    [FromServices] ITraconDrainState drainState,
                    CancellationToken cancellationToken)
                => ResumeAsync(
                    runId, httpContext, runner, runs, definitions, tenants, runAuthorizationHandler, attributionContext,
                    drainState, prefix, cancellationToken))
            .RequireRole(roles.Operator)
            .RequireApiKeyScope(ApiKeyScope.RunsWrite)
            .AddEndpointFilter(idempotencyFilter)
            .WithName("TraconResumeWorkflow")
            .WithTags("Tracon", "Workflows")
            .WithSummary("Resumes from a checkpoint and streams events over SSE, or queues it with 'Prefer: respond-async'.")
            .WithDescription(
                "Resuming opens a NEW run rather than continuing the old one: the original run " +
                "row is never rewritten, and the first streamed frame reports the new run id. " +
                "The body is optional — without a checkpoint id the run resumes from its latest " +
                "checkpoint. The run resumes with the version of the workflow's definition that " +
                "it recorded ('agentVersion' of its row), so an edit made while it waited changes " +
                "nothing; 'definitionVersion' names another stored version, and a version the " +
                "workflow does not have is 400, never a silent fall-back to the current one. A " +
                "version whose graph differs from the checkpoint's fails the run with 'the graph's " +
                "structure differs'. A run that recorded no version resumes with the current " +
                "definition. The engine must be registered; otherwise the response is 501. The " +
                "source run and its checkpoint are checked before the stream opens: an unknown run " +
                "is 404, and a run that is not a workflow run or has no such checkpoint is 400. A " +
                "failure after the stream opens arrives as an SSE error frame. Resuming STARTS " +
                "a run, so a registered IRunAuthorizationHandler is asked with the SOURCE run's " +
                "id; a denial returns 403 before any run row is opened. With 'Prefer: respond-async' the " +
                "resume is queued and answered with '202 Accepted', like a queued run, after the same " +
                "checks. A queued resume is not counted against the quota, the same as a " +
                "streamed one. 'Idempotency-Key' is accepted only together with 'Prefer: respond-async'.")
            .Accepts<WorkflowResumeHttpRequest>(true, "application/json")
            .Produces<string>(StatusCodes.Status200OK, contentType: "text/event-stream")
            .Produces<AcceptedRunResponse>(StatusCodes.Status202Accepted)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status501NotImplemented)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        builder.MapGet("/api/workflows/runs/{runId:guid}/requests", ListRequestsAsync)
            .RequireRole(roles.Reader)
            .RequireApiKeyScope(ApiKeyScope.RunsRead)
            .WithName("TraconListWorkflowRequests")
            .WithTags("Tracon", "Workflows")
            .WithSummary("Lists a run's pending human input requests.")
            .WithDescription(
                "Only a run in the 'AwaitingInput' state returns requests. Requests are read " +
                "from the run's event stream; there is no separate table. A run belonging to " +
                "another tenant, and a denial by a registered IRunAuthorizationHandler, both " +
                "return the same 404.")
            .ProducesProblem(StatusCodes.Status404NotFound);

        builder.MapPost("/api/workflows/runs/{runId:guid}/respond", (
                    Guid runId,
                    HttpContext httpContext,
                    [FromServices] IWorkflowRunner? runner,
                    [FromServices] IRunStore runs,
                    [FromServices] IWorkflowDefinitionStore definitions,
                    [FromServices] ITenantContext tenants,
                    [FromServices] IRunAuthorizationHandler? runAuthorizationHandler,
                    [FromServices] IRunAttributionContext? attributionContext,
                    [FromServices] ITraconDrainState drainState,
                    CancellationToken cancellationToken)
                => RespondAsync(
                    runId, httpContext, runner, runs, definitions, tenants, runAuthorizationHandler, attributionContext,
                    drainState, prefix, runEventPollInterval, cancellationToken))
            .RequireRole(roles.Operator)
            .RequireApiKeyScope(ApiKeyScope.RunsWrite)
            .AddEndpointFilter(idempotencyFilter)
            .WithName("TraconRespondWorkflowRequest")
            .WithTags("Tracon", "Workflows")
            .WithSummary("Responds to a pending request and resumes the run, streamed or queued with 'Prefer: respond-async'.")
            .Accepts<WorkflowRespondHttpRequest>(true, "application/json")
            .WithDescription(
                "The response is matched to the request re-published with the same ID in the " +
                "execution resumed from the checkpoint. Resuming opens a NEW runs row; events " +
                "stream over SSE — so this STARTS a run, and a registered " +
                "IRunAuthorizationHandler is asked with the source run's id; a denial returns " +
                "403 before any run row is opened. The answer is checked before the stream opens: a " +
                "response of the wrong shape, a run that is not waiting and an unknown request id are " +
                "all 400; an unknown run is 404. The run continues with the version of the workflow's " +
                "definition it recorded, or with the stored version 'definitionVersion' names (a " +
                "version the workflow does not have is 400). With 'Prefer: respond-async' it is then queued: the reply " +
                "is '202 Accepted' and the run continues in the background worker, so a machine " +
                "that answers a request does not have to keep a connection open. The queued answer " +
                "is stored with the job until the job is removed; do not put a secret in it. " +
                "'Idempotency-Key' is accepted only together with 'Prefer: respond-async'. Every answer that " +
                "starts a run is written to the audit trail as 'workflow.respond' before the run starts, with a " +
                "SHA-256 of the answer and never the answer itself; when that entry cannot be written the answer " +
                "is refused with 500 and nothing is started. A workflow whose responseMode is 'SingleAnswer' takes " +
                "one answer per request: repeating the same answer (approved, text and data; the checkpoint and " +
                "version do not count) starts nothing and follows the run the first one started - the same SSE " +
                "frames read from the stored run, or the same 202 with 'Prefer: respond-async' - and a different " +
                "answer is 409, which names neither the run nor the other answer. An answer whose run failed or " +
                "was cancelled before the workflow received it can be given again and starts a new run. With " +
                "'Branch', the default, every answer opens a new run.")
            .Produces<string>(StatusCodes.Status200OK, contentType: "text/event-stream")
            .Produces<AcceptedRunResponse>(StatusCodes.Status202Accepted)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status501NotImplemented)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        builder.MapGet("/api/workflows/runs/{runId:guid}/responses", ListResponsesAsync)
            .RequireRole(roles.Reader)
            .RequireApiKeyScope(ApiKeyScope.RunsRead)
            .WithName("TraconListWorkflowResponses")
            .WithTags("Tracon", "Workflows")
            .WithSummary("Lists the answers given to a run's human input requests.")
            .WithDescription(
                "One entry per answer that started a run, oldest first: the request it answered, the run it " +
                "started (responseRunId), who answered and when, and a SHA-256 of the answer (answerHash) - " +
                "never the answer itself. The hash tells two answers apart; it does not hide an answer with " +
                "few possible values, such as a yes or no. 'claimScope' is set when the workflow takes one " +
                "answer per request; 'status' is Accepted until the workflow received the answer (Delivered), " +
                "and Abandoned for an answer whose run stopped and was started again. A run belonging to " +
                "another tenant, and a denial by a registered IRunAuthorizationHandler, both return the same 404.")
            .Produces<IReadOnlyList<WorkflowResponseRecord>>()
            .ProducesProblem(StatusCodes.Status404NotFound);
    }

    private static async Task<Results<Ok<IReadOnlyList<WorkflowDescriptor>>, ProblemHttpResult>> ListAsync(
        [FromServices] IWorkflowDefinitionStore store,
        [FromServices] ITenantContext tenants,
        [FromServices] IWorkflowRunner? runner,
        CancellationToken cancellationToken)
    {
        // If the engine is registered, the catalog is read from it: only it
        // can also see workflows defined in code. If not registered,
        // database definitions are still listed - definition management is
        // independent of the engine.
        if (runner is not null)
        {
            return TypedResults.Ok(await runner.ListAsync(cancellationToken).ConfigureAwait(false));
        }

        var stored = await store.ListAsync(tenants.TenantId, cancellationToken).ConfigureAwait(false);

        return TypedResults.Ok<IReadOnlyList<WorkflowDescriptor>>(
            [.. stored.Select(Describe)]);
    }

    private static Ok<IReadOnlyList<WorkflowFunctionResponse>> ListFunctionsAsync(
        [FromServices] IWorkflowFunctionCatalog? functions)
        // No function is ever registered, or UseWorkflows() was never called:
        // an empty list, not a 501 - listing functions does not need the
        // execution engine, the same reasoning ListAsync applies to workflows.
        => TypedResults.Ok<IReadOnlyList<WorkflowFunctionResponse>>(
            [.. (functions?.List() ?? []).Select(WorkflowFunctionResponse.From)]);

    private static async Task<Results<Ok<WorkflowDefinition>, ProblemHttpResult>> GetAsync(
        string name,
        [FromServices] IWorkflowDefinitionStore store,
        [FromServices] ITenantContext tenants,
        [FromServices] IWorkflowRunner? runner,
        CancellationToken cancellationToken)
    {
        var definition = await store.GetAsync(tenants.TenantId, name, cancellationToken).ConfigureAwait(false);

        if (definition is not null)
        {
            return TypedResults.Ok(definition);
        }

        // 🚨 A CODE-defined workflow (AddWorkflow(...)) is never written to
        // IWorkflowDefinitionStore — it is a free-standing Workflow graph and
        // NEVER has an editable WorkflowDefinition (see
        // CodeWorkflowRegistration). It is confusing for this endpoint to say
        // "not found" while the workflow appears in the list and can be run;
        // at least the REASON is distinguished.
        if (runner is not null && await runner.GetAsync(name, cancellationToken).ConfigureAwait(false) is not null)
        {
            return TypedResults.Problem(
                title: "No editable definition",
                detail: $"'{name}' is a workflow defined in code (AddWorkflow). It is listed and " +
                        "can be run, but does not carry an editable WorkflowDefinition in the database.",
                statusCode: StatusCodes.Status404NotFound);
        }

        return NotFound(name);
    }

    private static async Task<Results<Ok<WorkflowGraph>, ProblemHttpResult>> GetGraphAsync(
        string name,
        [FromServices] IWorkflowRunner? runner,
        CancellationToken cancellationToken)
    {
        // The graph is extracted from the COMPILED workflow; a graph drawn
        // from the definition would not show nodes added by built-in
        // patterns, and run events would not match any node. Requires the
        // execution engine.
        if (runner is null)
        {
            return NotRegistered();
        }

        WorkflowGraph? graph;

        try
        {
            graph = await runner.GetGraphAsync(name, cancellationToken).ConfigureAwait(false);
        }
        catch (TraconException exception)
        {
            // An invalid definition or an agent missing from the catalog:
            // this is an error the user can fix, not a server error.
            return TypedResults.Problem(
                title: "Workflow graph could not be extracted",
                detail: exception.Message,
                statusCode: StatusCodes.Status400BadRequest);
        }

        return graph is null ? NotFound(name) : TypedResults.Ok(graph);
    }

    private static async Task<Results<Ok<WorkflowDefinition>, ProblemHttpResult>> SaveAsync(
        string name,
        HttpContext httpContext,
        [FromServices] IWorkflowDefinitionStore store,
        [FromServices] ITenantContext tenants,
        [FromServices] IWorkflowFunctionCatalog? functions,
        [FromServices] IWorkflowRunTimeoutPolicy? runTimeoutPolicy,
        [FromServices] IAgentCatalog agents,
        [FromServices] IAgentDefinitionStore agentDefinitions,
        CancellationToken cancellationToken)
    {
        var (bound, bindError) = await RequestBodyBinding
            .ReadAsync<WorkflowSaveRequest>(httpContext, cancellationToken)
            .ConfigureAwait(false);

        if (bindError is not null)
        {
            return bindError;
        }

        var request = bound!;

        var definition = new WorkflowDefinition
        {
            Name = name,
            DisplayName = request.DisplayName,
            Description = request.Description,
            Kind = request.Kind,
            AgentNames = request.AgentNames ?? [],
            Nodes = request.Nodes ?? [],
            ManagerAgentName = request.ManagerAgentName,
            MaxIterations = request.MaxIterations,
            HandoffInstructions = request.HandoffInstructions,
            RequirePlanApproval = request.RequirePlanApproval,
            Parameters = request.Parameters ?? [],
            RunTimeout = request.RunTimeout,
            ResponseMode = request.ResponseMode,
        };

        if (await ValidateDefinitionAsync(
                definition, functions, runTimeoutPolicy, agents, agentDefinitions, because: null, cancellationToken)
            .ConfigureAwait(false) is { } problem)
        {
            return problem;
        }

        var saved = await store.SaveAsync(tenants.TenantId, definition, cancellationToken).ConfigureAwait(false);

        return TypedResults.Ok(saved);
    }

    /// <summary>
    /// Judges a definition the way a save does: the rules the compiler applies, the
    /// registered functions and their settings, the type chain and the agent version pins.
    /// </summary>
    /// <param name="definition">The definition about to be written.</param>
    /// <param name="functions">The function registry; <see langword="null"/> when the engine is not registered.</param>
    /// <param name="runTimeoutPolicy">The host's run timeout settings; <see langword="null"/> without the engine.</param>
    /// <param name="agents">The agent catalog.</param>
    /// <param name="agentDefinitions">The store that keeps stored agents' versions.</param>
    /// <param name="because">
    /// A sentence that opens the refusal, for a write that is not a plain save (a rollback
    /// writes content nobody typed just now); <see langword="null"/> for a save.
    /// </param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The <c>400</c> to answer with; <see langword="null"/> when the definition may be written.</returns>
    /// <remarks>
    /// One method for both writers on purpose: a rollback is a save of stored content, and a
    /// version saved a month ago can name a function that was unregistered since. Judged by
    /// a second, shorter copy of these checks it would write a definition no run can use.
    /// </remarks>
    private static async ValueTask<ProblemHttpResult?> ValidateDefinitionAsync(
        WorkflowDefinition definition,
        IWorkflowFunctionCatalog? functions,
        IWorkflowRunTimeoutPolicy? runTimeoutPolicy,
        IAgentCatalog agents,
        IAgentDefinitionStore agentDefinitions,
        string? because,
        CancellationToken cancellationToken)
    {
        var name = definition.Name;

        // Validation happens AT SAVE TIME. Accepting an invalid definition
        // and blowing up at run time would force the user to start a run
        // just to find the error. The workflow compiler uses the same
        // check; the rule lives in a single place (Tracon.Core).
        // F-382: a name a fixed route uses is refused before anything else is judged.
        if ((WorkflowDefinitionValidator.ReservedNameProblem(name) ?? WorkflowDefinitionValidator.Validate(definition)) is { } message)
        {
            return Invalid(because, message);
        }

        // F-405: the ceiling is the host's setting, read through the engine's seam.
        // Without the engine there is no ceiling to read and nothing to run the value;
        // the range was checked above.
        if (runTimeoutPolicy is not null &&
            WorkflowDefinitionValidator.RunTimeoutCeilingProblem(definition, runTimeoutPolicy.RunTimeout, runTimeoutPolicy.MaxRunTimeout) is { } ceilingProblem)
        {
            return Invalid(because, ceilingProblem);
        }

        // A function node's name IS checked at save time, unlike an agent
        // name (agents live in a store that can change after save; the
        // function registry is fixed in code for the process lifetime, so
        // checking early gives an honest guarantee - phase 71).
        foreach (var node in definition.Nodes)
        {
            if (node.Kind != WorkflowNodeKind.Function)
            {
                continue;
            }

            if (functions is null || !functions.Contains(node.Name))
            {
                return Invalid(
                    because,
                    $"Workflow '{name}' uses function '{node.Name}', but no such function is " +
                    "registered. Register it with AddWorkflowFunction() before referencing it " +
                    "from a workflow definition.");
            }

            // The settings are checked by the registry that knows the function's settings
            // type (F-295): the same read the compiler repeats when a run starts. Same
            // reasoning as the name above - the registry is fixed for the process lifetime,
            // so the early check is an honest guarantee.
            if (functions.ValidateSettings(node.Name, node.Settings) is { } settingsProblem)
            {
                return Invalid(because, $"Workflow '{name}': {settingsProblem}");
            }
        }

        // F-341: each node must take what the node before it passes on.
        // Microsoft Agent Framework drops a message no handler takes, without an
        // error, so a chain that breaks this would end every run with no
        // output. Checked after the names, so an unregistered function is
        // reported by name first; the compiler repeats the check.
        if (WorkflowDefinitionValidator.ValidateNodeTypes(definition, functions) is { } typeProblem)
        {
            return Invalid(because, typeProblem);
        }

        // Phase 200: a pinned step must name a version its agent has. Unlike an agent's NAME
        // (which a definition may name before the agent exists), a version cannot appear later,
        // so refusing it here is an honest guarantee; the compiler repeats the check because the
        // agent can be deleted after the save.
        if (await AgentVersionPin.FindProblemAsync(definition, agents, agentDefinitions, cancellationToken)
                .ConfigureAwait(false) is { } pinProblem)
        {
            return Invalid(because, pinProblem);
        }

        return null;

        static ProblemHttpResult Invalid(string? because, string detail)
            => TypedResults.Problem(
                title: "Workflow definition invalid",
                detail: because is null ? detail : $"{because} {detail}",
                statusCode: StatusCodes.Status400BadRequest);
    }

    private static async Task<Results<Ok<IReadOnlyList<WorkflowDefinition>>, ProblemHttpResult>> ListVersionsAsync(
        string name,
        [FromServices] IWorkflowDefinitionStore store,
        [FromServices] ITenantContext tenants,
        [FromServices] IWorkflowRunner? runner,
        CancellationToken cancellationToken)
    {
        if (await store.GetAsync(tenants.TenantId, name, cancellationToken).ConfigureAwait(false) is null)
        {
            return await NoVersionHistoryAsync(name, runner, cancellationToken).ConfigureAwait(false);
        }

        return TypedResults.Ok(
            await store.ListVersionsAsync(tenants.TenantId, name, cancellationToken).ConfigureAwait(false));
    }

    private static async Task<Results<Ok<WorkflowVersionDiffResponse>, ProblemHttpResult>> GetVersionDiffAsync(
        string name,
        int a,
        int b,
        [FromServices] IWorkflowDefinitionStore store,
        [FromServices] ITenantContext tenants,
        [FromServices] IWorkflowRunner? runner,
        CancellationToken cancellationToken)
    {
        // Checked BEFORE the two version reads: without it, a workflow defined in code
        // answers "has no version 1", which states that the workflow is versioned here
        // and only that number is missing. Both halves of that are wrong.
        if (await store.GetAsync(tenants.TenantId, name, cancellationToken).ConfigureAwait(false) is null)
        {
            return await NoVersionHistoryAsync(name, runner, cancellationToken).ConfigureAwait(false);
        }

        var left = await store.GetVersionAsync(tenants.TenantId, name, a, cancellationToken).ConfigureAwait(false);
        var right = await store.GetVersionAsync(tenants.TenantId, name, b, cancellationToken).ConfigureAwait(false);

        if (left is null || right is null)
        {
            return TypedResults.Problem(
                title: "Version not found",
                detail: $"Workflow '{name}' has no version {(left is null ? a : b)}.",
                statusCode: StatusCodes.Status404NotFound);
        }

        return TypedResults.Ok(new WorkflowVersionDiffResponse { Left = left, Right = right });
    }

    private static async Task<Results<Ok<WorkflowDefinition>, ProblemHttpResult>> RollbackAsync(
        string name,
        HttpContext httpContext,
        [FromServices] IWorkflowDefinitionStore store,
        [FromServices] ITenantContext tenants,
        [FromServices] IWorkflowFunctionCatalog? functions,
        [FromServices] IWorkflowRunTimeoutPolicy? runTimeoutPolicy,
        [FromServices] IAgentCatalog agents,
        [FromServices] IAgentDefinitionStore agentDefinitions,
        [FromServices] IWorkflowRunner? runner,
        CancellationToken cancellationToken)
    {
        var (bound, bindError) = await RequestBodyBinding
            .ReadAsync<WorkflowRollbackRequest>(httpContext, cancellationToken)
            .ConfigureAwait(false);

        if (bindError is not null)
        {
            return bindError;
        }

        var request = bound!;

        if (await store.GetAsync(tenants.TenantId, name, cancellationToken).ConfigureAwait(false) is null)
        {
            // A workflow defined in code is changed by changing the application, so it has no
            // versions to roll back to: 409, the answer an agent defined in code gets for the same
            // request. Anything else has no stored definition and is a 404.
            return runner is not null && await runner.GetAsync(name, cancellationToken).ConfigureAwait(false) is not null
                ? TypedResults.Problem(
                    title: "Code-defined workflow cannot be modified",
                    detail: $"'{name}' is a workflow defined in code (AddWorkflow). It is not stored by Tracon, so it " +
                            "has no versions to roll back to; update the application code to change it.",
                    statusCode: StatusCodes.Status409Conflict)
                : NotFound(name);
        }

        var target = await store.GetVersionAsync(tenants.TenantId, name, request.Version, cancellationToken)
            .ConfigureAwait(false);

        if (target is null)
        {
            return TypedResults.Problem(
                title: "Version not found",
                detail: $"Workflow '{name}' has no version {request.Version}.",
                statusCode: StatusCodes.Status404NotFound);
        }

        // The stored content is judged the way a save is judged. It was valid when it was
        // saved, but a function it names may have been unregistered since, and a rollback that
        // wrote it back would produce a "current" definition no run can compile.
        if (await ValidateDefinitionAsync(
                target,
                functions,
                runTimeoutPolicy,
                agents,
                agentDefinitions,
                because: $"Version {request.Version} of workflow '{name}' cannot be restored as it is.",
                cancellationToken).ConfigureAwait(false) is { } problem)
        {
            return problem;
        }

        // No catch: the version was read above, and a version is never removed, so the store has
        // nothing left to report as "not found". A TraconException from here is a store fault (a
        // payload it cannot read) and must not be dressed as a 404.
        return TypedResults.Ok(
            await store.RollbackAsync(tenants.TenantId, name, request.Version, cancellationToken).ConfigureAwait(false));
    }

    /// <summary>
    /// The answer for a version-history read when the store holds no current definition under
    /// <paramref name="name"/>.
    /// </summary>
    /// <remarks>
    /// "The store has no definition" and "there is no such workflow" are not the same
    /// statement. A workflow defined in code is never written to the store, so the plain
    /// "no such workflow" answer would contradict <c>GET /api/workflows</c>, which lists it. The
    /// status stays <c>404</c> - the version-history resource really does not exist - but the
    /// reason is what the caller acts on.
    /// </remarks>
    private static async ValueTask<ProblemHttpResult> NoVersionHistoryAsync(
        string name,
        IWorkflowRunner? runner,
        CancellationToken cancellationToken)
        => runner is not null && await runner.GetAsync(name, cancellationToken).ConfigureAwait(false) is not null
            ? TypedResults.Problem(
                title: "Workflow has no version history",
                detail: $"'{name}' is a workflow defined in code (AddWorkflow). A code definition is not stored by " +
                        "Tracon, so it has no version history; its history is the application's source history.",
                statusCode: StatusCodes.Status404NotFound)
            : NotFound(name);

    private static async Task<Results<NoContent, ProblemHttpResult>> DeleteAsync(
        string name,
        [FromServices] IWorkflowDefinitionStore store,
        [FromServices] ITenantContext tenants,
        CancellationToken cancellationToken)
        => await store.DeleteAsync(tenants.TenantId, name, cancellationToken).ConfigureAwait(false)
            ? TypedResults.NoContent()
            : NotFound(name);

    private static async Task<IResult> RunAsync(
        string name,
        [FromServices] IWorkflowRunner? runner,
        [FromServices] QuotaEnforcer? quotaEnforcer,
        [FromServices] IRunAuthorizationHandler? runAuthorizationHandler,
        [FromServices] IRunAttributionContext? attributionContext,
        [FromServices] ISessionStore sessionStore,
        [FromServices] IOptionsMonitor<TraconSessionOwnershipOptions>? sessionOwnershipOptions,
        [FromServices] IOptionsMonitor<TraconOptions> traconOptions,
        [FromServices] ITenantContext tenantContext,
        ITraconDrainState drainState,
        string prefix,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        // 🚨 Checked BEFORE anything else, like the agent run endpoint: a
        // request refused here opens no 'runs' row and queues no job (F-431).
        if (DrainGate.Check(drainState) is { } drainProblem)
        {
            return drainProblem;
        }

        if (runner is null)
        {
            return NotRegistered();
        }

        var (request, bindError) = await RequestBodyBinding
            .ReadOptionalAsync<WorkflowRunHttpRequest>(httpContext, cancellationToken)
            .ConfigureAwait(false);

        if (bindError is not null)
        {
            return bindError;
        }

        // F-337: attribution is checked BEFORE the run starts, the same order
        // as the agent run endpoint (attribution → authorization → quota):
        // an oversized label set is refused, never trimmed.
        if (RunAttributionGate.Check(attributionContext) is { } attributionProblem)
        {
            return attributionProblem;
        }

        if (await runner.GetAsync(name, cancellationToken).ConfigureAwait(false) is not { } descriptor)
        {
            return NotFound(name);
        }

        // 🚨 Checked BEFORE the quota gate: an unauthorized call must not
        // consume the tenant's quota - phase 139, F-185, same ordering as
        // the agent run endpoint (AgentEndpoints.cs).
        if (await RunAuthorizationGate
                .CheckRunAsync(runAuthorizationHandler, tenantContext, name, request?.SessionId, attributionContext, httpContext, cancellationToken)
                .ConfigureAwait(false) is { } authorizationProblem)
        {
            return authorizationProblem;
        }

        // 🚨 Ownership guards the run surface too (phase 148); see
        // AgentEndpoints for why the session endpoints alone are not enough.
        if (await SessionOwnershipGate
                .CheckRunSessionAsync(sessionOwnershipOptions, attributionContext, sessionStore, request?.SessionId, httpContext, cancellationToken)
                .ConfigureAwait(false) is { } ownershipProblem)
        {
            return ownershipProblem;
        }

        // 🚨 HATA-S1-006: workflow runs pass through the SAME quota gate as
        // agents (see AgentEndpoints.cs, same pattern) — this check did not
        // exist before, and any defined quota (run count, tokens, cost)
        // could be bypassed via workflows.
        if (await QuotaGate
                .CheckAsync(quotaEnforcer, tenantContext, name, httpContext, cancellationToken)
                .ConfigureAwait(false) is { } quotaProblem)
        {
            return quotaProblem;
        }

        // F-294: the values are checked against the workflow's schema BEFORE the
        // stream opens, so the answer is a proper 400 with the SAME body an
        // agent run gives (missingParameters / unknownParameters /
        // tooLongParameters) - impossible once the SSE headers are sent. Last in
        // the order, like the agent run endpoint: attribution, authorization,
        // quota, parameters. The runner checks again: it is the authority, this
        // gate only answers early.
        var parameterCheck = AgentParameterValidator.ValidateValues(
            descriptor.Parameters,
            request?.Parameters,
            traconOptions.CurrentValue.MaxParameterValueLength);

        if (!parameterCheck.IsValid)
        {
            return AgentParameterGate.Problem(parameterCheck);
        }

        // F-318, F-396: a bad session id is refused before the 202 and before the
        // stream opens; the streamed path used to answer 200 and an SSE error frame.
        if (request?.SessionId is { Length: > 0 } requested && WorkflowSessionId.Problem(requested) is { } sessionProblem)
        {
            return TypedResults.Problem(
                title: "Session id invalid",
                detail: sessionProblem,
                statusCode: StatusCodes.Status400BadRequest);
        }

        var runId = TraconId.NewId();

        if (RespondAsyncPreference.IsRequested(httpContext))
        {
            // The session is fixed now, not by the worker: the queued row names
            // it from the start, and a run that resumes this one after a restart
            // must find its checkpoints under the same id.
            var sessionId = request?.SessionId is { Length: > 0 } given ? given : WorkflowSessionId.NewId();

            return await QueueAsync(
                new WorkflowRunJobRequest
                {
                    Operation = WorkflowRunJobPayload.RunOperation,
                    RunId = runId,
                    SessionId = sessionId,
                    Message = request?.Message,
                    Parameters = request?.Parameters,
                },
                name,
                sessionId,
                tenantContext,
                ReadAttribution(attributionContext, httpContext),
                prefix,
                httpContext,
                cancellationToken).ConfigureAwait(false);
        }

        return new WorkflowEventStream(
            runner.RunStreamingAsync(
                new WorkflowRunRequest
                {
                    WorkflowName = name,
                    Message = request?.Message,
                    SessionId = request?.SessionId,
                    RunId = runId,
                    Parameters = request?.Parameters,
                },
                cancellationToken),
            runId);
    }

    private static async Task<Results<Ok<IReadOnlyList<WorkflowCheckpointRecord>>, ProblemHttpResult>>
        ListCheckpointsAsync(
            Guid runId,
            [FromServices] IWorkflowCheckpointStore checkpoints,
            [FromServices] IRunStore runs,
            [FromServices] ITenantContext tenants,
            [FromServices] IRunAuthorizationHandler? runAuthorizationHandler,
            [FromServices] IRunAttributionContext? attributionContext,
            HttpContext httpContext,
            CancellationToken cancellationToken)
    {
        var record = await runs.GetRunAsync(runId, cancellationToken).ConfigureAwait(false);

        // If the tenant does not match, "not found" is returned; the
        // existence of another tenant's run is not leaked.
        if (record is null || !string.Equals(record.TenantId, tenants.TenantId, StringComparison.Ordinal))
        {
            return RunNotFound(runId);
        }

        // A checkpoint carries the workflow's own state, so reading the list is
        // reading the run (phase 147).
        if (await RunAuthorizationGate
                .CheckRunResourceAsync(
                    runAuthorizationHandler, tenants, runId, record.AgentName, record.SessionId,
                    attributionContext, RunAccess.Read, RunNotFound(runId), httpContext, cancellationToken)
                .ConfigureAwait(false) is { } authorizationProblem)
        {
            return authorizationProblem;
        }

        var list = await checkpoints
            .ListByRunAsync(tenants.TenantId, runId, cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.Ok(list);
    }

    private static async Task<IResult> ResumeAsync(
        Guid runId,
        HttpContext httpContext,
        IWorkflowRunner? runner,
        IRunStore runs,
        IWorkflowDefinitionStore definitions,
        ITenantContext tenants,
        IRunAuthorizationHandler? runAuthorizationHandler,
        IRunAttributionContext? attributionContext,
        ITraconDrainState drainState,
        string prefix,
        CancellationToken cancellationToken)
    {
        // 🚨 Checked BEFORE anything else, like the agent run endpoint: a
        // request refused here opens no 'runs' row and queues no job (F-431).
        if (DrainGate.Check(drainState) is { } drainProblem)
        {
            return drainProblem;
        }

        if (runner is null)
        {
            return NotRegistered();
        }

        var (request, bindError) = await RequestBodyBinding
            .ReadOptionalAsync<WorkflowResumeHttpRequest>(httpContext, cancellationToken)
            .ConfigureAwait(false);

        if (bindError is not null)
        {
            return bindError;
        }

        // F-337: attribution is checked BEFORE the run starts, the same order
        // as the agent run endpoint (attribution → authorization → quota):
        // an oversized label set is refused, never trimmed.
        if (RunAttributionGate.Check(attributionContext) is { } attributionProblem)
        {
            return attributionProblem;
        }

        // 🚨 Resume opens a NEW run row from somebody's checkpoint, so it is a
        // run-STARTING surface and its denial is a 403 like every other one.
        // The request carries the SOURCE run's id, the same shape replay uses.
        if (await AuthorizeWorkflowRunAsync(
                runId, runs, tenants, runAuthorizationHandler, attributionContext,
                RunAccess.Start, NotAuthorized(), httpContext, cancellationToken)
            .ConfigureAwait(false) is { } authorizationProblem)
        {
            return authorizationProblem;
        }

        var newRunId = TraconId.NewId();

        // F-318, F-396: the source run and its checkpoint are checked before the
        // 202 and before the stream opens. The streamed path used to answer 200
        // with an SSE error frame, and a checkpoint id that does not exist opened
        // a Failed run row. No quota gate: a resume is not a new run for quota
        // (K-394, K-162).
        var checkedSource = await CheckSourceAsync(runId, runs, tenants, "resumed", cancellationToken).ConfigureAwait(false);

        if (checkedSource.Problem is { } sourceProblem)
        {
            return sourceProblem;
        }

        var source = checkedSource.Source!;

        if (await CheckCheckpointAsync(source, request?.CheckpointId, tenants, httpContext, cancellationToken)
                .ConfigureAwait(false) is { } checkpointProblem)
        {
            return checkpointProblem;
        }

        if (await CheckDefinitionVersionAsync(source, request?.DefinitionVersion, runner, definitions, tenants, cancellationToken)
                .ConfigureAwait(false) is { } versionProblem)
        {
            return versionProblem;
        }

        if (RespondAsyncPreference.IsRequested(httpContext))
        {
            return await QueueAsync(
                new WorkflowRunJobRequest
                {
                    Operation = WorkflowRunJobPayload.ResumeOperation,
                    RunId = newRunId,
                    SessionId = source.SessionId,
                    SourceRunId = runId,
                    CheckpointId = request?.CheckpointId,
                    DefinitionVersion = request?.DefinitionVersion,
                },
                source.WorkflowName!,
                source.SessionId,
                tenants,
                ReadAttribution(attributionContext, httpContext),
                prefix,
                httpContext,
                cancellationToken).ConfigureAwait(false);
        }

        return new WorkflowEventStream(
            runner.ResumeStreamingAsync(
                new WorkflowResumeRequest
                {
                    RunId = runId,
                    CheckpointId = request?.CheckpointId,
                    NewRunId = newRunId,
                    DefinitionVersion = request?.DefinitionVersion,
                },
                cancellationToken),
            newRunId);
    }

    private static async Task<Results<Ok<IReadOnlyList<WorkflowPendingRequest>>, ProblemHttpResult>>
        ListRequestsAsync(
            Guid runId,
            [FromServices] IWorkflowRunner? runner,
            [FromServices] IRunStore runs,
            [FromServices] ITenantContext tenants,
            [FromServices] IRunAuthorizationHandler? runAuthorizationHandler,
            [FromServices] IRunAttributionContext? attributionContext,
            HttpContext httpContext,
            CancellationToken cancellationToken)
    {
        if (runner is null)
        {
            return NotRegistered();
        }

        if (await AuthorizeWorkflowRunAsync(
                runId, runs, tenants, runAuthorizationHandler, attributionContext,
                RunAccess.Read, RunNotFound(runId), httpContext, cancellationToken)
            .ConfigureAwait(false) is { } authorizationProblem)
        {
            return authorizationProblem;
        }

        try
        {
            var requests = await runner.ListPendingRequestsAsync(runId, cancellationToken).ConfigureAwait(false);

            return TypedResults.Ok(requests);
        }
        catch (TraconException exception)
        {
            // A tenant mismatch also falls into this catch and is answered
            // as "not found"; the existence of another tenant's run is not
            // leaked.
            return TypedResults.Problem(
                title: "Run not found",
                detail: exception.Message,
                statusCode: StatusCodes.Status404NotFound);
        }
    }

    private static async Task<IResult> RespondAsync(
        Guid runId,
        HttpContext httpContext,
        IWorkflowRunner? runner,
        IRunStore runs,
        IWorkflowDefinitionStore definitions,
        ITenantContext tenants,
        IRunAuthorizationHandler? runAuthorizationHandler,
        IRunAttributionContext? attributionContext,
        ITraconDrainState drainState,
        string prefix,
        TimeSpan runEventPollInterval,
        CancellationToken cancellationToken)
    {
        // 🚨 Checked BEFORE anything else, like the agent run endpoint: a
        // request refused here opens no 'runs' row and queues no job (F-431).
        if (DrainGate.Check(drainState) is { } drainProblem)
        {
            return drainProblem;
        }

        if (runner is null)
        {
            return NotRegistered();
        }

        var (request, bindError) = await RequestBodyBinding
            .ReadOptionalAsync<WorkflowRespondHttpRequest>(httpContext, cancellationToken)
            .ConfigureAwait(false);

        if (bindError is not null)
        {
            return bindError;
        }

        if (request is null || string.IsNullOrWhiteSpace(request.RequestId))
        {
            return TypedResults.Problem(
                title: "Response invalid",
                detail: "'requestId' is required.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        // F-337: attribution is checked BEFORE the run starts, the same order
        // as the agent run endpoint (attribution → authorization → quota):
        // an oversized label set is refused, never trimmed.
        if (RunAttributionGate.Check(attributionContext) is { } attributionProblem)
        {
            return attributionProblem;
        }

        // Answering a human-in-the-loop request continues the workflow on a NEW
        // run row, so this is a run-starting surface too.
        if (await AuthorizeWorkflowRunAsync(
                runId, runs, tenants, runAuthorizationHandler, attributionContext,
                RunAccess.Start, NotAuthorized(), httpContext, cancellationToken)
            .ConfigureAwait(false) is { } authorizationProblem)
        {
            return authorizationProblem;
        }

        var newRunId = TraconId.NewId();

        var respondRequest = new WorkflowRespondRequest
        {
            RunId = runId,
            RequestId = request.RequestId,
            Approved = request.Approved,
            Text = request.Text,

            // The raw JSON text is carried: the response type is resolved by
            // the workflow engine, according to the type the port reports.
            Json = request.Data?.GetRawText(),
            CheckpointId = request.CheckpointId,
            NewRunId = newRunId,
            DefinitionVersion = request.DefinitionVersion,
        };

        // F-341: the response is checked against the pending request BEFORE the
        // stream opens, so the contract's 400 is possible at all and no run row
        // is opened for an answer of the wrong shape. Last in the order, like
        // the parameter check on the run endpoint: attribution, authorization,
        // then the body. The engine checks again when the request is
        // republished; it is the authority, this gate only answers early.
        if (await runner.ValidateResponseAsync(respondRequest, cancellationToken).ConfigureAwait(false) is { } responseProblem)
        {
            return TypedResults.Problem(
                title: "Response invalid",
                detail: responseProblem,
                statusCode: StatusCodes.Status400BadRequest);
        }

        // F-318, F-396: ValidateResponseAsync judges the answer's shape but says
        // nothing about a run that is not waiting or a request id that is not
        // pending. Both are checked here, before the 202 and before the stream
        // opens; the streamed path used to answer 200 with an SSE error frame.
        var checkedSource = await CheckSourceAsync(runId, runs, tenants, "answered", cancellationToken).ConfigureAwait(false);

        if (checkedSource.Problem is { } sourceProblem)
        {
            return sourceProblem;
        }

        var source = checkedSource.Source!;

        if (source.Status != RunStatus.AwaitingInput)
        {
            return TypedResults.Problem(
                title: "Response invalid",
                detail: $"Run '{runId}' is not awaiting human input (status: {source.Status}). Only a run in " +
                        "'AwaitingInput' status can be responded to.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        if (await CheckDefinitionVersionAsync(source, request.DefinitionVersion, runner, definitions, tenants, cancellationToken)
                .ConfigureAwait(false) is { } versionProblem)
        {
            return versionProblem;
        }

        var pending = await runner.ListPendingRequestsAsync(runId, cancellationToken).ConfigureAwait(false);

        if (!pending.Any(candidate => string.Equals(candidate.RequestId, request.RequestId, StringComparison.Ordinal)))
        {
            return TypedResults.Problem(
                title: "Response invalid",
                detail: $"There is no pending request with id '{request.RequestId}' on run '{runId}'. Refresh " +
                        "the request list with GET /api/workflows/runs/{runId}/requests.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        var queued = RespondAsyncPreference.IsRequested(httpContext);

        // Phase 208: refused BEFORE the answer is decided. Deciding writes the audit entry, opens
        // the run and writes the answer's record; a 501 after them would leave all three behind.
        if (queued && QueuingDisabled(httpContext) is { } queuingProblem)
        {
            return queuingProblem;
        }

        // Read ONCE, here, through the reader that survives a throwing identity pipeline: the
        // run row the gate opens, the job payload and the record all carry these values. A getter
        // read after the claim threw there and left a claim with no run behind it (audit 🔴1).
        var attribution = ReadAttribution(attributionContext, httpContext);

        // Phase 208 (F-319): the answer is audited, its run row opened and its record written -
        // for a SingleAnswer workflow, claimed - after every other check and before the stream or
        // the queue. A repeat of the same answer follows the run the first one started; a
        // different one conflicts.
        var mode = await WorkflowResponseGate
            .ResolveModeAsync(source, runner, definitions, tenants.TenantId, cancellationToken)
            .ConfigureAwait(false);

        var decision = await WorkflowResponseGate
            .DecideAsync(
                new WorkflowResponseGate.Answer(
                    source,
                    request.RequestId,
                    WorkflowAnswerDigest.Compute(request.RequestId, request.Approved, request.Text, respondRequest.Json),
                    mode,
                    newRunId,
                    tenants.TenantId,
                    queued,
                    attribution.UserId,
                    attribution.Labels),
                httpContext.RequestServices,
                cancellationToken)
            .ConfigureAwait(false);

        switch (decision.Outcome)
        {
            case WorkflowResponseGate.Outcome.Conflict:
                // Neither the run the other answer started nor that answer is named: the
                // caller that gave the same answer learns the run from its own follow.
                return TypedResults.Problem(
                    title: "Request already answered",
                    detail: $"Request '{request.RequestId}' of run '{runId}' was already answered with a different " +
                            "answer, and this workflow takes one answer per request.",
                    statusCode: StatusCodes.Status409Conflict);

            case WorkflowResponseGate.Outcome.Gone:
                return TypedResults.Problem(
                    title: "Request already answered",
                    detail: $"Request '{request.RequestId}' of run '{runId}' was already answered with this answer, " +
                            "but the run it started no longer exists, so there is nothing to follow.",
                    statusCode: StatusCodes.Status409Conflict);

            // The job of a queued answer carries the run's id (K-924), so the 202 its first answer
            // got is rebuilt from the run id alone. An answer first given on a stream has no job;
            // the id then names the run only.
            case WorkflowResponseGate.Outcome.Follow when queued:
                return Accepted(decision.RunId, decision.RunId, prefix, httpContext);

            case WorkflowResponseGate.Outcome.Follow:
                return new WorkflowFollowStream(
                    decision.FollowRunId,
                    decision.RunId,
                    tenants.TenantId,
                    runs,
                    httpContext.RequestServices.GetRequiredService<IJobStore>(),
                    runEventPollInterval);
        }

        // The gate opened the run's row (Queued or Running) before it wrote the record that names it.
        var startedRunId = decision.RunId;

        if (queued)
        {
            // After the record: never on the request's token. A caller who goes away now must not
            // leave a queued row with no job behind the record.
            return await EnqueueAsync(
                new WorkflowRunJobRequest
                {
                    Operation = WorkflowRunJobPayload.RespondOperation,
                    RunId = startedRunId,
                    SessionId = source.SessionId,
                    SourceRunId = runId,
                    CheckpointId = request.CheckpointId,
                    DefinitionVersion = request.DefinitionVersion,
                    RequestId = request.RequestId,
                    Approved = request.Approved,
                    Text = request.Text,
                    Json = respondRequest.Json,
                },
                source.WorkflowName!,
                tenants,
                attribution,
                prefix,
                httpContext,
                CancellationToken.None).ConfigureAwait(false);
        }

        // The engine upserts the row the gate opened when it starts; WorkflowEventStream closes it
        // if the engine never does (it failed before its first event, or the caller went away).
        return new WorkflowEventStream(
            runner.RespondStreamingAsync(respondRequest with { NewRunId = startedRunId }, cancellationToken),
            startedRunId,
            preOpened: runs,
            tenants.TenantId);
    }

    /// <summary>
    /// Reads the caller's attribution once, through <c>RunAttributionReader</c>: a throwing or
    /// invalid value is logged and the run is recorded with no attribution, never failed for it.
    /// </summary>
    /// <param name="attributionContext">The attribution context, or <see langword="null"/>.</param>
    /// <param name="httpContext">The request's context, for its logger.</param>
    /// <returns>The user and the frozen labels, or <c>(null, null)</c>.</returns>
    private static (string? UserId, IReadOnlyDictionary<string, string>? Labels) ReadAttribution(
        IRunAttributionContext? attributionContext,
        HttpContext httpContext)
        => RunAttributionReader.Read(
            attributionContext,
            (message, exception) => httpContext.RequestServices.GetRequiredService<ILoggerFactory>()
                .CreateLogger("Tracon.WorkflowEndpoints")
                .LogWarning(exception, "{Message} The workflow run is recorded with no user and no labels.", message));

    /// <summary>Lists the answers given to a run's requests.</summary>
    /// <param name="runId">The source run.</param>
    /// <param name="runs">The run store.</param>
    /// <param name="responses">The answer records.</param>
    /// <param name="tenants">The tenant context.</param>
    /// <param name="runAuthorizationHandler">The optional run authorization gate.</param>
    /// <param name="attributionContext">The caller's attribution.</param>
    /// <param name="httpContext">The request's context.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The records; <c>404</c> for a run this tenant does not have or may not read.</returns>
    private static async Task<Results<Ok<IReadOnlyList<WorkflowResponseRecord>>, ProblemHttpResult>> ListResponsesAsync(
        Guid runId,
        [FromServices] IRunStore runs,
        [FromServices] IWorkflowResponseStore responses,
        [FromServices] ITenantContext tenants,
        [FromServices] IRunAuthorizationHandler? runAuthorizationHandler,
        [FromServices] IRunAttributionContext? attributionContext,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        // Another tenant's run and a missing one answer alike: the existence is not leaked.
        if (await runs.GetRunAsync(runId, cancellationToken).ConfigureAwait(false) is not { } source ||
            !string.Equals(source.TenantId, tenants.TenantId, StringComparison.Ordinal))
        {
            return RunNotFound(runId);
        }

        // The same gate and the same answer as the request list: read access, and a denial is
        // the 404 of a run that does not exist.
        if (await RunAuthorizationGate
                .CheckRunResourceAsync(
                    runAuthorizationHandler, tenants, runId, source.AgentName, source.SessionId,
                    attributionContext, RunAccess.Read, RunNotFound(runId), httpContext, cancellationToken)
                .ConfigureAwait(false) is { } authorizationProblem)
        {
            return authorizationProblem;
        }

        return TypedResults.Ok(await responses.ListBySourceRunAsync(runId, cancellationToken).ConfigureAwait(false));
    }

    /// <summary>The <c>501</c> of a host with queuing turned off; <see langword="null"/> when it is on.</summary>
    private static ProblemHttpResult? QueuingDisabled(HttpContext httpContext)
        => httpContext.RequestServices.GetRequiredService<IOptionsMonitor<TraconAsyncRunOptions>>().CurrentValue.Enabled
            ? null
            : TypedResults.Problem(
                title: "Queuing not enabled",
                detail: "The 'Prefer: respond-async' header was sent, but queuing support is disabled " +
                        "in this setup (TraconAsyncRunOptions.Enabled = false).",
                statusCode: StatusCodes.Status501NotImplemented);

    /// <summary>
    /// The <c>202</c> of a queued run: the run id, the job id and the two addresses. A repeated
    /// answer that follows a run gets the one its first answer got.
    /// </summary>
    private static Accepted<AcceptedRunResponse> Accepted(Guid runId, Guid jobId, string prefix, HttpContext httpContext)
    {
        // RFC 7240 treats the preference as advisory: a 202 says it was applied.
        RespondAsyncPreference.MarkApplied(httpContext);

        var location = $"{prefix}/api/runs/{runId}";

        return TypedResults.Accepted(location, new AcceptedRunResponse
        {
            RunId = runId,
            JobId = jobId,
            Location = location,
            EventsLocation = $"{location}/events",
        });
    }

    /// <summary>
    /// Queues one workflow operation: writes its <see cref="RunStatus.Queued"/> row,
    /// queues the job under the same id, and answers <c>202</c>.
    /// </summary>
    /// <param name="operation">The operation, without the caller's attribution.</param>
    /// <param name="workflowName">The workflow the run belongs to.</param>
    /// <param name="sessionId">The execution session the run uses.</param>
    /// <param name="tenants">The tenant context.</param>
    /// <param name="attribution">
    /// The caller's user and labels, read once through <c>RunAttributionReader</c> (already checked
    /// by the gate), so the queued row and the job payload carry the same values and a throwing
    /// identity pipeline records the run with no attribution instead of failing it.
    /// </param>
    /// <param name="prefix">The path prefix of the <c>Location</c>.</param>
    /// <param name="httpContext">The request's context.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The <c>202</c>, or the <c>501</c> of a host with queuing turned off.</returns>
    /// <remarks>
    /// <para>
    /// The row comes first, as for a queued agent run: a client that
    /// reads <c>GET /api/runs/{id}</c> before the worker leases the job sees
    /// <c>Queued</c>, not <c>404</c>, and the run can be cancelled from the queue.
    /// The worker's write upserts the row; the attribution written here survives it.
    /// </para>
    /// <para>
    /// If the job cannot be queued, the row is closed as <see cref="RunStatus.Failed"/>
    /// before the error is returned: a row left <see cref="RunStatus.Queued"/> with no job
    /// behind it would never move, and run reconciliation does not touch queued rows.
    /// </para>
    /// <para>
    /// The services are read from the request here, only on the queued path: a
    /// streamed request pays nothing for them, and none of them can be mistaken for a
    /// body parameter by minimal API inference.
    /// </para>
    /// </remarks>
    private static async Task<IResult> QueueAsync(
        WorkflowRunJobRequest operation,
        string workflowName,
        string? sessionId,
        ITenantContext tenants,
        (string? UserId, IReadOnlyDictionary<string, string>? Labels) attribution,
        string prefix,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (QueuingDisabled(httpContext) is { } queuingProblem)
        {
            return queuingProblem;
        }

        await httpContext.RequestServices.GetRequiredService<IRunStore>().StartRunAsync(
            new RunStartInfo
            {
                RunId = operation.RunId,
                AgentName = workflowName,
                Kind = RunKind.Workflow,
                WorkflowName = workflowName,
                Status = RunStatus.Queued,
                StartedAt = DateTimeOffset.UtcNow,
                TenantId = tenants.TenantId,
                UserId = attribution.UserId,
                Labels = attribution.Labels,
                SessionId = sessionId,
                IsStreaming = true,
            },
            cancellationToken).ConfigureAwait(false);

        return await EnqueueAsync(operation, workflowName, tenants, attribution, prefix, httpContext, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Queues the job of a run whose <see cref="RunStatus.Queued"/> row is already open, and
    /// answers <c>202</c>.
    /// </summary>
    /// <param name="operation">The operation, without the caller's attribution.</param>
    /// <param name="workflowName">The workflow the run belongs to.</param>
    /// <param name="tenants">The tenant context.</param>
    /// <param name="attribution">The caller's user and labels, read once.</param>
    /// <param name="prefix">The path prefix of the <c>Location</c>.</param>
    /// <param name="httpContext">The request's context.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The <c>202</c>.</returns>
    /// <remarks>
    /// A queue that refuses the job closes the queued row as <see cref="RunStatus.Failed"/>, so
    /// the row never waits for a job that does not exist.
    /// </remarks>
    private static async Task<IResult> EnqueueAsync(
        WorkflowRunJobRequest operation,
        string workflowName,
        ITenantContext tenants,
        (string? UserId, IReadOnlyDictionary<string, string>? Labels) attribution,
        string prefix,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var services = httpContext.RequestServices;
        var asyncOptions = services.GetRequiredService<IOptionsMonitor<TraconAsyncRunOptions>>().CurrentValue;
        var recoveryPolicy = services.GetService<IWorkflowRecoveryPolicy>();
        var runId = operation.RunId;
        var now = DateTimeOffset.UtcNow;

        var job = await QueuedRunPlaceholder.EnqueueAsync(
            services.GetRequiredService<IJobStore>(),
            services.GetRequiredService<IRunStore>(),
            new JobRecord
            {
                Id = runId,
                TenantId = tenants.TenantId,
                HandlerKey = JobHandlerKeys.WorkflowRun,
                Lane = JobLanes.Default,
                TargetName = workflowName,
                Status = JobStatus.Pending,
                Payload = WorkflowRunJobPayload.Write(operation with { UserId = attribution.UserId, Labels = attribution.Labels }),

                // 4-A: one lease for the run and one for each continuation while
                // recovery is on; otherwise the limit a queued agent run gets.
                MaxAttempts = WorkflowRecoveryPolicy.JobAttempts(recoveryPolicy, asyncOptions.MaxAttempts),
                ScheduledFor = now,
                CreatedAt = now,
            },
            [],
            services.GetRequiredService<ILoggerFactory>().CreateLogger("Tracon.WorkflowEndpoints"),
            cancellationToken).ConfigureAwait(false);

        return Accepted(runId, job.Id, prefix, httpContext);
    }

    /// <summary>
    /// Reads the run a resume or a response continues, queued or streamed, with the
    /// checks the engine would otherwise make inside the stream.
    /// </summary>
    /// <param name="runId">The source run.</param>
    /// <param name="runs">The run store.</param>
    /// <param name="tenants">The tenant context.</param>
    /// <param name="verb">"resumed" or "answered", for the refusal text.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The source run, or the problem: 404 for a run this tenant does not have, 400 for one that is not a workflow run.</returns>
    private static async ValueTask<(RunRecord? Source, ProblemHttpResult? Problem)> CheckSourceAsync(
        Guid runId,
        IRunStore runs,
        ITenantContext tenants,
        string verb,
        CancellationToken cancellationToken)
    {
        var source = await runs.GetRunAsync(runId, cancellationToken).ConfigureAwait(false);

        // Another tenant's run is "not found": its existence is not leaked.
        if (source is null || !string.Equals(source.TenantId, tenants.TenantId, StringComparison.Ordinal))
        {
            return (null, RunNotFound(runId));
        }

        if (source.Kind != RunKind.Workflow || source.WorkflowName is not { Length: > 0 } || source.SessionId is not { Length: > 0 })
        {
            return (null, TypedResults.Problem(
                title: "Run cannot be continued",
                detail: $"Run '{runId}' is not a workflow run with an execution session; it cannot be {verb}.",
                statusCode: StatusCodes.Status400BadRequest));
        }

        return (source, null);
    }

    /// <summary>Checks that a resume, queued or streamed, has a checkpoint to resume from.</summary>
    /// <param name="source">The source run.</param>
    /// <param name="checkpointId">The checkpoint the request names; the latest when <see langword="null"/>.</param>
    /// <param name="tenants">The tenant context.</param>
    /// <param name="httpContext">The request's context.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns><c>400</c> when the run wrote no checkpoint or does not have the one named; otherwise <see langword="null"/>.</returns>
    private static async ValueTask<ProblemHttpResult?> CheckCheckpointAsync(
        RunRecord source,
        string? checkpointId,
        ITenantContext tenants,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var checkpoints = await httpContext.RequestServices
            .GetRequiredService<IWorkflowCheckpointStore>()
            .ListByRunAsync(tenants.TenantId, source.Id, cancellationToken)
            .ConfigureAwait(false);

        if (checkpointId is { Length: > 0 }
                ? checkpoints.Any(candidate => string.Equals(candidate.CheckpointId, checkpointId, StringComparison.Ordinal))
                : checkpoints.Count > 0)
        {
            return null;
        }

        return TypedResults.Problem(
            title: "Checkpoint not found",
            detail: checkpointId is { Length: > 0 }
                ? $"Run '{source.Id}' has no checkpoint with id '{checkpointId}'. GET /api/workflows/runs/{source.Id}/checkpoints lists the ones it wrote."
                : $"Run '{source.Id}' has no checkpoint. A run started while checkpoint writing was disabled cannot be resumed.",
            statusCode: StatusCodes.Status400BadRequest);
    }

    /// <summary>
    /// Checks, before the 202 and before the stream opens, that the definition version a
    /// resume or a response will run with exists.
    /// </summary>
    /// <param name="source">The run being continued.</param>
    /// <param name="requested">The version the request names; <see langword="null"/> for the one the run recorded.</param>
    /// <param name="runner">The workflow engine, which knows a workflow defined in code.</param>
    /// <param name="definitions">The definition store.</param>
    /// <param name="tenants">The tenant context.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns><c>400</c> when the version is not one the workflow has; otherwise <see langword="null"/>.</returns>
    /// <remarks>
    /// The engine makes the same judgement when the run starts and fails the run with it, but by
    /// then the answer is a <c>200</c> and an error frame. A workflow that is gone
    /// is not judged here: the engine answers that as it always did.
    /// </remarks>
    private static async ValueTask<ProblemHttpResult?> CheckDefinitionVersionAsync(
        RunRecord source,
        int? requested,
        IWorkflowRunner? runner,
        IWorkflowDefinitionStore definitions,
        ITenantContext tenants,
        CancellationToken cancellationToken)
    {
        if (requested is < 1)
        {
            return TypedResults.Problem(
                title: "Definition version invalid",
                detail: $"'definitionVersion' is {requested}. Versions are numbered from 1; leave it out to continue " +
                        "with the version the run recorded.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        if ((requested ?? source.AgentVersion) is not { } version)
        {
            return null;
        }

        var name = source.WorkflowName!;

        if (await definitions.GetAsync(tenants.TenantId, name, cancellationToken).ConfigureAwait(false) is not { } current)
        {
            // Code-defined, or gone. A version was NAMED for a workflow that keeps none: say so,
            // instead of letting the run ignore it.
            return requested is not null &&
                   runner is not null &&
                   await runner.GetAsync(name, cancellationToken).ConfigureAwait(false) is not null
                ? TypedResults.Problem(
                    title: "Definition version invalid",
                    detail: $"'{name}' is a workflow defined in code. It has no stored versions, so 'definitionVersion' " +
                            "cannot name one.",
                    statusCode: StatusCodes.Status400BadRequest)
                : null;
        }

        if (version == current.Version ||
            await definitions.GetVersionAsync(tenants.TenantId, name, version, cancellationToken).ConfigureAwait(false) is not null)
        {
            return null;
        }

        return TypedResults.Problem(
            title: "Definition version not found",
            detail: requested is not null
                ? $"Workflow '{name}' has no version {version}. GET /api/workflows/{name}/versions lists the ones it has."
                : $"Run '{source.Id}' recorded version {version} of workflow '{name}', which the workflow no longer " +
                  "has, and a run is never resumed with a different definition than the one it recorded. Name a " +
                  "version with 'definitionVersion' to resume it with that one.",
            statusCode: StatusCodes.Status400BadRequest);
    }

    private static WorkflowDescriptor Describe(WorkflowDefinition definition)
        => new()
        {
            Name = definition.Name,
            DisplayName = definition.DisplayName,
            Description = definition.Description,
            Origin = AgentDefinitionOrigin.Database,
            Kind = definition.Kind,
            AgentNames = definition.AgentNames,
            Nodes = definition.Nodes,
            Parameters = definition.Parameters,
            ResponseMode = definition.ResponseMode,
            Version = definition.Version,
            UpdatedAt = definition.UpdatedAt,
        };

    private static ProblemHttpResult NotRegistered()
        => TypedResults.Problem(
            title: "Workflow engine not registered",
            detail: "Add the Tracon.Workflows package and call UseWorkflows() to run workflows.",
            statusCode: StatusCodes.Status501NotImplemented);

    private static ProblemHttpResult NotFound(string name)
        => TypedResults.Problem(
            title: "Workflow not found",
            detail: $"There is no workflow named '{name}'.",
            statusCode: StatusCodes.Status404NotFound);

    /// <summary>
    /// Asks the gate about a workflow run, once its tenant is settled.
    /// </summary>
    /// <remarks>
    /// A run that is missing or belongs to another tenant is deliberately
    /// left ALONE here: the runner already answers it with the same "there is
    /// no run with id" body it always did, and short-circuiting it here would
    /// change that answer for an installation with no handler registered. The
    /// gate is only asked about a run this tenant really has, which is also why
    /// another tenant's identity never reaches a consumer's handler.
    /// </remarks>
    private static async ValueTask<ProblemHttpResult?> AuthorizeWorkflowRunAsync(
        Guid runId,
        IRunStore runs,
        ITenantContext tenants,
        IRunAuthorizationHandler? runAuthorizationHandler,
        IRunAttributionContext? attributionContext,
        RunAccess access,
        ProblemHttpResult denied,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (runAuthorizationHandler is null)
        {
            return null;
        }

        if (await runs.GetRunAsync(runId, cancellationToken).ConfigureAwait(false) is not { } record ||
            !string.Equals(record.TenantId, tenants.TenantId, StringComparison.Ordinal))
        {
            return null;
        }

        return await RunAuthorizationGate
            .CheckRunResourceAsync(
                runAuthorizationHandler, tenants, runId, record.AgentName, record.SessionId,
                attributionContext, access, denied, httpContext, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>The response a denied run START gets: 403, like every other run-starting surface.</summary>
    private static ProblemHttpResult NotAuthorized()
        => TypedResults.Problem(
            title: "Run not authorized",
            detail: "The registered IRunAuthorizationHandler denied this run.",
            statusCode: StatusCodes.Status403Forbidden);

    private static ProblemHttpResult RunNotFound(Guid runId)
        => TypedResults.Problem(
            title: "Run not found",
            detail: $"There is no run with id '{runId}'.",
            statusCode: StatusCodes.Status404NotFound);

    /// <summary>
    /// Writes workflow events as SSE.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Written as a separate <see cref="IResult"/> because the status code
    /// cannot be changed after the stream has started; an <c>event: error</c>
    /// frame is sent on failure. The same pattern is also used for agent trial runs.
    /// </para>
    /// <para>
    /// A run whose row the endpoint opened itself (<paramref name="preOpened"/>: every answer's run,
    /// opened before its record so the record never names a missing run) is closed here when the
    /// engine never took it over - it failed before its first event, or the client went away
    /// first. Left open, the row would read as a run in flight and lock a single-answer request:
    /// a repeated answer follows a running run and never starts again.
    /// </para>
    /// </remarks>
    /// <param name="events">The engine's events.</param>
    /// <param name="runId">The run the stream announces.</param>
    /// <param name="preOpened">The run store the endpoint opened the row in; <see langword="null"/> when the engine opens it.</param>
    /// <param name="tenantId">The tenant of a pre-opened row.</param>
    private sealed class WorkflowEventStream(
        IAsyncEnumerable<RunEvent> events,
        Guid runId,
        IRunStore? preOpened = null,
        string? tenantId = null) : IResult
    {
        public async Task ExecuteAsync(HttpContext httpContext)
        {
            ArgumentNullException.ThrowIfNull(httpContext);

            var cancellationToken = httpContext.RequestAborted;
            var engineStarted = false;
            RunError? unstarted = null;

            try
            {
                await StreamAsync(httpContext, () => engineStarted = true, error => unstarted = error, cancellationToken)
                    .ConfigureAwait(false);
            }
            finally
            {
                if (preOpened is not null && !engineStarted)
                {
                    await preOpened.CompleteRunAsync(
                        new RunCompletion
                        {
                            RunId = runId,
                            Status = unstarted is null ? RunStatus.Canceled : RunStatus.Failed,
                            CompletedAt = DateTimeOffset.UtcNow,
                            Error = unstarted ?? new RunError
                            {
                                Type = nameof(OperationCanceledException),
                                Message = "The connection closed before the run started.",
                            },
                            TenantId = tenantId,
                        },
                        CancellationToken.None).ConfigureAwait(false);
                }
            }
        }

        private async Task StreamAsync(
            HttpContext httpContext,
            Action started,
            Action<RunError> failedBeforeStart,
            CancellationToken cancellationToken)
        {
            var writer = await SseWriter.StartAsync(httpContext.Response, cancellationToken).ConfigureAwait(false);
            // Stops the writer's keep-alive loop before the response ends (F-325).
            await using var writerScope = writer.ConfigureAwait(false);
            long sequence = 0;
            var startedSeen = false;

            try
            {
                await writer.WriteEventAsync(
                    sequence++,
                    "run",
                    JsonSerializer.Serialize(new WorkflowRunAccepted(runId), JsonOptions),
                    cancellationToken).ConfigureAwait(false);

                await foreach (var runEvent in events.WithCancellation(cancellationToken).ConfigureAwait(false))
                {
                    // The engine yields its first event only after it opened the run's row;
                    // from here on it closes the row itself, whatever happens.
                    if (!startedSeen)
                    {
                        startedSeen = true;
                        started();
                    }

                    await writer.WriteEventAsync(
                        sequence++,
                        "event",
                        JsonSerializer.Serialize(runEvent, JsonOptions),
                        cancellationToken).ConfigureAwait(false);
                }

                await writer.WriteEventAsync(
                    sequence,
                    "done",
                    JsonSerializer.Serialize(new WorkflowRunCompleted(runId), JsonOptions),
                    cancellationToken).ConfigureAwait(false);
            }
            // 🚨 `when (httpContext.RequestAborted.IsCancellationRequested)`,
            // not a bare catch. HttpClient reports its own request timeout as a
            // TaskCanceledException, so a provider that never answers raises one
            // while the client is still connected and waiting; a bare catch
            // ended the response as if the CLIENT had gone away, and the caller
            // saw a clean, empty success. Measured in Phase 157.
            catch (OperationCanceledException) when (httpContext.RequestAborted.IsCancellationRequested)
            {
                // The client disconnected. There is no one left to write to.
            }
            catch (Exception ex)
            {
                // 🚨 K-296, missed here until F-325: the SSE headers (200,
                // text/event-stream) have ALREADY been sent. The old filter caught
                // only TraconException, InvalidOperationException and
                // HttpRequestException, so anything else — the runner's own
                // OperationCanceledException when RunTimeout fired during the slot
                // wait, a consumer runner's exception, a store fault — closed the
                // connection with no 'error' frame, and the client could not tell
                // it from a dropped connection. Every exception becomes a frame.
                //
                // Only TraconException's own message is ours; SafeErrorText keeps
                // a foreign one (an HttpRequestException can carry a host:port) out.
                var correlationId = SafeErrorText.NewCorrelationId();

                httpContext.RequestServices.GetRequiredService<ILoggerFactory>()
                    .CreateLogger("Tracon.WorkflowEndpoints")
                    .LogError(ex, "Streaming workflow run {RunId} failed. (ref: {CorrelationId})", runId, correlationId);

                if (!startedSeen)
                {
                    failedBeforeStart(new RunError
                    {
                        Type = ex.GetType().Name,
                        Message = SafeErrorText.ForPersistence(ex, correlationId),
                    });
                }

                await writer.WriteEventAsync(
                    sequence,
                    "error",
                    JsonSerializer.Serialize(
                        new WorkflowRunFailed(ex.GetType().Name, SafeErrorText.ForPersistence(ex, correlationId)),
                        JsonOptions),
                    CancellationToken.None).ConfigureAwait(false);
            }
        }

        private sealed record WorkflowRunAccepted(Guid RunId);

        private sealed record WorkflowRunCompleted(Guid RunId);

        private sealed record WorkflowRunFailed(string Type, string Message);
    }

    /// <summary>
    /// Writes, as SSE, the work an identical earlier answer started: the frames of
    /// <see cref="WorkflowEventStream"/> (<c>run</c>, <c>event</c>…, <c>done</c>), read from the
    /// stored runs.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The follower does not own the work: when its connection drops, only this read stops, and the
    /// run goes on for the connection (or the job) that started it.
    /// </para>
    /// <para>
    /// <strong>It follows the work, not one run.</strong> A queued answer interrupted by a restart
    /// continues under a new run (its recovery chain). When the followed run ends, a continuation of
    /// it is followed next, announced with its own <c>run</c> frame; while the answer's queue job is
    /// not finished and no continuation exists yet, the stream waits for one.
    /// </para>
    /// <para>
    /// <strong>It never reports a missing run as success.</strong> A run whose row is gone, and work
    /// that ended without a single stored event (it never started), end with an <c>error</c> frame,
    /// not <c>done</c>.
    /// </para>
    /// <para>
    /// The stored events are the run's history and match the live stream with two measured
    /// differences. The stored <see cref="RunEventType.RunStarted"/> carries neither the workflow
    /// name nor the session; this stream fills them from the run, as the live stream writes them.
    /// The closing event keeps its stored sequence number, where the live stream numbers it one
    /// past. Events the engine writes beside the graph's (a function node's tool record, a quota
    /// notice) are stored only, so a follower can see more than the live client did.
    /// </para>
    /// </remarks>
    /// <param name="startRunId">The run to follow first: the last run of the answer's chain.</param>
    /// <param name="headRunId">The run the answer started; its queue job, if any, carries this id.</param>
    /// <param name="tenantId">The caller's tenant.</param>
    /// <param name="runs">The run store.</param>
    /// <param name="jobs">The job store.</param>
    /// <param name="pollInterval">How often new events are read.</param>
    private sealed class WorkflowFollowStream(
        Guid startRunId,
        Guid headRunId,
        string tenantId,
        IRunStore runs,
        IJobStore jobs,
        TimeSpan pollInterval) : IResult
    {
        public async Task ExecuteAsync(HttpContext httpContext)
        {
            ArgumentNullException.ThrowIfNull(httpContext);

            var cancellationToken = httpContext.RequestAborted;
            var writer = await SseWriter.StartAsync(httpContext.Response, cancellationToken).ConfigureAwait(false);
            await using var writerScope = writer.ConfigureAwait(false);
            long sequence = 0;
            var current = startRunId;
            var readAny = false;

            try
            {
                await writer.WriteEventAsync(
                    sequence++,
                    "run",
                    JsonSerializer.Serialize(new FollowedRun(current), JsonOptions),
                    cancellationToken).ConfigureAwait(false);

                while (true)
                {
                    await foreach (var (stored, run) in RunEventTail
                                       .ReadAsync(
                                           runs,
                                           current,
                                           next: 0,
                                           pollInterval,
                                           token => new ValueTask(writer.WriteKeepAliveAsync("waiting", token)),
                                           cancellationToken)
                                       .ConfigureAwait(false))
                    {
                        readAny = true;

                        var runEvent = stored.Type == RunEventType.RunStarted && run is not null
                            ? stored with { Text = run.WorkflowName ?? run.AgentName, Payload = run.SessionId }
                            : stored;

                        await writer.WriteEventAsync(
                            sequence++,
                            "event",
                            JsonSerializer.Serialize(runEvent, JsonOptions),
                            cancellationToken).ConfigureAwait(false);
                    }

                    if (await runs.GetRunAsync(current, cancellationToken).ConfigureAwait(false) is null)
                    {
                        await FailAsync(writer, sequence, "The run that took this answer no longer exists.").ConfigureAwait(false);

                        return;
                    }

                    if (await NextRunAsync(writer, cancellationToken).ConfigureAwait(false) is not { } next)
                    {
                        break;
                    }

                    current = next;

                    await writer.WriteEventAsync(
                        sequence++,
                        "run",
                        JsonSerializer.Serialize(new FollowedRun(current), JsonOptions),
                        cancellationToken).ConfigureAwait(false);
                }

                if (!readAny)
                {
                    await FailAsync(writer, sequence, "The run that took this answer never started.").ConfigureAwait(false);

                    return;
                }

                await writer.WriteEventAsync(
                    sequence,
                    "done",
                    JsonSerializer.Serialize(new FollowedRun(current), JsonOptions),
                    cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (httpContext.RequestAborted.IsCancellationRequested)
            {
                // The follower went away; the work it followed is not its own and goes on.
            }
            catch (Exception ex)
            {
                // The SSE headers are already sent: every failure becomes a frame (K-296).
                var correlationId = SafeErrorText.NewCorrelationId();

                httpContext.RequestServices.GetRequiredService<ILoggerFactory>()
                    .CreateLogger("Tracon.WorkflowEndpoints")
                    .LogError(ex, "Following workflow run {RunId} failed. (ref: {CorrelationId})", current, correlationId);

                await writer.WriteEventAsync(
                    sequence,
                    "error",
                    JsonSerializer.Serialize(
                        new FollowFailed(ex.GetType().Name, SafeErrorText.ForPersistence(ex, correlationId)),
                        JsonOptions),
                    CancellationToken.None).ConfigureAwait(false);
            }

            async ValueTask<Guid?> NextRunAsync(SseWriter sse, CancellationToken token)
            {
                while (true)
                {
                    var continuations = await runs
                        .QueryRunsAsync(
                            new RunQuery
                            {
                                TenantId = tenantId,
                                ContinuedFromRunId = current,
                                Kind = RunKind.Workflow,
                                Take = 1,
                            },
                            token)
                        .ConfigureAwait(false);

                    if (continuations.Count > 0)
                    {
                        return continuations[0].Id;
                    }

                    // The queue job still owns the work: its recovery will open a continuation.
                    if (await jobs.GetAsync(tenantId, headRunId, token).ConfigureAwait(false) is not
                        { Status: JobStatus.Pending or JobStatus.Leased or JobStatus.Running })
                    {
                        return null;
                    }

                    await sse.WriteKeepAliveAsync("waiting", token).ConfigureAwait(false);
                    await Task.Delay(pollInterval, token).ConfigureAwait(false);
                }
            }
        }

        private static Task FailAsync(SseWriter writer, long sequence, string message)
            => writer.WriteEventAsync(
                sequence,
                "error",
                JsonSerializer.Serialize(new FollowFailed(nameof(InvalidOperationException), message), JsonOptions),
                CancellationToken.None);

        private sealed record FollowedRun(Guid RunId);

        private sealed record FollowFailed(string Type, string Message);
    }
}
