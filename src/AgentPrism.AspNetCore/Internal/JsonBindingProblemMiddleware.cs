using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Http.Metadata;

namespace AgentPrism;

/// <summary>
/// Converts the <see cref="JsonException"/> thrown by minimal API's automatic body
/// binding (a missing <c>required</c> field, an unrecognized enum value) into a
/// <c>400</c> <c>ProblemDetails</c> instead of a generic <c>500</c>.
/// </summary>
/// <remarks>
/// <para>
/// AgentPrism is a library; it CANNOT depend on whether the consumer calls
/// <c>AddProblemDetails()</c> or <c>UseExceptionHandler()</c>. This middleware is attached from
/// <see cref="AgentPrismEndpointRouteBuilderExtensions.MapAgentPrism"/>,
/// INDEPENDENTLY of the consumer's setup, and catches the exception at its
/// source, before it reaches the consumer's own global handler.
/// </para>
/// <para>
/// It affects only endpoints tagged <c>AgentPrism</c> (checked via
/// <see cref="ITagsMetadata"/>) — the consumer's own endpoints are not touched.
/// Some endpoints (e.g. <c>AgentEndpoints.BindAgentDefinitionRequestAsync</c>)
/// already read the body by hand and produce their own <c>400</c> contract; this
/// middleware only kicks in for endpoints WITHOUT that check — the exception never
/// reaches this point on such endpoints.
/// </para>
/// <para>
/// This middleware is NOT sufficient BY ITSELF: minimal API's automatic body
/// binding only throws <c>JsonException</c> when
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
/// layer: it prevents a 500 (in Development) for an endpoint that, in the future,
/// forgets to read the body by hand.
/// </para>
/// </remarks>
internal static class JsonBindingProblemMiddleware
{
    public static async Task InvokeAsync(HttpContext httpContext, RequestDelegate next)
    {
        try
        {
            await next(httpContext).ConfigureAwait(false);
        }
        catch (BadHttpRequestException ex) when (
            ex.InnerException is JsonException jsonException &&
            !httpContext.Response.HasStarted &&
            IsAgentPrismEndpoint(httpContext))
        {
            await TypedResults.Problem(
                    title: RequestBodyBinding.ProblemTitle,
                    detail: jsonException.Message,
                    statusCode: StatusCodes.Status400BadRequest)
                .ExecuteAsync(httpContext)
                .ConfigureAwait(false);
        }
    }

    private static bool IsAgentPrismEndpoint(HttpContext httpContext)
        => httpContext.GetEndpoint()?.Metadata.GetMetadata<ITagsMetadata>() is { } tags &&
           tags.Tags.Contains("AgentPrism", StringComparer.Ordinal);
}
