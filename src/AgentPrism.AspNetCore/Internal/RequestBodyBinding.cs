using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;

namespace AgentPrism;

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
                .ReadFromJsonAsync<T>(cancellationToken)
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
                detail: ex.Message,
                statusCode: StatusCodes.Status400BadRequest));
        }
    }

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
                .ReadFromJsonAsync<T>(cancellationToken)
                .ConfigureAwait(false);

            return (value, null);
        }
        catch (JsonException ex)
        {
            return (default, TypedResults.Problem(
                title: ProblemTitle,
                detail: ex.Message,
                statusCode: StatusCodes.Status400BadRequest));
        }
    }
}
