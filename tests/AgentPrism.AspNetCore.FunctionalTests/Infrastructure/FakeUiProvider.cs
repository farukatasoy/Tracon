using Microsoft.AspNetCore.Http;

namespace AgentPrism.AspNetCore.FunctionalTests.Infrastructure;

/// <summary>
/// Statik varlik uretmeyen sahte arayuz kaynagi: kok istegini sabit bir metinle
/// karsilar, geri kalanini 404 birakir.
/// </summary>
/// <remarks>
/// Bu proje <c>AgentPrism.UI</c>'a bagimli degildir; kabuk-uc guvenlik davranisini
/// (loopback muafiyeti) gercek varlik derlemesi olmadan dogrulamak icin var.
/// </remarks>
internal sealed class FakeUiProvider : IAgentPrismUiProvider
{
    public bool HasAssets => true;

    public async ValueTask<bool> TryServeAsync(HttpContext context, string basePath, string relativePath)
    {
        if (relativePath.Length > 0)
        {
            return false;
        }

        context.Response.ContentType = "text/html";
        await context.Response.WriteAsync("<html>sahte kabuk</html>").ConfigureAwait(false);

        return true;
    }
}
