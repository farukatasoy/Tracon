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
                },
                CancellationToken.None).ConfigureAwait(false);
        }
        else
        {
            await store.ReleaseAsync(tenantId, key, CancellationToken.None).ConfigureAwait(false);
        }

        await original.WriteAsync(bytes, httpContext.RequestAborted).ConfigureAwait(false);
    }
}
