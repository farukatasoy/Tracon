using Microsoft.AspNetCore.Http;

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
/// </remarks>
internal sealed class AgentPrismEndpointFilter : IEndpointFilter
{
    private readonly bool _allowRemoteAccess;
    private readonly string? _authToken;

    /// <summary>Ayarlardan bir filtre kurar.</summary>
    /// <param name="options">Erisim ayarlari.</param>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> <see langword="null"/> ise.</exception>
    public AgentPrismEndpointFilter(AgentPrismEndpointOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        // Ayarlar kurulum aninda okunur. MapAgentPrism'den sonra yapilan bir
        // degisiklik calisan uclari etkilememelidir; erisim kurallarinin calisma
        // aninda sessizce gevsemesi guvenlik acisindan kabul edilemez.
        _allowRemoteAccess = options.AllowRemoteAccess;
        _authToken = options.AuthToken;
    }

    /// <inheritdoc />
    public ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        var httpContext = context.HttpContext;

        if (!_allowRemoteAccess && !LoopbackGuard.IsLocal(httpContext.Connection.RemoteIpAddress))
        {
            return new ValueTask<object?>(Results.Problem(
                title: "Uzak erisim kapali",
                detail: "AgentPrism uclari varsayilan olarak yalnizca ayni makineden erisilebilir. " +
                        "Uzak erisim icin AllowRemoteAccess ayarini acin ve bir kimlik dogrulama " +
                        "yontemi (AuthToken veya RequireAuthorization) yapilandirin.",
                statusCode: StatusCodes.Status403Forbidden));
        }

        if (_authToken is { Length: > 0 } expected)
        {
            var header = httpContext.Request.Headers.Authorization.ToString();

            if (!BearerTokenValidator.IsValid(header, expected))
            {
                httpContext.Response.Headers.WWWAuthenticate = "Bearer";

                return new ValueTask<object?>(Results.Problem(
                    title: "Kimlik dogrulanamadi",
                    detail: "Gecerli bir 'Authorization: Bearer <token>' basligi gerekiyor.",
                    statusCode: StatusCodes.Status401Unauthorized));
            }
        }

        return next(context);
    }
}
