using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace AgentPrism;

/// <summary>
/// A/B deneyi yonetimi, yasam dongusu ve sonuc goruntuleme uclari (Faz 19.3-19.4).
/// </summary>
/// <remarks>
/// 🚨 <see cref="IExperimentStore"/> disindaki tum bagimliliklar <c>[FromServices]</c>
/// ile <strong>acikca</strong> isaretlenir — gerekce <see cref="EvalEndpoints"/> ile
/// aynidir. Sonuclar bu depoda degil, <see cref="IRunStore.GetExperimentResultsAsync"/>
/// uzerinden gelir (K-041: hesap depoda yapilir).
/// </remarks>
internal static class ExperimentEndpoints
{
    /// <summary>Deney uclarini baglar.</summary>
    /// <param name="builder">Uc grubu.</param>
    /// <param name="roles">Cozulmus rol policy'leri.</param>
    public static void Map(IEndpointRouteBuilder builder, AgentPrismRolePolicies roles)
    {
        builder.MapGet("/api/experiments", ListAsync)
            .RequireRole(roles.Reader)
            .RequireApiKeyScope(ApiKeyScope.ExperimentsRead)
            .WithName("AgentPrismListExperiments")
            .WithTags("AgentPrism", "Experiments")
            .WithSummary("Bir kiracinin A/B deneylerini listeler.");

        builder.MapGet("/api/experiments/{name}", GetAsync)
            .RequireRole(roles.Reader)
            .RequireApiKeyScope(ApiKeyScope.ExperimentsRead)
            .WithName("AgentPrismGetExperiment")
            .WithTags("AgentPrism", "Experiments")
            .WithSummary("Tek bir A/B deneyini getirir.");

        builder.MapPut("/api/experiments/{name}", SaveAsync)
            .RequireRole(roles.Admin)
            .RequireApiKeyScope(ApiKeyScope.ExperimentsAdmin)
            .WithName("AgentPrismSaveExperiment")
            .WithTags("AgentPrism", "Experiments")
            .WithSummary("Deney olusturur veya gunceller.")
            .Accepts<ExperimentSaveRequest>("application/json")
            .WithDescription(
                "Yalnizca ayni agent'in surumleri arasinda deney kurulabilir; kod kaynakli " +
                "agent'larda surum gecmisi olmadigi icin reddedilir. Varyant agirliklari toplami 100 olmalidir.");

        builder.MapDelete("/api/experiments/{name}", DeleteAsync)
            .RequireRole(roles.Admin)
            .RequireApiKeyScope(ApiKeyScope.ExperimentsAdmin)
            .WithName("AgentPrismDeleteExperiment")
            .WithTags("AgentPrism", "Experiments")
            .WithSummary("Bir deneyi siler. Calisan bir deney once durdurulmalidir.");

        builder.MapPost("/api/experiments/{name}/start", StartAsync)
            .RequireRole(roles.Admin)
            .RequireApiKeyScope(ApiKeyScope.ExperimentsAdmin)
            .WithName("AgentPrismStartExperiment")
            .WithTags("AgentPrism", "Experiments")
            .WithSummary("Deneyi baslatir; trafik agirliklara gore bolunmeye baslar.")
            .WithDescription("Ayni agent icin ayni anda tek deney calisabilir.");

        builder.MapPost("/api/experiments/{name}/stop", StopAsync)
            .RequireRole(roles.Admin)
            .RequireApiKeyScope(ApiKeyScope.ExperimentsAdmin)
            .WithName("AgentPrismStopExperiment")
            .WithTags("AgentPrism", "Experiments")
            .WithSummary("Deneyi durdurur; yeni calistirmalar guncel surume gider.");

        builder.MapGet("/api/experiments/{name}/results", GetResultsAsync)
            .RequireRole(roles.Reader)
            .RequireApiKeyScope(ApiKeyScope.ExperimentsRead)
            .WithName("AgentPrismGetExperimentResults")
            .WithTags("AgentPrism", "Experiments")
            .WithSummary("Kol bazinda sayi, hata orani, token ve sure ozetini getirir.")
            .WithDescription("Istatistiksel bir 'kazanan' iddiasi yoktur; ham sayilar gosterilir.");

        builder.MapPut("/api/experiments/{name}/canary", SetCanaryAsync)
            .RequireRole(roles.Admin)
            .RequireApiKeyScope(ApiKeyScope.ExperimentsAdmin)
            .WithName("AgentPrismSetExperimentCanary")
            .WithTags("AgentPrism", "Experiments")
            .WithSummary("Kanarya kuralini tanimlar veya kaldirir (govde 'null').")
            .Accepts<CanaryPolicy>(true, "application/json")
            .WithDescription(
                "Yalnizca iki kollu deneylerde tanimlanabilir: kanaryaVariant kanarya, kalan TEK kol " +
                "kontrol sayilir. Deneyin durumundan bagimsiz calisir (Draft veya Running).");

        builder.MapGet("/api/experiments/{name}/canary", GetCanaryAsync)
            .RequireRole(roles.Reader)
            .RequireApiKeyScope(ApiKeyScope.ExperimentsRead)
            .WithName("AgentPrismGetExperimentCanary")
            .WithTags("AgentPrism", "Experiments")
            .WithSummary("Kanarya kuralini ve guncel degerlendirmesini getirir.")
            .WithDescription("Degerlendirme kalici degildir; her cagrida guncel calistirma sonuclariyla yeniden hesaplanir.");
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
    /// Bir kanarya kuralinin gecerliligini denetler. 56.4'un iki kollu kisiti
    /// burada zorlanir: kalan kol sayisi 1'den farkliysa oturum kararliligi
    /// (bkz. <see cref="CanaryPolicy"/> sinif belgesi) garanti edilemez.
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
