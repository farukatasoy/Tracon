using System.Security.Claims;
using Microsoft.Extensions.Options;

namespace Tracon.Core.UnitTests.Audit;

/// <summary>
/// <see cref="AmbientAuditActorResolver"/>'s actor resolution order via
/// <see cref="AuditActorContext"/>.
/// </summary>
public sealed class AmbientAuditActorResolverTests : IDisposable
{
    public AmbientAuditActorResolverTests() => AuditActorContext.Current = null;

    public void Dispose() => AuditActorContext.Current = null;

    [Fact]
    public void Returns_null_when_the_context_is_empty()
    {
        var resolver = new AmbientAuditActorResolver(Options.Create(new TraconOptions()));

        resolver.Resolve().ShouldBeNull();
    }

    [Fact]
    public void Returns_null_for_an_unauthenticated_user()
    {
        AuditActorContext.Current = new ClaimsPrincipal(
            new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "user-1")]));

        var resolver = new AmbientAuditActorResolver(Options.Create(new TraconOptions()));

        resolver.Resolve().ShouldBeNull();
    }

    [Fact]
    public void NameIdentifier_takes_priority_in_the_default_order()
    {
        AuditActorContext.Current = Authenticated(
            new Claim(ClaimTypes.NameIdentifier, "user-1"),
            new Claim(ClaimTypes.Name, "user-name"));

        var resolver = new AmbientAuditActorResolver(Options.Create(new TraconOptions()));

        resolver.Resolve().ShouldBe("user-1");
    }

    [Fact]
    public void Name_is_used_when_NameIdentifier_is_absent()
    {
        AuditActorContext.Current = Authenticated(new Claim(ClaimTypes.Name, "user-name"));

        var resolver = new AmbientAuditActorResolver(Options.Create(new TraconOptions()));

        resolver.Resolve().ShouldBe("user-name");
    }

    [Fact]
    public void Sub_is_used_when_NameIdentifier_and_Name_are_both_absent()
    {
        AuditActorContext.Current = Authenticated(new Claim("sub", "sub-value"));

        var resolver = new AmbientAuditActorResolver(Options.Create(new TraconOptions()));

        resolver.Resolve().ShouldBe("sub-value");
    }

    [Fact]
    public void Returns_null_when_no_claim_is_present()
    {
        AuditActorContext.Current = Authenticated();

        var resolver = new AmbientAuditActorResolver(Options.Create(new TraconOptions()));

        resolver.Resolve().ShouldBeNull();
    }

    [Fact]
    public void Configured_claim_type_overrides_the_default_order()
    {
        AuditActorContext.Current = Authenticated(
            new Claim(ClaimTypes.NameIdentifier, "user-1"),
            new Claim("tenant_id", "custom-value"));

        var options = new TraconOptions();
        options.Audit.ActorClaimType = "tenant_id";

        var resolver = new AmbientAuditActorResolver(Options.Create(options));

        resolver.Resolve().ShouldBe("custom-value");
    }

    private static ClaimsPrincipal Authenticated(params Claim[] claims)
        => new(new ClaimsIdentity(claims, authenticationType: "Test"));
}
