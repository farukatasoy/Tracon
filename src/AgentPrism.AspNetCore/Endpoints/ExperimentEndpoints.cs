using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace AgentPrism;

/// <summary>
/// A/B experiment management, lifecycle, and result viewing endpoints (Phase 19.3-19.4).
/// </summary>
/// <remarks>
/// 🚨 All dependencies outside of <see cref="IExperimentStore"/> are marked
/// <strong>explicitly</strong> with <c>[FromServices]</c> — the rationale is the
/// same as in <see cref="EvalEndpoints"/>. Results do not come from this store;
/// they come through <see cref="IRunStore.GetExperimentResultsAsync"/>
/// (K-041: the computation happens in the store).
/// </remarks>
internal static class ExperimentEndpoints
{
    /// <summary>Maps the experiment endpoints.</summary>
    /// <param name="builder">The endpoint group.</param>
    /// <param name="roles">The resolved role policies.</param>
    public static void Map(IEndpointRouteBuilder builder, AgentPrismRolePolicies roles)
    {
        builder.MapGet("/api/experiments", ListAsync)
            .RequireRole(roles.Reader)
            .RequireApiKeyScope(ApiKeyScope.ExperimentsRead)
            .WithName("AgentPrismListExperiments")
            .WithTags("AgentPrism", "Experiments")
            .WithSummary("Lists a tenant's A/B experiments.");

        builder.MapGet("/api/experiments/{name}", GetAsync)
            .RequireRole(roles.Reader)
            .RequireApiKeyScope(ApiKeyScope.ExperimentsRead)
            .WithName("AgentPrismGetExperiment")
            .WithTags("AgentPrism", "Experiments")
            .WithSummary("Gets a single A/B experiment.");

        builder.MapPut("/api/experiments/{name}", SaveAsync)
            .RequireRole(roles.Admin)
            .RequireApiKeyScope(ApiKeyScope.ExperimentsAdmin)
            .WithName("AgentPrismSaveExperiment")
            .WithTags("AgentPrism", "Experiments")
            .WithSummary("Creates or updates an experiment.")
            .Accepts<ExperimentSaveRequest>("application/json")
            .WithDescription(
                "An experiment can only be set up between versions of the same agent; " +
                "code-sourced agents have no version history, so they are rejected. Variant weights must sum to 100.");

        builder.MapDelete("/api/experiments/{name}", DeleteAsync)
            .RequireRole(roles.Admin)
            .RequireApiKeyScope(ApiKeyScope.ExperimentsAdmin)
            .WithName("AgentPrismDeleteExperiment")
            .WithTags("AgentPrism", "Experiments")
            .WithSummary("Deletes an experiment. A running experiment must be stopped first.");

        builder.MapPost("/api/experiments/{name}/start", StartAsync)
            .RequireRole(roles.Admin)
            .RequireApiKeyScope(ApiKeyScope.ExperimentsAdmin)
            .WithName("AgentPrismStartExperiment")
            .WithTags("AgentPrism", "Experiments")
            .WithSummary("Starts the experiment; traffic begins splitting according to the weights.")
            .WithDescription("Only one experiment can run for the same agent at a time.");

        builder.MapPost("/api/experiments/{name}/stop", StopAsync)
            .RequireRole(roles.Admin)
            .RequireApiKeyScope(ApiKeyScope.ExperimentsAdmin)
            .WithName("AgentPrismStopExperiment")
            .WithTags("AgentPrism", "Experiments")
            .WithSummary("Stops the experiment; new runs go to the current version.");

        builder.MapGet("/api/experiments/{name}/results", GetResultsAsync)
            .RequireRole(roles.Reader)
            .RequireApiKeyScope(ApiKeyScope.ExperimentsRead)
            .WithName("AgentPrismGetExperimentResults")
            .WithTags("AgentPrism", "Experiments")
            .WithSummary("Gets a per-arm summary of count, error rate, tokens, and duration.")
            .WithDescription("There is no statistical claim of a 'winner'; raw counts are shown.");

        builder.MapPut("/api/experiments/{name}/canary", SetCanaryAsync)
            .RequireRole(roles.Admin)
            .RequireApiKeyScope(ApiKeyScope.ExperimentsAdmin)
            .WithName("AgentPrismSetExperimentCanary")
            .WithTags("AgentPrism", "Experiments")
            .WithSummary("Defines or removes the canary rule (a 'null' body removes it).")
            .Accepts<CanaryPolicy>(true, "application/json")
            .WithDescription(
                "Can only be defined on two-arm experiments: canaryVariant is the canary, " +
                "and the single remaining arm counts as control. Works regardless of the experiment's status (Draft or Running).");

        builder.MapGet("/api/experiments/{name}/canary", GetCanaryAsync)
            .RequireRole(roles.Reader)
            .RequireApiKeyScope(ApiKeyScope.ExperimentsRead)
            .WithName("AgentPrismGetExperimentCanary")
            .WithTags("AgentPrism", "Experiments")
            .WithSummary("Gets the canary rule and its current evaluation.")
            .WithDescription("The evaluation is not persisted; it is recalculated on every call using current run results.");
    }

    private static async Task<Ok<IReadOnlyList<Experiment>>> ListAsync(
        [FromServices] IExperimentStore store,
        [FromServices] ITenantContext tenants,
        CancellationToken cancellationToken)
    {
        var experiments = await store.ListAsync(tenants.TenantId, cancellationToken).ConfigureAwait(false);
        return TypedResults.Ok(experiments);
    }

    private static async Task<Results<Ok<Experiment>, ProblemHttpResult>> GetAsync(
        string name,
        [FromServices] IExperimentStore store,
        [FromServices] ITenantContext tenants,
        CancellationToken cancellationToken)
    {
        var experiment = await store.GetAsync(tenants.TenantId, name, cancellationToken).ConfigureAwait(false);
        return experiment is null ? ExperimentNotFound(name) : TypedResults.Ok(experiment);
    }

    private static async Task<Results<Ok<Experiment>, ProblemHttpResult>> SaveAsync(
        string name,
        HttpContext httpContext,
        [FromServices] IExperimentStore store,
        [FromServices] IAgentCatalog catalog,
        [FromServices] IAgentDefinitionStore definitions,
        [FromServices] ITenantContext tenants,
        CancellationToken cancellationToken)
    {
        var (bound, bindError) = await RequestBodyBinding
            .ReadAsync<ExperimentSaveRequest>(httpContext, cancellationToken)
            .ConfigureAwait(false);

        if (bindError is not null)
        {
            return bindError;
        }

        var request = bound!;

        if (string.IsNullOrWhiteSpace(request.AgentName))
        {
            return InvalidExperiment("'agentName' is required.");
        }

        var descriptors = await catalog.ListAsync(cancellationToken).ConfigureAwait(false);
        var descriptor = descriptors.FirstOrDefault(
            candidate => string.Equals(candidate.Name, request.AgentName, StringComparison.Ordinal));

        if (descriptor is null)
        {
            return TypedResults.Problem(
                title: "Agent not found",
                detail: $"There is no agent named '{request.AgentName}'.",
                statusCode: StatusCodes.Status404NotFound);
        }

        if (descriptor.Origin == AgentDefinitionOrigin.Code)
        {
            return InvalidExperiment(
                $"'{request.AgentName}' is defined in code and has no version history. Experiments cannot be set up on code-sourced agents.");
        }

        if (request.Variants.Count == 0)
        {
            return InvalidExperiment("An experiment must have at least one variant.");
        }

        var totalWeight = request.Variants.Sum(static variant => variant.Weight);

        if (totalWeight != 100)
        {
            return InvalidExperiment($"Variant weights must sum to 100; currently {totalWeight}.");
        }

        foreach (var variant in request.Variants)
        {
            if (await definitions.GetVersionAsync(request.AgentName, variant.Version, cancellationToken).ConfigureAwait(false) is null)
            {
                return InvalidExperiment($"Agent '{request.AgentName}' has no version {variant.Version}.");
            }
        }

        var existing = await store.GetAsync(tenants.TenantId, name, cancellationToken).ConfigureAwait(false);

        var experiment = new Experiment
        {
            Id = existing?.Id ?? AgentPrismId.NewId(),
            TenantId = tenants.TenantId,
            Name = name,
            AgentName = request.AgentName,
            Variants = request.Variants,
        };

        try
        {
            var saved = await store.SaveAsync(experiment, cancellationToken).ConfigureAwait(false);
            return TypedResults.Ok(saved);
        }
        catch (AgentPrismException ex)
        {
            return TypedResults.Problem(title: "Experiment could not be saved", detail: ex.Message, statusCode: StatusCodes.Status409Conflict);
        }
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> DeleteAsync(
        string name,
        [FromServices] IExperimentStore store,
        [FromServices] ITenantContext tenants,
        CancellationToken cancellationToken)
    {
        try
        {
            return await store.DeleteAsync(tenants.TenantId, name, cancellationToken).ConfigureAwait(false)
                ? TypedResults.NoContent()
                : ExperimentNotFound(name);
        }
        catch (AgentPrismException ex)
        {
            return TypedResults.Problem(title: "Experiment could not be deleted", detail: ex.Message, statusCode: StatusCodes.Status409Conflict);
        }
    }

    private static async Task<Results<Ok<Experiment>, ProblemHttpResult>> StartAsync(
        string name,
        [FromServices] IExperimentStore store,
        [FromServices] ITenantContext tenants,
        CancellationToken cancellationToken)
    {
        try
        {
            return TypedResults.Ok(await store.StartAsync(tenants.TenantId, name, cancellationToken).ConfigureAwait(false));
        }
        catch (AgentPrismException ex)
        {
            return TypedResults.Problem(title: "Experiment could not be started", detail: ex.Message, statusCode: StatusCodes.Status409Conflict);
        }
    }

    private static async Task<Results<Ok<Experiment>, ProblemHttpResult>> StopAsync(
        string name,
        [FromServices] IExperimentStore store,
        [FromServices] ITenantContext tenants,
        CancellationToken cancellationToken)
    {
        try
        {
            return TypedResults.Ok(await store.StopAsync(tenants.TenantId, name, cancellationToken).ConfigureAwait(false));
        }
        catch (AgentPrismException ex)
        {
            return TypedResults.Problem(title: "Experiment could not be stopped", detail: ex.Message, statusCode: StatusCodes.Status409Conflict);
        }
    }

    private static async Task<Results<Ok<ExperimentResultsResponse>, ProblemHttpResult>> GetResultsAsync(
        string name,
        [FromServices] IExperimentStore store,
        [FromServices] IRunStore runStore,
        [FromServices] ITenantContext tenants,
        CancellationToken cancellationToken)
    {
        var experiment = await store.GetAsync(tenants.TenantId, name, cancellationToken).ConfigureAwait(false);

        if (experiment is null)
        {
            return ExperimentNotFound(name);
        }

        var results = await runStore
            .GetExperimentResultsAsync(new ExperimentResultsQuery { ExperimentId = experiment.Id, TenantId = tenants.TenantId }, cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.Ok(new ExperimentResultsResponse { Experiment = experiment, Results = results });
    }

    private static async Task<Results<Ok<Experiment>, ProblemHttpResult>> SetCanaryAsync(
        string name,
        HttpContext httpContext,
        [FromServices] IExperimentStore store,
        [FromServices] ITenantContext tenants,
        CancellationToken cancellationToken)
    {
        var (policy, bindError) = await RequestBodyBinding
            .ReadOptionalAsync<CanaryPolicy>(httpContext, cancellationToken)
            .ConfigureAwait(false);

        if (bindError is not null)
        {
            return bindError;
        }

        var experiment = await store.GetAsync(tenants.TenantId, name, cancellationToken).ConfigureAwait(false);

        if (experiment is null)
        {
            return ExperimentNotFound(name);
        }

        if (policy is not null)
        {
            var validationError = ValidateCanaryPolicy(experiment, policy);

            if (validationError is not null)
            {
                return InvalidExperiment(validationError);
            }
        }

        try
        {
            var updated = await store.SetCanaryPolicyAsync(tenants.TenantId, name, policy, cancellationToken).ConfigureAwait(false);
            return TypedResults.Ok(updated);
        }
        catch (AgentPrismException ex)
        {
            return TypedResults.Problem(title: "Canary rule could not be updated", detail: ex.Message, statusCode: StatusCodes.Status409Conflict);
        }
    }

    private static async Task<Results<Ok<ExperimentCanaryResponse>, ProblemHttpResult>> GetCanaryAsync(
        string name,
        [FromServices] IExperimentStore store,
        [FromServices] IRunStore runStore,
        [FromServices] ITenantContext tenants,
        [FromServices] TimeProvider? timeProvider,
        CancellationToken cancellationToken)
    {
        var experiment = await store.GetAsync(tenants.TenantId, name, cancellationToken).ConfigureAwait(false);

        if (experiment is null)
        {
            return ExperimentNotFound(name);
        }

        if (experiment.Canary is not { } policy)
        {
            return TypedResults.Ok(new ExperimentCanaryResponse { Policy = null, Evaluation = null });
        }

        var results = await runStore
            .GetExperimentResultsAsync(new ExperimentResultsQuery { ExperimentId = experiment.Id, TenantId = tenants.TenantId }, cancellationToken)
            .ConfigureAwait(false);

        var now = (timeProvider ?? TimeProvider.System).GetUtcNow();
        var evaluation = CanaryEvaluator.Evaluate(policy, results, now);

        return TypedResults.Ok(new ExperimentCanaryResponse { Policy = policy, Evaluation = evaluation });
    }

    /// <summary>
    /// Validates a canary rule. The two-arm constraint from 56.4 is enforced
    /// here: if the number of remaining arms is not 1, session stickiness
    /// (see the <see cref="CanaryPolicy"/> class documentation) cannot be guaranteed.
    /// </summary>
    private static string? ValidateCanaryPolicy(Experiment experiment, CanaryPolicy policy)
    {
        if (experiment.Variants.Count != 2)
        {
            return "A canary rule can only be defined on two-variant experiments.";
        }

        if (!experiment.Variants.Any(variant => string.Equals(variant.Name, policy.CanaryVariant, StringComparison.Ordinal)))
        {
            return $"There is no variant named '{policy.CanaryVariant}'.";
        }

        if (policy.MinSampleSize <= 0)
        {
            return "'minSampleSize' must be positive.";
        }

        if (policy.MaxErrorRateDelta is < 0 or > 1)
        {
            return "'maxErrorRateDelta' must be between 0 and 1.";
        }

        if (policy.MinScore is < 0 or > 100)
        {
            return "'minScore' must be between 0 and 100.";
        }

        if (policy.RampSteps.Count > 0)
        {
            if (policy.RampSteps.Any(step => step is <= 0 or > 100))
            {
                return "'rampSteps' values must be between 0 (exclusive) and 100.";
            }

            if (!policy.RampSteps.SequenceEqual(policy.RampSteps.OrderBy(static step => step).Distinct()))
            {
                return "'rampSteps' must be strictly increasing, with no duplicates.";
            }

            if (policy.RampInterval <= TimeSpan.Zero)
            {
                return "'rampInterval' must be positive.";
            }
        }

        return null;
    }

    private static ProblemHttpResult InvalidExperiment(string detail)
        => TypedResults.Problem(title: "Experiment invalid", detail: detail, statusCode: StatusCodes.Status400BadRequest);

    private static ProblemHttpResult ExperimentNotFound(string name)
        => TypedResults.Problem(
            title: "Experiment not found",
            detail: $"There is no experiment named '{name}'.",
            statusCode: StatusCodes.Status404NotFound);
}
