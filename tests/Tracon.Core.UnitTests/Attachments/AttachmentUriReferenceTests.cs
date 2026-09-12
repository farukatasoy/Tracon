namespace Tracon.Core.UnitTests.Attachments;

/// <summary>
/// Construction and resolution of the attachment reference Uri.
/// </summary>
public sealed class AttachmentUriReferenceTests
{
    [Fact]
    public void Constructed_reference_resolves_back()
    {
        var id = TraconId.NewId();

        var uri = AttachmentUriReference.Create("/tracon", id);

        AttachmentUriReference.TryParse(uri, out var parsed).ShouldBeTrue();
        parsed.ShouldBe(id);
    }

    [Fact]
    public void Absolute_uri_also_resolves()
    {
        var id = TraconId.NewId();

        var absolute = new Uri($"https://example.com/tracon/api/attachments/{id}");

        AttachmentUriReference.TryParse(absolute, out var parsed).ShouldBeTrue();
        parsed.ShouldBe(id);
    }

    [Fact]
    public void Reference_built_with_a_different_prefix_also_resolves()
    {
        // Resolution does NOT KNOW the prefix; it only looks for the
        // '/api/attachments/{id}' trail. This keeps Core independent of the
        // endpoints' path configuration.
        var id = TraconId.NewId();

        var uri = AttachmentUriReference.Create("/different-prefix", id);

        AttachmentUriReference.TryParse(uri, out var parsed).ShouldBeTrue();
        parsed.ShouldBe(id);
    }

    [Fact]
    public void Unrelated_uri_does_not_resolve()
    {
        AttachmentUriReference.TryParse(new Uri("https://example.com/other/path"), out _).ShouldBeFalse();
    }

    [Fact]
    public void Invalid_id_does_not_resolve()
    {
        AttachmentUriReference.TryParse(
            new Uri("/tracon/api/attachments/not-a-guid", UriKind.Relative),
            out _).ShouldBeFalse();
    }
}
