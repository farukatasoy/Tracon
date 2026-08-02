namespace AgentPrism.Core.UnitTests.Attachments;

/// <summary>
/// Ek referans Uri'sinin kurulmasi ve geri cozulmesi.
/// </summary>
public sealed class AttachmentUriReferenceTests
{
    [Fact]
    public void Kurulan_referans_geri_cozulur()
    {
        var id = AgentPrismId.NewId();

        var uri = AttachmentUriReference.Create("/agentprism", id);

        AttachmentUriReference.TryParse(uri, out var parsed).ShouldBeTrue();
        parsed.ShouldBe(id);
    }

    [Fact]
    public void Mutlak_uri_de_cozulur()
    {
        var id = AgentPrismId.NewId();

        var absolute = new Uri($"https://example.com/agentprism/api/attachments/{id}");

        AttachmentUriReference.TryParse(absolute, out var parsed).ShouldBeTrue();
        parsed.ShouldBe(id);
    }

    [Fact]
    public void Farkli_onek_ile_kurulan_referans_da_cozulur()
    {
        // Cozumleme onegi BILMEZ; yalniz '/api/attachments/{id}' izine bakar.
        // Boylece Core, uc noktalarin yol yapilandirmasindan bagimsiz kalir.
        var id = AgentPrismId.NewId();

        var uri = AttachmentUriReference.Create("/farkli-onek", id);

        AttachmentUriReference.TryParse(uri, out var parsed).ShouldBeTrue();
        parsed.ShouldBe(id);
    }

    [Fact]
    public void Ilgisiz_uri_cozulmez()
    {
        AttachmentUriReference.TryParse(new Uri("https://example.com/baska/yol"), out _).ShouldBeFalse();
    }

    [Fact]
    public void Gecersiz_kimlik_cozulmez()
    {
        AttachmentUriReference.TryParse(
            new Uri("/agentprism/api/attachments/not-a-guid", UriKind.Relative),
            out _).ShouldBeFalse();
    }
}
