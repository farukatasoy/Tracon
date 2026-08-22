namespace AgentPrism;

/// <summary>
/// The default <see cref="IContentProtector"/> — writes and reads plaintext
/// unchanged.
/// </summary>
/// <remarks>
/// Registered by <c>AddAgentPrism()</c> as the default, and replaced by
/// <c>AddContentProtection(...)</c> when a consumer opts in. Kept internal:
/// a consumer never needs to reference it directly, only
/// <see cref="IContentProtector.IsEnabled"/>.
/// </remarks>
internal sealed class NullContentProtector : IContentProtector
{
    /// <summary>The single shared instance.</summary>
    public static readonly NullContentProtector Instance = new();

    private NullContentProtector()
    {
    }

    /// <inheritdoc />
    public bool IsEnabled => false;

    /// <inheritdoc />
    public string Protect(string plaintext)
    {
        ArgumentNullException.ThrowIfNull(plaintext);

        return plaintext;
    }

    /// <inheritdoc />
    /// <exception cref="AgentPrismException">
    /// <paramref name="stored"/> carries an encryption envelope — content protection
    /// was turned on at some point and is not configured now.
    /// </exception>
    public string Unprotect(string stored)
    {
        ArgumentNullException.ThrowIfNull(stored);

        if (!ContentProtectionEnvelope.TryUnwrap(stored, out var keyId, out _, out _, out _))
        {
            return stored;
        }

        throw new AgentPrismException(
            $"A value protected with content protection key '{keyId}' was read, but content protection is not " +
            "configured in this process. Call AddContentProtection with the same key configuration used to write it.");
    }

    /// <inheritdoc />
    public byte[] ProtectBytes(ReadOnlySpan<byte> plaintext) => plaintext.ToArray();

    /// <inheritdoc />
    /// <exception cref="AgentPrismException">
    /// <paramref name="stored"/> carries an encryption envelope — content protection
    /// was turned on at some point and is not configured now.
    /// </exception>
    public byte[] UnprotectBytes(ReadOnlySpan<byte> stored)
    {
        var bytes = stored.ToArray();

        if (!ContentProtectionEnvelope.TryUnwrapBinary(bytes, out var keyId, out _, out _, out _))
        {
            return bytes;
        }

        throw new AgentPrismException(
            $"A value protected with content protection key '{keyId}' was read, but content protection is not " +
            "configured in this process. Call AddContentProtection with the same key configuration used to write it.");
    }
}
