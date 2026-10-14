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
/// was off, with nothing to say so. The binder now binds an unreadable value as a
/// value with an undefined bit, and this validator refuses any value with an
/// undefined bit, whatever its source.
/// </para>
/// <para>
/// The failure comes from the bound <see cref="PatternContentGuardOptions.MaskedPii"/>,
/// not from the text the binder kept: code that sets the member after binding wins
/// and clears the error, the same rule the scheduling collections follow. The kept
/// text only makes the message name the value as the operator wrote it.
/// </para>
/// <para>
/// Hand-written; <c>ValidateDataAnnotations()</c> relies on reflection and produces
/// <c>IL2026</c>. <c>Enum.GetValues&lt;T&gt;</c> and <c>Enum.GetNames&lt;T&gt;</c>
/// are AOT-clean.
/// </para>
/// </remarks>
internal sealed class PatternContentGuardOptionsValidator : IValidateOptions<PatternContentGuardOptions>
{
    /// <summary>
    /// The value the binder binds for a <c>MaskedPii</c> text that does not parse:
    /// only the sign bit, which no <see cref="PiiPatterns"/> member defines.
    /// </summary>
    internal const PiiPatterns UnreadableMaskedPii = (PiiPatterns)int.MinValue;

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

        var message = options.UnreadableMaskedPii is { } unreadable && UndefinedBits(unreadable.Bound) == undefined
            ? $"{nameof(PatternContentGuardOptions)}.{nameof(PatternContentGuardOptions.MaskedPii)} is '{unreadable.Text}', " +
              $"which is not a comma-separated list of {nameof(PiiPatterns)} names. Known names: {knownNames}."
            : $"{nameof(PatternContentGuardOptions)}.{nameof(PatternContentGuardOptions.MaskedPii)} is " +
              $"{((int)options.MaskedPii).ToString(CultureInfo.InvariantCulture)}, which carries a bit no " +
              $"{nameof(PiiPatterns)} member defines. Known names: {knownNames}.";

        return ValidateOptionsResult.Fail(message);
    }

    /// <summary>Returns the bits of <paramref name="value"/> that no <see cref="PiiPatterns"/> member defines.</summary>
    /// <param name="value">The value to check.</param>
    /// <returns><see cref="PiiPatterns.None"/> when every bit is defined.</returns>
    internal static PiiPatterns UndefinedBits(PiiPatterns value) => value & ~DefinedBits;
}
