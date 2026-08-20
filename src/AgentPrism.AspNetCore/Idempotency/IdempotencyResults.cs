using System.Text;
using Microsoft.AspNetCore.Http;

namespace AgentPrism;

/// <summary>Writes a stored idempotency response back out, unchanged.</summary>
internal sealed class IdempotencyReplayResult(IdempotencyResponse response) : IResult
{
    /// <inheritdoc />
    public async Task ExecuteAsync(HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        httpContext.Response.StatusCode = response.StatusCode;
        httpContext.Response.ContentType = response.ContentType;
        httpContext.Response.Headers[IdempotencyFilter.ReplayedHeaderName] = "true";

        // HATA-S3-008: the original response's non-body headers (Location,
        // Preference-Applied) are replayed too — not just body/status code/content type.
        foreach (var header in response.Headers)
        {
            httpContext.Response.Headers[header.Key] = header.Value;
        }

        await httpContext.Response.WriteAsync(response.Body, httpContext.RequestAborted).ConfigureAwait(false);
    }
}

/// <summary>
/// Buffers the bytes the inner result ACTUALLY writes, saves them to the
/// idempotency store, then writes them to the real response.
/// </summary>
/// <remarks>
/// An endpoint filter cannot see what gets written INSIDE the returned
/// <see cref="IResult"/> — ASP.NET Core runs that result OUTSIDE the filter chain.
/// This wrapper CAPTURES those bytes by triggering the actual write inside its
/// own <see cref="ExecuteAsync"/> (temporarily swapping the body for a
/// <see cref="MemoryStream"/>) — the same technique ASP.NET Core's response
/// caching middleware uses.
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

        // 🚨 A failed run is NOT stored; the reservation is released so a retry with
        // the same key works (docs/arsiv/fazlar/43-IDEMPOTENCY-KEY.md, section 43.2).
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
    /// Captures the headers other than Content-Type/body length/the replay marker
    /// itself — each of these is already produced correctly by ASP.NET Core when
    /// the response is rewritten (or simply omitted).
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
