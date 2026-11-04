using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace Tracon;

/// <summary>
/// Binds a tenant to a provider's credential, by <strong>name</strong> only.
/// </summary>
/// <remarks>
/// This record carries no secret value, only the name of the
/// configuration key the value is read from at call time.
/// applies to this store the same way it applies to MCP server credentials:
/// the database backup, the audit trail, and every HTTP response built from
/// this type never carry a secret.
/// </remarks>
public sealed record TenantProviderBinding
{
    /// <summary>Gets the tenant this binding belongs to.</summary>
    public required string TenantId { get; init; }

    /// <summary>
    /// Gets the provider name. Matches <see cref="ModelBinding.Provider"/> for a model
    /// provider, and <see cref="ISpeechSynthesizer.ProviderName"/> or
    /// <see cref="ISpeechTranscriber.ProviderName"/> for a speech provider.
    /// </summary>
    /// <remarks>
    /// Compared <strong>case-insensitively</strong>, like every other place a
    /// provider name is matched (the provider registry, the tenant egress
    /// policy, the admin endpoint). A store persists the value
    /// <see cref="NormalizeProviderName"/> returns, so <c>"OpenAI"</c> and
    /// <c>"openai"</c> are one binding and never two.
    /// </remarks>
    public required string ProviderName { get; init; }

    /// <summary>
    /// Returns the canonical form a store persists and looks a provider name
    /// up by.
    /// </summary>
    /// <param name="providerName">The provider name as written by the caller.</param>
    /// <returns>The canonical (invariant lower-case) form.</returns>
    /// <remarks>
    /// <para>
    /// An <see cref="ITenantProviderBindingStore"/> implementation must apply
    /// this to the name it writes <em>and</em> to the name it is queried with.
    /// A store that skips it is case-sensitive while the rest of the system is
    /// not, and a tenant whose binding was saved under a different letter case
    /// silently falls through to the global setup credential — the wrong
    /// tenant gets billed and no error is raised.
    /// </para>
    /// <para>
    /// Normalizing the stored <em>value</em> (rather than comparing
    /// case-insensitively at query time) is deliberate: a bare <c>=</c>
    /// predicate then behaves identically on PostgreSQL, SQLite and SQL Server
    /// whatever their collation, the primary key keeps rejecting duplicates on
    /// all three, and the index stays usable.
    /// </para>
    /// <para>
    /// The alphabet is closed on purpose, as the tenant identifier's is
    /// (<see cref="AmbientTenantScope.Normalize"/>): the ASCII letters, the digits, <c>_</c>, <c>.</c> and <c>-</c>.
    /// Outside it the engines disagree on what one name is. SQL Server's default collation reads a full-width
    /// <c>o</c> as the plain one, so two providers would share one binding there and stay two on PostgreSQL and
    /// SQLite; the migration that folded the stored names to lower case turned <c>É</c> into <c>é</c> on SQL Server and
    /// left it alone on PostgreSQL. A caller that holds client text asks <see cref="TryNormalizeProviderName"/>
    /// first and answers the client.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentException">
    /// <paramref name="providerName"/> is null, blank, or holds a character other than an ASCII letter, a digit, <c>_</c>,
    /// <c>.</c> or <c>-</c>. The message names the index and the code point of the first such character and never repeats
    /// the value.
    /// </exception>
    public static string NormalizeProviderName(string providerName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerName);

        var invalid = IndexOfCharacterOutsideAlphabet(providerName);

        if (invalid >= 0)
        {
            throw new ArgumentException(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"A provider name may hold only the ASCII letters, the digits, '_', '.' and '-'; the character at index {invalid} is U+{char.ConvertToUtf32(providerName, invalid):X4}."),
                nameof(providerName));
        }

        return providerName.ToLowerInvariant();
    }

    /// <summary>
    /// Returns the canonical form of a provider name, or says it has none.
    /// </summary>
    /// <param name="providerName">The provider name as written by the caller; may be <see langword="null"/>.</param>
    /// <param name="canonical">The canonical form when the method returns <see langword="true"/>; <see langword="null"/> otherwise.</param>
    /// <returns>
    /// <see langword="true"/> when <paramref name="providerName"/> is not blank and holds only the ASCII letters, the
    /// digits, <c>_</c>, <c>.</c> and <c>-</c>; <see langword="false"/> otherwise.
    /// </returns>
    /// <remarks>
    /// This is the check for text that came from a client: a route segment. It decides without throwing, so the caller
    /// can answer <c>400</c> instead of failing. It applies exactly the rule of <see cref="NormalizeProviderName"/>.
    /// </remarks>
    public static bool TryNormalizeProviderName(string? providerName, [NotNullWhen(true)] out string? canonical)
    {
        if (string.IsNullOrWhiteSpace(providerName) || IndexOfCharacterOutsideAlphabet(providerName) >= 0)
        {
            canonical = null;

            return false;
        }

        canonical = providerName.ToLowerInvariant();

        return true;
    }

    private static int IndexOfCharacterOutsideAlphabet(string providerName)
    {
        for (var index = 0; index < providerName.Length; index++)
        {
            if (providerName[index] is not ((>= 'a' and <= 'z') or (>= 'A' and <= 'Z') or (>= '0' and <= '9') or '_' or '.' or '-'))
            {
                return index;
            }
        }

        return -1;
    }

    /// <summary>
    /// Gets the configuration <strong>key name</strong> the credential value is
    /// read from. Never the value itself.
    /// </summary>
    /// <remarks>
    /// Must start with <see cref="TraconTenantProviderOptions.AllowedConfigurationPrefix"/>;
    /// resolution is rejected otherwise.
    /// </remarks>
    public required string ApiKeyConfigurationName { get; init; }

    /// <summary>Gets the optional provider endpoint override for this tenant.</summary>
    public string? Endpoint { get; init; }

    /// <summary>Gets the time this binding was last written.</summary>
    public required DateTimeOffset UpdatedAt { get; init; }
}
