using System.Globalization;

namespace Tracon;

/// <summary>Builds the file name of an attachment Tracon writes itself.</summary>
/// <remarks>
/// <para>
/// The extension comes from the type <see cref="AttachmentTypeGuard"/> DETECTED,
/// never from a fixed string or the provider's claim. Four write sites once had
/// their own rule: the speech endpoint wrote <c>.mp3</c> for OGG and WAV audio,
/// and the conversation driver wrote <c>.bin</c> for everything.
/// A downloaded file then opened in the wrong program or in none.
/// </para>
/// <para>
/// <c>internal</c>: <c>Tracon.Voice</c> and <c>Tracon.AspNetCore</c> reach it
/// through <c>InternalsVisibleTo</c>. It is not a consumer seam.
/// </para>
/// </remarks>
internal static class AttachmentFileNames
{
    /// <summary>Builds a file name such as <c>speech-20261012-143005-1234567.ogg</c>.</summary>
    /// <param name="prefix">What produced the content, for example <c>speech</c> or <c>image</c>.</param>
    /// <param name="mediaType">The type the attachment guard detected.</param>
    /// <param name="createdAt">The moment of the write.</param>
    /// <returns>The file name.</returns>
    /// <remarks>
    /// The timestamp carries ticks: one call can write several attachments in the
    /// same second (an image batch), and the name should still tell them apart.
    /// </remarks>
    public static string For(string prefix, string mediaType, DateTimeOffset createdAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(prefix);
        ArgumentNullException.ThrowIfNull(mediaType);

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{prefix}-{createdAt.UtcDateTime:yyyyMMdd-HHmmss-fffffff}.{ExtensionFor(mediaType)}");
    }

    /// <summary>Maps a detected media type to a file extension.</summary>
    /// <param name="mediaType">The type the attachment guard detected.</param>
    /// <returns>The extension without a dot; <c>bin</c> for an unknown type.</returns>
    /// <remarks>
    /// The list follows the signatures <see cref="AttachmentTypeGuard"/> knows. A
    /// type added there gets its extension here.
    /// </remarks>
    public static string ExtensionFor(string mediaType)
        => mediaType switch
        {
            "image/png" => "png",
            "image/jpeg" => "jpg",
            "image/gif" => "gif",
            "image/webp" => "webp",
            "application/pdf" => "pdf",
            "audio/wav" => "wav",
            "audio/ogg" => "ogg",
            "audio/mpeg" => "mp3",
            "text/plain" => "txt",
            _ => "bin",
        };
}
