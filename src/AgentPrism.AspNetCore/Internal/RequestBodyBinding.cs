using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;

namespace AgentPrism;

/// <summary>
/// Istek govdesini elle okur — minimal API'nin otomatik <c>[FromBody]</c>
/// baglamasi yerine.
/// </summary>
/// <remarks>
/// <para>
/// Minimal API'nin kendi govde baglamasi bir ayristirma hatasinda (eksik
/// <c>required</c> alan, taninmayan enum degeri) <c>JsonException</c>'i yalniz
/// <c>RouteHandlerOptions.ThrowOnBadRequest</c> acikken firlatir — bu bayrak
/// VARSAYILAN olarak yalniz <c>Development</c> ortaminda actiktir. Production'da
/// (gercek dagitimlarin cogu) govde hatasi sessizce govdesiz bir <c>400</c>
/// ile sonuclanir; <see cref="JsonBindingProblemMiddleware"/> bu yolu HIC
/// GOREMEZ (istisna atilmaz). Bu metot govdeyi HER ZAMAN elle okuyup
/// <c>JsonException</c>'i dogrudan yakalar — ortamdan BAGIMSIZ olarak ayni
/// <c>ProblemDetails</c> sozlesmesini uretir (HATA-S2-006, HATA-S2-007).
/// </para>
/// </remarks>
internal static class RequestBodyBinding
{
    /// <summary>Diger elle-baglama uclarinin da kullandigi ortak baslik.</summary>
    internal const string ProblemTitle = "Gecersiz istek govdesi";

    /// <summary>
    /// Govdeyi <typeparamref name="T"/> olarak okur.
    /// </summary>
    /// <typeparam name="T">Beklenen govde tipi.</typeparam>
    /// <param name="httpContext">Istek baglami.</param>
    /// <param name="cancellationToken">Iptal token'i.</param>
    /// <returns>
    /// Basariliysa <c>Value</c> dolu ve <c>Error</c> <see langword="null"/>'dur.
    /// Govde bos veya ayristirilamazsa <c>Value</c> <see langword="null"/> ve
    /// <c>Error</c> cagiranin dogrudan dondurebilecegi bir <c>400</c>
    /// <see cref="ProblemHttpResult"/> tasir.
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
                    detail: "Govde bos olamaz.",
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
    /// Govdeyi <typeparamref name="T"/> olarak okur; govde YOKSA hata uretmez,
    /// <c>Value</c> <see langword="null"/> doner — orijinal <c>[FromBody] T?</c>
    /// baglamasinin izin verdigi opsiyonel govde davranisini korur.
    /// </summary>
    /// <typeparam name="T">Beklenen govde tipi.</typeparam>
    /// <param name="httpContext">Istek baglami.</param>
    /// <param name="cancellationToken">Iptal token'i.</param>
    /// <returns>Bkz. <see cref="ReadAsync{T}"/>; tek fark govde yoklugunun hata SAYILMAMASIDIR.</returns>
    /// <remarks>
    /// <para>
    /// 🚨 Bos govdeyi <c>Content-Length</c> basligina bakarak ONCEDEN elemeye
    /// CALISILMADI: <c>TestServer</c> altinda istemcinin gerçekten gonderdigi
    /// <c>Content-Length</c> guvenilir DEGIL (bir regresyonla ampirik olarak
    /// dogrulandi — dolu bir govde bos sayildi). Bunun yerine <c>Content-Type</c>
    /// denetlenir: hic govde/tip gondermeyen bir istek icin
    /// <c>HttpRequest.ReadFromJsonAsync&lt;T&gt;</c> <see cref="JsonException"/>
    /// DEGIL <see cref="InvalidOperationException"/> firlatir ("bilinen bir JSON
    /// icerik tipi degil") — bu, bos govdenin dogru ayirt edicisidir.
    /// </para>
    /// <para>
    /// <c>Content-Type: application/json</c> ile birlikte JSON <c>null</c>
    /// govdesi gonderen bir istek (<c>JsonContent.Create&lt;T&gt;(null)</c>)
    /// bu denetimden GECER; <c>ReadFromJsonAsync</c> boyle bir govdede zaten
    /// <see langword="null"/> doner, istisna atmaz.
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
