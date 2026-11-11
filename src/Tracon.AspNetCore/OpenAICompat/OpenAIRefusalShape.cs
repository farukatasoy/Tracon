using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Tracon;

/// <summary>
/// Gives a refusal that a filter writes before the endpoint's own code the error shape of the route it refuses.
/// </summary>
/// <remarks>
/// The OpenAI-compatible routes answer every error as <c>{"error":{"message":…,"type":…}}</c>, which is what the OpenAI SDKs
/// parse and what the published document lists for them. The filters that sit in front of those routes (the access filter,
/// the idempotency filter) write <c>ProblemDetails</c> everywhere else, so each of them asks this class instead of building
/// the result itself. A route says it is one of these with <see cref="OpenAIDenialShapeMarker"/>.
/// </remarks>
internal static class OpenAIRefusalShape
{
    /// <summary>The <c>error.type</c> of a request the rate limit refused.</summary>
    internal const string RateLimitType = "rate_limit_error";

    /// <summary>Whether the matched endpoint answers its errors in the OpenAI shape.</summary>
    /// <param name="httpContext">The current request.</param>
    /// <returns><see langword="true"/> on a route marked with <see cref="OpenAIDenialShapeMarker"/>.</returns>
    internal static bool Applies(HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        return httpContext.GetEndpoint()?.Metadata.GetMetadata<OpenAIDenialShapeMarker>() is not null;
    }

    /// <summary>Builds a refusal in the shape of the matched route: OpenAI on an OpenAI-compatible route, <c>ProblemDetails</c> elsewhere.</summary>
    /// <param name="httpContext">The current request.</param>
    /// <param name="title">The <c>ProblemDetails</c> title. The OpenAI shape has no title and drops it.</param>
    /// <param name="detail">The message the caller reads.</param>
    /// <param name="statusCode">The HTTP status.</param>
    /// <returns>The refusal.</returns>
    internal static IResult Refuse(HttpContext httpContext, string title, string detail, int statusCode)
        => Applies(httpContext)
            ? OpenAICompatSupport.Error(statusCode, detail, TypeFor(statusCode))
            : Results.Problem(title: title, detail: detail, statusCode: statusCode);

    /// <summary>Re-writes a <c>ProblemDetails</c> refusal in the OpenAI shape; the status and the message stay.</summary>
    /// <param name="refusal">The refusal a filter built.</param>
    /// <returns>The same refusal as an OpenAI error.</returns>
    internal static IResult From(ProblemHttpResult refusal)
    {
        ArgumentNullException.ThrowIfNull(refusal);

        var problem = refusal.ProblemDetails;
        var status = problem.Status ?? StatusCodes.Status500InternalServerError;

        return OpenAICompatSupport.Error(status, problem.Detail ?? problem.Title ?? "The request was refused.", TypeFor(status));
    }

    private static string TypeFor(int status) => status switch
    {
        StatusCodes.Status401Unauthorized => ProblemDetailsOnDenialHandler.OpenAIUnauthorizedType,
        StatusCodes.Status403Forbidden => ProblemDetailsOnDenialHandler.OpenAIForbiddenType,
        StatusCodes.Status404NotFound => TraconErrorTypes.NotFoundError,
        StatusCodes.Status429TooManyRequests => RateLimitType,
        StatusCodes.Status503ServiceUnavailable => TraconErrorTypes.ServiceUnavailable,
        _ => TraconErrorTypes.InvalidRequestError,
    };
}
