using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Tracon.AspNetCore.FunctionalTests.Infrastructure;

/// <summary>
/// A test authentication scheme that treats every request as authenticated.
/// </summary>
/// <remarks>
/// This is needed so authorization policy tests follow the real production
/// path: a failing policy on an unauthenticated request produces a <c>401</c>
/// and a challenge; on an authenticated request it returns <c>403</c>. The
/// latter clearly shows the "policy ran and denied" case.
/// </remarks>
internal sealed class TestAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    /// <summary>The name of the test scheme.</summary>
    public const string SchemeName = "Test";

    /// <summary>Registers the test scheme.</summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection, for chaining.</returns>
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
