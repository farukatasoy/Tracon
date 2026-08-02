using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AgentPrism.AspNetCore.FunctionalTests.Infrastructure;

/// <summary>
/// Her istegi kimligi dogrulanmis sayan test semasi.
/// </summary>
/// <remarks>
/// Authorization policy testlerinin gercek uretim yolunu izlemesi icin gereklidir:
/// kimlik dogrulanmamis bir istekte basarisiz policy <c>401</c> ve bir challenge
/// uretir; kimlik dogrulanmis bir istekte ise <c>403</c> doner. Ikincisi
/// "policy calisti ve reddetti" durumunu net bicimde gosterir.
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
