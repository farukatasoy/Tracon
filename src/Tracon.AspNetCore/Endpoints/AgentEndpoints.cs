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
                "run history does not depend on the definition still existing.");

        builder.MapGet("/api/agents/{name}/versions", ListVersionsAsync)
            .RequireRole(roles.Reader)
            .RequireApiKeyScope(ApiKeyScope.AgentsRead)
            .WithName("TraconListAgentVersions")
            .WithTags("Tracon", "Agents")
            .WithSummary("Lists a definition's version history, newest first.")
            .WithDescription(
                "Every entry is a full definition snapshot, not a delta, so a single entry is " +
                "enough to inspect or restore a past state. The agent must have a current stored " +
                "definition; a code-defined or deleted name returns 404. Code agents have no " +
                "version history at all — their history is the application's source history.");

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
                "either version is missing the response is 404 and names the one that was not found.");

        builder.MapPost("/api/agents/{name}/run", async (
                string name,
                IAgentCatalog catalog,
                AgentSessionManager sessions,
                IAttachmentStore attachmentStore,
                IJobStore jobStore,
                IRunStore runStore,
                ITenantContext tenantContext,
                ExperimentAssignmentResolver experimentAssignment,
                IAgentDefinitionStore definitionStore,
                AgentDefinitionCompiler compiler,
                IEnumerable<IAgentDecorator> decorators,
                IOptionsMonitor<TraconAsyncRunOptions> asyncRunOptions,
                IOptionsMonitor<TraconOptions> optionsMonitor,
                ContextWindowEstimator contextWindowEstimator,
                ITraconDrainState drainState,
                [FromServices] QuotaEnforcer? quotaEnforcer,
                [FromServices] IRunAttributionContext? attributionContext,
                [FromServices] ISessionStore sessionStore,
                [FromServices] IOptionsMonitor<TraconSessionOwnershipOptions>? sessionOwnershipOptions,
                [FromServices] IRunAuthorizationHandler? runAuthorizationHandler,
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

                // 🚨 Attribution is checked BEFORE the run starts, so an
                // oversized label set is REJECTED rather than trimmed. The
                // AgentRunRequest body is deliberately NOT a source of
                // attribution: a userId field there would let any client write
                // spend against another user's name.
                if (RunAttributionGate.Check(attributionContext) is { } attributionProblem)
                {
                    return attributionProblem;
                }

                // 🚨 Authorization runs AFTER attribution (it needs the resolved
                // UserId) and BEFORE the quota check (an unauthorized call must
                // not consume the tenant's quota) - phase 139, F-185.
                if (await RunAuthorizationGate
                        .CheckRunAsync(runAuthorizationHandler, tenantContext, name, request!.SessionId, attributionContext, cancellationToken)
                        .ConfigureAwait(false) is { } authorizationProblem)
                {
                    return authorizationProblem;
                }

                // 🚨 Ownership guards the run surface too, not only the session
                // endpoints (phase 148): continuing a conversation reads its
                // whole history back into the model, so leaving this door open
                // would make gating GET /api/sessions/{id} decorative. Inert
                // while ownership is off, which is the default.
                if (await SessionOwnershipGate
                        .CheckRunSessionAsync(sessionOwnershipOptions, attributionContext, sessionStore, request!.SessionId, cancellationToken)
                        .ConfigureAwait(false) is { } ownershipProblem)
                {
                    return ownershipProblem;
                }

                // 🚨 The quota check happens BEFORE the run starts. An in-progress
                // run is not cut off when the quota is exceeded (K-162); only a new
                // run gets a 429. Queuing also counts as a new run.
                if (await QuotaGate
                        .CheckAsync(quotaEnforcer, tenantContext, name, httpContext, cancellationToken)
                        .ConfigureAwait(false) is { } quotaProblem)
                {
                    return quotaProblem;
                }

                // Pre-flight context-window check (phase 62, F-59): disabled
                // by default, and even when enabled it never touches a
                // provider — only PreflightGate/ContextWindowEstimator run.
                if (await PreflightGate
                        .CheckAsync(optionsMonitor, contextWindowEstimator, catalog, name, request!.Message, cancellationToken)
                        .ConfigureAwait(false) is { } preflightProblem)
                {
                    return preflightProblem;
                }

                if (WantsAsync(httpContext))
                {
                    return await RunQueuedAsync(
                        name,
                        request!,
                        catalog,
                        jobStore,
                        runStore,
                        tenantContext,
                        attributionContext,
                        asyncRunOptions.CurrentValue,
                        prefix,
                        httpContext,
                        cancellationToken).ConfigureAwait(false);
                }

                return await RunAsync(
                    name,
                    request!,
                    catalog,
                    sessions,
                    attachmentStore,
                    tenantContext,
                    experimentAssignment,
                    definitionStore,
                    compiler,
                    decorators,
                    optionsMonitor,
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
                "check, so a denied run never consumes the tenant's quota." +
                "When session ownership is turned on, naming another user's session in 'sessionId' " +
                "is also refused with 403, and opening a NEW session is refused the same way when no " +
                "authenticated identity can be resolved to own it ('errorType': 'session_owner_required').")
            // The success response is SSE by default (see AgentRunStream); but a
            // request carrying the 'Idempotency-Key' header gets a JSON body, and
            // one carrying 'Prefer: respond-async' gets a 202 body.
            .Produces<string>(StatusCodes.Status200OK, contentType: "text/event-stream")
            .Produces<AcceptedRunResponse>(StatusCodes.Status202Accepted)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
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
                .CheckAsync(optionsMonitor, definitionStore, name, version: null, request!.Parameters, cancellationToken)
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
                detail: $"'{name}' has no resolvable model binding (a code agent built from a factory, " +
                         "for example); its context window cannot be estimated.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        return TypedResults.Ok(estimator.Estimate(binding, request!.Message));
    }

    /// <summary>
    /// Determines whether the request carries the <c>Prefer: respond-async</c> preference (RFC 7240).
    /// </summary>
    /// <remarks>
    /// For a request that does NOT carry the header, this check is a single
    /// dictionary lookup; no extra query is made (the no-surprises rule: no silent cost).
    /// </remarks>
    private static bool WantsAsync(HttpContext httpContext)
        => httpContext.Request.Headers.TryGetValue("Prefer", out var values) &&
           values.Any(static value => value is not null &&
               value.Split(',').Any(static token =>
                   token.Trim().Equals("respond-async", StringComparison.OrdinalIgnoreCase)));

    private static async Task<Results<Ok<AgentVersionDiffResponse>, ProblemHttpResult>> GetVersionDiffAsync(
        string name,
        int a,
        int b,
        IAgentDefinitionStore definitions,
        CancellationToken cancellationToken)
    {
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
            else if (registration?.Factory is { } factory)
            {
                // Best-effort only: the caller fully controls how a factory agent
                // is built (CodeAgentRegistration.FromFactory), so invoking it here
                // to peek at 'Instructions' must not turn a read-only detail view
                // into a 500 when the factory throws.
                try
                {
                    if (factory(httpContext.RequestServices) is Microsoft.Agents.AI.ChatClientAgent chatClientAgent)
                    {
                        factoryInstructions = chatClientAgent.Instructions;
                    }
                }
                catch (Exception exception)
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

        if (Validate(request) is { } invalid)
        {
            return invalid;
        }

        if (await ValidateCallGraphAsync(catalog, request, cancellationToken).ConfigureAwait(false) is { } cycle)
        {
            return cycle;
        }

        if (await ValidateEntitiesAsync(validator, request, cancellationToken).ConfigureAwait(false) is { } entities)
        {
            return entities;
        }

        if (await FindDescriptorAsync(catalog, request.Name, cancellationToken).ConfigureAwait(false) is { } existing)
        {
            return TypedResults.Problem(
                title: "Agent name in use",
                detail: existing.Origin switch
                {
                    AgentDefinitionOrigin.Code => $"'{request.Name}' is an agent defined in code and cannot be changed from the " +
                                                  "management API. Code wins name conflicts, so a definition written with the same " +
                                                  "name would never resolve.",
                    AgentDefinitionOrigin.Custom => $"'{request.Name}' belongs to the '{existing.SourceName}' agent source and cannot " +
                                                    "be changed from the management API.",
                    _ => $"A definition named '{request.Name}' already exists. Use PUT to update it.",
                },
                statusCode: StatusCodes.Status409Conflict);
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

        if (Validate(request) is { } invalid)
        {
            return invalid;
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

        if (Validate(request) is { } invalid)
        {
            return invalid;
        }

        if (await GuardCodeAgentAsync(catalog, name, cancellationToken).ConfigureAwait(false) is { } conflict)
        {
            return conflict;
        }

        if (await ValidateCallGraphAsync(catalog, request, cancellationToken).ConfigureAwait(false) is { } cycle)
        {
            return cycle;
        }

        if (await ValidateEntitiesAsync(validator, request, cancellationToken).ConfigureAwait(false) is { } entities)
        {
            return entities;
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
        if (await GuardCodeAgentAsync(catalog, name, cancellationToken).ConfigureAwait(false) is { } conflict)
        {
            return conflict;
        }

        return await definitions.DeleteAsync(name, cancellationToken).ConfigureAwait(false)
            ? TypedResults.NoContent()
            : NotFound(name);
    }

    private static async Task<Results<Ok<IReadOnlyList<AgentDefinition>>, ProblemHttpResult>> ListVersionsAsync(
        string name,
        IAgentDefinitionStore definitions,
        CancellationToken cancellationToken)
    {
        if (await definitions.GetAsync(name, cancellationToken).ConfigureAwait(false) is null)
        {
            return NotFound(name);
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

        if (await GuardCodeAgentAsync(catalog, name, cancellationToken).ConfigureAwait(false) is { } conflict)
        {
            return conflict;
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
        IAgentCatalog catalog,
        AgentSessionManager sessions,
        IAttachmentStore attachmentStore,
        ITenantContext tenantContext,
        ExperimentAssignmentResolver experimentAssignment,
        IAgentDefinitionStore definitionStore,
        AgentDefinitionCompiler compiler,
        IEnumerable<IAgentDecorator> decorators,
        IOptionsMonitor<TraconOptions> optionsMonitor,
        string prefix,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        // Approval decisions and tool results are valid requests on their own:
        // a user approving a pending call or answering a client-side tool call
        // does not write a new message.
        if (string.IsNullOrWhiteSpace(request.Message) &&
            request.Approvals.Count == 0 &&
            request.ToolResults.Count == 0 &&
            request.AttachmentIds.Count == 0)
        {
            return Results.Problem(
                title: "Empty request",
                detail: "One of 'message', 'attachmentIds', 'approvals', or 'toolResults' is required.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        if (request.Approvals.Count > 0 && string.IsNullOrWhiteSpace(request.SessionId))
        {
            return Results.Problem(
                title: "Session required for approval",
                detail: "A pending approval request lives in session history; 'sessionId' is required.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        if (request.ToolResults.Count > 0 && string.IsNullOrWhiteSpace(request.SessionId))
        {
            return Results.Problem(
                title: "Session required for tool result",
                detail: "A pending client-side tool call lives in session history; 'sessionId' is required.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        // Attachment ownership is validated before the stream starts: once the
        // response has begun, we cannot return a proper ProblemDetails.
        var attachments = new List<AttachmentDescriptor>(request.AttachmentIds.Count);

        foreach (var attachmentId in request.AttachmentIds)
        {
            if (await attachmentStore.GetAsync(tenantContext.TenantId, attachmentId, cancellationToken)
                    .ConfigureAwait(false) is not { } descriptor)
            {
                return Results.Problem(
                    title: "Attachment not found",
                    detail: $"There is no attachment with id '{attachmentId}', or it does not belong to this tenant.",
                    statusCode: StatusCodes.Status400BadRequest);
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
            .CheckAsync(optionsMonitor, definitionStore, name, assignment?.Version, request.Parameters, cancellationToken)
            .ConfigureAwait(false);

        if (parameterGate.Problem is { } parameterProblem)
        {
            return parameterProblem;
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
                    new AgentDescriptor
                    {
                        Name = sourceDefinition.Name,
                        DisplayName = sourceDefinition.DisplayName,
                        Description = sourceDefinition.Description,
                        Origin = AgentDefinitionOrigin.Database,
                        SourceName = "database",
                        Version = sourceDefinition.Version,
                        Model = sourceDefinition.Model,
                        ToolNames = sourceDefinition.ToolNames,
                        SkillNames = sourceDefinition.SkillNames,
                        CallableAgentNames = sourceDefinition.CallableAgentNames,
                        UsesHarness = sourceDefinition.Harness is not null,
                        UpdatedAt = sourceDefinition.UpdatedAt,
                    },
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
                return Results.Problem(
                    title: "Tool result too large",
                    detail: $"'result' and 'errorMessage' cannot exceed {ClientToolResultResolver.MaxResultLength} " +
                            "characters; an unbounded tool result would consume the model's context window.",
                    statusCode: StatusCodes.Status400BadRequest);
            }

            var toolResultsSession = await sessions
                .GetOrCreateSessionAsync(agent, request.SessionId!, cancellationToken)
                .ConfigureAwait(false);

            var chatHistory = httpContext.RequestServices.GetRequiredService<Microsoft.Agents.AI.ChatHistoryProvider>();

            var match = await ClientToolResultResolver
                .MatchAsync(request.ToolResults, agent, toolResultsSession, chatHistory, cancellationToken)
                .ConfigureAwait(false);

            switch (match.Kind)
            {
                case ClientToolResultMatchKind.UnknownCallId:
                    return Results.Problem(
                        title: "Unknown tool call",
                        detail: $"There is no pending client-side tool call with id '{match.CallId}' in this session.",
                        statusCode: StatusCodes.Status400BadRequest);

                case ClientToolResultMatchKind.AlreadyAnswered:
                    return Results.Problem(
                        title: "Tool call already answered",
                        detail: $"The client-side tool call with id '{match.CallId}' already has a result.",
                        statusCode: StatusCodes.Status409Conflict);

                case ClientToolResultMatchKind.Success:
                default:
                    break;
            }
        }

        // 🚨 Phase 43: a request carrying 'Idempotency-Key' runs non-streaming.
        // The stored response must be deduplicatable; storing an SSE body
        // (loss of timing information, unpredictable size) is out of scope for
        // this phase (docs/arsiv/fazlar/43-IDEMPOTENCY-KEY.md, section 43.4). This is why
        // the decision is made here, at the moment the stream mode is CHOSEN,
        // rather than in IdempotencyFilter: the filter never SEES a streaming
        // request, because a request carrying the header is already non-streaming.
        var streaming = !httpContext.Request.Headers.ContainsKey(IdempotencyFilter.HeaderName);

        return new AgentRunStream(agent, name, request, sessions, attachments, prefix, runId, assignment, streaming);
    }

    /// <summary>
    /// Queues the run and returns <c>202 Accepted</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Approval decisions, client-side tool results, and attachments are not
    /// supported in this version: all three assume a live client connection
    /// (approval and tool result: input for the next turn; attachment: a
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
    /// </remarks>
    private static async Task<IResult> RunQueuedAsync(
        string name,
        AgentRunRequest request,
        IAgentCatalog catalog,
        IJobStore jobStore,
        IRunStore runStore,
        ITenantContext tenantContext,
        IRunAttributionContext? attributionContext,
        TraconAsyncRunOptions options,
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

        if (request.Approvals.Count > 0 || request.ToolResults.Count > 0 || request.AttachmentIds.Count > 0 ||
            request.Parameters is { Count: > 0 } || request.Documents.Count > 0)
        {
            return Results.Problem(
                title: "Not supported",
                detail: "A queued run ('Prefer: respond-async') does not support approval decisions, " +
                        "client-side tool results, attachments, parameters, or documents in this version.",
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

        Microsoft.Agents.AI.AIAgent? agent;

        try
        {
            agent = await catalog.ResolveAsync(name, request.Culture, cancellationToken).ConfigureAwait(false);
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

        var runId = TraconId.NewId();
        var now = DateTimeOffset.UtcNow;

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
                UserId = attributionContext?.UserId,
                Labels = attributionContext?.Labels,
                SessionId = request.SessionId,
            },
            cancellationToken).ConfigureAwait(false);

        // 🚨 Job.Id is given the SAME value as the run id (TraconId.NewId()
        // is NOT called again here). In Phase 17's general pattern the job id
        // and the run id are separate; here they are deliberately merged,
        // otherwise a second way (scanning the job store) would be needed for
        // GET /api/runs/{id} to return something other than 404 before the
        // worker picks up the job.
        var job = await jobStore.EnqueueAsync(
            new JobRecord
            {
                Id = runId,
                TenantId = tenantContext.TenantId,
                HandlerKey = JobHandlerKeys.AgentRun,
                Lane = lane,
                TargetName = name,
                Status = JobStatus.Pending,
                Payload = BuildQueuedRunPayload(runId, request.Message, request.SessionId, attributionContext?.UserId),
                MaxAttempts = options.MaxAttempts,
                ScheduledFor = now,
                CreatedAt = now,
            },
            [],
            cancellationToken).ConfigureAwait(false);

        // 🚨 RFC 7240 treats the preference as *advisory*; the server may ignore
        // it. Tracon does not carry this ambiguity: if the preference was
        // applied, the response carries BOTH 202 AND 'Preference-Applied'
        // (repeats the same rule from Phase 43).
        httpContext.Response.Headers["Preference-Applied"] = "respond-async";

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
    private static JsonElement BuildQueuedRunPayload(Guid runId, string message, string? sessionId, string? userId)
        => JsonSerializer.SerializeToElement(new
        {
            runId = runId.ToString(),
            message,
            sessionId,

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
        Microsoft.Agents.AI.AIAgent agent,
        string agentName,
        AgentRunRequest request,
        AgentSessionManager sessions,
        IReadOnlyList<AttachmentDescriptor> attachments,
        string prefix,
        Guid runId,
        ExperimentAssignment? assignment,
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

            Microsoft.Agents.AI.AgentSession? session = null;
            long sequence = 0;

            try
            {
                if (!string.IsNullOrWhiteSpace(request.SessionId))
                {
                    session = await sessions
                        .GetOrCreateSessionAsync(agent, request.SessionId, cancellationToken)
                        .ConfigureAwait(false);
                }

                var messages = await BuildMessagesAsync(httpContext, session, cancellationToken)
                    .ConfigureAwait(false);

                await writer.WriteEventAsync(
                    sequence++,
                    "run",
                    JsonSerializer.Serialize(new AgentRunAccepted(runId, request.SessionId), JsonOptions),
                    cancellationToken).ConfigureAwait(false);

                var updates = agent.RunStreamingAsync(
                    messages,
                    session,
                    new TraconRunOptions
                    {
                        RunId = runId,
                        AgentVersion = assignment?.Version,
                        ExperimentId = assignment?.ExperimentId,
                        Variant = assignment?.Variant,

                        // 🚨 Phase 142: this is the ONLY delivery path for a synchronous
                        // run's tool-approval presentation — the queue path's mailbox
                        // (GET /api/approvals/pending) does not exist for a run started
                        // without 'Prefer: respond-async', and the 'update' frames above
                        // carry MAF's own ToolApprovalRequestContent verbatim, which has
                        // no field to carry a presentation. A separate 'approvals' frame,
                        // sent right before the stream's own 'done' frame, is how the
                        // console's approval card learns an entity's name.
                        BeforePendingApprovalIsPublished = async (producedMessages, presentations, hookCancellation) =>
                        {
                            var requests = ChildRunApproval.CollectRequests(producedMessages);

                            if (requests.Count == 0)
                            {
                                return;
                            }

                            var announcements = requests
                                .Select(request =>
                                {
                                    var presentation = presentations.GetValueOrDefault(request.RequestId);

                                    return new PendingApprovalAnnouncement(
                                        request.RequestId,
                                        request.ToolCall is FunctionCallContent call ? call.Name : request.ToolCall.CallId,
                                        presentation?.EntityType,
                                        presentation?.EntityId,
                                        presentation?.EntityName,
                                        presentation?.Message);
                                })
                                .ToArray();

                            await writer.WriteEventAsync(
                                sequence++,
                                "approvals",
                                JsonSerializer.Serialize(announcements, JsonOptions),
                                hookCancellation).ConfigureAwait(false);
                        },
                    },
                    cancellationToken);

                await foreach (var update in updates.ConfigureAwait(false))
                {
                    var payload = JsonSerializer.Serialize(update, AIJsonUtilities.DefaultOptions);
                    await writer.WriteEventAsync(sequence++, "update", payload, cancellationToken).ConfigureAwait(false);
                }

                if (session is not null)
                {
                    await sessions.SaveSessionAsync(agent, session, cancellationToken).ConfigureAwait(false);
                }

                sequence = await WriteQuotaThresholdNoticesAsync(httpContext, runId, writer, sequence, cancellationToken)
                    .ConfigureAwait(false);

                await writer.WriteEventAsync(
                    sequence,
                    "done",
                    JsonSerializer.Serialize(new AgentRunCompleted(request.SessionId), JsonOptions),
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
                // The client disconnected. No one is left to write to.
            }
            catch (Exception ex)
            {
                // 🚨 K-296: the SSE headers (200, text/event-stream) have ALREADY been
                // sent. A narrow 'when' filter (only TraconException/InvalidOperationException/
                // HttpRequestException) would miss real provider SDK exceptions uncaught
                // (e.g. Anthropic's AnthropicApiException derives DIRECTLY from Exception,
                // NOT from HttpRequestException) — the connection would close WITHOUT
                // producing an 'error' frame, and the client would mistake this for
                // silent success. Nothing goes uncaught here: the client ALWAYS
                // receives an 'error' frame.
                var correlationId = SafeErrorText.NewCorrelationId();
                var logger = httpContext.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("Tracon.AgentEndpoints");
                logger.LogError(ex, "Streaming agent run {RunId} failed. (ref: {CorrelationId})", runId, correlationId);

                await writer.WriteEventAsync(
                    sequence,
                    "error",
                    JsonSerializer.Serialize(
                        new AgentRunFailed(ex.GetType().Name, SafeErrorText.ForPersistence(ex, correlationId)),
                        JsonOptions),
                    CancellationToken.None).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Mirrors any quota threshold notice this run's completion just wrote
        /// into its own persisted event stream onto THIS SSE connection, as a
        /// <c>custom</c> frame ahead of <c>done</c>.
        /// </summary>
        /// <remarks>
        /// <para>
        /// This stream only ever forwards Microsoft Agent Framework's own
        /// <see cref="Microsoft.Agents.AI.AgentResponseUpdate"/>s as <c>update</c>
        /// frames — a notice written through <c>AgentRunScope.Writer</c> mid-run
        /// (this one included) never passes through that path. By the time
        /// the update loop above finishes, the run has already closed and any
        /// notice is already durably written; reading it back here is what
        /// gives this connection the SAME frame <c>GET /api/runs/{id}/events</c>
        /// serves, byte for byte — not a second, independently built copy.
        /// </para>
        /// <para>
        /// Skipped without a store round-trip when the run stream option is
        /// off (the default): nothing would be found anyway, and a
        /// consumer who never turned this on should not pay for the query.
        /// </para>
        /// </remarks>
        private static async ValueTask<long> WriteQuotaThresholdNoticesAsync(
            HttpContext httpContext,
            Guid runId,
            SseWriter writer,
            long sequence,
            CancellationToken cancellationToken)
        {
            var quotaOptions = httpContext.RequestServices.GetService<IOptionsMonitor<TraconQuotaOptions>>();

            if (quotaOptions?.CurrentValue.PublishThresholdToRunStream is not true)
            {
                return sequence;
            }

            var runs = httpContext.RequestServices.GetRequiredService<IRunStore>();

            await foreach (var runEvent in runs.ReadEventsAsync(runId, 0, cancellationToken).ConfigureAwait(false))
            {
                if (runEvent.Type == RunEventType.Custom &&
                    string.Equals(runEvent.CustomType, RunEventCustomTypes.QuotaThreshold, StringComparison.Ordinal))
                {
                    await writer.WriteEventAsync(
                        sequence++,
                        "custom",
                        JsonSerializer.Serialize(runEvent, JsonOptions),
                        cancellationToken).ConfigureAwait(false);
                }
            }

            return sequence;
        }

        private async Task ExecuteBufferedAsync(HttpContext httpContext)
        {
            var cancellationToken = httpContext.RequestAborted;

            Microsoft.Agents.AI.AgentSession? session = null;

            try
            {
                if (!string.IsNullOrWhiteSpace(request.SessionId))
                {
                    session = await sessions
                        .GetOrCreateSessionAsync(agent, request.SessionId, cancellationToken)
                        .ConfigureAwait(false);
                }

                var messages = await BuildMessagesAsync(httpContext, session, cancellationToken)
                    .ConfigureAwait(false);

                var response = await agent.RunAsync(
                    messages,
                    session,
                    new TraconRunOptions
                    {
                        RunId = runId,
                        AgentVersion = assignment?.Version,
                        ExperimentId = assignment?.ExperimentId,
                        Variant = assignment?.Variant,
                    },
                    cancellationToken).ConfigureAwait(false);

                if (session is not null)
                {
                    await sessions.SaveSessionAsync(agent, session, cancellationToken).ConfigureAwait(false);
                }

                await Results.Json(
                        new AgentRunResult(runId, request.SessionId, response),
                        AIJsonUtilities.DefaultOptions,
                        statusCode: StatusCodes.Status200OK)
                    .ExecuteAsync(httpContext).ConfigureAwait(false);
            }
            // 🚨 `when (httpContext.RequestAborted.IsCancellationRequested)`,
            // not a bare catch. HttpClient reports its own request timeout as a
            // TaskCanceledException, so a provider that never answers raises one
            // while the client is still connected and waiting; a bare catch
            // ended the response as if the CLIENT had gone away, and the caller
            // saw a clean, empty success. Measured in Phase 157.
            catch (OperationCanceledException) when (httpContext.RequestAborted.IsCancellationRequested)
            {
                // The client disconnected.
            }
            catch (TraconContentBlockedException ex)
            {
                // 🚨 A block is a CLIENT error: the request was understood, but the
                // policy did not let it through, and retrying would not help. A 502
                // would say "upstream is broken" and steer the client toward
                // retrying. The ProblemDetails carries the guard and rule name; it
                // does NOT carry the blocked text (nor does ex.Message).
                await Results.Problem(
                        title: "Content blocked",
                        detail: ex.Message,
                        statusCode: StatusCodes.Status422UnprocessableEntity,
                        extensions: new Dictionary<string, object?>(StringComparer.Ordinal)
                        {
                            ["errorType"] = TraconContentBlockedException.ContentBlockedErrorType,
                            ["guard"] = ex.GuardName,
                            ["rule"] = ex.RuleName,
                            ["direction"] = ex.Direction.ToString(),
                        })
                    .ExecuteAsync(httpContext).ConfigureAwait(false);
            }
            catch (TraconSessionOwnerRequiredException ex)
            {
                // 🚨 403, not 500: session ownership is on and the deployment
                // asked for exactly this refusal, so it is a policy decision
                // and not a fault. Not 401 either - the caller cleared the
                // endpoint's own role policy and is authenticated as far as
                // ASP.NET Core is concerned; what is missing is an identity the
                // attribution pipeline can name as the session's owner.
                await Results.Problem(
                        title: "Session owner required",
                        detail: ex.Message,
                        statusCode: StatusCodes.Status403Forbidden,
                        extensions: new Dictionary<string, object?>(StringComparer.Ordinal)
                        {
                            ["errorType"] = TraconSessionOwnerRequiredException.SessionOwnerRequiredErrorType,
                        })
                    .ExecuteAsync(httpContext).ConfigureAwait(false);
            }
            catch (TraconSessionConflictException ex)
            {
                // 🚨 HATA-004: when two concurrent initial requests arrive for the
                // same NEW session, the loser lands here. A 502 would say "upstream
                // is broken"; the actual cause is a client-side race condition — a
                // 409 and a short retry are the correct fix.
                await Results.Problem(
                        title: "Session conflict",
                        detail: ex.Message,
                        statusCode: StatusCodes.Status409Conflict,
                        extensions: new Dictionary<string, object?>(StringComparer.Ordinal)
                        {
                            ["errorType"] = TraconSessionConflictException.SessionConflictErrorType,
                        })
                    .ExecuteAsync(httpContext).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // 🚨 HATA-S2-003/HATA-S3-005: K-296's fix covered only the streaming
                // sibling path (ExecuteStreamingAsync above) and MISSED this
                // non-streaming path. A narrow 'when' filter (only TraconException/
                // InvalidOperationException/HttpRequestException) would let real
                // provider SDK exceptions go uncaught, leaking into ASP.NET Core's
                // generic handler and producing a bare 500. Nothing goes uncaught
                // here — OperationCanceledException and the more specific
                // Tracon exceptions already have their own catch blocks above.
                var correlationId = SafeErrorText.NewCorrelationId();
                var logger = httpContext.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("Tracon.AgentEndpoints");
                logger.LogError(ex, "Agent run {RunId} failed. (ref: {CorrelationId})", runId, correlationId);

                await Results.Problem(
                        title: "Agent run failed",
                        detail: SafeErrorText.ForPersistence(ex, correlationId),
                        statusCode: StatusCodes.Status502BadGateway)
                    .ExecuteAsync(httpContext).ConfigureAwait(false);
            }
        }

        private static JsonSerializerOptions JsonOptions { get; } = new(JsonSerializerDefaults.Web);

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
        private async ValueTask<List<ChatMessage>> BuildMessagesAsync(
            HttpContext httpContext,
            Microsoft.Agents.AI.AgentSession? session,
            CancellationToken cancellationToken)
        {
            var messages = new List<ChatMessage>(2);
            var services = httpContext.RequestServices;

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
                    services.GetRequiredService<IAuditLog>(),
                    services.GetRequiredService<IAuditActorResolver>(),
                    loggerFactory.CreateLogger(typeof(ToolApprovalResolver).FullName!),
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
                        cancellationToken).ConfigureAwait(false);

                    if (toolResultsMessage is not null)
                    {
                        messages.Add(toolResultsMessage);
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
                contents.Add(new UriContent(AttachmentUriReference.Create(prefix, attachment.Id), attachment.MediaType));
            }

            if (contents.Count > 0)
            {
                messages.Add(new ChatMessage(ChatRole.User, contents));
            }

            return messages;
        }

        private sealed record AgentRunAccepted(Guid RunId, string? SessionId);

        private sealed record AgentRunCompleted(string? SessionId);

        private sealed record AgentRunFailed(string Type, string Message);

        /// <summary>The JSON response for a non-streaming (Idempotency-Key) run.</summary>
        private sealed record AgentRunResult(Guid RunId, string? SessionId, Microsoft.Agents.AI.AgentResponse Response);

        /// <summary>
        /// The <c>approvals</c> SSE frame: the presentation resolved for one pending
        /// tool-approval request, sent right before the stream's <c>done</c> frame.
        /// </summary>
        private sealed record PendingApprovalAnnouncement(
            string RequestId,
            string ToolName,
            string? EntityType,
            string? EntityId,
            string? EntityName,
            string? Message);
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

    /// <summary>
    /// Validates the definition's call graph and produces <c>400</c> if it has a problem.
    /// </summary>
    /// <remarks>
    /// The check happens <strong>at save time</strong>. If it were left to run
    /// time, the user would only see the error after running the agent and
    /// after the depth counter filled up - that is, after spending tokens.
    /// </remarks>
    private static async ValueTask<ProblemHttpResult?> ValidateCallGraphAsync(
        IAgentCatalog catalog,
        AgentDefinitionRequest request,
        CancellationToken cancellationToken)
    {
        if (request.CallableAgentNames.Count == 0)
        {
            return null;
        }

        var descriptors = await catalog.ListAsync(cancellationToken).ConfigureAwait(false);

        return AgentCallGraph.Validate(request.Name, request.CallableAgentNames, descriptors) is { } problem
            ? TypedResults.Problem(
                title: "Call graph invalid",
                detail: problem,
                statusCode: StatusCodes.Status400BadRequest)
            : null;
    }

    /// <summary>
    /// Fully validates the definition before saving — including model,
    /// tool, skill, and callable-agent EXISTENCE checks. Before this check
    /// existed, only the separate <c>POST /api/agents/validate</c> endpoint was
    /// called; the SAVE path itself would save an unknown skill/tool name
    /// without any error.
    /// </summary>
    private static async ValueTask<ProblemHttpResult?> ValidateEntitiesAsync(
        AgentDefinitionValidator validator,
        AgentDefinitionRequest request,
        CancellationToken cancellationToken)
    {
        var report = await validator
            .ValidateAsync(request.ToDefinition(), cancellationToken)
            .ConfigureAwait(false);

        if (report.Valid)
        {
            return null;
        }

        var detail = string.Join(
            " ",
            report.Messages
                .Where(static message => message.Severity == ValidationSeverity.Error)
                .Select(static message => message.Message));

        return TypedResults.Problem(
            title: "Definition invalid",
            detail: detail,
            statusCode: StatusCodes.Status400BadRequest);
    }

    private static async ValueTask<ProblemHttpResult?> GuardCodeAgentAsync(
        IAgentCatalog catalog,
        string name,
        CancellationToken cancellationToken)
    {
        var descriptor = await FindDescriptorAsync(catalog, name, cancellationToken).ConfigureAwait(false);

        return descriptor?.Origin switch
        {
            AgentDefinitionOrigin.Code => TypedResults.Problem(
                title: "Code-defined agent cannot be modified",
                detail: $"'{name}' is defined in code. Code definitions are validated at compile time " +
                        "and cannot be changed from the management API; update the application code to change it.",
                statusCode: StatusCodes.Status409Conflict),
            AgentDefinitionOrigin.Custom => TypedResults.Problem(
                title: "Custom-source agent cannot be modified",
                detail: $"'{name}' belongs to the '{descriptor.SourceName}' agent source and cannot be changed from the management API.",
                statusCode: StatusCodes.Status409Conflict),
            _ => null,
        };
    }

    private static ProblemHttpResult? Validate(AgentDefinitionRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return TypedResults.Problem(
                title: "Agent name empty",
                detail: "'name' is required.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        if (string.IsNullOrWhiteSpace(request.Model?.Provider) || string.IsNullOrWhiteSpace(request.Model.Model))
        {
            return TypedResults.Problem(
                title: "Model binding missing",
                detail: "'model.provider' and 'model.model' are required.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        return null;
    }

    private static ProblemHttpResult NotFound(string name)
        => TypedResults.Problem(
            title: "Agent not found",
            detail: $"There is no agent named '{name}'.",
            statusCode: StatusCodes.Status404NotFound);

    /// <summary>
    /// Reads the body by hand (instead of minimal API's automatic JSON
    /// binding): a parsing error (for example, an unrecognized enum value)
    /// thus falls under this endpoint's own <c>400</c> contract, rather than
    /// falling through to a generic <c>500</c> from a <see cref="JsonException"/>
    /// thrown and left uncaught during minimal API's binding stage.
    /// </summary>
    private static async Task<(AgentDefinitionRequest? Request, ProblemHttpResult? Error)> BindAgentDefinitionRequestAsync(
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        try
        {
            var request = await httpContext.Request
                .ReadFromJsonAsync<AgentDefinitionRequest>(cancellationToken)
                .ConfigureAwait(false);

            if (request is null)
            {
                return (null, TypedResults.Problem(
                    title: "Invalid request body",
                    detail: "The body cannot be empty.",
                    statusCode: StatusCodes.Status400BadRequest));
            }

            return (request, null);
        }
        catch (JsonException ex)
        {
            return (null, TypedResults.Problem(
                title: "Invalid request body",
                detail: ex.Message,
                statusCode: StatusCodes.Status400BadRequest));
        }
    }
}
