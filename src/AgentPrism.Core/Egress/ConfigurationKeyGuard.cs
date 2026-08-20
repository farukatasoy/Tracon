namespace AgentPrism;

/// <summary>
/// Verifies that a configuration key name a stored record points at sits
/// under an allowed prefix.
/// </summary>
/// <remarks>
/// <para>
/// A record never holds a secret <em>value</em>; it holds the <strong>name</strong>
/// of the configuration key the value is read from. That
/// alone is not enough. Without a prefix restriction, an administrator could
/// bind a record to an unrelated key such as <c>ConnectionStrings:Default</c>
/// — unable to read its value, but able to make AgentPrism send it somewhere.
/// </para>
/// <para>
/// The rule is enforced twice on every surface: once where the record is
/// saved, and again where the value is resolved. A record written before the
/// prefix was configured must not silently read an out-of-prefix key.
/// </para>
/// </remarks>
public static class ConfigurationKeyGuard
{
    /// <summary>Throws unless a configuration key name sits under the allowed prefix.</summary>
    /// <param name="configurationKeyName">The configuration key name the record carries.</param>
    /// <param name="allowedPrefix">The only prefix a key name may start with.</param>
    /// <param name="fieldName">The name of the field being checked, as it appears to the caller.</param>
    /// <exception cref="ArgumentException"><paramref name="allowedPrefix"/> is empty.</exception>
    /// <exception cref="AgentPrismException">
    /// <paramref name="configurationKeyName"/> is empty, or does not start with
    /// <paramref name="allowedPrefix"/>. The message names both the field and
    /// the prefix, so the operator can see which field to correct.
    /// </exception>
    public static void RequirePrefix(string? configurationKeyName, string allowedPrefix, string fieldName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(allowedPrefix);

        if (string.IsNullOrWhiteSpace(configurationKeyName))
        {
            throw new AgentPrismException($"'{fieldName}' cannot be empty.");
        }

        if (!configurationKeyName.StartsWith(allowedPrefix, StringComparison.Ordinal))
        {
            throw new AgentPrismException(
                $"'{configurationKeyName}' is outside the allowed prefix. '{fieldName}' may only " +
                $"reference a configuration key under '{allowedPrefix}'.");
        }
    }
}
