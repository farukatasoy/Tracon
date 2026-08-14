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
    private readonly bool _requireLoopback;
    private readonly bool _requireBearerToken;
    private readonly string? _authToken;
    private readonly string? _staticAuthToken;

    /// <summary>Ayarlardan bir filtre kurar.</summary>
    /// <param name="options">Erisim ayarlari.</param>
    /// <param name="requireBearerToken">
    /// Bearer token katmani uygulansin mi. Arayuzun statik varliklari icin
    /// <see langword="false"/> gecilir: bir tarayici <c>&lt;script src&gt;</c>
    /// istegine <c>Authorization</c> basligi ekleyemez, dolayisiyla bu katman
    /// kabugu kilitlerse arayuz hicbir zaman acilamaz ve kullanici token'i
    /// girebilecegi bir ekran goremezdi. Kabuk veri tasimaz; authorization
    /// policy katmani yine uygulanir ve her veri ucu tam korumada kalir.
    /// </param>
    /// <param name="requireLoopback">
    /// Loopback kisiti uygulansin mi. Arayuzun statik varliklari icin
    /// <see langword="false"/> gecilir: kisit kabuga uygulanirsa loopback disi
    /// bir istemci JS paketini hic indiremez, <c>AccessGate</c>'in kendisi
    /// (HATA-S4-003) hicbir zaman calisamaz ve kullanici "Erisim reddedildi"
    /// kartini goremeden ham sunucu JSON'iyla kalir. Kabuk veri tasimaz;
    /// gercek koruma veri uclarindaki (bu bayrak varsayilan <see langword="true"/>
    /// olan) filtre orneklerinden gelir — kabuk yalnizca oraya yapilan bir
    /// probe istegi 403 dondugunde kullaniciya dogru mesaji gosterebilir.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> <see langword="null"/> ise.</exception>
    public AgentPrismEndpointFilter(AgentPrismEndpointOptions options, bool requireBearerToken = true, bool requireLoopback = true)
    {
        ArgumentNullException.ThrowIfNull(options);

        // Ayarlar kurulum aninda okunur. MapAgentPrism'den sonra yapilan bir
        // degisiklik calisan uclari etkilememelidir; erisim kurallarinin calisma
        // aninda sessizce gevsemesi guvenlik acisindan kabul edilemez.
        _allowRemoteAccess = options.AllowRemoteAccess;
        _requireLoopback = requireLoopback;
        _requireBearerToken = requireBearerToken;
        _authToken = requireBearerToken ? options.AuthToken : null;
        _staticAuthToken = options.AuthToken;
    }

    /// <inheritdoc />
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        var httpContext = context.HttpContext;

        if (_requireLoopback && !_allowRemoteAccess && !LoopbackGuard.IsLocal(httpContext.Connection.RemoteIpAddress))
        {
            return Results.Problem(
                title: "Remote access disabled",
                detail: "AgentPrism endpoints are reachable only from the same machine by default. " +
                        "For remote access, enable the AllowRemoteAccess setting and configure an " +
                        "authentication method (AuthToken or RequireAuthorization).",
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
            return CheckTenancyWhitelist(httpContext) is { } rejectedNoAuth
                ? rejectedNoAuth
                : await Proceed(httpContext, next, context).ConfigureAwait(false);
        }

        if (_authToken is { Length: > 0 } expected && BearerTokenValidator.IsValid(header, expected))
        {
            return CheckTenancyWhitelist(httpContext) is { } rejectedStaticToken
                ? rejectedStaticToken
                : await Proceed(httpContext, next, context).ConfigureAwait(false);
        }

        if (!_requireBearerToken
            && _staticAuthToken is { Length: > 0 } configured
            && BearerTokenValidator.IsValid(header, configured))
        {
            // Bu uc grubu bearer token katmanini UYGULAMAZ (K1) ama sunulan
            // deger AgentPrism'in kendi statik AuthToken'iyla eslesiyor — bu bir
            // API anahtari degildir, IApiKeyStore aramasi hicbir zaman bulamaz ve
            // asagidaki genel Unauthorized()'a duserdi (HATA-S1-014): dogru token
            // ile hic token verilmemis gibi ayni yanit donerdi. Baslik YOKMUS gibi
            // notr davranilir — reddetmez, ek yetki de vermez; ucun kendi mantigina
            // (varsa, ornegin bir 404/400) ulasilir.
            return CheckTenancyWhitelist(httpContext) is { } rejectedGroupToken
                ? rejectedGroupToken
                : await Proceed(httpContext, next, context).ConfigureAwait(false);
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

    /// <summary>
    /// <see cref="AgentPrismTenancyOptions.AllowedTenants"/> doluyken, beyaz listede
    /// OLMAYAN bicimce gecerli bir aday kiraciyi reddeder.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 🚨 Bu denetim olmadan <see cref="HttpTenantContext.TenantId"/>'nin <c>??</c>
    /// zinciri (<c>Resolve() ?? DefaultTenantId</c>) beyaz listenin reddettigi bir
    /// adayi SESSIZCE varsayilan kiraciya dusururdu — tam olarak
    /// <see cref="AgentPrismTenancyOptions.AllowedTenants"/>'in kendi XML belgesinin
    /// yasakladigi durum ("listede olmayan bir deger varsayilan kiraciya dusmez").
    /// Bu yuzden aday burada, istek endpoint'e ulasmadan ONCE, ayni kaynaktan
    /// (claim veya baslik — <see cref="HttpTenantContext.Resolve"/> ile birebir
    /// ayni oncelik) okunup denetlenir.
    /// </para>
    /// <para>
    /// Aday HIC saglanmamissa (ne claim ne baslik) ya da bicimce gecersizse bu
    /// denetim devreye girmez — varsayilan kiraciya dusmek o durumda kasitli
    /// davranistir (K1: coklu kiracilik acilmadan hicbir sey degismez).
    /// </para>
    /// </remarks>
    private static ProblemHttpResult? CheckTenancyWhitelist(HttpContext httpContext)
    {
        var tenancyOptions = httpContext.RequestServices.GetService<IOptions<AgentPrismTenancyOptions>>()?.Value;

        if (tenancyOptions is not { Enabled: true, AllowedTenants.Count: > 0 })
        {
            return null;
        }

        string? candidate;

        if (tenancyOptions.ClaimType is { Length: > 0 } claimType)
        {
            candidate = httpContext.User.Identity?.IsAuthenticated == true
                ? httpContext.User.FindFirst(claimType)?.Value
                : null;
        }
        else if (tenancyOptions.AllowHeaderResolution)
        {
            candidate = httpContext.Request.Headers[tenancyOptions.HeaderName].ToString();
        }
        else
        {
            candidate = null;
        }

        if (!HttpTenantContext.IsValidTenantId(candidate)
            || tenancyOptions.AllowedTenants.Contains(candidate, StringComparer.Ordinal))
        {
            return null;
        }

        return TypedResults.Problem(
            title: "Tenant rejected",
            detail: "The resolved tenant is not on the allow list. This request is NOT silently " +
                    "downgraded to the default tenant's data; it is rejected.",
            statusCode: StatusCodes.Status403Forbidden);
    }

    private static ProblemHttpResult Unauthorized(HttpContext httpContext)
    {
        httpContext.Response.Headers.WWWAuthenticate = "Bearer";

        return TypedResults.Problem(
            title: "Authentication failed",
            detail: "A valid 'Authorization: Bearer <token>' header is required.",
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
            title: "Tenant mismatch",
            detail: $"The '{tenancyOptions.HeaderName}' header CANNOT override the tenant the API " +
                    "key is bound to. Remove the header or give a value matching the key's tenant.",
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
            title: "Insufficient scope",
            detail: $"This endpoint requires the '{requirement.Scope}' scope; the key does not carry it.",
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
