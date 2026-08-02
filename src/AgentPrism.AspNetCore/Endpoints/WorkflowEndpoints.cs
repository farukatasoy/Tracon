using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace AgentPrism;

/// <summary>
/// Workflow katalogu, tanim yonetimi, calistirma ve kontrol noktasi uclari.
/// </summary>
/// <remarks>
/// <para>
/// 🚨 <see cref="IWorkflowRunner"/> ve <c>IWorkflowCheckpointStore</c>
/// parametreleri <c>[FromServices]</c> ile <strong>acikca</strong> isaretlenir.
/// Minimal API, kayitli olmayabilecek bir tipi gorup "body" cikarimina gecer ve
/// tek bir uctaki bu hata <em>butun</em> uclari kirar - Faz 9'da olculdu
/// (119 testin 112'si ayni anda dustu, hata mesaji hedeften kopuktu).
/// </para>
/// <para>
/// Yurutme motoru kayitli degilse calistirma uclari <c>501</c> doner; tanim
/// yonetimi calismaya devam eder. Ayni desen MCP tazeleme ucunda kuruldu.
/// </para>
/// </remarks>
internal static class WorkflowEndpoints
{
    private static JsonSerializerOptions JsonOptions { get; } = new(JsonSerializerDefaults.Web);

    /// <summary>Workflow uclarini baglar.</summary>
    /// <param name="builder">Uc grubu.</param>
    /// <param name="roles">Cozulmus rol policy'leri.</param>
    public static void Map(IEndpointRouteBuilder builder, AgentPrismRolePolicies roles)
    {
        builder.MapGet("/api/workflows", ListAsync)
            .RequireRole(roles.Reader)
            .WithName("AgentPrismListWorkflows")
            .WithSummary("Kodda tanimli ve veritabaninda saklanan workflow'lari listeler.");

        builder.MapGet("/api/workflows/{name}", GetAsync)
            .RequireRole(roles.Reader)
            .WithName("AgentPrismGetWorkflow")
            .WithSummary("Tek bir workflow tanimini dondurur.");

        builder.MapPut("/api/workflows/{name}", SaveAsync)
            .RequireRole(roles.Admin)
            .WithName("AgentPrismSaveWorkflow")
            .WithSummary("Workflow tanimi olusturur veya gunceller.");

        builder.MapDelete("/api/workflows/{name}", DeleteAsync)
            .RequireRole(roles.Admin)
            .WithName("AgentPrismDeleteWorkflow")
            .WithSummary("Bir workflow tanimini siler.");

        builder.MapPost("/api/workflows/{name}/run", RunAsync)
            .RequireRole(roles.Operator)
            .WithName("AgentPrismRunWorkflow")
            .WithSummary("Workflow'u calistirir ve olaylarini SSE ile akitir.")
            .WithDescription(
                "Her cerceve bir RunEvent tasir. Ilk cerceve calistirma kimligini bildirir; " +
                "workflow icinde cagrilan her agent kendi runs satirini acar ve " +
                "GET /api/runs/{runId}/tree ile agac olarak gorulur.");

        builder.MapGet("/api/workflows/runs/{runId:guid}/checkpoints", ListCheckpointsAsync)
            .RequireRole(roles.Reader)
            .WithName("AgentPrismListWorkflowCheckpoints")
            .WithSummary("Bir workflow calistirmasinin kontrol noktalarini listeler.");

        builder.MapPost("/api/workflows/runs/{runId:guid}/resume", ResumeAsync)
            .RequireRole(roles.Operator)
            .WithName("AgentPrismResumeWorkflow")
            .WithSummary("Bir kontrol noktasindan devam eder ve olaylari SSE ile akitir.");
    }

    private static async Task<Results<Ok<IReadOnlyList<WorkflowDescriptor>>, ProblemHttpResult>> ListAsync(
        [FromServices] IWorkflowDefinitionStore store,
        [FromServices] ITenantContext tenants,
        [FromServices] IWorkflowRunner? runner,
        CancellationToken cancellationToken)
    {
        // Motor kayitliysa katalog ondan okunur: yalnizca o, kodda tanimli
        // workflow'lari da gorur. Kayitli degilse veritabani tanimlari yine
        // listelenir - tanim yonetimi motordan bagimsizdir.
        if (runner is not null)
        {
            return TypedResults.Ok(await runner.ListAsync(cancellationToken).ConfigureAwait(false));
        }

        var stored = await store.ListAsync(tenants.TenantId, cancellationToken).ConfigureAwait(false);

        return TypedResults.Ok<IReadOnlyList<WorkflowDescriptor>>(
            [.. stored.Select(Describe)]);
    }

    private static async Task<Results<Ok<WorkflowDefinition>, ProblemHttpResult>> GetAsync(
        string name,
        [FromServices] IWorkflowDefinitionStore store,
        [FromServices] ITenantContext tenants,
        CancellationToken cancellationToken)
    {
        var definition = await store.GetAsync(tenants.TenantId, name, cancellationToken).ConfigureAwait(false);

        return definition is null ? NotFound(name) : TypedResults.Ok(definition);
    }

    private static async Task<Results<Ok<WorkflowDefinition>, ProblemHttpResult>> SaveAsync(
        string name,
        [FromBody] WorkflowSaveRequest request,
        [FromServices] IWorkflowDefinitionStore store,
        [FromServices] ITenantContext tenants,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var definition = new WorkflowDefinition
        {
            Name = name,
            DisplayName = request.DisplayName,
            Description = request.Description,
            Kind = request.Kind,
            AgentNames = request.AgentNames ?? [],
            ManagerAgentName = request.ManagerAgentName,
            MaxIterations = request.MaxIterations,
            HandoffInstructions = request.HandoffInstructions,
        };

        // Dogrulama KAYIT ANINDA yapilir. Gecersiz bir tanimi kabul edip
        // calistirma aninda patlatmak, kullaniciyi hatayi bulmak icin bir
        // calistirma baslatmaya zorlardi. Ayni denetimi workflow derleyicisi de
        // kullanir; kural tek yerde (AgentPrism.Core) yasar.
        if (WorkflowDefinitionValidator.Validate(definition) is { } message)
        {
            return TypedResults.Problem(
                title: "Workflow tanimi gecersiz",
                detail: message,
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
        [FromBody] WorkflowRunHttpRequest? request,
        [FromServices] IWorkflowRunner? runner,
        CancellationToken cancellationToken)
    {
        if (runner is null)
        {
            return NotRegistered();
        }

        if (await runner.GetAsync(name, cancellationToken).ConfigureAwait(false) is null)
        {
            return NotFound(name);
        }

        var runId = AgentPrismId.NewId();

        return new WorkflowEventStream(
            runner.RunStreamingAsync(
                new WorkflowRunRequest
                {
                    WorkflowName = name,
                    Message = request?.Message,
                    SessionId = request?.SessionId,
                    RunId = runId,
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
            CancellationToken cancellationToken)
    {
        var record = await runs.GetRunAsync(runId, cancellationToken).ConfigureAwait(false);

        // Kiraci eslesmezse "bulunamadi" denir; baska bir kiracinin
        // calistirmasinin varligi sizdirilmaz.
        if (record is null || !string.Equals(record.TenantId, tenants.TenantId, StringComparison.Ordinal))
        {
            return RunNotFound(runId);
        }

        var list = await checkpoints
            .ListByRunAsync(tenants.TenantId, runId, cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.Ok(list);
    }

    private static async Task<IResult> ResumeAsync(
        Guid runId,
        [FromBody] WorkflowResumeHttpRequest? request,
        [FromServices] IWorkflowRunner? runner,
        CancellationToken cancellationToken)
    {
        if (runner is null)
        {
            return NotRegistered();
        }

        var newRunId = AgentPrismId.NewId();

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

    private static WorkflowDescriptor Describe(WorkflowDefinition definition)
        => new()
        {
            Name = definition.Name,
            DisplayName = definition.DisplayName,
            Description = definition.Description,
            Origin = AgentDefinitionOrigin.Database,
            Kind = definition.Kind,
            AgentNames = definition.AgentNames,
            Version = definition.Version,
            UpdatedAt = definition.UpdatedAt,
        };

    private static ProblemHttpResult NotRegistered()
        => TypedResults.Problem(
            title: "Workflow motoru kayitli degil",
            detail: "Workflow calistirmak icin AgentPrism.Workflows paketini ekleyin ve UseWorkflows() cagirin.",
            statusCode: StatusCodes.Status501NotImplemented);

    private static ProblemHttpResult NotFound(string name)
        => TypedResults.Problem(
            title: "Workflow bulunamadi",
            detail: $"'{name}' adinda bir workflow yok.",
            statusCode: StatusCodes.Status404NotFound);

    private static ProblemHttpResult RunNotFound(Guid runId)
        => TypedResults.Problem(
            title: "Calistirma bulunamadi",
            detail: $"'{runId}' kimlikli calistirma yok.",
            statusCode: StatusCodes.Status404NotFound);

    /// <summary>
    /// Workflow olaylarini SSE olarak yazar.
    /// </summary>
    /// <remarks>
    /// Ayri bir <see cref="IResult"/> olarak yazilir cunku akis basladiktan
    /// sonra durum kodu degistirilemez; hata durumunda <c>event: error</c>
    /// cercevesi gonderilir. Ayni desen agent deneme calistirmasinda da kullanildi.
    /// </remarks>
    private sealed class WorkflowEventStream(IAsyncEnumerable<RunEvent> events, Guid runId) : IResult
    {
        public async Task ExecuteAsync(HttpContext httpContext)
        {
            ArgumentNullException.ThrowIfNull(httpContext);

            var cancellationToken = httpContext.RequestAborted;
            var writer = await SseWriter.StartAsync(httpContext.Response, cancellationToken).ConfigureAwait(false);
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
            catch (OperationCanceledException)
            {
                // Istemci baglantiyi kesti. Yazacak kimse kalmadi.
            }
            catch (Exception ex) when (ex is AgentPrismException or InvalidOperationException or HttpRequestException)
            {
                await writer.WriteEventAsync(
                    sequence,
                    "error",
                    JsonSerializer.Serialize(new WorkflowRunFailed(ex.GetType().Name, ex.Message), JsonOptions),
                    CancellationToken.None).ConfigureAwait(false);
            }
        }

        private sealed record WorkflowRunAccepted(Guid RunId);

        private sealed record WorkflowRunCompleted(Guid RunId);

        private sealed record WorkflowRunFailed(string Type, string Message);
    }
}
