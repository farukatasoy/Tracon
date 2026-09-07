using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.Json;

namespace AgentPrism;

/// <summary>
/// The single place that defines and reads the envelope <see cref="AesGcmContentProtector"/>
/// wraps protected values in — one shape for text, one for binary content.
/// </summary>
/// <remarks>
/// <para>
/// The text envelope is valid JSON on purpose: six of the ten protected
/// columns enforce a JSON-validity constraint at the schema level, and changing
/// their column type would need a data migration across three providers. Wrapping ciphertext in a small JSON
/// object satisfies the constraint with no migration.
/// </para>
/// <para>
/// <c>$apEnc</c> and the binary magic number are <strong>self-describing</strong>
/// tags: their presence — not any configuration — is what tells the read
/// path a value is protected. A value that never carries the tag is
/// returned unchanged, so protection can be turned on and off without
/// breaking rows written under the other setting.
/// </para>
/// </remarks>
internal static class ContentProtectionEnvelope
{
    /// <summary>The AES-GCM nonce size, in bytes.</summary>
    public const int NonceSizeBytes = 12;

    /// <summary>The AES-GCM authentication tag size, in bytes.</summary>
    public const int TagSizeBytes = 16;

    /// <summary>The AES-256 key size, in bytes.</summary>
    public const int KeySizeBytes = 32;

    private const string TagProperty = "$apEnc";
    private const int FormatVersion = 1;
    private const string KeyIdProperty = "kid";
    private const string NonceProperty = "n";
    private const string CiphertextProperty = "c";

    private static ReadOnlySpan<byte> BinaryMagic => "APEB"u8;
    private const byte BinaryFormatVersion = 1;

    /// <summary>Builds the JSON envelope for one protected text value.</summary>
    /// <param name="keyId">The key identifier the value was protected with.</param>
    /// <param name="nonce">The AES-GCM nonce.</param>
    /// <param name="ciphertext">The ciphertext.</param>
    /// <param name="tag">The AES-GCM authentication tag.</param>
    /// <returns>The envelope, as JSON text.</returns>
    public static string Wrap(string keyId, ReadOnlySpan<byte> nonce, ReadOnlySpan<byte> ciphertext, ReadOnlySpan<byte> tag)
    {
        var combined = new byte[ciphertext.Length + tag.Length];
        ciphertext.CopyTo(combined);
        tag.CopyTo(combined.AsSpan(ciphertext.Length));

        var buffer = new ArrayBufferWriter<byte>();

        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteNumber(TagProperty, FormatVersion);
            writer.WriteString(KeyIdProperty, keyId);
            writer.WriteBase64String(NonceProperty, nonce);
            writer.WriteBase64String(CiphertextProperty, combined);
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    /// <summary>Attempts to parse a stored text value as an envelope.</summary>
    /// <param name="stored">The value as read back from storage.</param>
    /// <param name="keyId">The key identifier, when parsing succeeds.</param>
    /// <param name="nonce">The nonce, when parsing succeeds.</param>
    /// <param name="ciphertext">The ciphertext, when parsing succeeds.</param>
    /// <param name="tag">The authentication tag, when parsing succeeds.</param>
    /// <returns>
    /// <see langword="true"/> when <paramref name="stored"/> carries the
    /// <c>$apEnc</c> tag; <see langword="false"/> for a plain value that was
    /// never protected.
    /// </returns>
    /// <exception cref="AgentPrismException">
    /// <paramref name="stored"/> carries the tag but the envelope is malformed.
    /// </exception>
    public static bool TryUnwrap(
        string stored,
        [NotNullWhen(true)] out string? keyId,
        out byte[] nonce,
        out byte[] ciphertext,
        out byte[] tag)
    {
        keyId = null;
        nonce = [];
        ciphertext = [];
        tag = [];

        JsonDocument document;

        try
        {
            document = JsonDocument.Parse(stored);
        }
        catch (JsonException)
        {
            return false;
        }

        using (document)
        {
            var root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty(TagProperty, out _))
            {
                return false;
            }

            if (!root.TryGetProperty(KeyIdProperty, out var keyIdElement) || keyIdElement.GetString() is not { Length: > 0 } parsedKeyId)
            {
                throw new AgentPrismException("A protected value's envelope carries no key id.");
            }

            if (!root.TryGetProperty(NonceProperty, out var nonceElement) || !root.TryGetProperty(CiphertextProperty, out var ciphertextElement))
            {
                throw new AgentPrismException($"A protected value's envelope (key id '{parsedKeyId}') is missing its nonce or ciphertext.");
            }

            var combined = ciphertextElement.GetBytesFromBase64();

            if (combined.Length < TagSizeBytes)
            {
                throw new AgentPrismException($"A protected value's envelope (key id '{parsedKeyId}') has a ciphertext shorter than the authentication tag.");
            }

            keyId = parsedKeyId;
            nonce = nonceElement.GetBytesFromBase64();
            ciphertext = combined[..^TagSizeBytes];
            tag = combined[^TagSizeBytes..];

            return true;
        }
    }

    /// <summary>Builds the binary envelope for one protected binary value.</summary>
    /// <param name="keyId">The key identifier the value was protected with.</param>
    /// <param name="nonce">The AES-GCM nonce.</param>
    /// <param name="ciphertext">The ciphertext.</param>
    /// <param name="tag">The AES-GCM authentication tag.</param>
    /// <returns>Magic number, format version, key id length, key id, nonce, ciphertext, tag — in that order.</returns>
    /// <exception cref="AgentPrismException"><paramref name="keyId"/> is too long to fit the header's one-byte length.</exception>
    public static byte[] WrapBinary(string keyId, ReadOnlySpan<byte> nonce, ReadOnlySpan<byte> ciphertext, ReadOnlySpan<byte> tag)
    {
        var keyIdBytes = Encoding.UTF8.GetBytes(keyId);

        if (keyIdBytes.Length > byte.MaxValue)
        {
            throw new AgentPrismException(
                $"Content protection key id '{keyId}' is {keyIdBytes.Length} UTF-8 bytes long; " +
                $"the binary envelope header allows at most {byte.MaxValue}.");
        }

        var headerLength = BinaryMagic.Length + 1 + 1 + keyIdBytes.Length + nonce.Length;
        var result = new byte[headerLength + ciphertext.Length + tag.Length];
        var span = result.AsSpan();

        BinaryMagic.CopyTo(span);
        var offset = BinaryMagic.Length;
        span[offset++] = BinaryFormatVersion;
        span[offset++] = (byte)keyIdBytes.Length;
        keyIdBytes.CopyTo(span[offset..]);
        offset += keyIdBytes.Length;
        nonce.CopyTo(span[offset..]);
        offset += nonce.Length;
        ciphertext.CopyTo(span[offset..]);
        offset += ciphertext.Length;
        tag.CopyTo(span[offset..]);

        return result;
    }

    /// <summary>Attempts to parse a stored binary value as an envelope.</summary>
    /// <param name="stored">The value as read back from storage.</param>
    /// <param name="keyId">The key identifier, when parsing succeeds.</param>
    /// <param name="nonce">The nonce, when parsing succeeds.</param>
    /// <param name="ciphertext">The ciphertext, when parsing succeeds.</param>
    /// <param name="tag">The authentication tag, when parsing succeeds.</param>
    /// <returns>
    /// <see langword="true"/> when <paramref name="stored"/> carries the magic
    /// number; <see langword="false"/> for a plain value that was never
    /// protected.
    /// </returns>
    /// <exception cref="AgentPrismException">
    /// <paramref name="stored"/> carries the magic number but the envelope is malformed.
    /// </exception>
    public static bool TryUnwrapBinary(
        byte[] stored,
        [NotNullWhen(true)] out string? keyId,
        out byte[] nonce,
        out byte[] ciphertext,
        out byte[] tag)
    {
        keyId = null;
        nonce = [];
        ciphertext = [];
        tag = [];

        if (stored.Length < BinaryMagic.Length + 2 || !stored.AsSpan(0, BinaryMagic.Length).SequenceEqual(BinaryMagic))
        {
            return false;
        }

        var offset = BinaryMagic.Length;
        var version = stored[offset++];

        if (version != BinaryFormatVersion)
        {
            throw new AgentPrismException($"Unsupported content protection binary envelope version {version}.");
        }

        var keyIdLength = stored[offset++];

        if (stored.Length < offset + keyIdLength + NonceSizeBytes + TagSizeBytes)
        {
            throw new AgentPrismException("A protected binary value is truncated.");
        }

        keyId = Encoding.UTF8.GetString(stored, offset, keyIdLength);
        offset += keyIdLength;
        nonce = stored[offset..(offset + NonceSizeBytes)];
        offset += NonceSizeBytes;
        ciphertext = stored[offset..^TagSizeBytes];
        tag = stored[^TagSizeBytes..];

        return true;
    }
}
