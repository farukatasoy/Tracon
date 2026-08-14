using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace AgentPrism;

/// <summary>
/// <c>Idempotency-Key</c> basligini taniyan ve tekrarlanan istekleri
/// yeniden calistirmadan yanitlayan uc filtresi (Faz 43).
/// </summary>
/// <remarks>
/// <para>
/// Yalniz <see cref="AgentPrismEndpointRouteBuilderExtensions.MapAgentPrism"/>'in
/// Idempotency-Key destekleyen UC rotalarina eklenir — grubun tamamina degil.
/// Baslik TASIMAYAN bir istek icin filtre hemen <c>next</c>'e devreder; hicbir
/// sorgu atilmaz (K1: sessiz maliyet yoktur).
/// </para>
/// <para>
/// 🚨 <c>QuotaGate</c>'ten ONCE calisir: uc grubunun filtre zincirinde
/// <see cref="AgentPrismRateLimitFilter"/>'dan SONRA, handler govdesinden
/// (dolayisiyla <c>QuotaGate</c>'ten) ONCE. Tekrarlanan bir istek bu sayede
/// kotayi ikinci kez TUKETMEZ (docs/43-IDEMPOTENCY-KEY.md, bolum 43.1).
/// </para>
/// <para>
/// Ham govde <see cref="AgentPrismEndpointRouteBuilderExtensions.MapAgentPrism"/>
/// icinde kosullu olarak eklenen bir ara yazilimla ONCEDEN tamponlanir
/// (<c>HttpRequest.EnableBuffering</c>) — yalniz baslik TASIYAN istekler icin.
/// Bu, <c>/api/agents/{name}/run</c> gibi govdesi minimal API tarafindan
/// OTOMATIK baglanan uclarda bile parmak izinin HAM baytlardan hesaplanmasini
/// saglar: baglama, filtrenin InvokeAsync'inden ONCE govdeyi tuketir, ama
/// tamponlama sayesinde akis geri sarilabilir kalir.
/// </para>
/// </remarks>
internal sealed class IdempotencyFilter : IEndpointFilter
{
    /// <summary>Idempotency anahtarinin tasindigi HTTP baslik adi.</summary>
    public const string HeaderName = "Idempotency-Key";

    /// <summary>Saklanan bir yanit tekrar dondurulurken eklenen baslik.</summary>
    public const string ReplayedHeaderName = "Idempotency-Replayed";

    private readonly IOptionsMonitor<AgentPrismIdempotencyOptions> _optionsMonitor;

    /// <summary>Yeni bir idempotency filtresi olusturur.</summary>
    /// <param name="optionsMonitor">Idempotency ayarlari.</param>
    /// <exception cref="ArgumentNullException"><paramref name="optionsMonitor"/> <see langword="null"/> ise.</exception>
    public IdempotencyFilter(IOptionsMonitor<AgentPrismIdempotencyOptions> optionsMonitor)
    {
        ArgumentNullException.ThrowIfNull(optionsMonitor);

        _optionsMonitor = optionsMonitor;
    }

    /// <inheritdoc />
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        var httpContext = context.HttpContext;

        if (!httpContext.Request.Headers.TryGetValue(HeaderName, out var values) ||
            values.Count == 0 ||
            string.IsNullOrWhiteSpace(values[0]))
        {
            return await next(context).ConfigureAwait(false);
        }

        var key = values[0]!;
        var options = _optionsMonitor.CurrentValue;

        if (!options.Enabled)
        {
            return Results.Problem(
                title: "Idempotency support disabled",
                detail: $"The '{HeaderName}' header was sent, but idempotency support is disabled in " +
                        "this setup (AgentPrismIdempotencyOptions.Enabled = false). The request is " +
                        "processed ANYWAY, but a repeated request runs again; do NOT assume you are protected.",
                statusCode: StatusCodes.Status501NotImplemented);
        }

        if (key.Length > options.MaxKeyLength)
        {
            return Results.Problem(
                title: "Idempotency-Key too long",
                detail: $"The key may be at most {options.MaxKeyLength} characters; received length {key.Length}.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        var cancellationToken = httpContext.RequestAborted;
        var (isStreaming, fingerprint) = await InspectBodyAsync(httpContext, cancellationToken).ConfigureAwait(false);

        if (isStreaming)
        {
            return Results.Problem(
                title: "Idempotency-Key not supported on streaming requests",
                detail: $"A request carrying the '{HeaderName}' header cannot be streaming (SSE, " +
                        "'stream: true'). Replaying a stored SSE body is out of scope for this " +
                        "version (docs/43-IDEMPOTENCY-KEY.md, section 43.4).",
                statusCode: StatusCodes.Status400BadRequest);
        }

        var tenants = httpContext.RequestServices.GetRequiredService<ITenantContext>();
        var store = httpContext.RequestServices.GetRequiredService<IIdempotencyStore>();

        var reservation = await store.ReserveAsync(
            new IdempotencyRequest
            {
                TenantId = tenants.TenantId,
                Key = key,
                Fingerprint = fingerprint,
                CreatedAt = DateTimeOffset.UtcNow,
            },
            cancellationToken).ConfigureAwait(false);

        switch (reservation.State)
        {
            case IdempotencyState.Completed:
                return new IdempotencyReplayResult(reservation.Response!);

            case IdempotencyState.InProgress:
                return Results.Problem(
                    title: "Request already in progress",
                    detail: $"A request with key '{key}' is still being processed. Retry with the same body.",
                    statusCode: StatusCodes.Status409Conflict);

            case IdempotencyState.FingerprintMismatch:
                return Results.Problem(
                    title: "Idempotency-Key used for a different request",
                    detail: $"The key '{key}' was already used on a request that completed with a " +
                            "DIFFERENT body. Do not reuse the same key for a different request.",
                    statusCode: StatusCodes.Status422UnprocessableEntity);

            default:
                return await ExecuteAndCaptureAsync(context, next, store, tenants.TenantId, key, cancellationToken)
                    .ConfigureAwait(false);
        }
    }

    private static async ValueTask<object?> ExecuteAndCaptureAsync(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next,
        IIdempotencyStore store,
        string tenantId,
        string key,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await next(context).ConfigureAwait(false);

            if (result is not IResult inner)
            {
                // Beklenmez: uc her zaman IResult dondurur. Yakalayamadigimiz
                // icin ayirmayi serbest birakiriz; bir sonraki istek yeniden dener.
                await store.ReleaseAsync(tenantId, key, CancellationToken.None).ConfigureAwait(false);

                return result;
            }

            return new IdempotencyCapturingResult(inner, store, tenantId, key);
        }
        catch
        {
            await store.ReleaseAsync(tenantId, key, CancellationToken.None).ConfigureAwait(false);

            throw;
        }
    }

    /// <summary>
    /// Ham govdeyi okuyup akis bayragini ve parmak izini cikarir; okuma sonrasi
    /// govde bastan okunabilir birakilir (asagi akisin kendi baglamasi/okumasi
    /// icin).
    /// </summary>
    private static async ValueTask<(bool IsStreaming, string Fingerprint)> InspectBodyAsync(
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var request = httpContext.Request;

        byte[] bytes;

        if (request.Body.CanSeek)
        {
            request.Body.Position = 0;
        }

        using (var buffer = new MemoryStream())
        {
            await request.Body.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
            bytes = buffer.ToArray();
        }

        if (request.Body.CanSeek)
        {
            request.Body.Position = 0;
        }

        var isStreaming = false;

        if (bytes.Length > 0)
        {
            try
            {
                using var document = JsonDocument.Parse(bytes);

                isStreaming = document.RootElement.ValueKind == JsonValueKind.Object &&
                              document.RootElement.TryGetProperty("stream", out var streamFlag) &&
                              streamFlag.ValueKind == JsonValueKind.True;
            }
            catch (JsonException)
            {
                // Gecersiz JSON: akis bayragi yok sayilir, alt katman kendi
                // dogrulamasini yapip uygun hatayi dondurur.
            }
        }

        var prefix = Encoding.UTF8.GetBytes(request.Method + "\n" + request.Path + "\n");
        var combined = new byte[prefix.Length + bytes.Length];
        prefix.CopyTo(combined, 0);
        bytes.CopyTo(combined, prefix.Length);

        var hash = SHA256.HashData(combined);

        return (isStreaming, Convert.ToHexString(hash).ToLowerInvariant());
    }
}
