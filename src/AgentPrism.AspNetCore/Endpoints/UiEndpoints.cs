using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace AgentPrism;

/// <summary>
/// Routes that serve the embedded management UI's static assets.
/// </summary>
/// <remarks>
/// <para>
/// The routes use a catch-all (<c>{**path}</c>) pattern. In ASP.NET Core routing,
/// literal segments take precedence <strong>over</strong> the catch-all pattern, so the
/// <c>/api/*</c> and <c>/v1/*</c> endpoints always win.
/// </para>
/// <para>
/// Even so, these two prefixes are also explicitly rejected here. A misspelled
/// API path (<c>/api/agentz</c>) would fall through to the catch-all and the UI's
/// <c>index.html</c> file would be returned. For an API client this is a silent
/// failure that is hard to debug — it gets <c>200 text/html</c> instead of the expected
/// <c>404</c>.
/// </para>
/// </remarks>
internal static class UiEndpoints
{
    private static readonly string[] ReservedPrefixes = ["api/", "v1/"];

    private static readonly string[] HttpMethods = ["GET", "HEAD"];

    /// <summary>Maps the UI routes.</summary>
    /// <param name="builder">The endpoint group.</param>
    /// <param name="provider">The asset source.</param>
    /// <param name="prefix">The normalized path prefix. Example: <c>/agentprism</c>.</param>
    public static void Map(IEndpointRouteBuilder builder, IAgentPrismUiProvider provider, string prefix)
    {
        var basePath = prefix + "/";

        // HEAD is also mapped. A GET-only endpoint returns 405 to a HEAD request;
        // reverse proxies and health checks probe static assets with HEAD and report
        // that as an outage. The body is written, Kestrel discards it in the HEAD response.
        builder.MapMethods("/", HttpMethods, (HttpContext context) => ServeAsync(context, provider, basePath, string.Empty))
            .WithName("AgentPrismUiRoot")
            .ExcludeFromDescription();

        builder.MapMethods("/{**path}", HttpMethods, (HttpContext context, string path) => ServeAsync(context, provider, basePath, path))
            .WithName("AgentPrismUiAsset")
            .ExcludeFromDescription();
    }

    /// <summary>
    /// Hands the request off to the UI asset source; writes <c>404</c> if it is not served.
    /// </summary>
    /// <remarks>
    /// Writes the response directly instead of returning a result. The reason is
    /// technical: a route handler whose only parameter is <see cref="HttpContext"/> and
    /// that returns <c>Task&lt;T&gt;</c> is treated by ASP.NET Core as a
    /// <c>RequestDelegate</c>, and the returned value is silently discarded (<c>ASP0016</c>).
    /// </remarks>
    private static async Task ServeAsync(
        HttpContext context,
        IAgentPrismUiProvider provider,
        string basePath,
        string relativePath)
    {
        var path = relativePath.TrimStart('/');

        foreach (var reserved in ReservedPrefixes)
        {
            if (path.StartsWith(reserved, StringComparison.Ordinal))
            {
                await NotFound(path).ExecuteAsync(context).ConfigureAwait(false);

                return;
            }
        }

        if (!await provider.TryServeAsync(context, basePath, path).ConfigureAwait(false))
        {
            await NotFound(path).ExecuteAsync(context).ConfigureAwait(false);
        }
    }

    private static IResult NotFound(string path)
        => Results.Problem(
            title: "Not found",
            detail: $"There is no endpoint or console asset at path '{path}'.",
            statusCode: StatusCodes.Status404NotFound);
}
