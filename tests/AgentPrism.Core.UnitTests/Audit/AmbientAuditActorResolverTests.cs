using System.Security.Claims;
using Microsoft.Extensions.Options;

namespace AgentPrism.Core.UnitTests.Audit;

/// <summary>
/// <see cref="AmbientAuditActorResolver"/>'in <see cref="AuditActorContext"/>
/// uzerinden aktor cozumleme sirasi.
/// </summary>
public sealed class AmbientAuditActorResolverTests : IDisposable
{
    public AmbientAuditActorResolverTests() => AuditActorContext.Current = null;

    public void Dispose() => AuditActorContext.Current = null;

    [Fact]
    public void Baglam_bos_ise_null_doner()
    {
        var resolver = new AmbientAuditActorResolver(Options.Create(new AgentPrismOptions()));

        resolver.Resolve().ShouldBeNull();
    }

    [Fact]
    public void Kimlik_dogrulanmamis_kullanici_null_doner()
    {
        AuditActorContext.Current = new ClaimsPrincipal(
            new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "kullanici-1")]));

        var resolver = new AmbientAuditActorResolver(Options.Create(new AgentPrismOptions()));

        resolver.Resolve().ShouldBeNull();
    }

    [Fact]
    public void Varsayilan_sirada_NameIdentifier_oncelikli()
    {
        AuditActorContext.Current = Authenticated(
            new Claim(ClaimTypes.NameIdentifier, "kullanici-1"),
            new Claim(ClaimTypes.Name, "kullanici-adi"));

        var resolver = new AmbientAuditActorResolver(Options.Create(new AgentPrismOptions()));

        resolver.Resolve().ShouldBe("kullanici-1");
    }

    [Fact]
    public void NameIdentifier_yoksa_Name_kullanilir()
    {
        AuditActorContext.Current = Authenticated(new Claim(ClaimTypes.Name, "kullanici-adi"));

        var resolver = new AmbientAuditActorResolver(Options.Create(new AgentPrismOptions()));

        resolver.Resolve().ShouldBe("kullanici-adi");
    }

    [Fact]
    public void NameIdentifier_ve_Name_yoksa_sub_kullanilir()
    {
        AuditActorContext.Current = Authenticated(new Claim("sub", "sub-degeri"));

        var resolver = new AmbientAuditActorResolver(Options.Create(new AgentPrismOptions()));

        resolver.Resolve().ShouldBe("sub-degeri");
    }

    [Fact]
    public void Hicbir_claim_yoksa_null_doner()
    {
        AuditActorContext.Current = Authenticated();

        var resolver = new AmbientAuditActorResolver(Options.Create(new AgentPrismOptions()));

        resolver.Resolve().ShouldBeNull();
    }

    [Fact]
    public void Yapilandirilmis_claim_tipi_varsayilan_sirayi_gecersiz_kilar()
    {
        AuditActorContext.Current = Authenticated(
            new Claim(ClaimTypes.NameIdentifier, "kullanici-1"),
            new Claim("tenant_id", "ozel-deger"));

        var options = new AgentPrismOptions();
        options.Audit.ActorClaimType = "tenant_id";

        var resolver = new AmbientAuditActorResolver(Options.Create(options));

        resolver.Resolve().ShouldBe("ozel-deger");
    }

    private static ClaimsPrincipal Authenticated(params Claim[] claims)
        => new(new ClaimsIdentity(claims, authenticationType: "Test"));
}
