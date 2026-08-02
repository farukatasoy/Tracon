using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AgentPrism.Ui.E2ETests.Infrastructure;

/// <summary>
/// Her istegi kimligi dogrulanmis sayan test semasi.
/// </summary>
/// <remarks>
/// Rol tabanli dugme gizlemeyi gercek bir tarayicida test etmek icin gerekli:
/// bir authorization policy'nin basarisiz olmasi <c>/api/meta</c> yanitindaki
/// rol alanini <see langword="false"/> yapar ve arayuz buna gore dugmeleri
/// gizler. Deseni <c>AgentPrism.AspNetCore.FunctionalTests</c> ile aynidir.
/// </remarks>
internal sealed class TestAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    /// <summary>Test semasinin adi.</summary>
    public const string SchemeName = "Test";

    /// <summary>Test semasini kaydeder.</summary>
    /// <param name="services">Servis koleksiyonu.</param>
    /// <returns>Zincirin devami.</returns>
    public static IServiceCollection Add(IServiceCollection services)
    {
        services
            .AddAuthentication(SchemeName)
            .AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>(SchemeName, configureOptions: null);

        return services;
    }

    /// <inheritdoc />
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var identity = new ClaimsIdentity([new Claim(ClaimTypes.Name, "test-kullanici")], SchemeName);
        var principal = new ClaimsPrincipal(identity);

        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName)));
    }
}
