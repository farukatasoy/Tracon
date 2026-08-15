namespace AgentPrism;

/// <summary>
/// The built-in pattern families for <see cref="PatternContentGuard"/>.
/// </summary>
/// <remarks>
/// Each family is enabled <strong>independently</strong>. Enabling them all together
/// compounds false-positive risk and can make the guard impractical to use.
/// </remarks>
[Flags]
public enum PiiPatterns
{
    /// <summary>No pattern is enabled. This is the default.</summary>
    None = 0,

    /// <summary>An email address.</summary>
    Email = 1,

    /// <summary>An IBAN: two-letter country code, two check digits, and 11 to 30 alphanumeric characters.</summary>
    Iban = 2,

    /// <summary>
    /// A credit card number. It <strong>uses Luhn validation</strong>.
    /// </summary>
    /// <remarks>
    /// A plain <c>\d{16}</c> match would mask every order number and make the guard
    /// impractical to use.
    /// </remarks>
    CreditCard = 4,

    /// <summary>
    /// A Turkish national identification number. It <strong>validates check digits</strong>.
    /// </summary>
    /// <remarks>
    /// A random 11-digit value is not masked. Only a number that satisfies the tenth
    /// and eleventh digit rules is masked.
    /// </remarks>
    TurkishNationalId = 8,

    /// <summary>
    /// A provider API key pattern: <c>sk-…</c>, <c>ghp_…</c>, or <c>AKIA…</c>.
    /// </summary>
    /// <remarks>
    /// A user can accidentally put a key in a prompt. That prompt goes to the provider
    /// and can enter the provider log.
    /// </remarks>
    ProviderApiKey = 16,
}
