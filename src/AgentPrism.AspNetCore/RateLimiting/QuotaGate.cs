using System.Globalization;
using Microsoft.AspNetCore.Http;

namespace AgentPrism;

/// <summary>
/// Bir calistirma baslamadan once kota denetimini yapan ve asimda
/// <c>429</c> ureten yardimci.
/// </summary>
/// <remarks>
/// <para>
/// Denetim, calistirmayi baslatan her ucta <strong>acikca</strong> cagrilir; bir
/// uc filtresi degildir. Sebep: OpenAI uyumlu uclarda agent adi rota degerinde
/// degil govdedeki <c>model</c> alanindadir ve bir filtrenin govdeyi okumasi
/// istegi iki kez ayristirmayi gerektirirdi.
/// </para>
/// <para>
/// 🚨 Devam eden bir calistirma kota asilinca <strong>kesilmez</strong> (K-162).
/// Bu kapi yalnizca <em>yeni</em> calistirmayi durdurur.
/// </para>
/// </remarks>
internal static class QuotaGate
{
    /// <summary>Kotayi denetler; asilmissa dondurulecek yaniti uretir.</summary>
    /// <param name="enforcer">Kota denetleyici. <see langword="null"/> ise denetim yapilmaz.</param>
    /// <param name="tenants">Kiraci baglami.</param>
    /// <param name="agentName">Calistirilacak agent'in adi.</param>
    /// <param name="httpContext">Istek baglami. <c>Retry-After</c> basligi buraya yazilir.</param>
    /// <param name="cancellationToken">Iptal belirteci.</param>
    /// <returns>
    /// Kota asilmissa dondurulecek <c>429</c> yaniti; asilmamissa
    /// <see langword="null"/>.
    /// </returns>
    public static async ValueTask<IResult?> CheckAsync(
        QuotaEnforcer? enforcer,
        ITenantContext tenants,
        string agentName,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (enforcer is null)
        {
            return null;
        }

        var decision = await enforcer
            .CheckAsync(tenants.TenantId, agentName, cancellationToken)
            .ConfigureAwait(false);

        if (decision.IsAllowed)
        {
            return null;
        }

        // Sayacin ne zaman sifirlanacagi biliniyorsa istemciye Retry-After
        // olarak verilir; istemcinin tahmin etmesi gerekmemelidir.
        if (decision.ResetsAt is { } resetsAt)
        {
            var seconds = Math.Max(1, (int)Math.Ceiling((resetsAt - DateTimeOffset.UtcNow).TotalSeconds));

            httpContext.Response.Headers.RetryAfter =
                seconds.ToString(CultureInfo.InvariantCulture);
        }

        return Results.Problem(
            title: "Kota asildi",
            detail: decision.Reason ?? "Bu kiraci icin tanimli kota asildi.",
            statusCode: StatusCodes.Status429TooManyRequests,
            extensions: BuildExtensions(decision));
    }

    private static Dictionary<string, object?> BuildExtensions(QuotaDecision decision)
    {
        // ProblemDetails'in uzantilari makine tarafindan okunabilir olmalidir:
        // istemci "hangi kota, ne kadar, ne zaman sifirlanir" sorularini metni
        // ayristirmadan yanitlayabilmelidir.
        var extensions = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["quotaMetric"] = decision.Metric?.ToString(),
            ["quotaPeriod"] = decision.Period?.ToString(),
            ["quotaLimit"] = decision.Limit,
            ["quotaUsed"] = decision.Used,
        };

        if (decision.AgentName is { Length: > 0 } agentName)
        {
            extensions["quotaAgentName"] = agentName;
        }

        if (decision.ResetsAt is { } resetsAt)
        {
            extensions["quotaResetsAt"] = resetsAt.ToString("O", CultureInfo.InvariantCulture);
        }

        if (decision.CostFellBackToTokens)
        {
            extensions["quotaCostFellBackToTokens"] = true;
        }

        return extensions;
    }
}
