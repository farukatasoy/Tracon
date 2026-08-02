using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

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
            .WithSummary("Bir calistirmanin olaylarini SSE ile akitir; canli ve gecmise donuk ayni yoldur.")
            .WithDescription(
                "Baglanti koparsa istemci 'Last-Event-ID' basligiyla kaldigi sira numarasindan devam eder. " +
                "Calistirma hala suruyorsa akis tamamlanana kadar acik kalir.");
    }

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
                    if (snapshot is null || snapshot.Status != RunStatus.Running)
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
