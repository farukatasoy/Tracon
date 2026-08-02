using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.AI;

namespace AgentPrism;

/// <summary>
/// Agent katalogu ve tanim yonetimi uclari.
/// </summary>
/// <remarks>
/// Kodda tanimli agent'lar salt okunurdur. Ad cakismasinda kod kazanir
/// (karar K-003); veritabanina yazilan ayni adli bir tanim hicbir zaman
/// cozulmezdi. Bu yuzden yazma uclari boyle bir istegi sessizce kabul etmek
/// yerine <c>409 Conflict</c> dondurur.
/// </remarks>
internal static class AgentEndpoints
{
    /// <summary>Agent uclarini baglar.</summary>
    /// <param name="builder">Uc grubu.</param>
    public static void Map(IEndpointRouteBuilder builder)
    {
        builder.MapGet("/api/agents", async Task<Ok<IReadOnlyList<AgentDescriptor>>> (
                IAgentCatalog catalog,
                CancellationToken cancellationToken)
                => TypedResults.Ok(await catalog.ListAsync(cancellationToken).ConfigureAwait(false)))
            .WithName("AgentPrismListAgents")
            .WithSummary("Kodda ve veritabaninda tanimli tum agent'lari listeler.");

        builder.MapGet("/api/agents/{name}", GetAgentAsync)
            .WithName("AgentPrismGetAgent")
            .WithSummary("Bir agent'in katalog ozetini ve varsa kalici tanimini dondurur.");

        builder.MapPost("/api/agents", CreateAgentAsync)
            .WithName("AgentPrismCreateAgent")
            .WithSummary("Yeni bir agent tanimi olusturur.");

        builder.MapPut("/api/agents/{name}", UpdateAgentAsync)
            .WithName("AgentPrismUpdateAgent")
            .WithSummary("Bir agent tanimini gunceller ve yeni bir surum uretir.");

        builder.MapDelete("/api/agents/{name}", DeleteAgentAsync)
            .WithName("AgentPrismDeleteAgent")
            .WithSummary("Bir agent tanimini ve surum gecmisini siler.");

        builder.MapGet("/api/agents/{name}/versions", ListVersionsAsync)
            .WithName("AgentPrismListAgentVersions")
            .WithSummary("Bir tanimin surum gecmisini yeniden eskiye listeler.");

        builder.MapPost("/api/agents/{name}/rollback", RollbackAsync)
            .WithName("AgentPrismRollbackAgent")
            .WithSummary("Bir tanimi onceki bir surumun icerigiyle yeni surum olarak yazar.");

        builder.MapPost("/api/agents/{name}/run", RunAsync)
            .WithName("AgentPrismRunAgent")
            .WithSummary("Bir agent'i deneme amaciyla calistirir ve yaniti SSE ile akitir.");
    }

    private static async Task<Results<Ok<AgentDetailResponse>, ProblemHttpResult>> GetAgentAsync(
        string name,
        IAgentCatalog catalog,
        IAgentDefinitionStore definitions,
        CancellationToken cancellationToken)
    {
        var descriptor = await FindDescriptorAsync(catalog, name, cancellationToken).ConfigureAwait(false);

        if (descriptor is null)
        {
            return NotFound(name);
        }

        var definition = await definitions.GetAsync(name, cancellationToken).ConfigureAwait(false);

        return TypedResults.Ok(new AgentDetailResponse
        {
            Descriptor = descriptor,
            Definition = definition,
            IsEditable = descriptor.Origin == AgentDefinitionOrigin.Database,
        });
    }

    private static async Task<Results<Created<AgentDefinition>, ProblemHttpResult>> CreateAgentAsync(
        AgentDefinitionRequest request,
        IAgentCatalog catalog,
        IAgentDefinitionStore definitions,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (Validate(request) is { } invalid)
        {
            return invalid;
        }

        if (await FindDescriptorAsync(catalog, request.Name, cancellationToken).ConfigureAwait(false) is { } existing)
        {
            return TypedResults.Problem(
                title: "Agent adi kullanimda",
                detail: existing.Origin == AgentDefinitionOrigin.Code
                    ? $"'{request.Name}' kodda tanimli bir agent'tir ve yonetim API'sinden degistirilemez. " +
                      "Ad cakismasinda kod kazandigi icin ayni adla yazilan bir tanim hicbir zaman cozulmezdi."
                    : $"'{request.Name}' adinda bir tanim zaten var. Guncellemek icin PUT kullanin.",
                statusCode: StatusCodes.Status409Conflict);
        }

        var saved = await definitions
            .SaveAsync(request.ToDefinition(), cancellationToken).ConfigureAwait(false);

        return TypedResults.Created($"{httpContext.Request.Path}/{Uri.EscapeDataString(saved.Name)}", saved);
    }

    private static async Task<Results<Ok<AgentDefinition>, ProblemHttpResult>> UpdateAgentAsync(
        string name,
        AgentDefinitionRequest request,
        IAgentCatalog catalog,
        IAgentDefinitionStore definitions,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(name, request.Name, StringComparison.Ordinal))
        {
            return TypedResults.Problem(
                title: "Ad uyusmuyor",
                detail: $"Yoldaki ad '{name}', govdedeki ad '{request.Name}'. Agent adi degistirilemez; " +
                        "yeni bir ad icin yeni bir tanim olusturun.",
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
        AgentRollbackRequest request,
        IAgentCatalog catalog,
        IAgentDefinitionStore definitions,
        CancellationToken cancellationToken)
    {
        if (await GuardCodeAgentAsync(catalog, name, cancellationToken).ConfigureAwait(false) is { } conflict)
        {
            return conflict;
        }

        try
        {
            return TypedResults.Ok(
                await definitions.RollbackAsync(name, request.Version, cancellationToken).ConfigureAwait(false));
        }
        catch (AgentPrismException ex)
        {
            return TypedResults.Problem(
                title: "Geri alinamadi",
                detail: ex.Message,
                statusCode: StatusCodes.Status404NotFound);
        }
    }

    private static async Task<IResult> RunAsync(
        string name,
        AgentRunRequest request,
        IAgentCatalog catalog,
        AgentSessionManager sessions,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Message))
        {
            return Results.Problem(
                title: "Mesaj bos",
                detail: "'message' alani zorunludur.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        Microsoft.Agents.AI.AIAgent? agent;

        // Cozumleme bildirimsel bir tanimi derler; bilinmeyen tool veya saglayici
        // burada hata verir. Yanit henuz baslamadigi icin duzgun bir ProblemDetails
        // dondurebiliyoruz - akis basladiktan sonra bu mumkun olmaz.
        try
        {
            agent = await catalog.ResolveAsync(name, cancellationToken).ConfigureAwait(false);
        }
        catch (AgentPrismException ex)
        {
            return Results.Problem(
                title: "Agent derlenemedi",
                detail: ex.Message,
                statusCode: StatusCodes.Status400BadRequest);
        }

        if (agent is null)
        {
            return Results.Problem(
                title: "Agent bulunamadi",
                detail: $"'{name}' adinda bir agent yok.",
                statusCode: StatusCodes.Status404NotFound);
        }

        return new AgentRunStream(agent, request, sessions);
    }

    /// <summary>
    /// Deneme calistirmasinin yanitini SSE olarak yazar.
    /// </summary>
    /// <remarks>
    /// Ayri bir <see cref="IResult"/> olarak yazilir cunku akis basladiktan sonra
    /// durum kodu degistirilemez; hata durumunda <c>event: error</c> cercevesi
    /// gonderilir.
    /// </remarks>
    private sealed class AgentRunStream(
        Microsoft.Agents.AI.AIAgent agent,
        AgentRunRequest request,
        AgentSessionManager sessions) : IResult
    {
        public async Task ExecuteAsync(HttpContext httpContext)
        {
            ArgumentNullException.ThrowIfNull(httpContext);

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

                var updates = agent.RunStreamingAsync(request.Message, session, cancellationToken: cancellationToken);

                await foreach (var update in updates.ConfigureAwait(false))
                {
                    var payload = JsonSerializer.Serialize(update, AIJsonUtilities.DefaultOptions);
                    await writer.WriteEventAsync(sequence++, "update", payload, cancellationToken).ConfigureAwait(false);
                }

                if (session is not null)
                {
                    await sessions.SaveSessionAsync(agent, session, cancellationToken).ConfigureAwait(false);
                }

                await writer.WriteEventAsync(
                    sequence,
                    "done",
                    JsonSerializer.Serialize(new AgentRunCompleted(request.SessionId), JsonOptions),
                    cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Istemci baglantiyi kesti. Yazacak kimse kalmadi.
            }
            catch (Exception ex) when (ex is AgentPrismException or InvalidOperationException or HttpRequestException)
            {
                await writer.WriteEventAsync(
                    sequence,
                    "error",
                    JsonSerializer.Serialize(new AgentRunFailed(ex.GetType().Name, ex.Message), JsonOptions),
                    CancellationToken.None).ConfigureAwait(false);
            }
        }

        private static JsonSerializerOptions JsonOptions { get; } = new(JsonSerializerDefaults.Web);

        private sealed record AgentRunCompleted(string? SessionId);

        private sealed record AgentRunFailed(string Type, string Message);
    }

    private static async ValueTask<AgentDescriptor?> FindDescriptorAsync(
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

    private static async ValueTask<ProblemHttpResult?> GuardCodeAgentAsync(
        IAgentCatalog catalog,
        string name,
        CancellationToken cancellationToken)
    {
        var descriptor = await FindDescriptorAsync(catalog, name, cancellationToken).ConfigureAwait(false);

        return descriptor?.Origin == AgentDefinitionOrigin.Code
            ? TypedResults.Problem(
                title: "Kodda tanimli agent degistirilemez",
                detail: $"'{name}' kodda tanimlidir. Kod tanimlari derleme zamaninda dogrulanir ve " +
                        "yonetim API'sinden degistirilemez; degisiklik icin uygulama kodunu guncelleyin.",
                statusCode: StatusCodes.Status409Conflict)
            : null;
    }

    private static ProblemHttpResult? Validate(AgentDefinitionRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return TypedResults.Problem(
                title: "Agent adi bos",
                detail: "'name' alani zorunludur.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        if (string.IsNullOrWhiteSpace(request.Model?.Provider) || string.IsNullOrWhiteSpace(request.Model.Model))
        {
            return TypedResults.Problem(
                title: "Model baglantisi eksik",
                detail: "'model.provider' ve 'model.model' alanlari zorunludur.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        return null;
    }

    private static ProblemHttpResult NotFound(string name)
        => TypedResults.Problem(
            title: "Agent bulunamadi",
            detail: $"'{name}' adinda bir agent yok.",
            statusCode: StatusCodes.Status404NotFound);
}
