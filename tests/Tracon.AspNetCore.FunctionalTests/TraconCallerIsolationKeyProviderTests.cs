using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// How <see cref="TraconCallerIsolationKeyProvider"/> names the caller
/// when Tracon's endpoint filter did not run.
/// </summary>
/// <remarks>
/// <para>
/// The filter is what puts the signed-in user into the ambient audit actor. An
/// application that maps its own Microsoft Agent Framework A2A endpoint beside
/// <c>UseA2A()</c> gets the provider too (it is registered once for the whole
/// container) but never passes through that filter, so the ambient actor is empty.
/// The provider then reads the request's own user by the same rules; otherwise
/// every signed-in caller of that endpoint would be refused.
/// </para>
/// </remarks>
public sealed class TraconCallerIsolationKeyProviderTests
{
    [Fact]
    public async Task A_signed_in_user_is_named_from_the_request_when_no_ambient_actor_was_set()
    {
        var provider = Create(Signed(new Claim(ClaimTypes.NameIdentifier, "ada")));

        (await provider.GetIsolationKeyAsync(TestContext.Current.CancellationToken)).ShouldBe("user:ada");
    }

    [Fact]
    public async Task The_configured_actor_claim_applies_to_the_request_user_too()
    {
        var user = Signed(new Claim(ClaimTypes.NameIdentifier, "ada"), new Claim("employee_id", "e-7"));

        (await Create(user, actorClaimType: "employee_id").GetIsolationKeyAsync(TestContext.Current.CancellationToken))
            .ShouldBe("user:e-7");
        (await Create(user, actorClaimType: "missing-claim").GetIsolationKeyAsync(TestContext.Current.CancellationToken))
            .ShouldBeNull("a signed-in user with no readable actor is refused, not moved to the anonymous key");
    }

    [Fact]
    public async Task A_request_without_a_user_is_the_anonymous_caller()
        => (await Create(new ClaimsPrincipal(new ClaimsIdentity())).GetIsolationKeyAsync(TestContext.Current.CancellationToken))
            .ShouldBe("anonymous");

    [Fact]
    public async Task Outside_a_request_there_is_no_caller()
    {
        var provider = new TraconCallerIsolationKeyProvider(
            new HttpContextAccessor(),
            new AmbientAuditActorResolver(Options.Create(new TraconOptions())),
            Options.Create(new TraconOptions()));

        (await provider.GetIsolationKeyAsync(TestContext.Current.CancellationToken)).ShouldBeNull();
    }

    private static ClaimsPrincipal Signed(params Claim[] claims)
        => new(new ClaimsIdentity(claims, authenticationType: "Test"));

    private static TraconCallerIsolationKeyProvider Create(ClaimsPrincipal user, string? actorClaimType = null)
    {
        var options = new TraconOptions();
        options.Audit.ActorClaimType = actorClaimType;

        return new TraconCallerIsolationKeyProvider(
            new HttpContextAccessor { HttpContext = new DefaultHttpContext { User = user } },
            new AmbientAuditActorResolver(Options.Create(options)),
            Options.Create(options));
    }
}
