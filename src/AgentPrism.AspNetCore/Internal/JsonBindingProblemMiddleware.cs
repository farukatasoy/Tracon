using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Http.Metadata;

namespace AgentPrism;

/// <summary>
/// Minimal API'nin otomatik govde baglamasinin firlattigi <see cref="JsonException"/>'i
/// (eksik <c>required</c> alan, taninmayan enum degeri) genel <c>500</c> yerine
/// <c>400</c> <c>ProblemDetails</c>'e cevirir.
/// </summary>
/// <remarks>
/// <para>
/// AgentPrism bir kutuphanedir; tuketicinin <c>AddProblemDetails()</c>/
/// <c>UseExceptionHandler()</c> cagirip cagirmadigina bagli KALAMAZ (HATA-S2-006,
/// HATA-S2-007). Bu ara yazilim <see cref="AgentPrismEndpointRouteBuilderExtensions.MapAgentPrism"/>
/// icinden, tuketicinin ayarindan BAGIMSIZ olarak takilir ve istisnayi
/// tuketicinin kendi genel handler'ina ulasmadan, kaynaginda yakalar.
/// </para>
/// <para>
/// Yalniz <c>AgentPrism</c> etiketli uclari etkiler (<see cref="ITagsMetadata"/>
/// denetimi) — tuketicinin kendi uclarina karisilmaz. Bazi uclar (orn.
/// <c>AgentEndpoints.BindAgentDefinitionRequestAsync</c>) govdeyi zaten elle
/// okuyup kendi <c>400</c> sozlesmesini uretiyor; bu ara yazilim yalniz o
/// denetimin OLMADIGI uclar icin devreye girer — istisna boyle uclarda hic
/// buraya ulasmaz.
/// </para>
/// <para>
/// 🚨 Bu ara yazilim TEK BASINA yeterli DEGILDIR: minimal API'nin otomatik
/// govde baglamasi <c>JsonException</c>'i yalniz <c>RouteHandlerOptions.ThrowOnBadRequest</c>
/// acikken (varsayilan: yalniz <c>IHostEnvironment.IsDevelopment()</c>) firlatir.
/// Production'da (varsayilan ortam) bu bayrak KAPALIDIR — minimal API govde
/// hatasini kendisi yakalar, istisna hic atilmaz ve govdesiz cikri bir <c>400</c>
/// yazar (500 degil, ama <c>ProblemDetails</c> de degil). Bu ara yazilim o yolu
/// GOREMEZ. Bu yuzden govde baglayan HER uc (eskiden <c>[FromBody]</c> veya
/// ortuk baglama kullanan tumu — bkz. <c>docs/openapi/agentprism.json</c>'daki
/// <c>requestBody</c> tasiyan rotalar) govdeyi KENDI elle okur
/// (<see cref="RequestBodyBinding.ReadAsync{T}"/>) — bu, ortamdan BAGIMSIZ
/// calisir. Bu ara yazilim yalniz savunma katmanidir: gelecekte elle okumayi
/// unutan bir uc icin (Development'ta) 500'u onler.
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
