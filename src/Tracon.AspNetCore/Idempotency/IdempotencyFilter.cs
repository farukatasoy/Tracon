using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Tracon;

/// <summary>
/// Endpoint filter that recognizes the <c>Idempotency-Key</c> header and answers
/// repeated requests without re-running them.
/// </summary>
/// <remarks>
/// <para>
/// Attached only to the Idempotency-Key-supporting endpoint routes of
/// <see cref="TraconEndpointRouteBuilderExtensions.MapTracon"/> — not to the
/// whole group. For a request that does NOT carry the header, the filter passes
/// straight through to <c>next</c>; no query is issued (the no-surprises rule: no silent cost).
/// </para>
/// <para>
/// Runs BEFORE <c>QuotaGate</c>: in the endpoint group's filter chain, it runs
/// AFTER <see cref="TraconRateLimitFilter"/>, but BEFORE the handler body (and
/// therefore before <c>QuotaGate</c>). This means a repeated request does NOT
/// consume the quota a second time.
/// </para>
/// <para>
/// The raw body is buffered UP FRONT (<c>HttpRequest.EnableBuffering</c>) by
/// middleware conditionally added inside
/// <see cref="TraconEndpointRouteBuilderExtensions.MapTracon"/> — only for
/// requests that CARRY the header. This ensures the fingerprint is computed from
/// the RAW bytes even on endpoints whose body is bound AUTOMATICALLY by minimal
/// API (e.g. <c>/api/agents/{name}/run</c>): binding consumes the body BEFORE the
/// filter's InvokeAsync runs, but buffering keeps the stream rewindable.
/// </para>
/// <para>
/// <strong>Only a JSON body is read.</strong> A body without a JSON content type is
/// refused with <c>415</c> or ignored by every endpoint behind this filter, and the
/// JSON body limit does not cover it; copying it into memory would cost the request
/// whatever the server accepts. See <see cref="ReadsBody"/>.
/// </para>
/// <para>
/// <strong>Streaming endpoints.</strong> A stored response must be
/// replayable, and an SSE stream is not: buffering one would hold a long
/// workflow run in memory and replay a frozen copy of it. An endpoint that only
/// streams unless the caller queues the work gets the instance built with
/// <c>requireRespondAsync</c>: there, a key without <c>Prefer: respond-async</c>
/// is refused with <c>400</c> before anything is reserved or run, and the queued
/// <c>202</c> is what is stored and replayed.
/// </para>
/// </remarks>
internal sealed class IdempotencyFilter : IEndpointFilter
{
    /// <summary>The HTTP header name that carries the idempotency key.</summary>
    public const string HeaderName = "Idempotency-Key";

    /// <summary>The header added when a stored response is replayed.</summary>
    public const string ReplayedHeaderName = "Idempotency-Replayed";

    private readonly IOptionsMonitor<TraconIdempotencyOptions> _optionsMonitor;
    private readonly bool _requireRespondAsync;

    /// <summary>Creates a new idempotency filter.</summary>
    /// <param name="optionsMonitor">The idempotency settings.</param>
    /// <param name="requireRespondAsync">
    /// Whether a key is accepted only on a request that also carries
    /// <c>Prefer: respond-async</c>. Set for an endpoint whose synchronous answer
    /// is a stream, which cannot be stored and replayed.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="optionsMonitor"/> is <see langword="null"/>.</exception>
    public IdempotencyFilter(IOptionsMonitor<TraconIdempotencyOptions> optionsMonitor, bool requireRespondAsync = false)
    {
        ArgumentNullException.ThrowIfNull(optionsMonitor);

        _optionsMonitor = optionsMonitor;
        _requireRespondAsync = requireRespondAsync;
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
                        "this setup (TraconIdempotencyOptions.Enabled = false). The request is " +
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

        // 🚨 Refused BEFORE the reservation and the run: without the preference
        // the endpoint answers with a stream, and the capturing result below
        // would buffer the whole run in memory to store a copy no replay can
        // reproduce.
        if (_requireRespondAsync && !RespondAsyncPreference.IsRequested(httpContext))
        {
            return Results.Problem(
                title: "Idempotency-Key requires Prefer: respond-async",
                detail: $"This endpoint streams its answer, and a stream cannot be stored and replayed. Send the " +
                        $"'{HeaderName}' header together with 'Prefer: respond-async': the run is queued and the " +
                        "'202 Accepted' answer is what a repeated request gets back.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        var cancellationToken = httpContext.RequestAborted;
        var (isStreaming, fingerprint) = await InspectBodyAsync(httpContext, cancellationToken).ConfigureAwait(false);

        if (isStreaming)
        {
            return Results.Problem(
                title: "Idempotency-Key not supported on streaming requests",
                detail: $"A request carrying the '{HeaderName}' header cannot be streaming (SSE, " +
                        "'stream: true'). Replaying a stored SSE body is not supported.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        var tenants = httpContext.RequestServices.GetRequiredService<ITenantContext>();
        var store = httpContext.RequestServices.GetRequiredService<IIdempotencyStore>();

        // The reservation is known by this time: Complete and Release carry it, so a request that was taken over (it ran longer
        // than ReservationTimeout) cannot store its response under, or delete, the record of the request that took the key.
        var reservedAt = DateTimeOffset.UtcNow;

        var reservation = await store.ReserveAsync(
            new IdempotencyRequest
            {
                TenantId = tenants.TenantId,
                Key = key,
                Fingerprint = fingerprint,
                CreatedAt = reservedAt,
                ReservationTimeout = options.ReservationTimeout,
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
                return await ExecuteAndCaptureAsync(context, next, store, tenants.TenantId, key, reservedAt, cancellationToken)
                    .ConfigureAwait(false);
        }
    }

    private static async ValueTask<object?> ExecuteAndCaptureAsync(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next,
        IIdempotencyStore store,
        string tenantId,
        string key,
        DateTimeOffset reservedAt,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await next(context).ConfigureAwait(false);

            if (result is not IResult inner)
            {
                // Not expected: the endpoint always returns IResult. Since we cannot
                // capture it, release the reservation; the next request retries.
                await store.ReleaseAsync(tenantId, key, reservedAt, CancellationToken.None).ConfigureAwait(false);

                return result;
            }

            return new IdempotencyCapturingResult(inner, store, tenantId, key, reservedAt);
        }
        catch
        {
            await store.ReleaseAsync(tenantId, key, reservedAt, CancellationToken.None).ConfigureAwait(false);

            throw;
        }
    }

    /// <summary>
    /// Whether the filter reads the body of <paramref name="request"/> to fingerprint it: only a JSON body, the one kind
    /// every endpoint behind the filter reads and the one <see cref="RequestBodyLimitMiddleware"/> bounds.
    /// </summary>
    /// <param name="request">The request.</param>
    /// <returns><see langword="true"/> when the request declares a JSON content type.</returns>
    /// <remarks>
    /// A body without a JSON content type is refused with <c>415</c> by the endpoint, or, on the endpoints that take an
    /// optional body, ignored. Copying it into memory would only make the request cost what the server accepts (30 MB on
    /// Kestrel) for nothing, because the JSON limit does not apply to it. The request is then fingerprinted as the
    /// bodyless request it is to the endpoint. The buffering middleware asks the same question, so a body that is not read
    /// is not buffered either.
    /// </remarks>
    internal static bool ReadsBody(HttpRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        return request.HasJsonContentType();
    }

    /// <summary>
    /// Reads the raw body and extracts the streaming flag and the fingerprint; after
    /// reading, the body is left readable from the start (for downstream binding/reading).
    /// </summary>
    /// <remarks>
    /// The body is copied once: the buffer is sized from the declared length (up to the endpoint's JSON limit), the
    /// fingerprint is computed from it in place, and the streaming flag is read from it without another copy.
    /// </remarks>
    private static async ValueTask<(bool IsStreaming, string Fingerprint)> InspectBodyAsync(
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var request = httpContext.Request;

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(Encoding.UTF8.GetBytes(request.Method + "\n" + request.Path + "\n"));

        var isStreaming = false;

        if (ReadsBody(request))
        {
            if (request.Body.CanSeek)
            {
                request.Body.Position = 0;
            }

            using var buffer = new MemoryStream(CapacityHint(httpContext));
            await request.Body.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);

            if (request.Body.CanSeek)
            {
                request.Body.Position = 0;
            }

            if (buffer.Length > 0 && buffer.TryGetBuffer(out var bytes))
            {
                hash.AppendData(bytes);
                isStreaming = IsStreamRequested(bytes.AsSpan());
            }
        }

        return (isStreaming, Convert.ToHexStringLower(hash.GetHashAndReset()));
    }

    /// <summary>
    /// Whether the top level of <paramref name="json"/> is an object whose <c>stream</c> property is <c>true</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Read with a forward-only reader and not parsed into a document: a document keeps an index about as long as the
    /// body itself (measured: a second copy of an 8 MiB body) to answer one question about one property.
    /// </para>
    /// <para>
    /// The answer is the one the document gave. The last of two equal names wins, an escaped name counts, a nested property
    /// does not, and a body that is not valid JSON as a whole is never streaming: the reader goes on to the end so the lower
    /// layer, which validates the body, is the one to refuse it.
    /// </para>
    /// </remarks>
    private static bool IsStreamRequested(ReadOnlySpan<byte> json)
    {
        var reader = new Utf8JsonReader(json);

        try
        {
            if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
            {
                return false;
            }

            var streaming = false;

            while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
            {
                var isStream = reader.ValueTextEquals("stream"u8);

                reader.Read();
                streaming = isStream ? reader.TokenType == JsonTokenType.True : streaming;
                reader.Skip();
            }

            // Past the closing brace: anything but the end of the data throws.
            return reader.TokenType == JsonTokenType.EndObject && !reader.Read() && streaming;
        }
        catch (JsonException)
        {
            // Invalid JSON: the streaming flag is ignored; the lower layer runs
            // its own validation and returns the appropriate error.
            return false;
        }
    }

    /// <summary>
    /// The size to start the body buffer at: the declared length, so the copy does not grow the buffer by doubling, but
    /// never more than the endpoint's JSON limit — a length the caller states is not trusted past it.
    /// </summary>
    private static int CapacityHint(HttpContext httpContext)
    {
        var ceiling = httpContext.GetEndpoint()?.Metadata.GetMetadata<JsonBodyLimitMetadata>()?.MaxBytes ?? 0;

        return (int)Math.Clamp(Math.Min(httpContext.Request.ContentLength ?? 0, ceiling), 0, Array.MaxLength);
    }
}
