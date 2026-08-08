using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace AgentPrism;

/// <summary>
/// Loopback kisitini ve bearer token denetimini uygulayan uc filtresi.
/// </summary>
/// <remarks>
/// <para>
/// Ucuncu katman olan authorization policy bu filtreye ait degildir; ASP.NET Core'un
/// kendi yetkilendirme boru hattina <c>RequireAuthorization</c> ile baglanir ve bu
/// filtreden <em>once</em> calisir.
/// </para>
/// <para>
/// Reddetme yanitlari nedeni acikca yazar ancak beklenen token hakkinda hicbir
/// bilgi vermez.
/// </para>
/// <para>
/// 🚨 Faz 53: bearer token katmani artik IKI kimlik kaynagi taniyabilir.
/// Once <see cref="AgentPrismEndpointOptions.AuthToken"/> ile sabit karsilastirma
/// denenir (degismedi). Eslesmezse ve bir <c>IApiKeyStore</c> kayitliysa, sunulan
/// deger o depoda aranir; gecerli, iptal edilmemis ve suresi gecmemis bir anahtar
/// bulunursa istek o anahtarin kiracisi ve kapsamiyla devam eder (bolum 53.5).
/// </para>
/// </remarks>
internal sealed class AgentPrismEndpointFilter : IEndpointFilter
{
    /// <summary>Son kullanim damgasinin en az bu araliktan sonra yeniden yazilmasi (Acik Soru 4).</summary>
    private static readonly TimeSpan LastUsedTouchInterval = TimeSpan.FromMinutes(1);

    private readonly bool _allowRemoteAccess;
    private readonly string? _authToken;

    /// <summary>Ayarlardan bir filtre kurar.</summary>
    /// <param name="options">Erisim ayarlari.</param>
    /// <param name="requireBearerToken">
    /// Bearer token katmani uygulansin mi. Arayuzun statik varliklari icin
    /// <see langword="false"/> gecilir: bir tarayici <c>&lt;script src&gt;</c>
    /// istegine <c>Authorization</c> basligi ekleyemez, dolayisiyla bu katman
    /// kabugu kilitlerse arayuz hicbir zaman acilamaz ve kullanici token'i
    /// girebilecegi bir ekran goremezdi. Kabuk veri tasimaz; loopback ve
    /// authorization policy katmanlari yine uygulanir ve her veri ucu tam
    /// korumada kalir.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> <see langword="null"/> ise.</exception>
    public AgentPrismEndpointFilter(AgentPrismEndpointOptions options, bool requireBearerToken = true)
    {
        ArgumentNullException.ThrowIfNull(options);

        // Ayarlar kurulum aninda okunur. MapAgentPrism'den sonra yapilan bir
        // degisiklik calisan uclari etkilememelidir; erisim kurallarinin calisma
        // aninda sessizce gevsemesi guvenlik acisindan kabul edilemez.
        _allowRemoteAccess = options.AllowRemoteAccess;
        _authToken = requireBearerToken ? options.AuthToken : null;
    }

    /// <inheritdoc />
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        var httpContext = context.HttpContext;

        if (!_allowRemoteAccess && !LoopbackGuard.IsLocal(httpContext.Connection.RemoteIpAddress))
        {
            return Results.Problem(
                title: "Uzak erisim kapali",
                detail: "AgentPrism uclari varsayilan olarak yalnizca ayni makineden erisilebilir. " +
                        "Uzak erisim icin AllowRemoteAccess ayarini acin ve bir kimlik dogrulama " +
                        "yontemi (AuthToken veya RequireAuthorization) yapilandirin.",
                statusCode: StatusCodes.Status403Forbidden);
        }

        var header = httpContext.Request.Headers.Authorization.ToString();

        if (header.Length == 0)
        {
            if (_authToken is { Length: > 0 })
            {
                return Unauthorized(httpContext);
            }

            // Ne statik token ne baslik var: bugunku davranis degismez (K1).
            return await Proceed(httpContext, next, context).ConfigureAwait(false);
        }

        if (_authToken is { Length: > 0 } expected && BearerTokenValidator.IsValid(header, expected))
        {
            return await Proceed(httpContext, next, context).ConfigureAwait(false);
        }

        var candidate = BearerTokenValidator.TryExtractToken(header);

        if (candidate is not null && httpContext.RequestServices.GetService<IApiKeyStore>() is { } apiKeyStore)
        {
            var timeProvider = httpContext.RequestServices.GetService<TimeProvider>() ?? TimeProvider.System;
            var record = await ApiKeyAuthenticator
                .AuthenticateAsync(apiKeyStore, candidate, timeProvider, httpContext.RequestAborted)
                .ConfigureAwait(false);

            if (record is not null)
            {
                var conflict = CheckTenantHeaderConflict(httpContext, record);

                if (conflict is not null)
                {
                    return conflict;
                }

                var scopeDenied = CheckScope(httpContext, record);

                if (scopeDenied is not null)
                {
                    return scopeDenied;
                }

                ApiKeyRequestContext.Set(httpContext, record);
                await TouchLastUsedIfStale(apiKeyStore, record, timeProvider, httpContext.RequestAborted)
                    .ConfigureAwait(false);

                return await Proceed(httpContext, next, context).ConfigureAwait(false);
            }
        }

        return Unauthorized(httpContext);
    }

    private static ValueTask<object?> Proceed(
        HttpContext httpContext,
        EndpointFilterDelegate next,
        EndpointFilterInvocationContext context)
    {
        // Denetim izi aktorunu ortam (ambient) baglamina tasir. AgentPrism.Core'daki
        // AmbientAuditActorResolver bunu bir AsyncLocal uzerinden okur; boylece Core,
        // ASP.NET Core'a bagimlilik eklemeden "kim yapti" sorusunu yanitlayabilir.
        // Gerekce: docs/09-YONETISIM-VE-DENETIM-IZI.md, bolum 9.2.
        AuditActorContext.Current = httpContext.User;

        return next(context);
    }

    private static ProblemHttpResult Unauthorized(HttpContext httpContext)
    {
        httpContext.Response.Headers.WWWAuthenticate = "Bearer";

        return TypedResults.Problem(
            title: "Kimlik dogrulanamadi",
            detail: "Gecerli bir 'Authorization: Bearer <token>' basligi gerekiyor.",
            statusCode: StatusCodes.Status401Unauthorized);
    }

    /// <summary>
    /// <c>X-AgentPrism-Tenant</c> basligi API anahtarinin kiracisindan farkli bir
    /// kiraci soyluyorsa reddeder.
    /// </summary>
    /// <remarks>
    /// 🚨 Baslik anahtari EZEMEZ (bolum 53.5). Ezebilseydi anahtarin kiraci bagi
    /// hicbir sey ifade etmezdi.
    /// </remarks>
    private static ProblemHttpResult? CheckTenantHeaderConflict(HttpContext httpContext, ApiKeyRecord record)
    {
        var tenancyOptions = httpContext.RequestServices.GetService<IOptions<AgentPrismTenancyOptions>>()?.Value;

        if (tenancyOptions is not { AllowHeaderResolution: true })
        {
            return null;
        }

        var declared = httpContext.Request.Headers[tenancyOptions.HeaderName].ToString();

        if (declared.Length == 0 || string.Equals(declared, record.TenantId, StringComparison.Ordinal))
        {
            return null;
        }

        return TypedResults.Problem(
            title: "Kiraci uyusmuyor",
            detail: $"'{tenancyOptions.HeaderName}' basligi API anahtarinin baglandigi kiraciyi " +
                    "EZEMEZ. Basligi kaldirin veya anahtarin kiracisiyla eslesen bir deger verin.",
            statusCode: StatusCodes.Status403Forbidden);
    }

    /// <summary>
    /// Cagrilan uc bir kapsam gerektiriyorsa ve anahtar onu tasimiyorsa reddeder.
    /// </summary>
    private static ProblemHttpResult? CheckScope(HttpContext httpContext, ApiKeyRecord record)
    {
        var requirement = httpContext.GetEndpoint()?.Metadata.GetMetadata<ApiKeyScopeRequirement>();

        if (requirement is null || record.Scopes.Contains(requirement.Scope))
        {
            return null;
        }

        return TypedResults.Problem(
            title: "Kapsam yetersiz",
            detail: $"Bu uc '{requirement.Scope}' kapsamini gerektiriyor; anahtar bu kapsami tasimiyor.",
            statusCode: StatusCodes.Status403Forbidden);
    }

    /// <summary>
    /// Son kullanim damgasini yalnizca yeterince eskiyse gunceller (Acik Soru 4).
    /// </summary>
    /// <remarks>
    /// Her istekte yazmak sicak okuma yoluna gereksiz bir <c>UPDATE</c> eklerdi.
    /// </remarks>
    private static ValueTask TouchLastUsedIfStale(
        IApiKeyStore store,
        ApiKeyRecord record,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();

        if (record.LastUsedAt is { } lastUsedAt && now - lastUsedAt < LastUsedTouchInterval)
        {
            return default;
        }

        return store.TouchLastUsedAsync(record.Id, now, cancellationToken);
    }
}
