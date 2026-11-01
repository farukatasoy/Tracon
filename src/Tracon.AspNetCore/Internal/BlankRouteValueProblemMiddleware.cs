using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Http.Metadata;

namespace Tracon;

/// <summary>
/// Answers <c>404</c> when a route value that is only whitespace reached a store that refuses a blank name, instead of
/// letting the store's <see cref="ArgumentException"/> out as a bodyless <c>500</c>.
/// </summary>
/// <remarks>
/// <para>
/// A store guards its arguments with <c>ArgumentException.ThrowIfNullOrWhiteSpace</c>, which is right for a caller in
/// code. A route segment is not a caller in code: <c>/webhooks/%20/test</c> is a request anyone can make by mistake, and
/// twenty-six operations passed the segment on unchecked. Nothing is stored under a blank name, so the honest answer is
/// the one a name that was never saved already gets from the operations that do check: not found.
/// </para>
/// <para>
/// The mapping is narrow on purpose. It acts only on a plain <see cref="ArgumentException"/> (what the guard throws for an
/// empty or whitespace string), only when a route value of the request IS blank, and only on endpoints tagged
/// <c>Tracon</c>. An operation that validates its name itself, as the save endpoints do with a <c>400</c> that says
/// what is wrong with it, never reaches this code and keeps its answer. The same shape as
/// <see cref="StoreUnavailableProblemMiddleware"/>.
/// </para>
/// </remarks>
internal static class BlankRouteValueProblemMiddleware
{
    /// <summary>The title of the problem.</summary>
    internal const string ProblemTitle = "Not found";

    public static async Task InvokeAsync(HttpContext httpContext, RequestDelegate next)
    {
        try
        {
            await next(httpContext).ConfigureAwait(false);
        }
        catch (ArgumentException exception) when (
            exception.GetType() == typeof(ArgumentException) &&
            !httpContext.Response.HasStarted &&
            IsTraconEndpoint(httpContext) &&
            FindBlankRouteValue(httpContext) is not null)
        {
            await TypedResults.Problem(
                    title: ProblemTitle,
                    detail: $"The '{FindBlankRouteValue(httpContext)}' part of the path is blank, and nothing is stored under a blank name.",
                    statusCode: StatusCodes.Status404NotFound)
                .ExecuteAsync(httpContext)
                .ConfigureAwait(false);
        }
    }

    private static string? FindBlankRouteValue(HttpContext httpContext)
    {
        foreach (var (key, value) in httpContext.Request.RouteValues)
        {
            if (value is string text && string.IsNullOrWhiteSpace(text))
            {
                return key;
            }
        }

        return null;
    }

    private static bool IsTraconEndpoint(HttpContext httpContext)
        => httpContext.GetEndpoint()?.Metadata.GetMetadata<ITagsMetadata>() is { } tags &&
           tags.Tags.Contains("Tracon", StringComparer.Ordinal);
}
