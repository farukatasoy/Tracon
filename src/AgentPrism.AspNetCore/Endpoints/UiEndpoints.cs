using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace AgentPrism;

/// <summary>
/// Gomulu yonetim arayuzunun statik varliklarini sunan rotalar.
/// </summary>
/// <remarks>
/// <para>
/// Rotalar yakalayici (<c>{**path}</c>) desen kullanir. ASP.NET Core yonlendirmesinde
/// harfi harfine segmentler yakalayici desenden <strong>once</strong> gelir, bu yuzden
/// <c>/api/*</c> ve <c>/v1/*</c> uclari her zaman kazanir.
/// </para>
/// <para>
/// Yine de bu iki onek burada acikca reddedilir. Sebebi: yanlis yazilmis bir API yolu
/// (<c>/api/agentz</c>) yakalayiciya duser ve arayuzun <c>index.html</c> dosyasi
/// donerdi. Bir API istemcisi icin bu, hata ayiklanmasi zor bir sessiz basarisizliktir -
/// beklenen <c>404</c> yerine <c>200 text/html</c> alir.
/// </para>
/// </remarks>
internal static class UiEndpoints
{
    private static readonly string[] ReservedPrefixes = ["api/", "v1/"];

    private static readonly string[] HttpMethods = ["GET", "HEAD"];

    /// <summary>Arayuz rotalarini baglar.</summary>
    /// <param name="builder">Uc grubu.</param>
    /// <param name="provider">Varlik kaynagi.</param>
    /// <param name="prefix">Normalize edilmis yol oneki. Ornek: <c>/agentprism</c>.</param>
    public static void Map(IEndpointRouteBuilder builder, IAgentPrismUiProvider provider, string prefix)
    {
        var basePath = prefix + "/";

        // HEAD de eslenir. GET-only bir uc HEAD istegine 405 doner; ters vekiller
        // ve saglik denetimleri statik varliklari HEAD ile yoklar ve bunu bir
        // ariza olarak raporlar. Govde yazilir, Kestrel HEAD yanitinda onu atar.
        builder.MapMethods("/", HttpMethods, (HttpContext context) => ServeAsync(context, provider, basePath, string.Empty))
            .WithName("AgentPrismUiRoot")
            .ExcludeFromDescription();

        builder.MapMethods("/{**path}", HttpMethods, (HttpContext context, string path) => ServeAsync(context, provider, basePath, path))
            .WithName("AgentPrismUiAsset")
            .ExcludeFromDescription();
    }

    /// <summary>
    /// Istegi arayuz kaynagina devreder; karsilanmazsa <c>404</c> yazar.
    /// </summary>
    /// <remarks>
    /// Sonucu dondurmek yerine dogrudan yaniti yazar. Sebep teknik: tek parametresi
    /// <see cref="HttpContext"/> olan ve <c>Task&lt;T&gt;</c> donduren bir rota
    /// isleyicisi ASP.NET Core tarafindan <c>RequestDelegate</c> sayilir ve donen
    /// deger sessizce atilir (<c>ASP0016</c>).
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
            title: "Bulunamadi",
            detail: $"'{path}' yolunda bir uc veya arayuz varligi yok.",
            statusCode: StatusCodes.Status404NotFound);
}
