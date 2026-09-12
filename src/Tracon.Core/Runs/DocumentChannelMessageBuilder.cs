using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.AI;

namespace Tracon;

/// <summary>
/// Builds the <see cref="ChatMessage"/> a document is carried in and lets the
/// recording path tell such a message apart from ordinary instructions text.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Not a security guarantee.</strong> No provider gives a hard
/// guarantee that content wrapped this way is never treated as an
/// instruction; a capable-enough model can still be steered by content
/// inside a document. This is a convention and an audit trail: it keeps the
/// data channel visibly separate in the message list and in the run record.
/// </para>
/// <para>
/// A literal occurrence of either delimiter inside the document's own
/// content is defanged so it cannot be mistaken for a real boundary - the
/// document's content can never forge the end of the document.
/// </para>
/// </remarks>
public static class DocumentChannelMessageBuilder
{
    /// <summary>The <see cref="AIContent.AdditionalProperties"/> key carrying the document's name.</summary>
    public const string DocumentNameProperty = "tracon.documentName";

    /// <summary>The <see cref="AIContent.AdditionalProperties"/> key carrying the document's UTF-8 byte size.</summary>
    public const string DocumentSizeBytesProperty = "tracon.documentSizeBytes";

    /// <summary>The <see cref="AIContent.AdditionalProperties"/> key carrying the document's SHA-256 hash (lowercase hex).</summary>
    public const string DocumentSha256Property = "tracon.documentSha256";

    private const string BeginMarker = "-----BEGIN TRACON DOCUMENT-----";
    private const string EndMarker = "-----END TRACON DOCUMENT-----";
    private const string EscapedBeginMarker = "-----BEGIN TRACON DOCUMENT (escaped)-----";
    private const string EscapedEndMarker = "-----END TRACON DOCUMENT (escaped)-----";

    /// <summary>Builds the <see cref="ChatMessage"/> that carries a single document.</summary>
    /// <param name="document">The document to wrap.</param>
    /// <returns>
    /// A user-role message with a single <see cref="TextContent"/>, wrapped
    /// in a delimiter and marked through <see cref="AIContent.AdditionalProperties"/>.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="document"/> is <see langword="null"/>.</exception>
    public static ChatMessage Build(AgentRunDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var contentBytes = Encoding.UTF8.GetBytes(document.Content);
        var sizeBytes = contentBytes.Length;
        var sha256 = Convert.ToHexString(SHA256.HashData(contentBytes)).ToLowerInvariant();

        // 🚨 The name is escaped exactly like the content (K-320-style
        // measurement: the first draft only escaped Content, leaving Name a
        // second, unguarded way to forge the end-of-document boundary before
        // the real content even starts).
        var text = string.Concat(
            BeginMarker, "\nName: ", Escape(document.Name), "\n\n",
            Escape(document.Content),
            "\n", EndMarker);

        var content = new TextContent(text)
        {
            AdditionalProperties = new AdditionalPropertiesDictionary
            {
                [DocumentNameProperty] = document.Name,
                [DocumentSizeBytesProperty] = sizeBytes,
                [DocumentSha256Property] = sha256,
            },
        };

        return new ChatMessage(ChatRole.User, [content]);
    }

    /// <summary>Reports whether an <see cref="AIContent"/> was produced by <see cref="Build"/>.</summary>
    /// <param name="content">The content to inspect.</param>
    /// <param name="summary">The document's name, size, and hash when this returns <see langword="true"/>.</param>
    public static bool TryGetSummary(AIContent content, out DocumentAttachmentSummary summary)
    {
        ArgumentNullException.ThrowIfNull(content);

        if (content.AdditionalProperties is { } properties &&
            properties.TryGetValue(DocumentNameProperty, out var name) && name is string documentName &&
            properties.TryGetValue(DocumentSizeBytesProperty, out var size) && size is int sizeBytes &&
            properties.TryGetValue(DocumentSha256Property, out var hash) && hash is string sha256)
        {
            summary = new DocumentAttachmentSummary(documentName, sizeBytes, sha256);
            return true;
        }

        summary = default;
        return false;
    }

    /// <summary>
    /// Defangs a literal occurrence of either delimiter inside caller-supplied
    /// text (the document's name or its content) so it can never be mistaken
    /// for the real boundary.
    /// </summary>
    private static string Escape(string text)
        => text
            .Replace(BeginMarker, EscapedBeginMarker, StringComparison.Ordinal)
            .Replace(EndMarker, EscapedEndMarker, StringComparison.Ordinal);
}

/// <summary>A document attachment's identifying information, without its content.</summary>
/// <param name="Name">The document's name.</param>
/// <param name="SizeBytes">The document's UTF-8 byte size.</param>
/// <param name="Sha256">The document's SHA-256 hash, as lowercase hex.</param>
public readonly record struct DocumentAttachmentSummary(string Name, int SizeBytes, string Sha256);
