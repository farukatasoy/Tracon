using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
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
internal static class DocumentChannelMessageBuilder
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

    /// <summary>What every block <see cref="Build"/> writes starts with: the marker, then the name line.</summary>
    private const string BlockHeader = BeginMarker + "\nName: ";

    /// <summary>What every block <see cref="Build"/> writes ends with.</summary>
    private const string BlockFooter = "\n" + EndMarker;

    /// <summary>The blank line between the name line and the content.</summary>
    private const string NameSeparator = "\n\n";

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

        // 🚨 The name is escaped exactly like the content (a
        // measurement: the first draft only escaped Content, leaving Name a
        // second, unguarded way to forge the end-of-document boundary before
        // the real content even starts).
        var text = string.Concat(
            BlockHeader, Escape(document.Name), NameSeparator,
            Escape(document.Content),
            BlockFooter);

        return CreateMessage(text, document.Name, document.Content);
    }

    /// <summary>
    /// Builds the document channel messages for reference text, the way an eval
    /// case's <c>Context</c> reaches the agent.
    /// </summary>
    /// <param name="text">The reference text.</param>
    /// <param name="plainTextName">The document name plain text is carried under.</param>
    /// <returns>
    /// One message per block when <paramref name="text"/> is made only of blocks
    /// <see cref="Build"/> wrote (what promoting a run with documents stores),
    /// each block's text kept verbatim; otherwise a single message that wraps
    /// <paramref name="text"/> under <paramref name="plainTextName"/>.
    /// </returns>
    /// <remarks>
    /// <para>
    /// Splitting is unambiguous for blocks <see cref="Build"/> wrote: a literal
    /// marker inside a name or a content was escaped there, so the only real
    /// end marker in a block is its own last line. Text that is anything else -
    /// a hand-written context, or blocks with extra text around them - is
    /// wrapped as one document instead, and its markers are escaped like any
    /// other content: it can never forge a boundary.
    /// </para>
    /// <para>
    /// A verbatim block's recorded name is read up to the block's first blank
    /// line, and its size and hash are those of the text after it. For a block
    /// <see cref="Build"/> wrote from a name with no blank line and a content
    /// with no literal marker, this is exactly the summary the original carried;
    /// otherwise only the run record's summary of that document differs, never
    /// the text the model receives.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<ChatMessage> BuildFromText(string text, string plainTextName)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(plainTextName);

        if (TrySplitBlocks(text, out var blocks))
        {
            return [.. blocks.Select(static block => CreateMessage(block.Text, block.Name, block.Content))];
        }

        return [Build(new AgentRunDocument { Name = plainTextName, Content = text })];
    }

    /// <summary>
    /// Reports whether a message is a document channel message: a single
    /// <see cref="TextContent"/> that <see cref="TryGetSummary"/> recognizes.
    /// </summary>
    /// <param name="message">The message to inspect.</param>
    /// <returns><see langword="true"/> for a document channel message.</returns>
    public static bool IsDocumentMessage(ChatMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);

        return message.Contents is [TextContent content] && TryGetSummary(content, out _);
    }

    /// <summary>Reports whether text is exactly one block <see cref="Build"/> wrote, and nothing else.</summary>
    /// <param name="text">The text to inspect.</param>
    /// <returns><see langword="true"/> when the text is one whole block.</returns>
    /// <remarks>
    /// For a reader that has the text but lost the marker: a content guard that
    /// rewrites a document's text builds a new <see cref="TextContent"/> without
    /// it. Never used to decide what a run's query is - anyone can type a
    /// block, and the run record must not let them hide a request that way.
    /// </remarks>
    public static bool IsSingleBlock(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        return TrySplitBlocks(text, out var blocks) && blocks is [var block] && block.Text.Length == text.Length;
    }

    /// <summary>The name a masked document carries when its block can no longer be read.</summary>
    public const string MaskedDocumentName = "[masked document]";

    /// <summary>
    /// Builds the content a content guard writes in place of a document it masked:
    /// the masked text, still marked as a document.
    /// </summary>
    /// <param name="maskedText">The document's text after the guard rewrote it.</param>
    /// <returns>A marked <see cref="TextContent"/>.</returns>
    /// <remarks>
    /// <para>
    /// The mark is rebuilt from the masked text, never copied from the original: the
    /// original carries the raw name and the hash of the raw content, and both would
    /// reach the run record (<c>DocumentAttached</c>) past the guard. When the masked
    /// text is still one whole block, its own name line and content give the mark;
    /// otherwise the document is named <see cref="MaskedDocumentName"/>.
    /// </para>
    /// <para>
    /// Without a mark the record took the document for the run's request and wrote no
    /// <c>DocumentAttached</c>.
    /// </para>
    /// </remarks>
    public static TextContent BuildMasked(string maskedText)
    {
        ArgumentNullException.ThrowIfNull(maskedText);

        var (name, content) = TrySplitBlocks(maskedText, out var blocks) && blocks is [var block] && block.Text.Length == maskedText.Length
            ? (block.Name, block.Content)
            : (MaskedDocumentName, maskedText);

        return (TextContent)CreateMessage(maskedText, name, content).Contents[0];
    }

    // Measured on SQLite 2026-10-12: a SQL IRunInputStore keeps
    // messages as JSON, and AdditionalProperties read back holds JsonElement,
    // not string/int. Accepting only the CLR types made every stored input that
    // a replay, a continuation or a promotion read back look like it carried no
    // document, and the run record took the document for the query.

    /// <summary>Reports whether an <see cref="AIContent"/> was produced by <see cref="Build"/>.</summary>
    /// <param name="content">The content to inspect.</param>
    /// <param name="summary">The document's name, size, and hash when this returns <see langword="true"/>.</param>
    /// <remarks>
    /// The marker is read both as written and after a JSON round trip, where
    /// its values come back as <see cref="JsonElement"/>: a SQL
    /// <see cref="IRunInputStore"/> stores messages as JSON, and a replay, a
    /// continuation or a promotion reads them back from there.
    /// </remarks>
    public static bool TryGetSummary(AIContent content, out DocumentAttachmentSummary summary)
    {
        ArgumentNullException.ThrowIfNull(content);

        if (content.AdditionalProperties is { } properties &&
            properties.TryGetValue(DocumentNameProperty, out var name) && ReadString(name) is { } documentName &&
            properties.TryGetValue(DocumentSizeBytesProperty, out var size) && ReadInt32(size) is { } sizeBytes &&
            properties.TryGetValue(DocumentSha256Property, out var hash) && ReadString(hash) is { } sha256)
        {
            summary = new DocumentAttachmentSummary(documentName, sizeBytes, sha256);
            return true;
        }

        summary = default;
        return false;
    }

    private static string? ReadString(object? value) => value switch
    {
        string text => text,
        JsonElement { ValueKind: JsonValueKind.String } element => element.GetString(),
        _ => null,
    };

    private static int? ReadInt32(object? value) => value switch
    {
        int number => number,
        JsonElement { ValueKind: JsonValueKind.Number } element when element.TryGetInt32(out var number) => number,
        _ => null,
    };

    /// <summary>Builds the message for a block's text, marked with its name, size and hash.</summary>
    private static ChatMessage CreateMessage(string text, string name, string content)
    {
        var contentBytes = Encoding.UTF8.GetBytes(content);
        var sha256 = Convert.ToHexString(SHA256.HashData(contentBytes)).ToLowerInvariant();

        var textContent = new TextContent(text)
        {
            AdditionalProperties = new AdditionalPropertiesDictionary
            {
                [DocumentNameProperty] = name,
                [DocumentSizeBytesProperty] = contentBytes.Length,
                [DocumentSha256Property] = sha256,
            },
        };

        return new ChatMessage(ChatRole.User, [textContent]);
    }

    /// <summary>
    /// Splits text made only of whole blocks, with nothing but whitespace
    /// between and around them, into its blocks; fails for anything else.
    /// </summary>
    private static bool TrySplitBlocks(string text, out List<DocumentBlock> blocks)
    {
        blocks = [];
        var position = SkipWhitespace(text, 0);

        while (position < text.Length)
        {
            if (!text.AsSpan(position).StartsWith(BlockHeader, StringComparison.Ordinal))
            {
                return false;
            }

            var nameStart = position + BlockHeader.Length;

            // Searched from the name on: an empty name and an empty content put
            // the footer's own line break right after the separator. The
            // escaped variants never match the real marker.
            var footerAt = text.IndexOf(BlockFooter, nameStart, StringComparison.Ordinal);

            if (footerAt < 0)
            {
                return false;
            }

            // The blank line must lie wholly before the footer, as Build writes
            // it; an empty content leaves the footer's line break third.
            var separatorAt = text.IndexOf(NameSeparator, nameStart, footerAt - nameStart, StringComparison.Ordinal);

            if (separatorAt < 0)
            {
                return false;
            }

            var blockEnd = footerAt + BlockFooter.Length;

            blocks.Add(new DocumentBlock(
                text[position..blockEnd],
                text[nameStart..separatorAt],
                text[(separatorAt + NameSeparator.Length)..footerAt]));

            position = SkipWhitespace(text, blockEnd);
        }

        return blocks.Count > 0;
    }

    private static int SkipWhitespace(string text, int position)
    {
        while (position < text.Length && char.IsWhiteSpace(text[position]))
        {
            position++;
        }

        return position;
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

/// <summary>One block of document channel text: its whole text, and the name and content read from it.</summary>
/// <param name="Text">The block's text, from its begin marker to its end marker.</param>
/// <param name="Name">The name line's value, as written (escaped).</param>
/// <param name="Content">The content between the name line and the end marker, as written (escaped).</param>
internal readonly record struct DocumentBlock(string Text, string Name, string Content);

/// <summary>A document attachment's identifying information, without its content.</summary>
/// <param name="Name">The document's name.</param>
/// <param name="SizeBytes">The document's UTF-8 byte size.</param>
/// <param name="Sha256">The document's SHA-256 hash, as lowercase hex.</param>
internal readonly record struct DocumentAttachmentSummary(string Name, int SizeBytes, string Sha256);
