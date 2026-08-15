namespace AgentPrism;

/// <summary>
/// Check-digit algorithms that validate pattern matches.
/// </summary>
/// <remarks>
/// <para>
/// 🚨 This class is what makes <see cref="PatternContentGuard"/> usable. A bare
/// <c>\d{16}</c> match would mask every order number, and a bare <c>\d{11}</c>
/// match would mask every tracking number; a guard like that gets turned off on
/// day one.
/// </para>
/// <para>
/// The methods take a <see cref="ReadOnlySpan{T}"/> and allocate nothing: they
/// are called for every match on the hot path.
/// </para>
/// </remarks>
internal enum CheckDigitKind
{
    /// <summary>No validation is done; a pattern match alone is sufficient.</summary>
    None = 0,

    /// <summary>Luhn check digit (credit card).</summary>
    Luhn = 1,

    /// <summary>Turkish national ID number check digits.</summary>
    TurkishNationalId = 2,
}

internal static class CheckDigits
{
    /// <summary>
    /// Luhn check-digit validation (credit card, IMEI).
    /// </summary>
    /// <param name="digits">
    /// A candidate sequence that may contain only digits and separators (space, hyphen).
    /// </param>
    /// <returns><see langword="true"/> if the check digit holds.</returns>
    public static bool IsValidLuhn(ReadOnlySpan<char> digits)
    {
        var sum = 0;
        var count = 0;

        // Walked right to left: the doubling order is determined from the last digit.
        for (var index = digits.Length - 1; index >= 0; index--)
        {
            var character = digits[index];

            if (!char.IsAsciiDigit(character))
            {
                continue;
            }

            var value = character - '0';

            if (count % 2 == 1)
            {
                value *= 2;

                if (value > 9)
                {
                    value -= 9;
                }
            }

            sum += value;
            count++;
        }

        return count >= 12 && sum % 10 == 0;
    }

    /// <summary>
    /// Turkish national ID number check-digit validation.
    /// </summary>
    /// <param name="digits">An eleven-digit candidate sequence.</param>
    /// <returns><see langword="true"/> if both check digits hold.</returns>
    /// <remarks>
    /// Rule: 10th digit = ((sum of 1st, 3rd, 5th, 7th, 9th) × 7 − (sum of 2nd,
    /// 4th, 6th, 8th)) mod 10; 11th digit = (sum of the first ten digits) mod 10.
    /// The first digit cannot be zero.
    /// </remarks>
    public static bool IsValidTurkishNationalId(ReadOnlySpan<char> digits)
    {
        if (digits.Length != 11 || digits[0] == '0')
        {
            return false;
        }

        var oddSum = 0;
        var evenSum = 0;
        var firstTenSum = 0;

        for (var index = 0; index < 10; index++)
        {
            if (!char.IsAsciiDigit(digits[index]))
            {
                return false;
            }

            var value = digits[index] - '0';
            firstTenSum += value;

            // 🚨 Only the FIRST NINE digits go into the odd/even sums. The tenth
            // digit (index 9) is a check digit and cannot be its own formula's
            // input; including it in the sums would make every valid number
            // look invalid.
            if (index >= 9)
            {
                continue;
            }

            if (index % 2 == 0)
            {
                oddSum += value;
            }
            else
            {
                evenSum += value;
            }
        }

        if (!char.IsAsciiDigit(digits[10]))
        {
            return false;
        }

        // The subtraction can be negative; in C# a negative mod returns
        // negative, so 10 is added and the mod is taken again.
        var tenth = (((oddSum * 7) - evenSum) % 10 + 10) % 10;

        if (tenth != digits[9] - '0')
        {
            return false;
        }

        return firstTenSum % 10 == digits[10] - '0';
    }
}
