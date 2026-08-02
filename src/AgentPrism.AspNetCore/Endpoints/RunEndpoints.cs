using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace AgentPrism;

/// <summary>
/// Calistirma kaydi ve olay akisi uclari.
/// </summary>
internal static class RunEndpoints
{
    /// <summary>Calistirma uclarini baglar.</summary>
    /// <param name="builder">Uc grubu.</param>
    /// <param name="options">Erisim ve akis ayarlari.</param>
    public static void Map(IEndpointRouteBuilder builder, AgentPrismEndpointOptions options)
    {
        builder.MapGet("/api/runs", async Task<Ok<IReadOnlyList<RunRecord>>> (
                IRunStore runs,
                string? agentName,
                RunStatus? status,
                string? sessionId,
                DateTimeOffset? startedAfter,
                int? skip,
                int? take,
                CancellationToken cancellationToken) =>
            {
                var records = await runs.QueryRunsAsync(
                    new RunQuery
                    {
                        AgentName = agentName,
                        Status = status,
                        SessionId = sessionId,
                        StartedAfter = startedAfter,
                        Skip = Math.Max(skip ?? 0, 0),
                        Take = Math.Clamp(take ?? 50, 1, 200),
                    },
                    cancellationToken).ConfigureAwait(false);

                return TypedResults.Ok(records);
            })
            .WithName("AgentPrismListRuns")
            .WithSummary("Calistirmalari en yeniden eskiye listeler.");

        builder.MapGet("/api/runs/{runId:guid}", async Task<Results<Ok<RunRecord>, ProblemHttpResult>> (
                Guid runId,
                IRunStore runs,
                CancellationToken cancellationToken)
                => await runs.GetRunAsync(runId, cancellationToken).ConfigureAwait(false) is { } record
                    ? TypedResults.Ok(record)
                    : NotFound(runId))
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
            _ => "unknown",
        };
    }
}
