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
    public static void Map(
        IEndpointRouteBuilder builder,
        TraconRolePolicies roles,
        string prefix,
        IdempotencyFilter idempotencyFilter)
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
                "settingsSchema null and its nodes must carry no settings. The schema is for display: " +
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
                "never put a secret in them, name its configuration key instead. The call " +
                "replaces the whole definition: omitted fields are cleared, not merged. The name " +
                "comes from the path and is not taken from the body. The name 'functions' is " +
                "reserved (400): GET /api/workflows/functions uses it.")
            .Accepts<WorkflowSaveRequest>("application/json");

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
                "checkpoint cannot be resumed once the definition it needs is gone.");

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
                    [FromServices] ITenantContext tenants,
                    [FromServices] IRunAuthorizationHandler? runAuthorizationHandler,
                    [FromServices] IRunAttributionContext? attributionContext,
                    [FromServices] ITraconDrainState drainState,
                    CancellationToken cancellationToken)
                => ResumeAsync(
                    runId, httpContext, runner, runs, tenants, runAuthorizationHandler, attributionContext, drainState,
                    prefix, cancellationToken))
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
                "checkpoint. The engine must be registered; otherwise the response is 501. The " +
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
                    [FromServices] ITenantContext tenants,
                    [FromServices] IRunAuthorizationHandler? runAuthorizationHandler,
                    [FromServices] IRunAttributionContext? attributionContext,
                    [FromServices] ITraconDrainState drainState,
                    CancellationToken cancellationToken)
                => RespondAsync(
                    runId, httpContext, runner, runs, tenants, runAuthorizationHandler, attributionContext, drainState,
                    prefix, cancellationToken))
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
                "all 400; an unknown run is 404. With 'Prefer: respond-async' it is then queued: the reply " +
                "is '202 Accepted' and the run continues in the background worker, so a machine " +
                "that answers a request does not have to keep a connection open. The queued answer " +
                "is stored with the job until the job is removed; do not put a secret in it. " +
                "'Idempotency-Key' is accepted only together with 'Prefer: respond-async'.")
            .Produces<string>(StatusCodes.Status200OK, contentType: "text/event-stream")
            .Produces<AcceptedRunResponse>(StatusCodes.Status202Accepted)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status501NotImplemented)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);
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
        };

        // Validation happens AT SAVE TIME. Accepting an invalid definition
        // and blowing up at run time would force the user to start a run
        // just to find the error. The workflow compiler uses the same
        // check; the rule lives in a single place (Tracon.Core).
        // F-382: a name a fixed route uses is refused before anything else is judged.
        if ((WorkflowDefinitionValidator.ReservedNameProblem(name) ?? WorkflowDefinitionValidator.Validate(definition)) is { } message)
        {
            return TypedResults.Problem(
                title: "Workflow definition invalid",
                detail: message,
                statusCode: StatusCodes.Status400BadRequest);
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
                return TypedResults.Problem(
                    title: "Workflow definition invalid",
                    detail: $"Workflow '{name}' uses function '{node.Name}', but no such function is " +
                            "registered. Register it with AddWorkflowFunction() before referencing it " +
                            "from a workflow definition.",
                    statusCode: StatusCodes.Status400BadRequest);
            }

            // The settings are checked by the registry that knows the function's settings
            // type (F-295): the same read the compiler repeats when a run starts. Same
            // reasoning as the name above - the registry is fixed for the process lifetime,
            // so the early check is an honest guarantee.
            if (functions.ValidateSettings(node.Name, node.Settings) is { } settingsProblem)
            {
                return TypedResults.Problem(
                    title: "Workflow definition invalid",
                    detail: $"Workflow '{name}': {settingsProblem}",
                    statusCode: StatusCodes.Status400BadRequest);
            }
        }

        // F-341: each node must take what the node before it passes on.
        // Microsoft Agent Framework drops a message no handler takes, without an
        // error, so a chain that breaks this would end every run with no
        // output. Checked after the names, so an unregistered function is
        // reported by name first; the compiler repeats the check.
        if (WorkflowDefinitionValidator.ValidateNodeTypes(definition, functions) is { } typeProblem)
        {
            return TypedResults.Problem(
                title: "Workflow definition invalid",
                detail: typeProblem,
                statusCode: StatusCodes.Status400BadRequest);
        }

        var saved = await store.SaveAsync(tenants.TenantId, definition, cancellationToken).ConfigureAwait(false);

        return TypedResults.Ok(saved);
    }

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
                attributionContext,
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
                },
                source.WorkflowName!,
                source.SessionId,
                tenants,
                attributionContext,
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

        var pending = await runner.ListPendingRequestsAsync(runId, cancellationToken).ConfigureAwait(false);

        if (!pending.Any(candidate => string.Equals(candidate.RequestId, request.RequestId, StringComparison.Ordinal)))
        {
            return TypedResults.Problem(
                title: "Response invalid",
                detail: $"There is no pending request with id '{request.RequestId}' on run '{runId}'. Refresh " +
                        "the request list with GET /api/workflows/runs/{runId}/requests.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        if (RespondAsyncPreference.IsRequested(httpContext))
        {
            return await QueueAsync(
                new WorkflowRunJobRequest
                {
                    Operation = WorkflowRunJobPayload.RespondOperation,
                    RunId = newRunId,
                    SessionId = source.SessionId,
                    SourceRunId = runId,
                    CheckpointId = request.CheckpointId,
                    RequestId = request.RequestId,
                    Approved = request.Approved,
                    Text = request.Text,
                    Json = respondRequest.Json,
                },
                source.WorkflowName!,
                source.SessionId,
                tenants,
                attributionContext,
                prefix,
                httpContext,
                cancellationToken).ConfigureAwait(false);
        }

        return new WorkflowEventStream(
            runner.RespondStreamingAsync(respondRequest, cancellationToken),
            newRunId);
    }

    /// <summary>
    /// Queues one workflow operation: writes its <see cref="RunStatus.Queued"/> row,
    /// queues the job under the same id, and answers <c>202</c>.
    /// </summary>
    /// <param name="operation">The operation, without the caller's attribution.</param>
    /// <param name="workflowName">The workflow the run belongs to.</param>
    /// <param name="sessionId">The execution session the run uses.</param>
    /// <param name="tenants">The tenant context.</param>
    /// <param name="attributionContext">The caller's attribution, already checked by the gate.</param>
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
        IRunAttributionContext? attributionContext,
        string prefix,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var services = httpContext.RequestServices;
        var asyncOptions = services.GetRequiredService<IOptionsMonitor<TraconAsyncRunOptions>>().CurrentValue;

        if (!asyncOptions.Enabled)
        {
            return TypedResults.Problem(
                title: "Queuing not enabled",
                detail: "The 'Prefer: respond-async' header was sent, but queuing support is disabled " +
                        "in this setup (TraconAsyncRunOptions.Enabled = false).",
                statusCode: StatusCodes.Status501NotImplemented);
        }

        var runStore = services.GetRequiredService<IRunStore>();
        var jobStore = services.GetRequiredService<IJobStore>();
        var recoveryPolicy = services.GetService<IWorkflowRecoveryPolicy>();

        // Read once, so the queued row and the job payload carry the same values
        // (RunAttributionGate already checked them).
        var userId = attributionContext?.UserId;
        var labels = attributionContext?.Labels;
        var runId = operation.RunId;
        var now = DateTimeOffset.UtcNow;

        await runStore.StartRunAsync(
            new RunStartInfo
            {
                RunId = runId,
                AgentName = workflowName,
                Kind = RunKind.Workflow,
                WorkflowName = workflowName,
                Status = RunStatus.Queued,
                StartedAt = now,
                TenantId = tenants.TenantId,
                UserId = userId,
                Labels = labels,
                SessionId = sessionId,
                IsStreaming = true,
            },
            cancellationToken).ConfigureAwait(false);

        // F-432: a queue that refuses the job closes the queued row above.
        var job = await QueuedRunPlaceholder.EnqueueAsync(
            jobStore,
            runStore,
            new JobRecord
            {
                Id = runId,
                TenantId = tenants.TenantId,
                HandlerKey = JobHandlerKeys.WorkflowRun,
                Lane = JobLanes.Default,
                TargetName = workflowName,
                Status = JobStatus.Pending,
                Payload = WorkflowRunJobPayload.Write(operation with { UserId = userId, Labels = labels }),

                // 4-A: one lease for the run and one for each continuation while
                // recovery is on; otherwise the limit a queued agent run gets.
                MaxAttempts = WorkflowRecoveryPolicy.JobAttempts(recoveryPolicy, asyncOptions.MaxAttempts),
                ScheduledFor = now,
                CreatedAt = now,
            },
            [],
            services.GetRequiredService<ILoggerFactory>().CreateLogger("Tracon.WorkflowEndpoints"),
            cancellationToken).ConfigureAwait(false);

        // RFC 7240 treats the preference as advisory: a 202 says it was applied.
        RespondAsyncPreference.MarkApplied(httpContext);

        var location = $"{prefix}/api/runs/{runId}";

        return TypedResults.Accepted(location, new AcceptedRunResponse
        {
            RunId = runId,
            JobId = job.Id,
            Location = location,
            EventsLocation = $"{location}/events",
        });
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
    /// Written as a separate <see cref="IResult"/> because the status code
    /// cannot be changed after the stream has started; an <c>event: error</c>
    /// frame is sent on failure. The same pattern is also used for agent trial runs.
    /// </remarks>
    private sealed class WorkflowEventStream(IAsyncEnumerable<RunEvent> events, Guid runId) : IResult
    {
        public async Task ExecuteAsync(HttpContext httpContext)
        {
            ArgumentNullException.ThrowIfNull(httpContext);

            var cancellationToken = httpContext.RequestAborted;
            var writer = await SseWriter.StartAsync(httpContext.Response, cancellationToken).ConfigureAwait(false);
            // Stops the writer's keep-alive loop before the response ends (F-325).
            await using var writerScope = writer.ConfigureAwait(false);
            long sequence = 0;

            try
            {
                await writer.WriteEventAsync(
                    sequence++,
                    "run",
                    JsonSerializer.Serialize(new WorkflowRunAccepted(runId), JsonOptions),
                    cancellationToken).ConfigureAwait(false);

                await foreach (var runEvent in events.WithCancellation(cancellationToken).ConfigureAwait(false))
                {
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
}
