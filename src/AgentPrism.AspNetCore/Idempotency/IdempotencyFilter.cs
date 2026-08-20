using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace AgentPrism;

/// <summary>
/// Endpoint filter that recognizes the <c>Idempotency-Key</c> header and answers
/// repeated requests without re-running them.
/// </summary>
/// <remarks>
/// <para>
/// Attached only to the Idempotency-Key-supporting endpoint routes of
/// <see cref="AgentPrismEndpointRouteBuilderExtensions.MapAgentPrism"/> — not to the
/// whole group. For a request that does NOT carry the header, the filter passes
/// straight through to <c>next</c>; no query is issued (the no-surprises rule: no silent cost).
/// </para>
/// <para>
/// Runs BEFORE <c>QuotaGate</c>: in the endpoint group's filter chain, it runs
/// AFTER <see cref="AgentPrismRateLimitFilter"/>, but BEFORE the handler body (and
/// therefore before <c>QuotaGate</c>). This means a repeated request does NOT
/// consume the quota a second time.
/// </para>
/// <para>
/// The raw body is buffered UP FRONT (<c>HttpRequest.EnableBuffering</c>) by
/// middleware conditionally added inside
/// <see cref="AgentPrismEndpointRouteBuilderExtensions.MapAgentPrism"/> — only for
/// requests that CARRY the header. This ensures the fingerprint is computed from
/// the RAW bytes even on endpoints whose body is bound AUTOMATICALLY by minimal
/// API (e.g. <c>/api/agents/{name}/run</c>): binding consumes the body BEFORE the
/// filter's InvokeAsync runs, but buffering keeps the stream rewindable.
/// </para>
/// </remarks>
internal sealed class IdempotencyFilter : IEndpointFilter
{
    /// <summary>The HTTP header name that carries the idempotency key.</summary>
    public const string HeaderName = "Idempotency-Key";

    /// <summary>The header added when a stored response is replayed.</summary>
    public const string ReplayedHeaderName = "Idempotency-Replayed";

    private readonly IOptionsMonitor<AgentPrismIdempotencyOptions> _optionsMonitor;

    /// <summary>Creates a new idempotency filter.</summary>
    /// <param name="optionsMonitor">The idempotency settings.</param>
    /// <exception cref="ArgumentNullException"><paramref name="optionsMonitor"/> is <see langword="null"/>.</exception>
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
                // Not expected: the endpoint always returns IResult. Since we cannot
                // capture it, release the reservation; the next request retries.
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
    /// Reads the raw body and extracts the streaming flag and the fingerprint; after
    /// reading, the body is left readable from the start (for downstream binding/reading).
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
                // Invalid JSON: the streaming flag is ignored; the lower layer runs
                // its own validation and returns the appropriate error.
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
