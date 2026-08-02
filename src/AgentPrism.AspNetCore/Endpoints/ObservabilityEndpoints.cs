using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace AgentPrism;

/// <summary>
/// Telemetri uclari: bir calistirmanin span agaci ve tool cagrilari.
/// </summary>
internal static class ObservabilityEndpoints
{
    /// <summary>Telemetri uclarini baglar.</summary>
    /// <param name="builder">Uc grubu.</param>
    public static void Map(IEndpointRouteBuilder builder)
    {
        builder.MapGet("/api/runs/{runId:guid}/trace", async Task<Results<Ok<RunTrace>, ProblemHttpResult>> (
                Guid runId,
                ITraceStore traces,
                CancellationToken cancellationToken)
                => await traces.GetTraceByRunAsync(runId, cancellationToken).ConfigureAwait(false) is { } trace
                    ? TypedResults.Ok(trace)
                    : TypedResults.Problem(
                        title: "Trace bulunamadi",
                        detail: $"'{runId}' calistirmasi icin kayitli span yok. Span yazma yolu " +
                                "orneklenir: basarili calistirmalarin yalnizca bir kismi kaydedilir " +
                                "(AgentPrism:Observability:SuccessSampleRatio).",
                        statusCode: StatusCodes.Status404NotFound))
            .WithName("AgentPrismGetRunTrace")
            .WithSummary("Bir calistirmanin span agacini dondurur.")
            .WithDescription(
                "Span'ler ornekleme ile yazilir. Hatali calistirmalarin span'leri varsayilan " +
                "olarak her zaman kaydedilir; basarililar yapilandirilabilir bir orandadir.");

        builder.MapGet("/api/runs/{runId:guid}/tools", async Task<Ok<IReadOnlyList<ToolInvocationRecord>>> (
                Guid runId,
                IRunStore runs,
                CancellationToken cancellationToken)
                => TypedResults.Ok(
                    await runs.ListToolInvocationsAsync(runId, cancellationToken).ConfigureAwait(false)))
            .WithName("AgentPrismListRunToolInvocations")
            .WithSummary("Bir calistirmanin tool cagrilarini zaman sirasina gore listeler.")
            .WithDescription(
                "Sure yalnizca akisli calistirmalarda olculur: akissiz calistirmada butun " +
                "mesajlar tek seferde gorulur ve cagri ile sonuc arasindaki gercek sure okunamaz.");

        builder.MapGet("/api/tools/usage", async Task<Ok<IReadOnlyList<ToolUsage>>> (
                IRunStore runs,
                DateTimeOffset? startedAfter,
                int? maxTools,
                CancellationToken cancellationToken)
                => TypedResults.Ok(await runs.GetToolUsageAsync(
                    new ToolUsageQuery
                    {
                        StartedAfter = startedAfter,
                        MaxTools = maxTools is { } max ? Math.Clamp(max, 1, 200) : 50,
                    },
                    cancellationToken).ConfigureAwait(false)))
            .WithName("AgentPrismToolUsage")
            .WithSummary("Tool bazinda cagri sayisi, hata orani ve ortalama sureyi dondurur.")
            .WithDescription("Ozet deponun kendisinde hesaplanir; sayfalanmis bir alt kume degildir.");
    }
}
