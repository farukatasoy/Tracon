using System.Text.Json;
using Microsoft.Extensions.AI;

namespace Tracon.Core.UnitTests.Recording;

/// <summary>
/// The <see cref="RunEventCustomTypes.Citation"/> draft built for one citation.
/// The payload is written by hand, valid JSON whatever the source carries, and the address in it
/// is the one field that can hold a credential.
/// </summary>
public sealed class CitationEventPayloadTests
{
    private static CitationAnnotation Citation(string? url) => new()
    {
        Title = "Release notes",
        Url = url is null ? null : new Uri(url, UriKind.RelativeOrAbsolute),
        ToolName = "web_search",
        Snippet = "Ships on Friday.",
        AnnotatedRegions = [new TextSpanAnnotatedRegion { StartIndex = 4, EndIndex = 20 }],
    };

    [Fact]
    public void A_citation_is_a_reserved_custom_event_with_every_key_the_provider_gave()
    {
        var draft = CitationEventPayload.Describe(Citation("https://example.com/notes"), recordUrl: true);

        draft.Type.ShouldBe(RunEventType.Custom);
        draft.CustomType.ShouldBe(RunEventCustomTypes.Citation);

        using var payload = JsonDocument.Parse(draft.Payload!);
        var root = payload.RootElement;
        root.GetProperty("type").GetString().ShouldBe("citation");
        root.GetProperty("title").GetString().ShouldBe("Release notes");
        root.GetProperty("url").GetString().ShouldBe("https://example.com/notes");
        root.GetProperty("toolName").GetString().ShouldBe("web_search");
        root.GetProperty("snippet").GetString().ShouldBe("Ships on Friday.");
        root.GetProperty("start").GetInt32().ShouldBe(4);
        root.GetProperty("end").GetInt32().ShouldBe(20);
        root.TryGetProperty("fileId", out _).ShouldBeFalse("the provider gave no file id");
    }

    [Fact]
    public void A_key_the_provider_left_out_is_not_in_the_payload()
    {
        var draft = CitationEventPayload.Describe(new CitationAnnotation { FileId = "file-1" }, recordUrl: false);

        using var payload = JsonDocument.Parse(draft.Payload!);
        payload.RootElement.EnumerateObject().Select(static p => p.Name).ShouldBe(["type", "fileId"]);
    }

    [Fact]
    public void The_text_names_the_tool_and_never_the_title()
    {
        CitationEventPayload.Describe(Citation("https://example.com/"), recordUrl: false).Text.ShouldBe("citation: web_search");
        CitationEventPayload.Describe(new CitationAnnotation { Title = "Salary review.pdf" }, recordUrl: false).Text.ShouldBe("citation");
    }

    [Theory]
    [InlineData("https://example.com/notes?sig=SECRET#top", "https://example.com")]
    [InlineData("https://example.com:8443/a/b?c=d", "https://example.com:8443")]
    [InlineData("http://example.com:80/a", "http://example.com")]
    [InlineData("https://example.com:443/a", "https://example.com")]
    [InlineData("https://[2001:db8::1]:8443/a?b=c", "https://[2001:db8::1]:8443")]
    public void By_default_only_the_scheme_and_host_are_written(string url, string expected)
    {
        var draft = CitationEventPayload.Describe(Citation(url), recordUrl: false);

        var json = draft.Payload!;

        UrlOf(draft).ShouldBe(expected);
        json.ShouldNotContain("SECRET");
        json.ShouldNotContain("/notes");
    }

    [Theory]
    [InlineData("https://example.com/notes?sig=abc&x=1#top", "https://example.com/notes?sig=abc&x=1#top")]
    [InlineData("https://example.com:8443/a", "https://example.com:8443/a")]
    [InlineData("https://example.com:443/a", "https://example.com/a")]
    public void With_the_option_on_the_whole_address_is_written(string url, string expected)
        => UrlOf(CitationEventPayload.Describe(Citation(url), recordUrl: true)).ShouldBe(expected);

    [Theory]
    [InlineData(false, "https://example.com")]
    [InlineData(true, "https://example.com/private?sig=abc")]
    public void User_information_is_never_written_in_either_mode(bool recordUrl, string expected)
    {
        // 🚨 `GetLeftPart(Authority)` keeps `user:password@`; it is a secret.
        var draft = CitationEventPayload.Describe(Citation("https://alice:hunter2@example.com/private?sig=abc"), recordUrl);

        var json = draft.Payload!;

        UrlOf(draft).ShouldBe(expected);
        json.ShouldNotContain("alice");
        json.ShouldNotContain("hunter2");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void An_address_that_is_not_an_absolute_http_address_is_not_written_and_the_event_still_is(bool recordUrl)
    {
        foreach (var url in new[] { "/relative/path", "javascript:alert(1)", "ftp://example.com/a", "file:///etc/passwd", "mailto:a@example.com", "data:text/plain,hi" })
        {
            var draft = CitationEventPayload.Describe(Citation(url), recordUrl);

            using var payload = JsonDocument.Parse(draft.Payload!);
            payload.RootElement.TryGetProperty("url", out _).ShouldBeFalse($"'{url}' must not be written");
            payload.RootElement.GetProperty("title").GetString().ShouldBe("Release notes");
            draft.CustomType.ShouldBe(RunEventCustomTypes.Citation);
        }
    }

    [Fact]
    public void A_whole_address_over_the_limit_is_written_as_its_scheme_and_host()
    {
        var url = "https://example.com/" + new string('a', 3000);

        UrlOf(CitationEventPayload.Describe(Citation(url), recordUrl: true)).ShouldBe("https://example.com");
    }

    [Fact]
    public void A_long_title_and_snippet_are_cut_and_the_payload_is_still_json()
    {
        var citation = new CitationAnnotation { Title = new string('t', 5000), Snippet = new string('s', 5000) };

        var draft = CitationEventPayload.Describe(citation, recordUrl: false);

        using var payload = JsonDocument.Parse(draft.Payload!);
        payload.RootElement.GetProperty("title").GetString()!.ShouldEndWith(RunEventWriter.TruncationMarker);
        payload.RootElement.GetProperty("title").GetString()!.Length.ShouldBeLessThan(2100);
        payload.RootElement.GetProperty("snippet").GetString()!.Length.ShouldBeLessThan(2100);
    }

    [Theory]
    [InlineData(300)]
    [InlineData(1000)]
    [InlineData(4000)]
    public void The_payload_fits_the_writers_limit_without_being_cut_by_it(int limit)
    {
        // The writer cuts a payload by characters without regard to its shape; a cut document cannot
        // be parsed. Characters outside ASCII cost up to six characters of escaped JSON each.
        var citation = new CitationAnnotation
        {
            Title = new string('é', 2000),
            Snippet = string.Concat(Enumerable.Repeat("😀", 1000)),
            Url = new Uri("https://example.com/a"),
            ToolName = "web_search",
        };

        var draft = CitationEventPayload.Describe(citation, recordUrl: true, maxPayloadLength: limit);

        draft.Payload!.Length.ShouldBeLessThanOrEqualTo(limit);
        JsonDocument.Parse(draft.Payload).Dispose();
    }

    [Fact]
    public void The_snippet_is_cut_before_the_title()
    {
        var citation = new CitationAnnotation { Title = "Release notes", Snippet = new string('s', 2000) };

        var draft = CitationEventPayload.Describe(citation, recordUrl: false, maxPayloadLength: 300);

        using var payload = JsonDocument.Parse(draft.Payload!);
        payload.RootElement.GetProperty("title").GetString().ShouldBe("Release notes");
        payload.RootElement.GetProperty("snippet").GetString()!.ShouldEndWith(RunEventWriter.TruncationMarker);
    }

    [Fact]
    public void A_cut_never_separates_the_halves_of_a_surrogate_pair()
    {
        var citation = new CitationAnnotation { Snippet = string.Concat(Enumerable.Repeat("😀", 1500)) };

        var draft = CitationEventPayload.Describe(citation, recordUrl: false);

        using var payload = JsonDocument.Parse(draft.Payload!);
        var snippet = payload.RootElement.GetProperty("snippet").GetString()!;

        snippet.ShouldEndWith(RunEventWriter.TruncationMarker);
        var kept = snippet[..^RunEventWriter.TruncationMarker.Length];
        (kept.Length % 2).ShouldBe(0, "the text is whole pairs, so a cut inside a pair leaves an odd length");
        char.IsHighSurrogate(kept[^1]).ShouldBeFalse("a high surrogate at the cut would be a lone half");
    }

    [Fact]
    public void A_lone_surrogate_from_the_source_becomes_the_replacement_character()
    {
        var draft = CitationEventPayload.Describe(new CitationAnnotation { Title = "bad \ud83d title" }, recordUrl: false);

        using var payload = JsonDocument.Parse(draft.Payload!);
        payload.RootElement.GetProperty("title").GetString().ShouldBe("bad � title");
    }

    [Fact]
    public void A_region_that_is_not_a_text_span_gives_no_span()
    {
        var citation = new CitationAnnotation { Title = "t", AnnotatedRegions = [new AnnotatedRegion()] };

        using var payload = JsonDocument.Parse(CitationEventPayload.Describe(citation, recordUrl: false).Payload!);

        payload.RootElement.TryGetProperty("start", out _).ShouldBeFalse();
        payload.RootElement.TryGetProperty("end", out _).ShouldBeFalse();
    }

    [Fact]
    public void A_span_with_one_end_missing_writes_only_the_end_the_provider_gave()
    {
        var citation = new CitationAnnotation { AnnotatedRegions = [new TextSpanAnnotatedRegion { StartIndex = 7 }] };

        using var payload = JsonDocument.Parse(CitationEventPayload.Describe(citation, recordUrl: false).Payload!);

        payload.RootElement.GetProperty("start").GetInt32().ShouldBe(7);
        payload.RootElement.TryGetProperty("end", out _).ShouldBeFalse();
    }

    private static string? UrlOf(RunEventDraft draft)
    {
        using var payload = JsonDocument.Parse(draft.Payload!);

        return payload.RootElement.TryGetProperty("url", out var url) ? url.GetString() : null;
    }
}
