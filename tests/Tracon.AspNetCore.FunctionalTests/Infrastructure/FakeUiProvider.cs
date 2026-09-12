using Microsoft.AspNetCore.Http;

namespace Tracon.AspNetCore.FunctionalTests.Infrastructure;

/// <summary>
/// A fake UI asset source that produces no static assets: it serves the root
/// request with a fixed text and leaves everything else as 404.
/// </summary>
/// <remarks>
/// This project does not depend on <c>Tracon.UI</c>; it exists to verify
/// shell-endpoint security behavior (the loopback exemption) without a real
/// asset build.
/// </remarks>
internal sealed class FakeUiProvider : ITraconUiProvider
{
    public bool HasAssets => true;

    public async ValueTask<bool> TryServeAsync(HttpContext context, string basePath, string relativePath)
    {
        if (relativePath.Length > 0)
        {
            return false;
        }

        context.Response.ContentType = "text/html";
        await context.Response.WriteAsync("<html>fake shell</html>").ConfigureAwait(false);

        return true;
    }
}
