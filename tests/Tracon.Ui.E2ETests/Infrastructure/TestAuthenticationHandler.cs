using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Tracon.Ui.E2ETests.Infrastructure;

/// <summary>
/// A test scheme that treats every request as authenticated.
/// </summary>
/// <remarks>
/// Needed to test role-based button hiding in a real browser: an
/// authorization policy failing sets the role field in the <c>/api/meta</c>
/// response to <see langword="false"/>, and the UI hides buttons accordingly.
/// Same pattern as <c>Tracon.AspNetCore.FunctionalTests</c>.
/// </remarks>
internal sealed class TestAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    /// <summary>The name of the test scheme.</summary>
    public const string SchemeName = "Test";

    /// <summary>Registers the test scheme.</summary>
    /// <param name="services">Service collection.</param>
    /// <returns>The chain continuation.</returns>
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
        var identity = new ClaimsIdentity([new Claim(ClaimTypes.Name, "test-user")], SchemeName);
        var principal = new ClaimsPrincipal(identity);

        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName)));
    }
}
