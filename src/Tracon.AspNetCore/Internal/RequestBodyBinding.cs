using System.Buffers;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Tracon;

/// <summary>
/// Reads the request body by hand — instead of minimal API's automatic
/// <c>[FromBody]</c> binding.
/// </summary>
/// <remarks>
/// <para>
/// Minimal API's own body binding only throws <c>JsonException</c> on a parse error
/// (a missing <c>required</c> field, an unrecognized enum value) when
/// <c>RouteHandlerOptions.ThrowOnBadRequest</c> is enabled — this flag is enabled
/// BY DEFAULT only in the <c>Development</c> environment. In production (most real
/// deployments), a body error silently results in a bodyless <c>400</c>;
/// <see cref="JsonBindingProblemMiddleware"/> CANNOT see this path at all (no
/// exception is thrown). This method ALWAYS reads the body by hand and catches
/// <c>JsonException</c> directly — it produces the same <c>ProblemDetails</c>
/// contract INDEPENDENTLY of the environment.
/// </para>
/// </remarks>
internal static class RequestBodyBinding
{
    /// <summary>The common title also used by the other manual-binding endpoints.</summary>
    internal const string ProblemTitle = "Invalid request body";

    /// <summary>
    /// The <c>detail</c> for a body that failed to parse: the exception's own
    /// message, followed by the JSON path of the property it failed on.
    /// </summary>
    /// <param name="exception">The parse failure.</param>
    /// <returns>The text to put in <c>ProblemDetails.detail</c>.</returns>
    /// <remarks>
    /// System.Text.Json fills <see cref="JsonException.Path"/> for every failure
    /// it routes, including one a converter threw, but it appends the path to
    /// <see cref="Exception.Message"/> only for exceptions it created itself.
    /// So a converter that writes a better sentence than the stock one — see
    /// <see cref="ExplainedEnumConverter{TEnum}"/> — would lose the single piece
    /// of the stock message that pointed at the offending field. The path is
    /// appended here, once, for both readers and for whatever else the
    /// framework raises.
    /// </remarks>
    internal static string Describe(JsonException exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        return exception.Path is { Length: > 0 } path && !exception.Message.Contains(path, StringComparison.Ordinal)
            ? $"{exception.Message} Path: {path}"
            : exception.Message;
    }

    /// <summary>
    /// Per-consumer <see cref="JsonSerializerOptions"/> derived from the
    /// application's own, keyed by the instance they were derived from.
    /// </summary>
    /// <remarks>
    /// <see cref="ConditionalWeakTable{TKey,TValue}"/> rather than a single
    /// static field: a test host — or a consumer hosting two Tracon
    /// applications in one process — has more than one options instance, and
    /// caching just the first would silently apply one application's
    /// converters to another's bodies.
    /// </remarks>
    private static readonly ConditionalWeakTable<JsonSerializerOptions, JsonSerializerOptions> BodyOptions = new();

    // Why this exists at all: a request record declares its collections
    // non-nullable AND initializes them (`IReadOnlyList<ToolApprovalDecision>
    // Approvals { get; init; } = [];`), but System.Text.Json OVERWRITES that
    // initializer when the body carries an explicit null. `{"approvals": null}`
    // therefore left a non-nullable property null, and every unconditional read
    // of it (`request.Approvals.Count`) threw NullReferenceException. Measured
    // across the contracts: 13 properties carry that shape, and four of them are
    // on AgentRunRequest alone - one of which answered 200 and then failed
    // inside the SSE stream, after the caller had been told the run started.
    /// <summary>
    /// The application's JSON options plus <c>RespectNullableAnnotations</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A body that states <c>null</c> for a property the contract declares
    /// non-nullable is invalid, and this makes the platform say so: such a
    /// value throws <see cref="JsonException"/>, which both readers below
    /// already turn into the same <c>400</c> <c>ProblemDetails</c> every other
    /// malformed body gets. Omitting the property is unaffected — the
    /// contract's own initializer still applies — and a property that
    /// genuinely accepts "not provided" is declared nullable and keeps
    /// accepting <c>null</c>.
    /// </para>
    /// <para>
    /// The options are derived from the application's own rather than built
    /// fresh, so a consumer's converters registered through
    /// <c>ConfigureHttpJsonOptions</c> keep applying to Tracon's bodies.
    /// The derived instance reads request bodies only; responses are still
    /// written with the application's own options, so nothing Tracon
    /// serializes can start throwing on a null.
    /// </para>
    /// <para>
    /// It also carries <see cref="ExplainedEnumConverterFactory"/>, which is
    /// what makes a refused enum value name itself and its alternatives. That
    /// converter lives here, on the read options, rather than on the enum types:
    /// a type-level attribute reaches schema generation too, and measured there
    /// it cost the published OpenAPI document the <c>enum</c> list of all 42
    /// enums. An options-level converter takes precedence over a type-level
    /// attribute, so it wins for bodies and is invisible everywhere else. It is
    /// APPENDED, not inserted first: a consumer who registered their own enum
    /// converter through <c>ConfigureHttpJsonOptions</c> was copied in ahead of
    /// it and keeps winning, which is the same rule the store registrations
    /// follow — the consumer's registration always wins.
    /// </para>
    /// </remarks>
    private static JsonSerializerOptions ReadOptions(HttpContext httpContext)
    {
        var applicationOptions = httpContext.RequestServices
            .GetRequiredService<IOptions<JsonOptions>>()
            .Value.SerializerOptions;

        return BodyOptions.GetValue(applicationOptions, CreateBodyOptions);
    }

    private static JsonSerializerOptions CreateBodyOptions(JsonSerializerOptions source)
    {
        var options = new JsonSerializerOptions(source) { RespectNullableAnnotations = true };

        options.Converters.Add(new ExplainedEnumConverterFactory());

        return options;
    }

    /// <summary>Per-consumer options of <see cref="ReadStrictAsync{T}"/>, keyed the way <see cref="BodyOptions"/> is.</summary>
    private static readonly ConditionalWeakTable<JsonSerializerOptions, JsonSerializerOptions> StrictBodyOptions = new();

    private static JsonSerializerOptions CreateStrictBodyOptions(JsonSerializerOptions source)
    {
        var options = CreateBodyOptions(source);

        options.UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow;

        return options;
    }

    /// <summary>
    /// Reads the body as <typeparamref name="T"/>.
    /// </summary>
    /// <typeparam name="T">The expected body type.</typeparam>
    /// <param name="httpContext">The request context.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>
    /// On success, <c>Value</c> is populated and <c>Error</c> is <see langword="null"/>.
    /// If the body is empty or cannot be parsed, <c>Value</c> is <see langword="null"/>
    /// and <c>Error</c> carries a <c>400</c> <see cref="ProblemHttpResult"/> the
    /// caller can return directly.
    /// </returns>
    public static async Task<(T? Value, ProblemHttpResult? Error)> ReadAsync<T>(
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        try
        {
            var value = await httpContext.Request
                .ReadFromJsonAsync<T>(ReadOptions(httpContext), cancellationToken)
                .ConfigureAwait(false);

            if (value is null)
            {
                return (default, TypedResults.Problem(
                    title: ProblemTitle,
                    detail: "The body cannot be empty.",
                    statusCode: StatusCodes.Status400BadRequest));
            }

            return (value, null);
        }
        catch (JsonException ex)
        {
            return (default, TypedResults.Problem(
                title: ProblemTitle,
                detail: Describe(ex),
                statusCode: StatusCodes.Status400BadRequest));
        }
    }

    /// <summary>
    /// Reads a body that is a long-lived file format: at most <paramref name="maxBytes"/> long, and refused when it
    /// carries a member the type does not have.
    /// </summary>
    /// <typeparam name="T">The expected body type.</typeparam>
    /// <param name="httpContext">The request context.</param>
    /// <param name="maxBytes">The longest body accepted.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>
    /// The value and the bytes it was read from (a caller that audits the body hashes these), or a problem:
    /// <c>413</c> over the limit, <c>415</c> for a body that is not JSON, <c>400</c> for an empty body or one that
    /// does not parse, whose detail names the path of the member that failed.
    /// </returns>
    /// <remarks>
    /// <para>
    /// The unmapped-member rule is the point. A file written by a later version may carry a field this version
    /// does not know, and dropping it silently would import a definition that is not the one in the file. The
    /// other readers here keep the framework's default, which ignores such a member: a save endpoint's body is
    /// not a file that outlives the request.
    /// </para>
    /// <para>
    /// The body is read in chunks and stops at the limit, so a body that lies about its length, or has none,
    /// cannot make the server buffer more than <paramref name="maxBytes"/>.
    /// </para>
    /// </remarks>
    public static async Task<(T? Value, byte[]? Bytes, ProblemHttpResult? Error)> ReadStrictAsync<T>(
        HttpContext httpContext,
        long maxBytes,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        if (!httpContext.Request.HasJsonContentType())
        {
            return (default, null, TypedResults.Problem(
                title: "Unsupported media type",
                detail: "The body must be JSON ('Content-Type: application/json').",
                statusCode: StatusCodes.Status415UnsupportedMediaType));
        }

        if (httpContext.Request.ContentLength is { } declared && declared > maxBytes)
        {
            return (default, null, TooLarge(maxBytes));
        }

        using var collected = new MemoryStream();
        var chunk = ArrayPool<byte>.Shared.Rent(81920);

        try
        {
            int read;

            while ((read = await httpContext.Request.Body.ReadAsync(chunk.AsMemory(), cancellationToken).ConfigureAwait(false)) > 0)
            {
                if (collected.Length + read > maxBytes)
                {
                    return (default, null, TooLarge(maxBytes));
                }

                collected.Write(chunk, 0, read);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(chunk);
        }

        var bytes = collected.ToArray();

        if (bytes.Length == 0)
        {
            return (default, null, EmptyBody());
        }

        var applicationOptions = httpContext.RequestServices
            .GetRequiredService<IOptions<JsonOptions>>()
            .Value.SerializerOptions;

        try
        {
            var value = JsonSerializer.Deserialize<T>(bytes, StrictBodyOptions.GetValue(applicationOptions, CreateStrictBodyOptions));

            return value is null ? (default, null, EmptyBody()) : (value, bytes, null);
        }
        catch (JsonException ex)
        {
            return (default, null, TypedResults.Problem(
                title: ProblemTitle,
                detail: Describe(ex),
                statusCode: StatusCodes.Status400BadRequest));
        }
    }

    private static ProblemHttpResult TooLarge(long maxBytes)
        => TypedResults.Problem(
            title: "Request body too large",
            detail: $"The body may be at most {maxBytes} bytes.",
            statusCode: StatusCodes.Status413PayloadTooLarge);

    private static ProblemHttpResult EmptyBody()
        => TypedResults.Problem(
            title: ProblemTitle,
            detail: "The body cannot be empty.",
            statusCode: StatusCodes.Status400BadRequest);

    /// <summary>
    /// Reads the body as <typeparamref name="T"/>; if the body is ABSENT, this does
    /// NOT produce an error — <c>Value</c> returns <see langword="null"/>, preserving
    /// the optional-body behavior the original <c>[FromBody] T?</c> binding allowed.
    /// </summary>
    /// <typeparam name="T">The expected body type.</typeparam>
    /// <param name="httpContext">The request context.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>See <see cref="ReadAsync{T}"/>; the only difference is that a missing body does NOT count as an error.</returns>
    /// <remarks>
    /// <para>
    /// We did NOT try to pre-filter an empty body by checking the
    /// <c>Content-Length</c> header: under <c>TestServer</c>, the <c>Content-Length</c>
    /// actually sent by the client is NOT reliable (empirically confirmed by a
    /// regression — a populated body was treated as empty). Instead, <c>Content-Type</c>
    /// is checked: for a request that sends no body/type at all,
    /// <c>HttpRequest.ReadFromJsonAsync&lt;T&gt;</c> throws
    /// <see cref="InvalidOperationException"/>, NOT <see cref="JsonException"/>
    /// ("not a known JSON content type") — this is the correct way to distinguish an
    /// empty body.
    /// </para>
    /// <para>
    /// A request that sends a JSON <c>null</c> body together with
    /// <c>Content-Type: application/json</c> (<c>JsonContent.Create&lt;T&gt;(null)</c>)
    /// PASSES this check; <c>ReadFromJsonAsync</c> already returns
    /// <see langword="null"/> for such a body without throwing.
    /// </para>
    /// </remarks>
    public static async Task<(T? Value, ProblemHttpResult? Error)> ReadOptionalAsync<T>(
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (!httpContext.Request.HasJsonContentType())
        {
            return (default, null);
        }

        try
        {
            var value = await httpContext.Request
                .ReadFromJsonAsync<T>(ReadOptions(httpContext), cancellationToken)
                .ConfigureAwait(false);

            return (value, null);
        }
        catch (JsonException ex)
        {
            return (default, TypedResults.Problem(
                title: ProblemTitle,
                detail: Describe(ex),
                statusCode: StatusCodes.Status400BadRequest));
        }
    }
}
