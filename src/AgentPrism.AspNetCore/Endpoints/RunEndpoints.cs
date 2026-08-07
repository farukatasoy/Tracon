using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;

namespace AgentPrism;

/// <summary>
/// Calistirma kaydi ve olay akisi uclari.
/// </summary>
internal static class RunEndpoints
{
    /// <summary>
    /// Tek bir agacta dondurulecek en fazla calistirma sayisi.
    /// </summary>
    /// <remarks>
    /// Ust sinir, butcenin <c>MaxTotalRuns</c> varsayilanindan (25) belirgin sekilde
    /// buyuktur: butce yukseltilmis bir kurulumda agac kirpilmis gorunmemelidir.
    /// Yine de sinirsiz degildir; sayfalanmayan bir uctur.
    /// </remarks>
    private const int MaxTreeSize = 200;

    /// <summary>Calistirma uclarini baglar.</summary>
    /// <param name="builder">Uc grubu.</param>
    /// <param name="options">Erisim ve akis ayarlari.</param>
    /// <param name="roles">Cozulmus rol policy'leri.</param>
    public static void Map(IEndpointRouteBuilder builder, AgentPrismEndpointOptions options, AgentPrismRolePolicies roles)
    {
        builder.MapGet("/api/runs", async Task<Ok<IReadOnlyList<RunRecord>>> (
                IRunStore runs,
                [FromQuery] string? agentName,
                [FromQuery] RunStatus? status,
                [FromQuery] string? sessionId,
                [FromQuery] DateTimeOffset? startedAfter,
                [FromQuery] bool? includeChildren,
                [FromQuery] Guid? parentRunId,
                [FromQuery] Guid? rootRunId,
                [FromQuery] int? skip,
                [FromQuery] int? take,
                CancellationToken cancellationToken) =>
            {
                var records = await runs.QueryRunsAsync(
                    new RunQuery
                    {
                        AgentName = agentName,
                        Status = status,
                        SessionId = sessionId,
                        StartedAfter = startedAfter,

                        // Varsayilan yalniz kok calistirmalardir: bir agent baska
                        // agent'lari cagirdiginda liste kullanicinin baslatmadigi
                        // satirlarla dolar.
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
            .WithName("AgentPrismListRuns")
            .WithTags("AgentPrism", "Runs")
            .WithSummary("Calistirmalari en yeniden eskiye listeler.")
            .WithDescription(
                "Varsayilan olarak YALNIZ kok calistirmalar doner. Alt calistirmalari da gormek icin " +
                "'includeChildren=true' kullanin; tek bir agacin tamami icin 'rootRunId', bir " +
                "calistirmanin dogrudan cocuklari icin 'parentRunId' verin.");

        builder.MapGet("/api/runs/{runId:guid}/tree", async Task<Results<Ok<IReadOnlyList<RunRecord>>, ProblemHttpResult>> (
                Guid runId,
                IRunStore runs,
                CancellationToken cancellationToken) =>
            {
                if (await runs.GetRunAsync(runId, cancellationToken).ConfigureAwait(false) is not { } record)
                {
                    return NotFound(runId);
                }

                // Agac her zaman KOKUNDEN cekilir. Bir alt calistirmanin detayindan
                // gelen istek de tum agaci dondurur; kullanici kardes dallari
                // gormeden agacin neresinde oldugunu anlayamaz.
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
            .WithName("AgentPrismGetRunTree")
            .WithTags("AgentPrism", "Runs")
            .WithSummary("Bir calistirmanin ait oldugu agacin tamamini kokunden dondurur.");

        builder.MapGet("/api/runs/{runId:guid}", async Task<Results<Ok<RunRecord>, ProblemHttpResult>> (
                Guid runId,
                IRunStore runs,
                CancellationToken cancellationToken)
                => await runs.GetRunAsync(runId, cancellationToken).ConfigureAwait(false) is { } record
                    ? TypedResults.Ok(record)
                    : NotFound(runId))
            .RequireRole(roles.Reader)
            .WithName("AgentPrismGetRun")
            .WithTags("AgentPrism", "Runs")
            .WithSummary("Tek bir calistirmanin ozetini dondurur.");

        builder.MapGet("/api/runs/{runId:guid}/events", async Task<Results<ProblemHttpResult, IResult>> (
                Guid runId,
                IRunStore runs,
                HttpContext httpContext,
                CancellationToken cancellationToken) =>
            {
                // Varlik denetimi akis baslamadan once yapilir; yanit basladiktan
                // sonra durum kodu degistirilemez.
                if (await runs.GetRunAsync(runId, cancellationToken).ConfigureAwait(false) is null)
                {
                    return NotFound(runId);
                }

                return new RunEventStream(runId, runs, options.RunEventPollInterval);
            })
            .RequireRole(roles.Reader)
            .WithName("AgentPrismStreamRunEvents")
            .WithTags("AgentPrism", "Runs")
            .WithSummary("Bir calistirmanin olaylarini SSE ile akitir; canli ve gecmise donuk ayni yoldur.")
            .WithDescription(
                "Baglanti koparsa istemci 'Last-Event-ID' basligiyla kaldigi sira numarasindan devam eder. " +
                "Calistirma hala suruyorsa akis tamamlanana kadar acik kalir.");

        builder.MapPost("/api/runs/{runId:guid}/cancel", CancelRunAsync)
            .RequireRole(roles.Operator)
            .WithName("AgentPrismCancelRun")
            .WithTags("AgentPrism", "Runs")
            .WithSummary("Suren bir calistirmanin iptalini ister.")
            .WithDescription(
                "202 yalnizca iptal ISTENDIGINI bildirir; nihai durum 'GET /api/runs/{id}' ile okunur. " +
                "Calistirma bu ornekte yurutulmuyorsa (baska bir ornek veya yeniden baslamis surec) 409 doner. " +
                "Kok calistirmanin iptali agactaki tum alt calistirmalari da durdurur; bir alt calistirmanin " +
                "tek basina iptali koku etkilemez.");

        builder.MapPost("/api/runs/{runId:guid}/feedback", SaveFeedbackAsync)
            .RequireRole(roles.Operator)
            .WithName("AgentPrismSaveRunFeedback")
            .WithTags("AgentPrism", "Runs")
            .WithSummary("Bir calistirmaya veya tek bir mesaja puan yazar.")
            .WithDescription(
                "Ayni yazar ayni hedefi (calistirma veya mesaj) ikinci kez puanladiginda satir " +
                "GUNCELLENIR, yeni satir acilmaz. 'messageId' bos birakilirsa puan tum calistirmaya aittir.");

        builder.MapGet("/api/runs/{runId:guid}/feedback", ListFeedbackAsync)
            .RequireRole(roles.Reader)
            .WithName("AgentPrismListRunFeedback")
            .WithTags("AgentPrism", "Runs")
            .WithSummary("Bir calistirmanin tum puanlarini listeler.");

        builder.MapDelete("/api/runs/{runId:guid}/feedback/{scoreId:guid}", DeleteFeedbackAsync)
            .RequireRole(roles.Operator)
            .WithName("AgentPrismDeleteRunFeedback")
            .WithTags("AgentPrism", "Runs")
            .WithSummary("Bir puani siler.");
    }

    private static async Task<Results<Ok<RunScore>, ProblemHttpResult>> SaveFeedbackAsync(
        Guid runId,
        [FromBody] RunFeedbackRequest request,
        [FromServices] IRunStore runs,
        [FromServices] IRunScoreStore scores,
        [FromServices] ITenantContext tenants,
        [FromServices] IAuditLog auditLog,
        [FromServices] IAuditActorResolver actorResolver,
        [FromServices] ILoggerFactory loggerFactory,
        [FromServices] TimeProvider? timeProvider,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.Kind == RunScoreKind.Binary && request.Value is not (0 or 1))
        {
            return InvalidFeedback("Ikili puan ('binary') yalniz 0 veya 1 olabilir.");
        }

        if (request.Kind == RunScoreKind.Stars && request.Value is < 1 or > 5)
        {
            return InvalidFeedback("Yildiz puani ('stars') 1 ile 5 arasinda olmalidir.");
        }

        var run = await runs.GetRunAsync(runId, cancellationToken).ConfigureAwait(false);

        // "Yok" ile "baska kiraciya ait" AYNI 404'u doner; ayri bir mesaj
        // varlik sizdirirdi (docs/31-GERI-BILDIRIM-VE-PUANLAMA.md, bolum 31.3).
        if (run is null || !string.Equals(run.TenantId, tenants.TenantId, StringComparison.Ordinal))
        {
            return NotFoundFeedback(runId);
        }

        var now = (timeProvider ?? TimeProvider.System).GetUtcNow();

        var saved = await scores.UpsertAsync(
            new RunScore
            {
                TenantId = tenants.TenantId,
                RunId = runId,
                MessageId = string.IsNullOrWhiteSpace(request.MessageId) ? null : request.MessageId,
                Kind = request.Kind,
                Value = request.Value,
                Comment = string.IsNullOrWhiteSpace(request.Comment) ? null : request.Comment,
                Source = "human",
                Author = actorResolver.Resolve(),
                CreatedAt = now,
            },
            cancellationToken).ConfigureAwait(false);

        await AuditRecorder.WriteAsync(
            auditLog,
            actorResolver,
            loggerFactory.CreateLogger("AgentPrism.RunEndpoints"),
            tenants.TenantId,
            action: "run.feedback.save",
            entity: $"run_score:{saved.Id}",
            before: null,
            after: $$"""{"runId":"{{runId}}","kind":"{{saved.Kind}}","value":{{saved.Value}}}""",
            cancellationToken).ConfigureAwait(false);

        return TypedResults.Ok(saved);
    }

    /// <summary>
    /// Suren bir calistirmanin iptalini ister.
    /// </summary>
    /// <remarks>
    /// Uc davranisi (bkz. docs/32-CALISTIRMA-IPTALI.md, bolum 32.2):
    /// <list type="bullet">
    /// <item>Calistirma yoksa veya baska bir kiraciya aitse <c>404</c> (varlik sizdirmamak icin ikisi ayni).</item>
    /// <item>Calistirma defterde varsa kaynak iptal edilir ve <c>202</c> doner.</item>
    /// <item>
    /// Calistirma <c>runs</c>'ta <see cref="RunStatus.Running"/> ama defterde yoksa
    /// bu ornek onu yurutmuyor demektir; <c>409</c> doner.
    /// </item>
    /// <item>Calistirma zaten sonlanmissa <c>409</c> doner ve mevcut durum yazilir.</item>
    /// <item>
    /// 🚨 Faz 46: <see cref="RunStatus.Queued"/> durumundaki bir calistirma
    /// HENUZ yurutulmuyordur; <see cref="IRunCancellationRegistry"/>'de kayitli
    /// olamaz. Bu durumda iptal <c>IJobStore.CancelAsync</c> ile KUYRUKTAN
    /// yapilir (Job.Id == RunId, Faz 46) ve <c>runs</c> satiri burada dogrudan
    /// <see cref="RunStatus.Canceled"/>'e kapatilir — isci is'i hic almadigi
    /// icin <c>RunRecordingAgent</c> bu satiriyi asla kapatmayacaktir.
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
        [FromServices] TimeProvider? timeProvider,
        CancellationToken cancellationToken)
    {
        var run = await runs.GetRunAsync(runId, cancellationToken).ConfigureAwait(false);

        // "Yok" ile "baska kiraciya ait" AYNI 404'u doner; ayri bir mesaj
        // varlik sizdirirdi (ayni gerekce SaveFeedbackAsync'te de gecerlidir).
        if (run is null || !string.Equals(run.TenantId, tenants.TenantId, StringComparison.Ordinal))
        {
            return NotFound(runId);
        }

        if (run.Status == RunStatus.Queued)
        {
            if (!await jobs.CancelAsync(tenants.TenantId, runId, cancellationToken).ConfigureAwait(false))
            {
                return TypedResults.Problem(
                    title: "Calistirma zaten sonlanmis",
                    detail: $"'{runId}' kimlikli calistirma zaten '{run.Status}' durumunda.",
                    statusCode: StatusCodes.Status409Conflict);
            }

            var now = (timeProvider ?? TimeProvider.System).GetUtcNow();

            await runs.CompleteRunAsync(
                new RunCompletion { RunId = runId, Status = RunStatus.Canceled, CompletedAt = now },
                cancellationToken).ConfigureAwait(false);

            await AuditRecorder.WriteAsync(
                auditLog,
                actorResolver,
                loggerFactory.CreateLogger("AgentPrism.RunEndpoints"),
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
                    title: "Calistirma bu ornekte yurutulmuyor",
                    detail: $"'{runId}' kimlikli calistirma 'Running' gorunuyor ama bu surecte kayitli degil. " +
                            "Baska bir ornekte calisiyor olabilir veya surec calistirma sirasinda yeniden baslamis olabilir.",
                    statusCode: StatusCodes.Status409Conflict)
                : TypedResults.Problem(
                    title: "Calistirma zaten sonlanmis",
                    detail: $"'{runId}' kimlikli calistirma zaten '{run.Status}' durumunda.",
                    statusCode: StatusCodes.Status409Conflict);
        }

        await AuditRecorder.WriteAsync(
            auditLog,
            actorResolver,
            loggerFactory.CreateLogger("AgentPrism.RunEndpoints"),
            tenants.TenantId,
            action: "run.cancel",
            entity: $"run:{runId}",
            before: null,
            after: null,
            cancellationToken).ConfigureAwait(false);

        // 202: iptal yalniz ISTENDI. cts.Cancel() bir garanti degildir; agent
        // belirteci bir sonraki denetim noktasinda gorur. Nihai durum
        // 'GET /api/runs/{id}' ile okunur.
        return TypedResults.Accepted($"/api/runs/{runId}", run);
    }

    private static async Task<Results<Ok<IReadOnlyList<RunScore>>, ProblemHttpResult>> ListFeedbackAsync(
        Guid runId,
        [FromServices] IRunStore runs,
        [FromServices] IRunScoreStore scores,
        [FromServices] ITenantContext tenants,
        CancellationToken cancellationToken)
    {
        var run = await runs.GetRunAsync(runId, cancellationToken).ConfigureAwait(false);

        if (run is null || !string.Equals(run.TenantId, tenants.TenantId, StringComparison.Ordinal))
        {
            return NotFoundFeedback(runId);
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
        CancellationToken cancellationToken)
    {
        var run = await runs.GetRunAsync(runId, cancellationToken).ConfigureAwait(false);

        if (run is null || !string.Equals(run.TenantId, tenants.TenantId, StringComparison.Ordinal))
        {
            return NotFoundFeedback(runId);
        }

        if (!await scores.DeleteAsync(tenants.TenantId, scoreId, cancellationToken).ConfigureAwait(false))
        {
            return TypedResults.Problem(
                title: "Puan bulunamadi",
                detail: $"'{scoreId}' kimlikli bir puan yok.",
                statusCode: StatusCodes.Status404NotFound);
        }

        await AuditRecorder.WriteAsync(
            auditLog,
            actorResolver,
            loggerFactory.CreateLogger("AgentPrism.RunEndpoints"),
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
            title: "Gecersiz puan",
            detail: detail,
            statusCode: StatusCodes.Status400BadRequest);

    private static ProblemHttpResult NotFoundFeedback(Guid runId) => NotFound(runId);

    private static ProblemHttpResult NotFound(Guid runId)
        => TypedResults.Problem(
            title: "Calistirma bulunamadi",
            detail: $"'{runId}' kimlikli bir calistirma yok.",
            statusCode: StatusCodes.Status404NotFound);

    /// <summary>
    /// Olaylari SSE olarak yazar; calistirma devam ediyorsa yeni olaylari yoklar.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Olay deposu bir bildirim kanali sunmaz, bu yuzden canli akis yoklamayla
    /// saglanir. Sira: <em>once durum okunur</em>, sonra olaylar bosaltilir. Tersi
    /// olsaydi, iki adim arasinda tamamlanan bir calistirmanin son olaylari
    /// yazilmadan dongu bitebilirdi.
    /// </para>
    /// <para>
    /// Olaylar append-only oldugu icin (karar K-014) yeniden oynatma ve canli akis
    /// ayni kod yolundan gecer; istemci farki gormez.
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

                    // Calistirma silinmis veya sonlanmissa tum olaylar yazilmistir.
                    // 🚨 Faz 46: 'Queued' de BEKLENEN bir ara durumdur — isci
                    // is'i henuz almamis olabilir. Yalniz Running/Queued disinda
                    // bir durum (veya kaydin kendisinin yoklugu) akisi kapatir;
                    // aksi halde 202'den hemen sonra baglanan bir istemci is
                    // hic baslamadan akisin kapandigini gorurdu.
                    if (snapshot is null || snapshot.Status is not (RunStatus.Running or RunStatus.Queued))
                    {
                        break;
                    }

                    if (!wroteAny)
                    {
                        await writer.WriteKeepAliveAsync("bekleniyor", cancellationToken).ConfigureAwait(false);
                    }

                    await Task.Delay(pollInterval, cancellationToken).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                // Istemci baglantiyi kesti; yazacak kimse kalmadi.
            }
        }

        private static JsonSerializerOptions JsonOptions { get; } = new(JsonSerializerDefaults.Web);

        /// <summary>
        /// Olay tipini SSE olay adina cevirir. Bu adlar <strong>kararli</strong>
        /// sozlesmedir; degistirmek istemcileri kirar.
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
            _ => "unknown",
        };
    }
}

/// <summary>Bir calistirma/mesaj puani yazmak icin istek govdesi.</summary>
public sealed record RunFeedbackRequest
{
    /// <summary>Puanin bicimi.</summary>
    public required RunScoreKind Kind { get; init; }

    /// <summary><see cref="RunScoreKind.Binary"/> icin 0/1, <see cref="RunScoreKind.Stars"/> icin 1..5.</summary>
    public required int Value { get; init; }

    /// <summary>Puanlanan mesajin kimligi. Bos birakilirsa puan tum calistirmaya aittir.</summary>
    public string? MessageId { get; init; }

    /// <summary>Serbest metin yorum.</summary>
    public string? Comment { get; init; }
}
