using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Tracon;

/// <summary>
/// Gives the <c>415</c> that the routing layer writes for a content type no route accepts the body that every other refusal
/// of Tracon's has.
/// </summary>
/// <remarks>
/// <para>
/// A route that declares what its body is (<c>Accepts&lt;T&gt;("application/json")</c>) is matched on the request's content
/// type before any code of Tracon runs. A request with another content type does not match, and the matcher answers with an
/// endpoint of its own that sets the status and writes nothing: a bare <c>415</c>, no body, no content type, in every
/// environment. A caller that had sent XML to <c>POST /agents</c> learned only the status, while the same mistake on a route
/// that reads its body by hand got a problem document that names the content type it takes.
/// </para>
/// <para>
/// The middleware waits for the endpoint to answer and then looks at what answered: a <c>415</c> that no route of the
/// application wrote (the endpoint that ran is not a <see cref="RouteEndpoint"/>) under Tracon's prefix. A route of the
/// consumer's keeps the bare <c>415</c>; the MCP and A2A endpoints keep what their protocol libraries answer.
/// </para>
/// <para>
/// <see cref="Declare"/> is the other half: the document lists the <c>415</c> on every route that takes a body.
/// </para>
/// </remarks>
internal static class UnsupportedMediaTypeProblemMiddleware
{
    private const string AttachmentUpload = "/api/attachments";

    /// <summary>Adds the middleware to the pipeline.</summary>
    /// <param name="app">The application builder.</param>
    /// <param name="prefix">The normalized path prefix of Tracon's endpoints.</param>
    public static void Map(IApplicationBuilder app, string prefix)
    {
        ArgumentNullException.ThrowIfNull(app);
        ArgumentException.ThrowIfNullOrEmpty(prefix);

        var protocolRoutes = new[] { prefix + "/mcp", prefix + "/a2a" };

        app.Use(next => async httpContext =>
        {
            await next(httpContext).ConfigureAwait(false);

            if (httpContext.Response.StatusCode != StatusCodes.Status415UnsupportedMediaType
                || httpContext.Response.HasStarted
                || httpContext.GetEndpoint() is null or RouteEndpoint
                || !httpContext.Request.Path.StartsWithSegments(prefix, StringComparison.Ordinal)
                || protocolRoutes.Any(route => httpContext.Request.Path.StartsWithSegments(route, StringComparison.Ordinal)))
            {
                return;
            }

            await Refusal(httpContext, prefix).ExecuteAsync(httpContext).ConfigureAwait(false);
        });
    }

    /// <summary>Lists the <c>415</c> on every endpoint that declares the content type of its body.</summary>
    /// <param name="builder">The endpoint being built.</param>
    public static void Declare(EndpointBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        if (!builder.Metadata.OfType<IAcceptsMetadata>().Any()
            || builder.Metadata.OfType<IProducesResponseTypeMetadata>().Any(static declared => declared.StatusCode == StatusCodes.Status415UnsupportedMediaType))
        {
            return;
        }

        builder.Metadata.Add(
            builder.Metadata.OfType<OpenAIDenialShapeMarker>().Any()
                ? new ProducesResponseTypeAttribute(typeof(OpenAICompatSupport.OpenAIErrorEnvelope), StatusCodes.Status415UnsupportedMediaType, "application/json")
                : new ProducesResponseTypeAttribute(typeof(ProblemDetails), StatusCodes.Status415UnsupportedMediaType, "application/problem+json"));
    }

    private static IResult Refusal(HttpContext httpContext, string prefix)
    {
        var path = httpContext.Request.Path;

        if (path.StartsWithSegments(prefix + "/v1", StringComparison.Ordinal))
        {
            return OpenAICompatSupport.Error(
                StatusCodes.Status415UnsupportedMediaType,
                "The body must be JSON ('Content-Type: application/json').");
        }

        return HttpMethods.IsPost(httpContext.Request.Method) && path.Equals(prefix + AttachmentUpload, StringComparison.OrdinalIgnoreCase)
            ? RequestBodyBinding.NotForm()
            : RequestBodyBinding.NotJson();
    }
}
