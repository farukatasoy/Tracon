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

    [Fact]
    public void A_reference_whose_properties_came_back_as_json_resolves_to_a_named_file_with_plain_text_properties()
    {
        // What the session store hands back: text properties are JSON elements, not the strings they were written as.
        var reference = new Microsoft.Extensions.AI.UriContent(
            AttachmentUriReference.Create("/tracon", TraconId.NewId()),
            "image/png")
        {
            AdditionalProperties = new()
            {
                [AttachmentUriReference.NameProperty] = System.Text.Json.JsonSerializer.SerializeToElement("photo.png"),
                ["detail"] = System.Text.Json.JsonSerializer.SerializeToElement("low"),
                ["count"] = System.Text.Json.JsonSerializer.SerializeToElement(3),
            },
        };

        var resolved = AttachmentUriReference.Resolve(reference, [1, 2, 3]);

        resolved.Name.ShouldBe("photo.png");
        resolved.AdditionalProperties.ShouldNotBeNull().ContainsKey(AttachmentUriReference.NameProperty).ShouldBeFalse();
        resolved.AdditionalProperties["detail"].ShouldBe("low");
        resolved.AdditionalProperties["count"].ShouldBeOfType<System.Text.Json.JsonElement>("a value that is not text is left as it came");
    }
}
