using System.Globalization;
using Microsoft.Extensions.Options;

namespace Tracon;

/// <summary>
/// Validates <see cref="PatternContentGuardOptions"/> when the application starts.
/// </summary>
/// <remarks>
/// <para>
/// One typo in <c>MaskedPii</c> (<c>"Emial,CreditCard"</c>) used to fail the
/// whole parse, so the value stayed <see cref="PiiPatterns.None"/> and PII masking
/// was off, with nothing to say so. The binder records a text it cannot read
/// (<see cref="UnreadableConfigurationValidator{TOptions}"/> names it); this
/// validator refuses any value with an undefined bit, whatever its source - a number
/// such as <c>128</c> parses, and so does an <c>AddPatternContentGuard(...)</c> callback.
/// </para>
/// <para>
/// Code that sets the member after binding wins and clears the error, the same rule
/// the scheduling collections follow.
/// </para>
/// <para>
/// Hand-written; <c>ValidateDataAnnotations()</c> relies on reflection and produces
/// <c>IL2026</c>. <c>Enum.GetValues&lt;T&gt;</c> and <c>Enum.GetNames&lt;T&gt;</c>
/// are AOT-clean.
/// </para>
/// </remarks>
internal sealed class PatternContentGuardOptionsValidator : IValidateOptions<PatternContentGuardOptions>
{
    private static readonly PiiPatterns DefinedBits =
        Enum.GetValues<PiiPatterns>().Aggregate(PiiPatterns.None, static (all, pattern) => all | pattern);

    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, PatternContentGuardOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var undefined = UndefinedBits(options.MaskedPii);

        if (undefined == PiiPatterns.None)
        {
            return ValidateOptionsResult.Success;
        }

        var knownNames = string.Join(", ", Enum.GetNames<PiiPatterns>());

        var message = $"{nameof(PatternContentGuardOptions)}.{nameof(PatternContentGuardOptions.MaskedPii)} is " +
                      $"{((int)options.MaskedPii).ToString(CultureInfo.InvariantCulture)}, which carries a bit no " +
                      $"{nameof(PiiPatterns)} member defines. Known names: {knownNames}.";

        return ValidateOptionsResult.Fail(message);
    }

    /// <summary>Returns the bits of <paramref name="value"/> that no <see cref="PiiPatterns"/> member defines.</summary>
    /// <param name="value">The value to check.</param>
    /// <returns><see cref="PiiPatterns.None"/> when every bit is defined.</returns>
    internal static PiiPatterns UndefinedBits(PiiPatterns value) => value & ~DefinedBits;
}
