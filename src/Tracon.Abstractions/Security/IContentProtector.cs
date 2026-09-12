namespace Tracon;

/// <summary>Extension point for at-rest encryption of stored content.</summary>
/// <remarks>
/// <para>
/// There is no default implementation; a consumer opts in explicitly through
/// <c>AddContentProtection</c>. When nothing is registered, the stores that
/// consume this interface fall back to a no-op implementation that writes
/// plaintext unchanged — today's behavior, with no surprises.
/// </para>
/// <para>
/// <strong>This protects data at rest, not a running process.</strong> A
/// process holding the key still sees plaintext once a value is read back;
/// this interface addresses a stolen backup, a discarded disk, or a
/// misconfigured table permission — not a compromised application server.
/// </para>
/// <para>
/// <see cref="Unprotect"/> and <see cref="UnprotectBytes"/> are
/// <strong>self-describing</strong>: they decide whether a value is
/// protected by looking at the value itself, not at configuration. A value
/// written before protection was turned on therefore stays readable after
/// protection is turned on, and a value written while protection was on
/// stays readable after it is turned off.
/// </para>
/// </remarks>
public interface IContentProtector
{
    /// <summary>Gets a value indicating whether this instance is ready to protect new writes.</summary>
    /// <remarks>
    /// A column-level decision (which columns are in scope) is layered on
    /// top of this by the caller; this flag only says whether the
    /// implementation itself has everything it needs (for example, a
    /// resolved encryption key).
    /// </remarks>
    bool IsEnabled { get; }

    /// <summary>Protects a text value before it is written to storage.</summary>
    /// <param name="plaintext">The value to protect.</param>
    /// <returns>
    /// The protected representation. It must be safe to store in the same
    /// column the plaintext would have gone into.
    /// </returns>
    string Protect(string plaintext);

    /// <summary>Reverses <see cref="Protect"/>.</summary>
    /// <param name="stored">The value as read back from storage.</param>
    /// <returns>
    /// The original plaintext. When <paramref name="stored"/> was never
    /// protected, it is returned unchanged.
    /// </returns>
    string Unprotect(string stored);

    /// <summary>Protects binary data before it is written to storage.</summary>
    /// <param name="plaintext">The bytes to protect.</param>
    /// <returns>
    /// The protected representation. It must be safe to store in the same
    /// column the plaintext would have gone into.
    /// </returns>
    byte[] ProtectBytes(ReadOnlySpan<byte> plaintext);

    /// <summary>Reverses <see cref="ProtectBytes"/>.</summary>
    /// <param name="stored">The bytes as read back from storage.</param>
    /// <returns>
    /// The original bytes. When <paramref name="stored"/> was never
    /// protected, it is returned unchanged.
    /// </returns>
    byte[] UnprotectBytes(ReadOnlySpan<byte> stored);
}
