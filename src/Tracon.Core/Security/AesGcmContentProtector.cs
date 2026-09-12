using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace Tracon;

/// <summary>
/// The built-in <see cref="IContentProtector"/> — AES-256-GCM, from the base
/// class library.
/// </summary>
/// <remarks>
/// <para>
/// A key's raw material is never held in <see cref="TraconContentProtectionOptions"/>:
/// <see cref="TraconContentProtectionOptions.Keys"/> maps a key id to the
/// <strong>name</strong> of another configuration key, and the raw value is
/// read from <see cref="IConfiguration"/> only when a key is first needed,
/// then cached — the same indirection used for provider credentials
/// elsewhere in Tracon, so the settings object stays safe to log or inspect.
/// </para>
/// <para>
/// Key rotation is lazy: writes always use <see cref="TraconContentProtectionOptions.ActiveKeyId"/>;
/// reads use whatever key id the value's own envelope carries. Adding a new
/// active key does not touch existing rows, and an old key stays configured
/// for as long as any row still carries its id.
/// </para>
/// </remarks>
internal sealed class AesGcmContentProtector : IContentProtector
{
    private readonly IOptionsMonitor<TraconContentProtectionOptions> _options;
    private readonly IConfiguration? _configuration;
    private readonly ConcurrentDictionary<string, byte[]> _keyCache = new(StringComparer.Ordinal);

    /// <summary>Creates a new AES-GCM content protector.</summary>
    /// <param name="options">The settings, including the key map and the active key id.</param>
    /// <param name="configuration">
    /// The application's configuration root, used to resolve a key id's raw material.
    /// When <see langword="null"/>, protecting or reading a value always fails —
    /// there would be nowhere to read a key's value from.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is <see langword="null"/>.</exception>
    public AesGcmContentProtector(IOptionsMonitor<TraconContentProtectionOptions> options, IConfiguration? configuration)
    {
        ArgumentNullException.ThrowIfNull(options);

        _options = options;
        _configuration = configuration;
    }

    /// <inheritdoc />
    public bool IsEnabled => _options.CurrentValue.Enabled;

    /// <inheritdoc />
    /// <exception cref="TraconException">
    /// <see cref="TraconContentProtectionOptions.ActiveKeyId"/> is not set, or its key
    /// cannot be resolved (see <see cref="Unprotect"/> for the resolution failures).
    /// </exception>
    public string Protect(string plaintext)
    {
        ArgumentNullException.ThrowIfNull(plaintext);

        var (keyId, key) = ResolveActiveKey();
        var nonce = RandomNumberGenerator.GetBytes(ContentProtectionEnvelope.NonceSizeBytes);
        var plaintextBytes = Encoding.UTF8.GetBytes(plaintext);
        var ciphertext = new byte[plaintextBytes.Length];
        var tag = new byte[ContentProtectionEnvelope.TagSizeBytes];

        using (var aesGcm = new AesGcm(key, ContentProtectionEnvelope.TagSizeBytes))
        {
            aesGcm.Encrypt(nonce, plaintextBytes, ciphertext, tag);
        }

        return ContentProtectionEnvelope.Wrap(keyId, nonce, ciphertext, tag);
    }

    /// <inheritdoc />
    /// <exception cref="TraconException">
    /// <paramref name="stored"/> carries an envelope whose key id is not configured
    /// (removed, or never added), or whose configured value does not resolve to a
    /// valid 32-byte AES-256 key.
    /// </exception>
    public string Unprotect(string stored)
    {
        ArgumentNullException.ThrowIfNull(stored);

        if (!ContentProtectionEnvelope.TryUnwrap(stored, out var keyId, out var nonce, out var ciphertext, out var tag))
        {
            return stored;
        }

        var key = ResolveKey(keyId);
        var plaintextBytes = new byte[ciphertext.Length];

        using (var aesGcm = new AesGcm(key, ContentProtectionEnvelope.TagSizeBytes))
        {
            aesGcm.Decrypt(nonce, ciphertext, tag, plaintextBytes);
        }

        return Encoding.UTF8.GetString(plaintextBytes);
    }

    /// <inheritdoc />
    /// <exception cref="TraconException">
    /// <see cref="TraconContentProtectionOptions.ActiveKeyId"/> is not set, or its key
    /// cannot be resolved.
    /// </exception>
    public byte[] ProtectBytes(ReadOnlySpan<byte> plaintext)
    {
        var (keyId, key) = ResolveActiveKey();
        var nonce = RandomNumberGenerator.GetBytes(ContentProtectionEnvelope.NonceSizeBytes);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[ContentProtectionEnvelope.TagSizeBytes];

        using (var aesGcm = new AesGcm(key, ContentProtectionEnvelope.TagSizeBytes))
        {
            aesGcm.Encrypt(nonce, plaintext, ciphertext, tag);
        }

        return ContentProtectionEnvelope.WrapBinary(keyId, nonce, ciphertext, tag);
    }

    /// <inheritdoc />
    /// <exception cref="TraconException">
    /// <paramref name="stored"/> carries an envelope whose key id is not configured, or
    /// whose configured value does not resolve to a valid 32-byte AES-256 key.
    /// </exception>
    public byte[] UnprotectBytes(ReadOnlySpan<byte> stored)
    {
        var bytes = stored.ToArray();

        if (!ContentProtectionEnvelope.TryUnwrapBinary(bytes, out var keyId, out var nonce, out var ciphertext, out var tag))
        {
            return bytes;
        }

        var key = ResolveKey(keyId);
        var plaintextBytes = new byte[ciphertext.Length];

        using (var aesGcm = new AesGcm(key, ContentProtectionEnvelope.TagSizeBytes))
        {
            aesGcm.Decrypt(nonce, ciphertext, tag, plaintextBytes);
        }

        return plaintextBytes;
    }

    private (string KeyId, byte[] Key) ResolveActiveKey()
    {
        var keyId = _options.CurrentValue.ActiveKeyId;

        if (string.IsNullOrWhiteSpace(keyId))
        {
            throw new TraconException(
                $"Content protection is enabled but {nameof(TraconContentProtectionOptions)}." +
                $"{nameof(TraconContentProtectionOptions.ActiveKeyId)} is not set.");
        }

        return (keyId, ResolveKey(keyId));
    }

    private byte[] ResolveKey(string keyId) => _keyCache.GetOrAdd(keyId, static (id, self) => self.LoadKey(id), this);

    private byte[] LoadKey(string keyId)
    {
        if (!_options.CurrentValue.Keys.TryGetValue(keyId, out var configurationKeyName))
        {
            throw new TraconException(
                $"Content protection key '{keyId}' is not configured. Add it to " +
                $"{nameof(TraconContentProtectionOptions)}.{nameof(TraconContentProtectionOptions.Keys)}, " +
                "or register AddContentProtection with the same keys used to write this data.");
        }

        var rawValue = _configuration?[configurationKeyName];

        if (string.IsNullOrWhiteSpace(rawValue))
        {
            throw new TraconException(
                $"Content protection key '{keyId}' points at configuration key '{configurationKeyName}', which has " +
                "no value. Set it with `dotnet user-secrets` or an environment variable.");
        }

        byte[] key;

        try
        {
            key = Convert.FromBase64String(rawValue);
        }
        catch (FormatException ex)
        {
            throw new TraconException(
                $"Content protection key '{keyId}' (configuration key '{configurationKeyName}') is not valid base64.", ex);
        }

        if (key.Length != ContentProtectionEnvelope.KeySizeBytes)
        {
            throw new TraconException(
                $"Content protection key '{keyId}' (configuration key '{configurationKeyName}') must decode to " +
                $"{ContentProtectionEnvelope.KeySizeBytes} bytes (AES-256); actual length {key.Length}.");
        }

        return key;
    }
}
