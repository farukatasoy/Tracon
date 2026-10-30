using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Tracon;

/// <summary>
/// Agent catalog and definition management endpoints.
/// </summary>
/// <remarks>
/// Agents defined in code are read-only. Code wins name conflicts;
/// a definition written to the database with the same name
/// would never resolve. This is why the write endpoints return
/// <c>409 Conflict</c> instead of silently accepting such a request.
/// </remarks>
internal static class AgentEndpoints
{
    /// <summary>Maps the agent endpoints.</summary>
    /// <param name="builder">The endpoint group.</param>
    /// <param name="roles">The resolved role policies.</param>
    /// <param name="prefix">
    /// The path prefix used when building attachment references (see <see cref="AttachmentUriReference"/>).
    /// </param>
    /// <param name="idempotencyFilter">
    /// The <c>Idempotency-Key</c> filter, added only to the
    /// <c>/api/agents/{name}/run</c> endpoint.
    /// </param>
    public static void Map(
        IEndpointRouteBuilder builder,
        TraconRolePolicies roles,
        string prefix,
        IdempotencyFilter idempotencyFilter)
    {
        builder.MapGet("/api/agents", async Task<Ok<IReadOnlyList<AgentDescriptor>>> (
                IAgentCatalog catalog,
                CancellationToken cancellationToken)
                => TypedResults.Ok(await catalog.ListAsync(cancellationToken).ConfigureAwait(false)))
            .RequireRole(roles.Reader)
            .RequireApiKeyScope(ApiKeyScope.AgentsRead)
            .WithName("TraconListAgents")
            .WithTags("Tracon", "Agents")
            .WithSummary("Lists all agents defined in code and in the database.")
            .WithDescription(
                "The list merges every registered agent source into a single view, ordered by " +
                "name. When two sources hold the same name, the source with the higher priority " +
                "wins and the other one is dropped from the list — code definitions win over " +
                "database definitions. The response is not paged; the number of agents is " +
                "bounded by the control plane, not by traffic. Each entry carries the origin, " +
                "so a client can tell an editable definition from a code-defined one.");

        builder.MapGet("/api/agents/{name}", GetAgentAsync)
            .RequireRole(roles.Reader)
            .RequireApiKeyScope(ApiKeyScope.AgentsRead)
            .WithName("TraconGetAgent")
            .WithTags("Tracon", "Agents")
            .WithSummary("Returns an agent's catalog summary and its persisted definition, if any.")
            .WithDescription(
                "A code-defined agent has no STORED definition, so 'isEditable' is always false for " +
                "it — code is changed by changing the application, not through this API. A code agent " +
                "declared declaratively (AddAgent(AgentDefinition)) still returns its full in-memory " +
                "definition in 'definition', including 'instructions'; only a code agent built from a " +
                "factory (AddAgent(name, factory)) has 'definition' as null, since there is no " +
                "AgentDefinition to return for one. For a factory agent whose concrete type exposes " +
                "instructions (currently only Microsoft.Agents.AI.ChatClientAgent), 'factoryInstructions' " +
                "carries a best-effort read of them instead; it is null when the type does not expose " +
                "them or reading them failed. 'isEditable' is the single field a client checks before " +
                "offering an edit form — it is true only when the agent's origin is the database.");

        builder.MapPost("/api/agents", CreateAgentAsync)
            .RequireRole(roles.Admin)
            .RequireApiKeyScope(ApiKeyScope.AgentsAdmin)
            .AddEndpointFilter(new RemovedFieldFilter("model.allowConcurrentToolCalls", "model.toolLoop.allowConcurrentToolCalls"))
            .AddEndpointFilter(new RemovedFieldFilter("callableAgentNames", "callableAgents: a list of { \"name\": ..., \"version\": ... } objects ('version' is optional)"))
            .WithName("TraconCreateAgent")
            .WithTags("Tracon", "Agents")
            .WithSummary("Creates a new agent definition.")
            .WithDescription(
                "The definition is fully validated before it is stored: the model binding, every " +
                "tool, skill, and callable agent must already exist, and the call graph must be " +
                "free of cycles. A failed check returns 400 and nothing is written. A name that " +
                "another definition already uses returns 409; a name that a code-defined agent " +
                "already uses also returns 409, because code wins name conflicts and the stored " +
                "definition would never resolve. On success the response is 201 with the saved " +
                "definition at version 1 and a Location header pointing at it.")
            .Accepts<AgentDefinitionRequest>("application/json")
            .ProducesProblem(StatusCodes.Status400BadRequest);

        builder.MapPost("/api/agents/validate", ValidateAgentAsync)
            .RequireRole(roles.Operator)
            .RequireApiKeyScope(ApiKeyScope.AgentsAdmin)
            .AddEndpointFilter(new RemovedFieldFilter("model.allowConcurrentToolCalls", "model.toolLoop.allowConcurrentToolCalls"))
            .AddEndpointFilter(new RemovedFieldFilter("callableAgentNames", "callableAgents: a list of { \"name\": ..., \"version\": ... } objects ('version' is optional)"))
            .WithName("TraconValidateAgent")
            .WithTags("Tracon", "Agents")
            .WithSummary("Compiles a definition without saving it and without calling any model.")
            .WithDescription(
                "A validation failure is NOT an HTTP error. When the body is well-formed the " +
                "response is always 200 and the outcome is carried in the report's 'valid' field, " +
                "with one message per finding. 400 is returned only when the body itself cannot " +
                "be read or the required name/model fields are missing — that is the single case " +
                "a pipeline needs in order to tell a transport error from a rejected definition. " +
                "No model provider is contacted and nothing is written.")
            .Accepts<AgentDefinitionRequest>("application/json")
            .ProducesProblem(StatusCodes.Status400BadRequest);

        builder.MapPut("/api/agents/{name}", UpdateAgentAsync)
            .RequireRole(roles.Admin)
            .RequireApiKeyScope(ApiKeyScope.AgentsAdmin)
            .AddEndpointFilter(new RemovedFieldFilter("model.allowConcurrentToolCalls", "model.toolLoop.allowConcurrentToolCalls"))
            .AddEndpointFilter(new RemovedFieldFilter("callableAgentNames", "callableAgents: a list of { \"name\": ..., \"version\": ... } objects ('version' is optional)"))
            .WithName("TraconUpdateAgent")
            .WithTags("Tracon", "Agents")
            .WithSummary("Updates an agent definition and produces a new version.")
            .WithDescription(
                "An agent's name is immutable: when the path name and the body name differ the " +
                "response is 400. A code-defined name returns 409 — code definitions are validated " +
                "at compile time and are changed by changing the application. The same existence " +
                "and call-graph checks as create apply, and a failed check writes nothing. Every " +
                "successful save appends a version rather than overwriting; the previous content " +
                "stays readable through the version history.")
            .Accepts<AgentDefinitionRequest>("application/json")
            .ProducesProblem(StatusCodes.Status400BadRequest);

        builder.MapDelete("/api/agents/{name}", DeleteAgentAsync)
            .RequireRole(roles.Admin)
            .RequireApiKeyScope(ApiKeyScope.AgentsAdmin)
            .WithName("TraconDeleteAgent")
            .WithTags("Tracon", "Agents")
            .WithSummary("Deletes an agent definition and its version history.")
            .WithDescription(
                "The delete removes the current definition together with every stored version; it " +
                "is not a soft delete and there is no rollback afterwards. A code-defined name " +
                "returns 409. A name with no stored definition returns 404, so the call is not " +
                "idempotent across repeats. Runs already recorded for the agent are kept — the " +
                "run history does not depend on the definition still existing. The version numbering " +
                "is kept too: an agent created again under the name starts one above the highest " +
                "version the name ever had, so a pin to a deleted version finds no version.");

        builder.MapGet("/api/agents/{name}/versions", ListVersionsAsync)
            .RequireRole(roles.Reader)
            .RequireApiKeyScope(ApiKeyScope.AgentsRead)
            .WithName("TraconListAgentVersions")
            .WithTags("Tracon", "Agents")
            .WithSummary("Lists a definition's version history, newest first.")
            .WithDescription(
                "Every entry is a full definition snapshot, not a delta, so a single entry is " +
                "enough to inspect or restore a past state. The agent must have a current stored " +
                "definition; a deleted name returns 404. A code-defined or agent-source name also " +
                "returns 404, with a reason that says so — such a definition is not stored by " +
                "Tracon and has no version history at all.");

        builder.MapPost("/api/agents/{name}/rollback", RollbackAsync)
            .RequireRole(roles.Admin)
            .RequireApiKeyScope(ApiKeyScope.AgentsAdmin)
            .WithName("TraconRollbackAgent")
            .WithTags("Tracon", "Agents")
            .WithSummary("Writes a definition as a new version with the content of a previous version.")
            .WithDescription(
                "A rollback moves forward, not backward: the old content is appended as a NEW " +
                "version and the history is never rewritten, so the rollback itself stays " +
                "auditable and can be rolled back in turn. An unknown version number returns 404; " +
                "a code-defined name returns 409.")
            .Accepts<AgentRollbackRequest>("application/json");

        builder.MapGet("/api/agents/{name}/versions/{a:int}/diff/{b:int}", GetVersionDiffAsync)
            .RequireRole(roles.Reader)
            .RequireApiKeyScope(ApiKeyScope.AgentsRead)
            .WithName("TraconGetAgentVersionDiff")
            .WithTags("Tracon", "Agents")
            .WithSummary("Returns two definition versions as raw JSON; the diff is computed in the UI.")
            .WithDescription(
                "The server does no diffing and takes no position on how a change should be " +
                "displayed; it returns both snapshots verbatim as 'left' and 'right' so the client " +
                "chooses the presentation. The two version numbers may be given in any order. When " +
                "either version is missing the response is 404 and names the one that was not found; " +
                "a code-defined or agent-source name returns 404 saying it has no version history.");

        builder.MapPost("/api/agents/{name}/run", async (
                string name,
                IAgentCatalog catalog,
                IJobStore jobStore,
                IRunStore runStore,
                ITenantContext tenantContext,
                ExperimentAssignmentResolver experimentAssignment,
                IAgentDefinitionStore definitionStore,
                IOptionsMonitor<TraconAsyncRunOptions> asyncRunOptions,
                IOptionsMonitor<TraconOptions> optionsMonitor,
                ITraconDrainState drainState,
                [FromServices] IRunAttributionContext? attributionContext,
                HttpContext httpContext,
                CancellationToken cancellationToken) =>
            {
                // 🚨 Checked BEFORE anything else: a run refused here never opens
                // a 'runs' row, so there is nothing to clean up. See
                // TraconDrainService.
                if (DrainGate.Check(drainState) is { } drainProblem)
                {
                    return drainProblem;
                }

                var (request, bindError) = await RequestBodyBinding
                    .ReadAsync<AgentRunRequest>(httpContext, cancellationToken)
                    .ConfigureAwait(false);

                if (bindError is not null)
                {
                    return bindError;
                }

                // The gates every run passes before it starts - attribution, authorization, session ownership, quota and
                // the context-window pre-flight, in that order - live in AgentTurnService, so the AG-UI endpoint passes
                // exactly the same ones.
                if (await AgentTurnService
                        .CheckRunGatesAsync(name, request!, httpContext, cancellationToken)
                        .ConfigureAwait(false) is { } gateProblem)
                {
                    return gateProblem;
                }

                if (RespondAsyncPreference.IsRequested(httpContext))
                {
                    return await RunQueuedAsync(
                        name,
                        request!,
                        catalog,
                        jobStore,
                        runStore,
                        tenantContext,
                        experimentAssignment,
                        attributionContext,
                        asyncRunOptions.CurrentValue,
                        definitionStore,
                        optionsMonitor,
                        prefix,
                        httpContext,
                        cancellationToken).ConfigureAwait(false);
                }

                return await RunAsync(
                    name,
                    request!,
                    prefix,
                    httpContext,
                    cancellationToken).ConfigureAwait(false);
            })
            .RequireRole(roles.Operator)
            .RequireApiKeyScope(ApiKeyScope.RunsWrite)
            .AddEndpointFilter(idempotencyFilter)
            .WithName("TraconRunAgent")
            .WithTags("Tracon", "Agents")
            .WithSummary("Runs an agent for trial purposes and streams the response via SSE.")
            .Accepts<AgentRunRequest>("application/json")
            .WithDescription(
                "If the quota is exceeded, the run does not start and a 429 is returned; the " +
                "ProblemDetails carries which quota was exceeded and when the counter resets. When " +
                "the pre-flight context-window check is enabled (disabled by default) and the prompt " +
                "is estimated to exceed the model's window, the run does not start and a 400 is " +
                "returned with the estimated and allowed token counts; no call reaches the provider. A " +
                "request carrying the 'Idempotency-Key' header runs with a single JSON response " +
                "(non-streaming) instead of SSE, because a replayed response cannot be " +
                "reconstructed from a stream. A request carrying the 'Prefer: respond-async' " +
                "header queues the run and returns '202 Accepted' with a 'Location' header. If a " +
                "registered IContentGuard blocks the content, the non-streaming response returns " +
                "'422' and the run's error type becomes 'content_blocked'; in the STREAMING " +
                "response the status code has already been sent, so the block arrives as an SSE " +
                "'error' event instead. If a registered IRunAuthorizationHandler denies the caller, " +
                "the run does not start and a 403 is returned; this check runs before the quota " +
                "check, so a denied run never consumes the tenant's quota. " +
                "When session ownership is turned on, naming another user's session in 'sessionId' " +
                "is also refused with 403, and opening a NEW session is refused the same way when no " +
                "authenticated identity can be resolved to own it ('errorType': 'session_owner_required'). " +
                "A run without a 'sessionId' that stops at a tool approval can never be answered: it closes " +
                "as Failed, the non-streaming response is 409 ('errorType': 'approval_unanswerable'), and the " +
                "stream ends with an 'error' event of that type instead of 'done'. Right before 'done', the " +
                "stream sends a 'custom' event for each notice the run recorded from a fixed list, with the same " +
                "data GET /api/runs/{id}/events serves: 'tracon.output.truncated' (a plain-text answer the " +
                "model cut off at its output token limit or its context window) and " +
                "'tracon.output.filtered' (an answer with text that the provider's content filter stopped; " +
                "an answer with no content at all fails the run instead) always, and " +
                "'tracon.quota.threshold' when the quota options publish it to the run stream. " +
                "An agent with a harness loop streams every iteration, so the 'update' events carry the drafts and " +
                "the feedback as well as the answer. After each iteration the loop evaluated, the stream sends a " +
                "'loop.iteration-completed' event, between that iteration's last 'update' and the first 'update' of the " +
                "next. Its data is the event GET /api/runs/{id}/events serves; its 'payload' holds 'iteration', " +
                "'continued', 'continuedBy', 'failedCriterion' and 'ceilingReached', and is null when " +
                "'RunRecording:RecordToolPayloads' is off. The answer is the iteration the last " +
                "event closes, when that event has 'continued' false. When the last event has 'ceilingReached' true, the " +
                "answer is the iteration after it, because the turn at the ceiling is not evaluated and has no event of " +
                "its own. After an event with 'continued' true, the next 'update' carries the feedback the loop gives the " +
                "model (role 'user'), not the model's answer. " +
                "A run that does not record its events, or whose store could not take the event, sends no " +
                "'loop.iteration-completed' event. " +
                "When an MCP server asks the user for input in the middle of a tool call (elicitation), " +
                "the run ends as 'AwaitingInput' and the stream sends an 'inputs' event right before " +
                "'done' (the non-streaming response carries the same list as 'inputs'): one entry per " +
                "request with 'requestId', 'toolName', 'serverName', 'message' and 'requestedSchema' " +
                "(a flat JSON schema of the fields to fill in). The answer is the input of the next run of " +
                "the same session ('inputResponses'); the tool call then runs again from its start with " +
                "the answer. Only this endpoint and the AG-UI endpoint ('Tracon.AGUI', where the answer is a " +
                "'resume' entry of the thread) can carry the answer, and only in a run with a 'sessionId': " +
                "on every other surface and in an agent called by another agent, the server's request is " +
                "answered with 'decline'. An answer for an unknown request or with values that do not fit " +
                "the requested fields is refused with 400, one for a request already answered with 409.")
            // The success response is SSE by default (see AgentRunStream); but a
            // request carrying the 'Idempotency-Key' header gets a JSON body, and
            // one carrying 'Prefer: respond-async' gets a 202 body.
            .Produces<string>(StatusCodes.Status200OK, contentType: "text/event-stream")
            .Produces<AcceptedRunResponse>(StatusCodes.Status202Accepted)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status429TooManyRequests)
            .ProducesProblem(StatusCodes.Status501NotImplemented)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        builder.MapPost("/api/agents/{name}/estimate", EstimateAsync)
            .RequireRole(roles.Reader)
            .RequireApiKeyScope(ApiKeyScope.RunsRead)
            .WithName("TraconEstimateContextWindow")
            .WithTags("Tracon", "Agents")
            .WithSummary("Estimates a prompt's token count against the agent's model, without calling the provider.")
            .WithDescription(
                "The diagnostic surface of the pre-flight context-window check: it " +
                "returns the same numbers the check on 'POST /api/agents/{name}/run' would use, " +
                "regardless of whether that check is enabled. No model provider is ever contacted. " +
                "The estimate is approximate — it uses a fixed reference tokenizer, not the bound " +
                "provider's own count. 'contextWindowTokens' and 'allowedPromptTokens' are null when " +
                "the agent's model is not found in the catalog; in that case 'wouldBeRejected' is " +
                "always false, since an unknown window can never be exceeded.")
            .Accepts<AgentRunRequest>("application/json")
            .Produces<ContextWindowEstimate>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound);
    }

    private static async Task<IResult> EstimateAsync(
        string name,
        IAgentCatalog catalog,
        IAgentDefinitionStore definitionStore,
        ContextWindowEstimator estimator,
        IOptionsMonitor<TraconOptions> optionsMonitor,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var (request, bindError) = await RequestBodyBinding
            .ReadAsync<AgentRunRequest>(httpContext, cancellationToken)
            .ConfigureAwait(false);

        if (bindError is not null)
        {
            return bindError;
        }

        // Same gate, same error shape as POST /api/agents/{name}/run - see
        // AgentParameterGate.
        if ((await AgentParameterGate
                .CheckAsync(optionsMonitor, definitionStore, httpContext.RequestServices, name, version: null, request!.Parameters, cancellationToken)
                .ConfigureAwait(false)).Problem is { } parameterProblem)
        {
            return parameterProblem;
        }

        if (await FindDescriptorAsync(catalog, name, cancellationToken).ConfigureAwait(false) is not { } descriptor)
        {
            return NotFound(name);
        }

        if (descriptor.Model is not { } binding)
        {
            return Results.Problem(
                title: "Agent has no model binding",
                detail: $"'{name}' has no resolvable model binding (a code agent built from a factory that declares " +
                         "no model, for example); its context window cannot be estimated. Declare the model with " +
                         "AddAgent(name, model, factory).",
                statusCode: StatusCodes.Status400BadRequest);
        }

        return TypedResults.Ok(estimator.Estimate(binding, request!.Message));
    }

    private static async Task<Results<Ok<AgentVersionDiffResponse>, ProblemHttpResult>> GetVersionDiffAsync(
        string name,
        int a,
        int b,
        IAgentCatalog catalog,
        IAgentDefinitionStore definitions,
        CancellationToken cancellationToken)
    {
        // Checked BEFORE the two version reads: without it, an agent whose
        // definition lives outside the store answers "has no version 1", which
        // states that the agent is versioned here and only that number is
        // missing. Both halves of that are wrong.
        if (await definitions.GetAsync(name, cancellationToken).ConfigureAwait(false) is null)
        {
            return await NoVersionHistoryAsync(catalog, name, cancellationToken).ConfigureAwait(false);
        }

        var left = await definitions.GetVersionAsync(name, a, cancellationToken).ConfigureAwait(false);
        var right = await definitions.GetVersionAsync(name, b, cancellationToken).ConfigureAwait(false);

        if (left is null || right is null)
        {
            return TypedResults.Problem(
                title: "Version not found",
                detail: $"Agent '{name}' has no version {(left is null ? a : b)}.",
                statusCode: StatusCodes.Status404NotFound);
        }

        return TypedResults.Ok(new AgentVersionDiffResponse { Left = left, Right = right });
    }

    private static async Task<Results<Ok<AgentDetailResponse>, ProblemHttpResult>> GetAgentAsync(
        string name,
        IAgentCatalog catalog,
        IAgentDefinitionStore definitions,
        IEnumerable<CodeAgentRegistration> codeRegistrations,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var descriptor = await FindDescriptorAsync(catalog, name, cancellationToken).ConfigureAwait(false);

        if (descriptor is null)
        {
            return NotFound(name);
        }

        var definition = await definitions.GetAsync(name, cancellationToken).ConfigureAwait(false);
        string? factoryInstructions = null;

        // 🚨 IAgentDefinitionStore only ever sees the DATABASE; a code agent's
        // definition lives in the CodeAgentRegistration singleton the consumer
        // registered and is never written there. Without this fallback,
        // 'definition' comes back null for EVERY code agent, hiding a
        // declarative one's instructions even though they are sitting right
        // here in memory.
        if (definition is null && descriptor.Origin == AgentDefinitionOrigin.Code)
        {
            var registration = codeRegistrations.FirstOrDefault(
                candidate => string.Equals(candidate.Name, name, StringComparison.Ordinal));

            if (registration?.Definition is { } codeDefinition)
            {
                definition = codeDefinition;
            }
            else if (registration is not null)
            {
                // Best-effort only: the caller fully controls how a factory agent
                // is built (CodeAgentRegistration.FromFactory), so invoking it here
                // to peek at 'Instructions' must not turn a read-only detail view
                // into a 500 when the factory throws.
                //
                // The same helper as the catalog calls, so a factory that declares its model
                // is recognized here too. This view never calls the model: it builds the client
                // without the tenant's key and egress check, for the factory to hold and nothing more.
                try
                {
                    var models = httpContext.RequestServices.GetRequiredService<IModelProviderRegistry>();
                    var built = await registration
                        .BuildFactoryAgentAsync(
                            httpContext.RequestServices,
                            (binding, _) => new ValueTask<IChatClient>(models.CreateChatClient(binding)),
                            cancellationToken)
                        .ConfigureAwait(false);

                    if (built.Agent is Microsoft.Agents.AI.ChatClientAgent chatClientAgent)
                    {
                        factoryInstructions = chatClientAgent.Instructions;
                    }
                }
                catch (Exception exception) when (OperationCancellation.IsFailure(exception, cancellationToken))
                {
                    httpContext.RequestServices.GetRequiredService<ILoggerFactory>()
                        .CreateLogger("Tracon.AgentEndpoints")
                        .LogWarning(exception, "Reading instructions from factory agent '{AgentName}' failed.", name);
                }
            }
        }

        return TypedResults.Ok(new AgentDetailResponse
        {
            Descriptor = descriptor,
            Definition = definition,
            FactoryInstructions = factoryInstructions,
            IsEditable = descriptor.Origin == AgentDefinitionOrigin.Database,
        });
    }

    private static async Task<Results<Created<AgentDefinition>, ProblemHttpResult>> CreateAgentAsync(
        IAgentCatalog catalog,
        IAgentDefinitionStore definitions,
        AgentDefinitionValidator validator,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var (bound, bindError) = await BindAgentDefinitionRequestAsync(httpContext, cancellationToken).ConfigureAwait(false);

        if (bindError is not null)
        {
            return bindError;
        }

        var request = bound!;

        if (AgentDefinitionWriteRules.Validate(request) is { } invalid)
        {
            return invalid.ToProblem();
        }

        if (await AgentDefinitionWriteRules.ValidateCallGraphAsync(catalog, request, cancellationToken).ConfigureAwait(false) is { } cycle)
        {
            return cycle.ToProblem();
        }

        var entityCheck = await AgentDefinitionWriteRules
            .ValidateEntitiesAsync(validator, request, overlay: null, cancellationToken)
            .ConfigureAwait(false);

        if (entityCheck.Finding is { } entities)
        {
            return entities.ToProblem();
        }

        if (await FindDescriptorAsync(catalog, request.Name, cancellationToken).ConfigureAwait(false) is { } existing)
        {
            return AgentDefinitionWriteRules.NameInUse(existing, request.Name).ToProblem();
        }

        var saved = await definitions
            .SaveAsync(request.ToDefinition(), cancellationToken).ConfigureAwait(false);

        return TypedResults.Created($"{httpContext.Request.Path}/{Uri.EscapeDataString(saved.Name)}", saved);
    }

    /// <summary>
    /// Compiles a definition without saving it and without calling any model.
    /// </summary>
    /// <remarks>
    /// A validation failure is not an HTTP error: if the request is well-formed, the
    /// response is always <c>200</c>, and the result is carried in the <see
    /// cref="AgentValidationReport.Valid"/> field. Only when the body cannot be parsed
    /// (this endpoint's own name/model field check) is <c>400</c> returned — this is
    /// the only genuine request error a CI that wants to distinguish a network error
    /// from a validation error would
    /// </remarks>
    private static async Task<Results<Ok<AgentValidationReport>, ProblemHttpResult>> ValidateAgentAsync(
        HttpContext httpContext,
        AgentDefinitionValidator validator,
        CancellationToken cancellationToken)
    {
        var (bound, bindError) = await BindAgentDefinitionRequestAsync(httpContext, cancellationToken).ConfigureAwait(false);

        if (bindError is not null)
        {
            return bindError;
        }

        var request = bound!;

        if (AgentDefinitionWriteRules.Validate(request) is { } invalid)
        {
            return invalid.ToProblem();
        }

        var report = await validator
            .ValidateAsync(request.ToDefinition(), cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.Ok(report);
    }

    private static async Task<Results<Ok<AgentDefinition>, ProblemHttpResult>> UpdateAgentAsync(
        string name,
        HttpContext httpContext,
        IAgentCatalog catalog,
        IAgentDefinitionStore definitions,
        AgentDefinitionValidator validator,
        CancellationToken cancellationToken)
    {
        var (bound, bindError) = await BindAgentDefinitionRequestAsync(httpContext, cancellationToken).ConfigureAwait(false);

        if (bindError is not null)
        {
            return bindError;
        }

        var request = bound!;

        if (!string.Equals(name, request.Name, StringComparison.Ordinal))
        {
            return TypedResults.Problem(
                title: "Name mismatch",
                detail: $"The path name is '{name}', the body name is '{request.Name}'. An agent's name " +
                        "cannot be changed; create a new definition for a new name.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        if (AgentDefinitionWriteRules.Validate(request) is { } invalid)
        {
            return invalid.ToProblem();
        }

        if (await AgentDefinitionWriteRules.GuardCodeAgentAsync(catalog, name, cancellationToken).ConfigureAwait(false) is { } conflict)
        {
            return conflict.ToProblem();
        }

        if (await AgentDefinitionWriteRules.ValidateCallGraphAsync(catalog, request, cancellationToken).ConfigureAwait(false) is { } cycle)
        {
            return cycle.ToProblem();
        }

        var entityCheck = await AgentDefinitionWriteRules
            .ValidateEntitiesAsync(validator, request, overlay: null, cancellationToken)
            .ConfigureAwait(false);

        if (entityCheck.Finding is { } entities)
        {
            return entities.ToProblem();
        }

        if (await definitions.GetAsync(name, cancellationToken).ConfigureAwait(false) is null)
        {
            return NotFound(name);
        }

        var saved = await definitions
            .SaveAsync(request.ToDefinition(), cancellationToken).ConfigureAwait(false);

        return TypedResults.Ok(saved);
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> DeleteAgentAsync(
        string name,
        IAgentCatalog catalog,
        IAgentDefinitionStore definitions,
        CancellationToken cancellationToken)
    {
        if (await AgentDefinitionWriteRules.GuardCodeAgentAsync(catalog, name, cancellationToken).ConfigureAwait(false) is { } conflict)
        {
            return conflict.ToProblem();
        }

        return await definitions.DeleteAsync(name, cancellationToken).ConfigureAwait(false)
            ? TypedResults.NoContent()
            : NotFound(name);
    }

    private static async Task<Results<Ok<IReadOnlyList<AgentDefinition>>, ProblemHttpResult>> ListVersionsAsync(
        string name,
        IAgentCatalog catalog,
        IAgentDefinitionStore definitions,
        CancellationToken cancellationToken)
    {
        if (await definitions.GetAsync(name, cancellationToken).ConfigureAwait(false) is null)
        {
            return await NoVersionHistoryAsync(catalog, name, cancellationToken).ConfigureAwait(false);
        }

        return TypedResults.Ok(
            await definitions.ListVersionsAsync(name, cancellationToken).ConfigureAwait(false));
    }

    private static async Task<Results<Ok<AgentDefinition>, ProblemHttpResult>> RollbackAsync(
        string name,
        HttpContext httpContext,
        IAgentCatalog catalog,
        IAgentDefinitionStore definitions,
        CancellationToken cancellationToken)
    {
        var (bound, bindError) = await RequestBodyBinding
            .ReadAsync<AgentRollbackRequest>(httpContext, cancellationToken)
            .ConfigureAwait(false);

        if (bindError is not null)
        {
            return bindError;
        }

        var request = bound!;

        if (await AgentDefinitionWriteRules.GuardCodeAgentAsync(catalog, name, cancellationToken).ConfigureAwait(false) is { } conflict)
        {
            return conflict.ToProblem();
        }

        try
        {
            return TypedResults.Ok(
                await definitions.RollbackAsync(name, request.Version, cancellationToken).ConfigureAwait(false));
        }
        catch (TraconException ex)
        {
            return TypedResults.Problem(
                title: "Rollback failed",
                detail: ex.Message,
                statusCode: StatusCodes.Status404NotFound);
        }
    }

    private static async Task<IResult> RunAsync(
        string name,
        AgentRunRequest request,
        string prefix,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var preparation = await AgentTurnService
            .PrepareAsync(name, request, prefix, httpContext, cancellationToken)
            .ConfigureAwait(false);

        if (preparation.Turn is not { } turn)
        {
            return preparation.Problem!;
        }

        // A request carrying 'Idempotency-Key' runs non-streaming.
        // The stored response must be deduplicatable; storing an SSE body
        // (loss of timing information, unpredictable size) is out of scope for
        // this change. This is why
        // the decision is made here, at the moment the stream mode is CHOSEN,
        // rather than in IdempotencyFilter: the filter never SEES a streaming
        // request, because a request carrying the header is already non-streaming.
        var streaming = !httpContext.Request.Headers.ContainsKey(IdempotencyFilter.HeaderName);

        return new AgentRunStream(turn, streaming);
    }

    /// <summary>
    /// Queues the run and returns <c>202 Accepted</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Approval decisions, client-side tool results, tool input answers, and attachments are not
    /// supported in this version: all four assume a live client connection
    /// (approval, tool result and tool input answer: input for the next turn; attachment: a
    /// <c>UriContent</c> reference that requires the path prefix), and a job
    /// running from the queue has no such context.
    /// </para>
    /// <para>
    /// The run id generated here is the id of both the <c>runs</c> row and the
    /// <c>jobs</c> record: they carry the same value. This ensures the
    /// <c>Location</c> returned to the client stays meaningful even before the
    /// job runs from the queue — if the actual row does not exist yet,
    /// <c>GET /api/runs/{id}</c> sees the just-written <see cref="RunStatus.Queued"/>
    /// row instead of a <c>404</c>.
    /// </para>
    /// <para>
    /// The experiment arm is assigned HERE, with the same key as the
    /// synchronous path (session id, else run id), and travels in the payload
    /// with the culture; the worker resolves that version and culture. The
    /// placeholder row carries no arm: only a run that actually starts counts
    /// in one.
    /// </para>
    /// </remarks>
    private static async Task<IResult> RunQueuedAsync(
        string name,
        AgentRunRequest request,
        IAgentCatalog catalog,
        IJobStore jobStore,
        IRunStore runStore,
        ITenantContext tenantContext,
        ExperimentAssignmentResolver experimentAssignment,
        IRunAttributionContext? attributionContext,
        TraconAsyncRunOptions options,
        IAgentDefinitionStore definitionStore,
        IOptionsMonitor<TraconOptions> optionsMonitor,
        string prefix,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (!options.Enabled)
        {
            return Results.Problem(
                title: "Queuing not enabled",
                detail: "The 'Prefer: respond-async' header was sent, but queuing support is disabled " +
                        "in this setup (TraconAsyncRunOptions.Enabled = false).",
                statusCode: StatusCodes.Status501NotImplemented);
        }

        if (string.IsNullOrWhiteSpace(request.Message))
        {
            return Results.Problem(
                title: "Empty request",
                detail: "'message' is required for a queued run.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        if (request.Approvals.Count > 0 || request.ToolResults.Count > 0 ||
            request.InputResponses.Count > 0 || request.AttachmentIds.Count > 0)
        {
            return Results.Problem(
                title: "Not supported",
                detail: "A queued run ('Prefer: respond-async') does not support approval decisions, " +
                        "client-side tool results, tool input answers, or attachments in this version.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        var lane = request.Lane ?? JobLanes.Default;

        if (!JobLanes.IsValidName(lane))
        {
            return Results.Problem(
                title: "Invalid lane",
                detail: $"'{lane}' is not a valid lane name. A lane name must be 1-64 characters: lowercase " +
                        "ASCII letters, digits, '.', '_', or '-', starting with a letter or digit.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        // Assigned at enqueue time, with the synchronous path's key
        // So a session lands in the same arm whichever way it runs.
        // The worker has no request to assign from, and resolving without the
        // arm ran the current version and counted the run in no arm.
        var runId = TraconId.NewId();
        var assignmentKey = request.SessionId ?? runId.ToString("D");

        var assignment = await experimentAssignment
            .ResolveAsync(tenantContext.TenantId, name, assignmentKey, cancellationToken)
            .ConfigureAwait(false);

        Microsoft.Agents.AI.AIAgent? agent;

        // Validated against the version and culture the worker will resolve,
        // so a compile failure of the assigned version is a 400 here rather
        // than a failed job after the 202.
        try
        {
            agent = assignment is null
                ? await catalog.ResolveAsync(name, request.Culture, cancellationToken).ConfigureAwait(false)
                : await catalog.ResolveAsync(name, assignment.Version, request.Culture, cancellationToken).ConfigureAwait(false);
        }
        catch (TraconException ex)
        {
            return Results.Problem(
                title: "Agent compilation failed",
                detail: ex.Message,
                statusCode: StatusCodes.Status400BadRequest);
        }

        if (agent is null)
        {
            return Results.Problem(
                title: "Agent not found",
                detail: $"There is no agent named '{name}'.",
                statusCode: StatusCodes.Status404NotFound);
        }

        // The values the caller sent are checked against the schema of
        // the version the worker will run (the experiment arm's), with the same
        // gate and the same 400 body the synchronous endpoint gives, before a run
        // row exists. The worker checks again: a payload is durable data.
        if (request.Parameters is { Count: > 0 })
        {
            var parameterGate = await AgentParameterGate
                .CheckAsync(optionsMonitor, definitionStore, httpContext.RequestServices, name, assignment?.Version, request.Parameters, cancellationToken)
                .ConfigureAwait(false);

            if (parameterGate.Problem is { } parameterProblem)
            {
                return parameterProblem;
            }
        }
        else if (agent.GetService<UnboundParameterGuard>() is { RequiresValues: true } unbound)
        {
            // Unchanged: no values were sent and the schema has a
            // required parameter with no default.
            return AgentParameterGate.Problem(unbound.Missing);
        }

        var now = DateTimeOffset.UtcNow;

        // Read once, so the placeholder row and the job payload carry the same
        // values (RunAttributionGate already checked them above).
        var queuedUserId = attributionContext?.UserId;
        var queuedLabels = attributionContext?.Labels;

        // Placeholder row: if the client calls GET /api/runs/{id} with this id
        // before the worker picks up the job and ACTUALLY runs the agent, it
        // should see Queued, not 404. When StartRunAsync is called a SECOND
        // time with the same id by the worker (see AgentRunJobHandler), the
        // store treats this as an UPSERT; no new row is OPENED.
        await runStore.StartRunAsync(
            new RunStartInfo
            {
                RunId = runId,
                AgentName = name,
                Status = RunStatus.Queued,
                StartedAt = now,
                TenantId = tenantContext.TenantId,

                // 🚨 Attribution is captured HERE, inside the HTTP request, and
                // not later by the worker: an HTTP-bound IRunAttributionContext
                // has no request to read from a background job. The worker's
                // second StartRunAsync call carries no user, and the store's
                // upsert COALESCEs rather than overwrites, so this value
                // survives.
                UserId = queuedUserId,
                Labels = queuedLabels,
                SessionId = request.SessionId,
            },
            cancellationToken).ConfigureAwait(false);

        // 🚨 Job.Id is given the SAME value as the run id (TraconId.NewId()
        // is NOT called again here). In the general pattern the job id
        // and the run id are separate; here they are deliberately merged,
        // otherwise a second way (scanning the job store) would be needed for
        // GET /api/runs/{id} to return something other than 404 before the
        // worker picks up the job.
        // A queue that refuses the job closes the placeholder row above.
        var job = await QueuedRunPlaceholder.EnqueueAsync(
            jobStore,
            runStore,
            new JobRecord
            {
                Id = runId,
                TenantId = tenantContext.TenantId,
                HandlerKey = JobHandlerKeys.AgentRun,
                Lane = lane,
                TargetName = name,
                Status = JobStatus.Pending,
                Payload = BuildQueuedRunPayload(
                    runId,
                    request.Message,
                    request.SessionId,
                    queuedUserId,
                    queuedLabels,
                    request.Culture,
                    assignment,
                    request.Parameters,
                    request.Documents),
                MaxAttempts = options.MaxAttempts,
                ScheduledFor = now,
                CreatedAt = now,
            },
            [],
            httpContext.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("Tracon.AgentEndpoints"),
            cancellationToken).ConfigureAwait(false);

        // 🚨 RFC 7240 treats the preference as *advisory*; the server may ignore
        // it. Tracon does not carry this ambiguity: if the preference was
        // applied, the response carries BOTH 202 AND 'Preference-Applied'
        // (repeats the same rule).
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
    /// Builds the payload for a <see cref="JobHandlerKeys.AgentRun"/> job. Parsing
    /// happens in <c>AgentRunJobHandler.ParsePayload</c> (hand-written, AOT-compatible).
    /// </summary>
    private static JsonElement BuildQueuedRunPayload(
        Guid runId,
        string message,
        string? sessionId,
        string? userId,
        IReadOnlyDictionary<string, string>? labels,
        string? culture,
        ExperimentAssignment? assignment,
        IReadOnlyDictionary<string, string>? parameters,
        IReadOnlyList<AgentRunDocument> documents)
        => JsonSerializer.SerializeToElement(new
        {
            runId = runId.ToString(),
            message,
            sessionId,

            // The values and the reference documents travel in the
            // durable payload. 🚨 jobs.payload is NOT a protected column
            // (ProtectedColumn lists none for it): the message text already
            // sits there in plain form, and these now sit beside it. The site
            // says so in background-work.md; a protection for the payload is its
            // own candidate. Absent when the caller sent none, so a payload an
            // older build wrote and one written now read the same.
            parameters = parameters is { Count: > 0 } ? parameters : null,
            documents = documents.Count > 0
                ? documents.Select(static document => new { name = document.Name, content = document.Content }).ToArray()
                : null,

            // The worker has no request to read these from. A job
            // without them (enqueued by an older build) runs the current
            // version with the default instructions, as it always did. The
            // worker checks the arm against the experiment store before it
            // honors it, because a scheduled job's payload is hand-written.
            culture,
            agentVersion = assignment?.Version,
            experimentId = assignment?.ExperimentId.ToString(),
            variant = assignment?.Variant,

            // 🚨 The identity is captured HERE, inside the HTTP request, for
            // the same reason the runs row captures it a few lines above: an
            // HTTP-bound IRunAttributionContext has nothing to read from
            // inside a background worker. Without it, a queued run that opens
            // a session would open it unowned - or, with
            // RequireAuthenticatedOwner on, fail in the worker long after the
            // caller received its 202. The worker replays this value through
            // AmbientRunAttributionScope so the session manager resolves the
            // owner from the one source it uses everywhere else.
            userId,

            // The labels travel with the user, for the same reason. The
            // runs row kept them through the placeholder, but the worker opened
            // its scope without them, so the run's sub-agents and tools saw
            // none. Absent when there are none, as an older build wrote it.
            labels = labels is { Count: > 0 } ? labels : null,
        });

    /// <summary>
    /// Writes the response for a trial run.
    /// </summary>
    /// <remarks>
    /// <para>
    /// By default <paramref name="streaming"/> is on and the response is SSE; it
    /// is written as a separate <see cref="IResult"/> because the status code
    /// cannot be changed once the stream has started, so an <c>event: error</c>
    /// frame is sent on failure instead.
    /// </para>
    /// <para>
    /// The first frame is <c>run</c> and carries the run id. The id is
    /// generated <em>by the caller</em>, using <see cref="TraconRunOptions"/>;
    /// otherwise the wrapper that writes the run record would generate its own
    /// id, and the streaming response could never be correlated with the
    /// <c>/api/runs/{id}</c> record.
    /// </para>
    /// <para>
    /// When <paramref name="streaming"/> is off (<c>Idempotency-Key</c>),
    /// the response is a single JSON body: since headers/status code have not
    /// been sent yet, an error can be returned with a real HTTP status code
    /// (502) — unlike the SSE branch, an <c>event: error</c> frame is not needed here.
    /// </para>
    /// </remarks>
    private sealed class AgentRunStream(
        PreparedTurn turn,
        bool streaming) : IResult
    {
        public async Task ExecuteAsync(HttpContext httpContext)
        {
            ArgumentNullException.ThrowIfNull(httpContext);

            if (streaming)
            {
                await ExecuteStreamingAsync(httpContext).ConfigureAwait(false);
            }
            else
            {
                await ExecuteBufferedAsync(httpContext).ConfigureAwait(false);
            }
        }

        private async Task ExecuteStreamingAsync(HttpContext httpContext)
        {
            var cancellationToken = httpContext.RequestAborted;
            var writer = await SseWriter.StartAsync(httpContext.Response, cancellationToken).ConfigureAwait(false);
            // Stops the writer's keep-alive loop before the response ends.
            await using var writerScope = writer.ConfigureAwait(false);

            long sequence = 0;

            try
            {
                var opened = await turn.OpenAsync(httpContext, cancellationToken).ConfigureAwait(false);

                await writer.WriteEventAsync(
                    sequence++,
                    "run",
                    JsonSerializer.Serialize(new AgentRunAccepted(turn.RunId, turn.Request.SessionId), JsonOptions),
                    cancellationToken).ConfigureAwait(false);

                var updates = opened.RunStreamingAsync(
                    // A separate 'approvals' frame, sent right before the stream's own 'done' frame, is how the
                    // console's approval card learns an entity's name (see PreparedTurn.CreateRunOptions).
                    onPendingApprovals: async (announcements, hookCancellation) =>
                        await writer.WriteEventAsync(
                            sequence++,
                            "approvals",
                            JsonSerializer.Serialize(announcements, JsonOptions),
                            hookCancellation).ConfigureAwait(false),

                    // The same slot as the 'approvals' frame: right before 'done'.
                    onPendingInputs: async (announcements, hookCancellation) =>
                        await writer.WriteEventAsync(
                            sequence++,
                            "inputs",
                            JsonSerializer.Serialize(announcements, JsonOptions),
                            hookCancellation).ConfigureAwait(false),

                    // The loop evaluator hands over each iteration event between two iterations, so this frame
                    // lands after the last update of the iteration and before the first of the next. The data is the
                    // event GET /api/runs/{id}/events serves, byte for byte.
                    onLoopIteration: async (iteration, hookCancellation) =>
                        await writer.WriteEventAsync(
                            sequence++,
                            "loop.iteration-completed",
                            RunNoticeMirror.Serialize(iteration),
                            hookCancellation).ConfigureAwait(false),
                    cancellationToken);

                List<string>? pending = null;

                // ANY update, not the last one. A tool turn cut at the limit
                // raises it too; the notice scan then finds no event, which costs one
                // read. Missing the final turn's reason would lose the notice. An earlier change:
                // a content-filter reason may leave a notice as well.
                var sawIncomplete = false;

                await foreach (var update in updates.ConfigureAwait(false))
                {
                    if (!opened.HasSession && ChildRunApproval.Describe(update.Contents) is { } names)
                    {
                        (pending ??= []).Add(names);
                    }

                    sawIncomplete |= FinishReasonDetectingChatClient.IsIncomplete(update.FinishReason);

                    var payload = JsonSerializer.Serialize(update, AIJsonUtilities.DefaultOptions);
                    await writer.WriteEventAsync(sequence++, "update", payload, cancellationToken).ConfigureAwait(false);
                }

                if (pending is not null)
                {
                    await CloseUnanswerableRunAsync(httpContext).ConfigureAwait(false);

                    await writer.WriteEventAsync(
                        sequence,
                        "error",
                        JsonSerializer.Serialize(
                            new AgentRunFailed(
                                SessionlessApprovalRun.ErrorType,
                                ApprovalNeedsSession(string.Join(", ", pending.Distinct(StringComparer.Ordinal)))),
                            JsonOptions),
                        cancellationToken).ConfigureAwait(false);

                    return;
                }

                await opened.SaveSessionAsync(httpContext, cancellationToken).ConfigureAwait(false);

                sequence = await RunNoticeMirror
                    .WriteAsync(httpContext, turn.RunId, writer, sequence, sawIncomplete, cancellationToken)
                    .ConfigureAwait(false);

                await writer.WriteEventAsync(
                    sequence,
                    "done",
                    JsonSerializer.Serialize(new AgentRunCompleted(turn.Request.SessionId), JsonOptions),
                    cancellationToken).ConfigureAwait(false);
            }
            // 🚨 `when (httpContext.RequestAborted.IsCancellationRequested)`,
            // not a bare catch. HttpClient reports its own request timeout as a
            // TaskCanceledException, so a provider that never answers raises one
            // while the client is still connected and waiting; a bare catch
            // ended the response as if the CLIENT had gone away, and the caller
            // saw a clean, empty success.
            catch (OperationCanceledException) when (httpContext.RequestAborted.IsCancellationRequested)
            {
                // The client disconnected. No one is left to write to.
            }
            catch (Exception ex)
            {
                // The SSE headers (200, text/event-stream) have ALREADY been
                // sent. A narrow 'when' filter (only TraconException/InvalidOperationException/
                // HttpRequestException) would miss real provider SDK exceptions uncaught
                // (e.g. Anthropic's AnthropicApiException derives DIRECTLY from Exception,
                // NOT from HttpRequestException) — the connection would close WITHOUT
                // producing an 'error' frame, and the client would mistake this for
                // silent success. Nothing goes uncaught here: the client ALWAYS
                // receives an 'error' frame.
                var (failureType, failureMessage) = AgentTurnService.DescribeStreamFailure(ex, turn.RunId, httpContext);

                await writer.WriteEventAsync(
                    sequence,
                    "error",
                    JsonSerializer.Serialize(new AgentRunFailed(failureType, failureMessage), JsonOptions),
                    CancellationToken.None).ConfigureAwait(false);
            }
        }

        /// <summary>The text a caller gets when a run without a session stopped at a tool approval.</summary>
        /// <param name="pending">The names of the tools that wait for approval.</param>
        /// <returns>The error message.</returns>
        private string ApprovalNeedsSession(string pending)
            => $"Agent '{turn.AgentName}' asked for approval to call '{pending}'. An approval is answered in the next turn " +
               "of a session, and this run has no 'sessionId', so it can never be answered; the run was closed as " +
               "failed. Run the agent with a 'sessionId' to answer approvals.";

        /// <summary>Closes the run, which stopped at an approval nobody can answer, as failed.</summary>
        /// <param name="httpContext">The request, for the run store, the tenant and the logger.</param>
        /// <returns>A task that completes when the row is written or the failure is logged.</returns>
        /// <remarks>Not the request's token: a caller that went away must not leave the run waiting forever.</remarks>
        private ValueTask CloseUnanswerableRunAsync(HttpContext httpContext)
            => SessionlessApprovalRun.CloseAsFailedAsync(
                httpContext.RequestServices.GetService<IRunStore>(),
                turn.RunId,
                httpContext.RequestServices.GetRequiredService<ITenantContext>().TenantId,
                DateTimeOffset.UtcNow,
                httpContext.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("Tracon.AgentEndpoints"),
                CancellationToken.None);

        private async Task ExecuteBufferedAsync(HttpContext httpContext)
        {
            var cancellationToken = httpContext.RequestAborted;

            try
            {
                var opened = await turn.OpenAsync(httpContext, cancellationToken).ConfigureAwait(false);

                IReadOnlyList<PendingToolInputAnnouncement>? inputs = null;

                var response = await opened.RunAsync(
                    // The non-streaming body carries the requests as 'inputs' instead of an SSE frame.
                    onPendingInputs: (announcements, _) =>
                    {
                        inputs = announcements;

                        return ValueTask.CompletedTask;
                    },
                    cancellationToken).ConfigureAwait(false);

                // Without a session nobody can ever answer the approval this run
                // stopped at; a 200 left it waiting forever.
                if (!opened.HasSession && ChildRunApproval.Describe(response.Messages) is { } pending)
                {
                    await CloseUnanswerableRunAsync(httpContext).ConfigureAwait(false);

                    await Results.Problem(
                            title: "Approval requires a session",
                            detail: ApprovalNeedsSession(pending),
                            statusCode: StatusCodes.Status409Conflict,
                            extensions: new Dictionary<string, object?>(StringComparer.Ordinal)
                            {
                                ["errorType"] = SessionlessApprovalRun.ErrorType,
                            })
                        .ExecuteAsync(httpContext).ConfigureAwait(false);

                    return;
                }

                await opened.SaveSessionAsync(httpContext, cancellationToken).ConfigureAwait(false);

                await Results.Json(
                        new AgentRunResult(turn.RunId, turn.Request.SessionId, response, inputs),
                        AIJsonUtilities.DefaultOptions,
                        statusCode: StatusCodes.Status200OK)
                    .ExecuteAsync(httpContext).ConfigureAwait(false);
            }
            // 🚨 `when (httpContext.RequestAborted.IsCancellationRequested)`,
            // not a bare catch. HttpClient reports its own request timeout as a
            // TaskCanceledException, so a provider that never answers raises one
            // while the client is still connected and waiting; a bare catch
            // ended the response as if the CLIENT had gone away, and the caller
            // saw a clean, empty success.
            catch (OperationCanceledException) when (httpContext.RequestAborted.IsCancellationRequested)
            {
                // The client disconnected.
            }
            catch (Exception ex)
            {
                await AgentTurnService.DescribeFailure(ex, turn.RunId, httpContext)
                    .ExecuteAsync(httpContext).ConfigureAwait(false);
            }
        }

        private static JsonSerializerOptions JsonOptions { get; } = new(JsonSerializerDefaults.Web);

        private sealed record AgentRunAccepted(Guid RunId, string? SessionId);

        private sealed record AgentRunCompleted(string? SessionId);

        private sealed record AgentRunFailed(string Type, string Message);

        /// <summary>The JSON response for a non-streaming (Idempotency-Key) run.</summary>
        private sealed record AgentRunResult(
            Guid RunId,
            string? SessionId,
            Microsoft.Agents.AI.AgentResponse Response,
            IReadOnlyList<PendingToolInputAnnouncement>? Inputs);
    }

    internal static async ValueTask<AgentDescriptor?> FindDescriptorAsync(
        IAgentCatalog catalog,
        string name,
        CancellationToken cancellationToken)
    {
        foreach (var descriptor in await catalog.ListAsync(cancellationToken).ConfigureAwait(false))
        {
            if (string.Equals(descriptor.Name, name, StringComparison.Ordinal))
            {
                return descriptor;
            }
        }

        return null;
    }

    private static ProblemHttpResult NotFound(string name)
        => TypedResults.Problem(
            title: "Agent not found",
            detail: $"There is no agent named '{name}'.",
            statusCode: StatusCodes.Status404NotFound);

    /// <summary>
    /// The answer for a version-history read when <see cref="IAgentDefinitionStore"/>
    /// holds nothing under <paramref name="name"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// "The database store has no row" and "there is no such agent" are not the
    /// same statement. A code agent's definition lives in the
    /// <c>CodeAgentRegistration</c> singleton and an <c>IAgentSource</c> agent's
    /// lives in that source; neither is ever written to the store, so the plain
    /// "no such agent" answer contradicts <c>GET /api/agents/{name}</c>, which
    /// answers <c>200</c> for the same name at the same moment.
    /// </para>
    /// <para>
    /// The status stays <c>404</c> — the version-history resource really does not
    /// exist — but the reason is what the caller acts on, and the two reasons
    /// call for different actions: edit the application source, or edit whatever
    /// the named agent source reads.
    /// </para>
    /// </remarks>
    private static async ValueTask<ProblemHttpResult> NoVersionHistoryAsync(
        IAgentCatalog catalog,
        string name,
        CancellationToken cancellationToken)
    {
        var descriptor = await FindDescriptorAsync(catalog, name, cancellationToken).ConfigureAwait(false);

        return descriptor?.Origin switch
        {
            AgentDefinitionOrigin.Code => TypedResults.Problem(
                title: VersionHistoryMissingTitle,
                detail: $"'{name}' is defined in code. A code definition is not stored by Tracon, " +
                        "so it has no version history; its history is the application's source history.",
                statusCode: StatusCodes.Status404NotFound),
            AgentDefinitionOrigin.Custom => TypedResults.Problem(
                title: VersionHistoryMissingTitle,
                detail: $"'{name}' belongs to the '{descriptor.SourceName}' agent source. Its definition " +
                        "is not stored by Tracon, so it has no version history.",
                statusCode: StatusCodes.Status404NotFound),
            _ => NotFound(name),
        };
    }

    /// <summary>Shared by both version-history reads so they cannot drift apart.</summary>
    private const string VersionHistoryMissingTitle = "Agent has no version history";

    /// <summary>
    /// Reads the body by hand (instead of minimal API's automatic JSON
    /// binding): a parsing error (for example, an unrecognized enum value)
    /// thus falls under this endpoint's own <c>400</c> contract, rather than
    /// falling through to a generic <c>500</c> from a <see cref="JsonException"/>
    /// thrown and left uncaught during minimal API's binding stage.
    /// </summary>
    /// <remarks>
    /// <strong>This used to be a second, hand-rolled copy</strong> of
    /// <see cref="RequestBodyBinding.ReadAsync{T}"/> that called
    /// <c>ReadFromJsonAsync</c> with no options at all, so the three agent
    /// definition endpoints silently missed everything the shared reader adds:
    /// <c>RespectNullableAnnotations</c>, and the enum converter that names a
    /// refused value and its alternatives. Both readers already produced the
    /// same title and the same status, which is what let the copy sit here
    /// unnoticed. It now delegates.
    /// </remarks>
    private static async Task<(AgentDefinitionRequest? Request, ProblemHttpResult? Error)> BindAgentDefinitionRequestAsync(
        HttpContext httpContext,
        CancellationToken cancellationToken)
        => await RequestBodyBinding
            .ReadAsync<AgentDefinitionRequest>(httpContext, cancellationToken)
            .ConfigureAwait(false);
}
