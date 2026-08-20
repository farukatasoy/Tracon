using System.Globalization;
using Microsoft.Extensions.Options;

namespace AgentPrism;

/// <summary>Validates an attachment candidate against size and media-type allow lists.</summary>
/// <remarks>
/// <para>
/// The client-provided <c>Content-Type</c> is not evidence. The type is derived
/// from the signature in the first bytes (magic bytes); the supplied value is
/// informational, like a file extension, and is ignored.
/// </para>
/// <para>
/// Executable content types, such as <c>application/x-msdownload</c>, are
/// deliberately absent from the allow list and no magic-byte rule produces
/// them. Such a file is always rejected.
/// </para>
/// </remarks>
public sealed class AttachmentTypeGuard
{
    private readonly AgentPrismAttachmentOptions _options;

    /// <summary>Initializes a validator from configuration.</summary>
    /// <param name="options">The AgentPrism options.</param>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is <see langword="null"/>.</exception>
    public AttachmentTypeGuard(IOptions<AgentPrismOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);

        _options = options.Value.Attachments;
    }

    /// <summary>Gets the configured attachment size limit.</summary>
    public long MaxBytes => _options.MaxBytes;

    /// <summary>
    /// Validates that content is non-empty, within the size limit, and has a
    /// magic byte signature matching an allowed type.
    /// </summary>
    /// <param name="data">The raw content to validate.</param>
    /// <returns>The validated type when valid; otherwise, the reason.</returns>
    public AttachmentValidationResult Validate(ReadOnlySpan<byte> data)
    {
        if (data.Length == 0)
        {
            return AttachmentValidationResult.Invalid("The attachment cannot be empty.");
        }

        if (data.Length > _options.MaxBytes)
        {
            return AttachmentValidationResult.Invalid(
                $"Attachment size exceeds the {_options.MaxBytes.ToString("N0", CultureInfo.InvariantCulture)} byte limit.");
        }

        if (SniffMediaType(data) is not { } sniffed)
        {
            return AttachmentValidationResult.Invalid(
                "File type not recognized. Supported types: " +
                string.Join(", ", _options.AllowedMediaTypes.OrderBy(static value => value, StringComparer.Ordinal)) + ".");
        }

        return IsAllowed(sniffed)
            ? AttachmentValidationResult.Valid(sniffed)
            : AttachmentValidationResult.Invalid($"'{sniffed}' is not an allowed type.");
    }

    private bool IsAllowed(string mediaType)
    {
        foreach (var allowed in _options.AllowedMediaTypes)
        {
            if (string.Equals(allowed, mediaType, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (allowed.EndsWith("/*", StringComparison.Ordinal) &&
                mediaType.StartsWith(allowed[..^1], StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Derives a known file signature from the leading bytes.</summary>
    /// <remarks>
    /// Adding a binary type requires a signature here. A type not added to the
    /// allow list is still rejected.
    /// </remarks>
    private static string? SniffMediaType(ReadOnlySpan<byte> data)
    {
        if (StartsWith(data, [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]))
        {
            return "image/png";
        }

        if (StartsWith(data, [0xFF, 0xD8, 0xFF]))
        {
            return "image/jpeg";
        }

        if (StartsWith(data, "GIF87a"u8) || StartsWith(data, "GIF89a"u8))
        {
            return "image/gif";
        }

        if (data.Length >= 12 &&
            StartsWith(data, "RIFF"u8) &&
            data.Slice(8, 4).SequenceEqual("WEBP"u8))
        {
            return "image/webp";
        }

        if (StartsWith(data, "%PDF-"u8))
        {
            return "application/pdf";
        }

        if (data.Length >= 12 && StartsWith(data, "RIFF"u8) && data.Slice(8, 4).SequenceEqual("WAVE"u8))
        {
            return "audio/wav";
        }

        if (StartsWith(data, "OggS"u8))
        {
            return "audio/ogg";
        }

        if (StartsWith(data, "ID3"u8) || IsMpegFrameSync(data))
        {
            return "audio/mpeg";
        }

        // text/plain has no reliable magic bytes. Valid UTF-8 content without a
        // control or NUL byte in its first 1 KB is treated as text.
        return LooksLikePlainText(data) ? "text/plain" : null;
    }

    /// <summary>
    /// Recognizes an MPEG audio frame header for an MP3 without an ID3 tag.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Frame synchronization is <strong>eleven one bits</strong>: the first byte is
    /// <c>0xFF</c>, and the top three bits of the second byte are <c>111</c>. The
    /// remaining bits encode version and layer, with many valid values such as
    /// <c>0xFB</c>, <c>0xF3</c>, <c>0xF2</c>, <c>0xFA</c>, and <c>0xE3</c>. Listing
    /// them individually would silently reject valid output, so the rule uses a bit
    /// mask.
    /// </para>
    /// <para>
    /// The version and layer fields are also checked to prevent false matches. Their
    /// respective <c>reserved</c> values, <c>01</c> and <c>00</c>, do not represent a
    /// valid frame. Without this check, every binary value beginning with <c>FF E0</c>
    /// would be treated as audio.
    /// </para>
    /// </remarks>
    private static bool IsMpegFrameSync(ReadOnlySpan<byte> data)
    {
        if (data.Length < 2 || data[0] != 0xFF || (data[1] & 0xE0) != 0xE0)
        {
            return false;
        }

        var version = (data[1] >> 3) & 0x03;   // 01 = ayrilmis
        var layer = (data[1] >> 1) & 0x03;     // 00 = ayrilmis

        return version != 0x01 && layer != 0x00;
    }

    private static bool LooksLikePlainText(ReadOnlySpan<byte> data)
    {
        var sample = data.Length > 1024 ? data[..1024] : data;

        // Any control byte other than horizontal tab, line feed, and carriage
        // return indicates binary content.
        foreach (var b in sample)
        {
            if (b < 0x20 && b is not (0x09 or 0x0A or 0x0D))
            {
                return false;
            }
        }

        try
        {
            _ = new System.Text.UTF8Encoding(false, throwOnInvalidBytes: true).GetString(sample);
            return true;
        }
        catch (System.Text.DecoderFallbackException)
        {
            return false;
        }
    }

    private static bool StartsWith(ReadOnlySpan<byte> data, ReadOnlySpan<byte> signature)
        => data.Length >= signature.Length && data[..signature.Length].SequenceEqual(signature);
}

/// <summary>The result of validating an attachment.</summary>
public readonly struct AttachmentValidationResult
{
    private AttachmentValidationResult(bool isValid, string? mediaType, string? error)
    {
        IsValid = isValid;
        MediaType = mediaType;
        Error = error;
    }

    /// <summary>Gets whether the content is valid.</summary>
    public bool IsValid { get; }

    /// <summary>The MIME type derived from the magic bytes, when valid.</summary>
    public string? MediaType { get; }

    /// <summary>Gets the reason shown to the user when invalid.</summary>
    public string? Error { get; }

    /// <summary>Creates a successful validation result.</summary>
    public static AttachmentValidationResult Valid(string mediaType) => new(true, mediaType, null);

    /// <summary>Creates a failed validation result.</summary>
    public static AttachmentValidationResult Invalid(string error) => new(false, null, error);
}
