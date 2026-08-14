using System.Text;
using Microsoft.AspNetCore.Http;

namespace AgentPrism;

/// <summary>Saklanan bir idempotency yanitini oldugu gibi yeniden yazar.</summary>
internal sealed class IdempotencyReplayResult(IdempotencyResponse response) : IResult
{
    /// <inheritdoc />
    public async Task ExecuteAsync(HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        httpContext.Response.StatusCode = response.StatusCode;
        httpContext.Response.ContentType = response.ContentType;
        httpContext.Response.Headers[IdempotencyFilter.ReplayedHeaderName] = "true";

        // HATA-S3-008: orijinal yanitin govde disi basliklari (Location,
        // Preference-Applied) da replay edilir — yalniz govde/durum kodu/
        // icerik tipi degil.
        foreach (var header in response.Headers)
        {
            httpContext.Response.Headers[header.Key] = header.Value;
        }

        await httpContext.Response.WriteAsync(response.Body, httpContext.RequestAborted).ConfigureAwait(false);
    }
}

/// <summary>
/// Ic sonucun ACTUALLY yazdigi baytlari tamponlayip idempotency deposuna
/// kaydeder, sonra gercek yanita yazar.
/// </summary>
/// <remarks>
/// Bir uc filtresi, dondurulen <see cref="IResult"/>'in ICINDE ne yazildigini
/// goremez — ASP.NET Core bu yaniti filtre zincirinin DISINDA calistirir.
/// Bu sarmalayici, gercek yazimi kendi <see cref="ExecuteAsync"/>'i icinde
/// tetikleyerek (govdeyi gecici olarak bir <see cref="MemoryStream"/> ile
/// degistirerek) o baytlari YAKALAR — ASP.NET Core'un yanit onbellekleme
/// ara yazilimiyla ayni teknik.
/// </remarks>
internal sealed class IdempotencyCapturingResult(
    IResult inner,
    IIdempotencyStore store,
    string tenantId,
    string key) : IResult
{
    /// <inheritdoc />
    public async Task ExecuteAsync(HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        var original = httpContext.Response.Body;
        var buffer = new MemoryStream();
        httpContext.Response.Body = buffer;

        try
        {
            await inner.ExecuteAsync(httpContext).ConfigureAwait(false);
        }
        catch
        {
            httpContext.Response.Body = original;
            await store.ReleaseAsync(tenantId, key, CancellationToken.None).ConfigureAwait(false);

            throw;
        }

        httpContext.Response.Body = original;

        var bytes = buffer.ToArray();
        var statusCode = httpContext.Response.StatusCode;

        // 🚨 Basarisiz calistirma SAKLANMAZ; kayit silinir ki ayni anahtarla
        // yeniden deneme calissin (docs/43-IDEMPOTENCY-KEY.md, bolum 43.2).
        if (statusCode is >= 200 and < 300)
        {
            await store.CompleteAsync(
                tenantId,
                key,
                new IdempotencyResponse
                {
                    StatusCode = statusCode,
                    ContentType = httpContext.Response.ContentType ?? "application/octet-stream",
                    Body = Encoding.UTF8.GetString(bytes),
                    Headers = CaptureReplayableHeaders(httpContext.Response.Headers),
                },
                CancellationToken.None).ConfigureAwait(false);
        }
        else
        {
            await store.ReleaseAsync(tenantId, key, CancellationToken.None).ConfigureAwait(false);
        }

        await original.WriteAsync(bytes, httpContext.RequestAborted).ConfigureAwait(false);
    }

    /// <summary>
    /// Content-Type/govde uzunlugu/kendi tekrar isareti disinda kalan
    /// basliklari yakalar — bunlarin her biri yeniden yazilirken (veya hic
    /// yazilmayarak) ASP.NET Core tarafindan zaten dogru uretilir.
    /// </summary>
    private static Dictionary<string, string> CaptureReplayableHeaders(IHeaderDictionary headers)
    {
        var captured = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var header in headers)
        {
            if (ManagedHeaderNames.Contains(header.Key))
            {
                continue;
            }

            captured[header.Key] = header.Value.ToString();
        }

        return captured;
    }

    private static readonly HashSet<string> ManagedHeaderNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Content-Type",
        "Content-Length",
        "Transfer-Encoding",
        IdempotencyFilter.ReplayedHeaderName,
    };
}
