using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Http.Metadata;

namespace Tracon;

/// <summary>
/// Converts a minimal API binding failure — the <see cref="JsonException"/> from
/// automatic body binding (a missing <c>required</c> field, an unrecognized enum
/// value), or the plain <see cref="BadHttpRequestException"/> from a query/route
/// parameter that fails to parse (a bad enum value, a bad number) — into a
/// <c>400</c> <c>ProblemDetails</c> instead of a generic <c>500</c>.
/// </summary>
/// <remarks>
/// <para>
/// Tracon is a library; it CANNOT depend on whether the consumer calls
/// <c>AddProblemDetails()</c> or <c>UseExceptionHandler()</c>. This middleware is attached from
/// <see cref="TraconEndpointRouteBuilderExtensions.MapTracon"/>,
/// INDEPENDENTLY of the consumer's setup, and catches the exception at its
/// source, before it reaches the consumer's own global handler.
/// </para>
/// <para>
/// It affects only endpoints tagged <c>Tracon</c> (checked via
/// <see cref="ITagsMetadata"/>) — the consumer's own endpoints are not touched.
/// Some endpoints (e.g. <c>AgentEndpoints.BindAgentDefinitionRequestAsync</c>)
/// already read the body by hand and produce their own <c>400</c> contract; this
/// middleware only kicks in for endpoints WITHOUT that check — the exception never
/// reaches this point on such endpoints.
/// </para>
/// <para>
/// This middleware is NOT sufficient BY ITSELF for the body-binding case: minimal
/// API's automatic body binding only throws <c>JsonException</c> when
/// <c>RouteHandlerOptions.ThrowOnBadRequest</c> is enabled (default: only under
/// <c>IHostEnvironment.IsDevelopment()</c>). In production (the default environment)
/// this flag is OFF — minimal API catches the body error itself, no exception is
/// ever thrown, and it writes a bodyless <c>400</c> (not 500, but not
/// <c>ProblemDetails</c> either). This middleware CANNOT see that path. That is why
/// EVERY endpoint that binds a body (all of them, whether they formerly used
/// <c>[FromBody]</c> or implicit binding — see the routes carrying
/// <c>requestBody</c> in the published OpenAPI document) reads the body BY
/// HAND ITSELF (<see cref="RequestBodyBinding.ReadAsync{T}"/>) — this works
/// INDEPENDENTLY of the environment. This middleware is only a defense-in-depth
/// layer for the body case: it prevents a 500 (in Development) for an endpoint
/// that, in the future, forgets to read the body by hand.
/// </para>
/// <para>
/// A query or route parameter that fails to parse (<c>?bucket=day</c> against a
/// case-sensitive <c>enum?</c> parameter, for example) is a DIFFERENT path: minimal
/// API throws <see cref="BadHttpRequestException"/> for it UNCONDITIONALLY, in every
/// environment, with no <see cref="JsonException"/> inner exception to key off of —
/// <c>ThrowOnBadRequest</c> only gates the body-binding path above.
/// Left uncaught, this reaches the consumer's default handler as a bare <c>500</c>
/// in production, not a bodyless <c>400</c> like the body case. There is no
/// "read it by hand" escape hatch for a route-templated minimal API parameter the
/// way there is for the body, so this middleware is the ONLY defense for it — it
/// must catch every <see cref="BadHttpRequestException"/>, not only the
/// JSON-body-shaped one.
/// </para>
/// </remarks>
internal static class JsonBindingProblemMiddleware
{
    /// <summary>The title used when a query or route parameter fails to parse (not a request body).</summary>
    internal const string ParameterProblemTitle = "Invalid request parameter";

    public static async Task InvokeAsync(HttpContext httpContext, RequestDelegate next)
    {
        try
        {
            await next(httpContext).ConfigureAwait(false);
        }
        catch (BadHttpRequestException ex) when (
            !httpContext.Response.HasStarted &&
            IsTraconEndpoint(httpContext))
        {
            var isBodyBindingFailure = ex.InnerException is JsonException;

            await TypedResults.Problem(
                    title: isBodyBindingFailure ? RequestBodyBinding.ProblemTitle : ParameterProblemTitle,
                    detail: isBodyBindingFailure ? ex.InnerException!.Message : ex.Message,
                    statusCode: StatusCodes.Status400BadRequest)
                .ExecuteAsync(httpContext)
                .ConfigureAwait(false);
        }
    }

    private static bool IsTraconEndpoint(HttpContext httpContext)
        => httpContext.GetEndpoint()?.Metadata.GetMetadata<ITagsMetadata>() is { } tags &&
           tags.Tags.Contains("Tracon", StringComparer.Ordinal);
}
