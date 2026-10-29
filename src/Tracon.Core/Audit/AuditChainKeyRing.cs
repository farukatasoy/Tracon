using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;

namespace Tracon;

/// <summary>
/// One signing key of the audit trail hash chain: its id and its value.
/// </summary>
/// <remarks>
/// The value is a <c>secret</c>. It is never written to the database, a log, a diagnostics
/// report or an HTTP response, so this type has no member that prints it.
/// </remarks>
internal readonly struct AuditChainKey
{
    /// <summary>Creates a key.</summary>
    /// <param name="id">The key id, as written to <c>audit_log.key_id</c>.</param>
    /// <param name="value">The key bytes.</param>
    public AuditChainKey(string id, byte[] value)
    {
        Id = id;
        Value = value;
    }

    /// <summary>Gets the key id.</summary>
    public string Id { get; }

    /// <summary>Gets the key bytes.</summary>
    public byte[] Value { get; }

    /// <summary>Returns the key id. The value is never part of the text.</summary>
    /// <returns>The text.</returns>
    public override string ToString() => $"AuditChainKey {Id}";
}

/// <summary>
/// The keys that sign and verify the audit trail hash chain: one active key that signs new entries,
/// and every other key the configuration still holds, which only verify.
/// </summary>
/// <remarks>
/// <para>
/// Built once, while the host starts, from <c>Tracon:AuditChainKeys:&lt;keyId&gt;</c> (the values,
/// base64) and <c>Tracon:Audit:ChainKeyId</c> (which key signs). The values are not bound to any options
/// object, so no <c>IOptions</c> dump, diagnostics report or health check can print one. A change needs a
/// restart, the same rule as <c>RequireEgressPolicy</c>.
/// </para>
/// <para>
/// A startup failure names the configuration KEY that is wrong and never carries a value.
/// </para>
/// </remarks>
internal sealed partial class AuditChainKeyRing
{
    /// <summary>The configuration section, relative to <c>Tracon</c>, that holds the key values.</summary>
    public const string KeysSectionName = "AuditChainKeys";

    /// <summary>The smallest key, in bytes. HMAC-SHA256 gains nothing from a key shorter than its block hash.</summary>
    public const int MinimumKeyBytes = 32;

    /// <summary>The configuration key that names the signing key, for messages and the profile check.</summary>
    internal static readonly string ChainKeyIdSetting =
        $"{TraconOptions.SectionName}:{nameof(TraconOptions.Audit)}:{nameof(TraconAuditOptions.ChainKeyId)}";

    private static readonly string KeysSetting = $"{TraconOptions.SectionName}:{KeysSectionName}";

    private readonly Dictionary<string, byte[]> _keys;

    private AuditChainKeyRing(AuditChainKey? active, Dictionary<string, byte[]> keys)
    {
        Active = active;
        _keys = keys;
    }

    /// <summary>Gets a ring with no key at all: every entry is written and checked without one.</summary>
    public static AuditChainKeyRing None { get; } = new(null, new Dictionary<string, byte[]>(StringComparer.Ordinal));

    /// <summary>Gets the key that signs new entries, or <see langword="null"/> when new entries are written unsigned.</summary>
    public AuditChainKey? Active { get; }

    /// <summary>Looks up a key by id.</summary>
    /// <param name="keyId">The id an entry names.</param>
    /// <param name="value">The key bytes when the ring holds the id.</param>
    /// <returns><see langword="true"/> when the ring holds the key.</returns>
    public bool TryGet(string keyId, out byte[] value)
    {
        ArgumentNullException.ThrowIfNull(keyId);

        return _keys.TryGetValue(keyId, out value!);
    }

    /// <summary>Builds a ring from key values already in hand. Tests use this; production reads configuration.</summary>
    /// <param name="activeKeyId">The id of the signing key, or <see langword="null"/> for none.</param>
    /// <param name="keys">Every key the ring holds, by id.</param>
    /// <returns>The ring.</returns>
    /// <exception cref="TraconException">An id or a value is not acceptable, or the active id has no key.</exception>
    public static AuditChainKeyRing Create(string? activeKeyId, IReadOnlyDictionary<string, byte[]> keys)
    {
        ArgumentNullException.ThrowIfNull(keys);

        var held = new Dictionary<string, byte[]>(StringComparer.Ordinal);

        foreach (var (id, value) in keys)
        {
            RequireValidId(id, $"{KeysSetting}:{id}");

            if (value.Length < MinimumKeyBytes)
            {
                throw new TraconException(TooShortMessage($"{KeysSetting}:{id}"));
            }

            held[id] = value;
        }

        return Assemble(activeKeyId, held);
    }

    /// <summary>Builds the ring from configuration, for the host start.</summary>
    /// <param name="activeKeyId"><c>Tracon:Audit:ChainKeyId</c>, or <see langword="null"/> or empty for none.</param>
    /// <param name="keysSection">
    /// The section that holds the values (<c>Tracon:AuditChainKeys</c>), or <see langword="null"/> when the
    /// host passed no configuration to <c>AddTracon</c>.
    /// </param>
    /// <returns>The ring.</returns>
    /// <exception cref="TraconException">
    /// A value is missing, is not base64 or is too short, an id is not acceptable, or the active id has no value.
    /// The message names the configuration key and never carries a value.
    /// </exception>
    public static AuditChainKeyRing FromConfiguration(string? activeKeyId, IConfiguration? keysSection)
    {
        var held = new Dictionary<string, byte[]>(StringComparer.Ordinal);

        if (keysSection is not null)
        {
            foreach (var child in keysSection.GetChildren())
            {
                var setting = $"{KeysSetting}:{child.Key}";

                RequireValidId(child.Key, setting);

                if (string.IsNullOrWhiteSpace(child.Value))
                {
                    throw new TraconException($"{setting} has no value. Set it to a base64 key of at least {MinimumKeyBytes} bytes.");
                }

                held[child.Key] = Decode(child.Value, setting);
            }
        }

        return Assemble(activeKeyId, held);
    }

    private static AuditChainKeyRing Assemble(string? activeKeyId, Dictionary<string, byte[]> held)
    {
        if (string.IsNullOrEmpty(activeKeyId))
        {
            return new AuditChainKeyRing(null, held);
        }

        RequireValidId(activeKeyId, ChainKeyIdSetting);

        if (!held.TryGetValue(activeKeyId, out var activeValue))
        {
            throw new TraconException(
                $"{ChainKeyIdSetting} names a key, but {KeysSetting}:{activeKeyId} has no value. " +
                $"Set it to a base64 key of at least {MinimumKeyBytes} bytes.");
        }

        return new AuditChainKeyRing(new AuditChainKey(activeKeyId, activeValue), held);
    }

    private static void RequireValidId(string id, string setting)
    {
        if (!KeyIdPattern().IsMatch(id))
        {
            throw new TraconException(
                $"{setting} is not a valid key id. A key id is 1 to 64 characters of lowercase letters, digits, " +
                "'.', '_' and '-', and starts with a letter or a digit. Lowercase only: configuration keys " +
                "ignore case, and the id is stored in the audit trail exactly as written.");
        }
    }

    private static byte[] Decode(string text, string setting)
    {
        var trimmed = text.Trim();
        var buffer = new byte[(trimmed.Length / 4 * 3) + 3];

        if (!Convert.TryFromBase64String(trimmed, buffer, out var written))
        {
            throw new TraconException($"{setting} is not valid base64.");
        }

        if (written < MinimumKeyBytes)
        {
            throw new TraconException(TooShortMessage(setting));
        }

        var key = buffer.AsSpan(0, written).ToArray();

        CryptographicOperations.ZeroMemory(buffer);

        return key;
    }

    private static string TooShortMessage(string setting)
        => $"{setting} decodes to fewer than {MinimumKeyBytes} bytes. Use a random key of at least {MinimumKeyBytes} bytes.";

    [GeneratedRegex(@"^[a-z0-9][a-z0-9._-]{0,63}\z", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex KeyIdPattern();
}
