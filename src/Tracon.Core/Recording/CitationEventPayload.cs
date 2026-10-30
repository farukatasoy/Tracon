using System.Text.Json;
using Microsoft.Extensions.AI;

namespace Tracon;

/// <summary>
/// Builds the <see cref="RunEventCustomTypes.Citation"/> event for one <see cref="CitationAnnotation"/>
/// the model attached to its response.
/// </summary>
/// <remarks>
/// <para>
/// The payload is written by hand, the same way as <see cref="ContentEventPayload"/>: a fixed set of
/// keys, each one present only when the provider gave it. Two keys hold free text from the source,
/// <c>title</c> and <c>snippet</c>, and both are cut so the JSON stays whole.
/// </para>
/// <para>
/// The address is the one field that can hold a credential: a retrieval tool's citation can point
/// at a signed address whose path or query is the credential. By default only the scheme and host are
/// written; the whole address is a consumer's choice (<c>RecordCitationUrls</c>). User information
/// (<c>user:password@</c>) is never written, in either mode.
/// </para>
/// <para>
/// <c>Text</c> carries no title: a document name can be personal data, and <c>Text</c> survives
/// <c>RecordToolPayloads</c> being off while the payload does not.
/// </para>
/// </remarks>
internal static class CitationEventPayload
{
    /// <summary>The longest address written whole; a longer one is written as its scheme and host.</summary>
    private const int MaxUrlLength = ContentEventPayload.MaxTextLength;

    /// <summary>Describes one citation as a reserved <c>Custom</c> event draft.</summary>
    /// <param name="citation">The citation.</param>
    /// <param name="recordUrl">Whether the whole address is written instead of its scheme and host.</param>
    /// <param name="maxPayloadLength">
    /// The writer's <c>MaxPayloadLength</c>, or zero or less for none. The snippet and then the title are cut
    /// so the whole payload fits it: the writer cuts a payload without regard to its shape, and a cut JSON
    /// document cannot be parsed.
    /// </param>
    /// <returns>The draft to append with the writer's reserved path.</returns>
    public static RunEventDraft Describe(CitationAnnotation citation, bool recordUrl, int maxPayloadLength = 0)
    {
        ArgumentNullException.ThrowIfNull(citation);

        var url = DescribeUrl(citation.Url, recordUrl);
        var (start, end) = SpanOf(citation);
        var title = ContentEventPayload.Sanitize(citation.Title);
        var snippet = ContentEventPayload.Sanitize(citation.Snippet);
        var titleLength = Math.Min(title?.Length ?? 0, ContentEventPayload.MaxTextLength);
        var snippetLength = Math.Min(snippet?.Length ?? 0, ContentEventPayload.MaxTextLength);

        string Build() => ContentEventPayload.Build(writer =>
        {
            ContentEventPayload.WriteString(writer, "type", "citation");
            WriteIfPresent(writer, "title", ContentEventPayload.Cut(title, titleLength));
            WriteIfPresent(writer, "url", url);
            WriteIfPresent(writer, "fileId", citation.FileId);
            WriteIfPresent(writer, "toolName", citation.ToolName);
            WriteIfPresent(writer, "snippet", ContentEventPayload.Cut(snippet, snippetLength));

            if (start is { } startIndex)
            {
                writer.WriteNumber("start", startIndex);
            }

            if (end is { } endIndex)
            {
                writer.WriteNumber("end", endIndex);
            }
        });

        var payload = Build();

        // Every dropped character is at least one character of escaped JSON, so removing as many as the
        // payload is over its limit is enough; a character outside ASCII costs up to six, which is why the
        // loop measures again. The snippet goes first: the title says what was cited.
        while (maxPayloadLength > 0 && payload.Length > maxPayloadLength && (snippetLength > 0 || titleLength > 0))
        {
            var over = payload.Length - maxPayloadLength;

            if (snippetLength > 0)
            {
                snippetLength = Math.Max(0, snippetLength - over);
            }
            else
            {
                titleLength = Math.Max(0, titleLength - over);
            }

            payload = Build();
        }

        var toolName = ContentEventPayload.Sanitize(citation.ToolName);

        return new RunEventDraft(RunEventType.Custom)
        {
            CustomType = RunEventCustomTypes.Citation,
            Text = string.IsNullOrEmpty(toolName) ? "citation" : $"citation: {toolName}",
            Payload = payload,
        };
    }

    /// <summary>Reduces a citation's address to what may be written.</summary>
    /// <param name="url">The address the provider gave, or <see langword="null"/>.</param>
    /// <param name="recordUrl">Whether the whole address may be written.</param>
    /// <returns>
    /// The scheme and host (and a non-default port), or the whole address without user information, or
    /// <see langword="null"/> for an address that is not an absolute <c>http</c> or <c>https</c> one.
    /// </returns>
    internal static string? DescribeUrl(Uri? url, bool recordUrl)
    {
        if (url is null ||
            !url.IsAbsoluteUri ||
            !(string.Equals(url.Scheme, Uri.UriSchemeHttp, StringComparison.Ordinal) ||
              string.Equals(url.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal)))
        {
            return null;
        }

        // 🚨 `GetLeftPart(Authority)` would keep the user information, which is a secret (K-059); the
        // components below never hold it.
        var origin = url.GetComponents(UriComponents.SchemeAndServer, UriFormat.UriEscaped);

        if (!recordUrl)
        {
            return origin;
        }

        var whole = url.GetComponents(UriComponents.AbsoluteUri & ~UriComponents.UserInfo, UriFormat.UriEscaped);

        return whole.Length <= MaxUrlLength ? whole : origin;
    }

    /// <summary>Reads the character span of the first text region a citation names.</summary>
    /// <param name="citation">The citation.</param>
    /// <returns>The start and end of the span; a part the provider left out is <see langword="null"/>.</returns>
    internal static (int? Start, int? End) SpanOf(CitationAnnotation citation)
    {
        if (citation.AnnotatedRegions is { } regions)
        {
            foreach (var region in regions)
            {
                if (region is TextSpanAnnotatedRegion span)
                {
                    return (span.StartIndex, span.EndIndex);
                }
            }
        }

        return (null, null);
    }

    private static void WriteIfPresent(Utf8JsonWriter writer, string name, string? value)
    {
        if (!string.IsNullOrEmpty(value))
        {
            ContentEventPayload.WriteString(writer, name, value);
        }
    }
}
