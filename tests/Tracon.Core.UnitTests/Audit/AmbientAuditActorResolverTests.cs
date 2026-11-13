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
    public void A_name_over_the_column_width_is_shortened_to_it_with_a_digest_and_stays_the_same_name()
    {
        // The actor is written to ten narrow columns; a claim of an identity provider has no bound of its own.
        var claim = "https://login.example.com/" + new string('u', 400);
        AuditActorContext.Current = Authenticated(new Claim(ClaimTypes.NameIdentifier, claim));

        var resolver = new AmbientAuditActorResolver(Options.Create(new TraconOptions()));

        var actor = resolver.Resolve()!;

        AmbientAuditActorResolver.MaxActorLength.ShouldBe(200);
        actor.Length.ShouldBe(AmbientAuditActorResolver.MaxActorLength);
        actor.ShouldStartWith("https://login.example.com/uuuu");
        resolver.Resolve().ShouldBe(actor, "the same caller is the same actor every time");
    }

    [Fact]
    public void Two_long_names_that_share_their_start_are_two_actors()
    {
        var start = new string('s', 300);

        AuditActorContext.Current = Authenticated(new Claim(ClaimTypes.NameIdentifier, start + "-a"));
        var resolver = new AmbientAuditActorResolver(Options.Create(new TraconOptions()));
        var first = resolver.Resolve();

        AuditActorContext.Current = Authenticated(new Claim(ClaimTypes.NameIdentifier, start + "-b"));
        var second = resolver.Resolve();

        string.Equals(first, second, StringComparison.Ordinal).ShouldBeFalse();
    }

    [Fact]
    public void A_name_that_fits_is_the_name_unchanged_on_every_path_of_the_resolver()
    {
        var exact = new string('e', AmbientAuditActorResolver.MaxActorLength);

        AuditActorContext.Current = Authenticated(new Claim(ClaimTypes.NameIdentifier, exact));
        new AmbientAuditActorResolver(Options.Create(new TraconOptions())).Resolve().ShouldBeSameAs(exact);

        var options = new TraconOptions();
        options.Audit.ActorClaimType = "email";
        AuditActorContext.Current = Authenticated(new Claim("email", exact));
        new AmbientAuditActorResolver(Options.Create(options)).Resolve().ShouldBeSameAs(exact);
    }

    [Fact]
    public void A_configured_claim_over_the_column_width_is_shortened_too()
    {
        var options = new TraconOptions();
        options.Audit.ActorClaimType = "email";
        AuditActorContext.Current = Authenticated(new Claim("email", new string('m', 500) + "@example.com"));

        new AmbientAuditActorResolver(Options.Create(options)).Resolve()!.Length.ShouldBe(AmbientAuditActorResolver.MaxActorLength);
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
