using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace Tracon;

/// <summary>Reads the text of one configuration key as a value type.</summary>
/// <typeparam name="T">The value type.</typeparam>
/// <param name="text">The non-empty text the operator wrote.</param>
/// <param name="value">The value, when the text is readable.</param>
/// <returns>Whether the text is readable.</returns>
internal delegate bool TryReadText<T>(string text, out T value);

/// <summary>
/// How the binders read the text of a configuration key, and what each reading expects
/// Numbers use <see cref="CultureInfo.InvariantCulture"/>.
/// </summary>
/// <remarks>
/// A decimal does not take a thousands separator: <c>NumberStyles.Number</c> read
/// <c>"1,5"</c> as fifteen, the one wrong reading that never failed. The separator is
/// <c>.</c>, as in the sample configuration.
/// </remarks>
internal static class ConfigurationValueKinds
{
    internal const string Int32Expectation = "a 32-bit whole number";

    internal const string Int64Expectation = "a 64-bit whole number";

    internal const string DoubleExpectation = "a number";

    internal const string DecimalExpectation = "a number written with '.' as the decimal separator";

    internal const string BooleanExpectation = "true or false";

    internal const string TimeSpanExpectation = "a time span such as 00:00:30";

    internal const string AbsoluteUriExpectation = "an absolute address such as https://example.com/";

    private const NumberStyles DecimalStyles =
        NumberStyles.AllowLeadingWhite | NumberStyles.AllowTrailingWhite | NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint;

    internal static bool TryInt32(string text, out int value)
        => int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);

    internal static bool TryInt64(string text, out long value)
        => long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);

    internal static bool TrySingle(string text, out float value)
        => float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);

    internal static bool TryDouble(string text, out double value)
        => double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);

    internal static bool TryDecimal(string text, out decimal value)
        => decimal.TryParse(text, DecimalStyles, CultureInfo.InvariantCulture, out value);

    internal static bool TryBoolean(string text, out bool value)
        => bool.TryParse(text, out value);

    internal static bool TryTimeSpan(string text, out TimeSpan value)
        => TimeSpan.TryParse(text, CultureInfo.InvariantCulture, out value);

    internal static bool TryAbsoluteUri(string text, [NotNullWhen(true)] out Uri? value)
        => Uri.TryCreate(text, UriKind.Absolute, out value);

    /// <summary>
    /// Reads an enum name or number. A plain enum accepts only one defined member: the parser
    /// takes any number, so <c>"7"</c> would otherwise become a member that does not exist, and
    /// it ORs a comma-separated list, so <c>"Tenant,Global"</c> would become a third member.
    /// A <c>[Flags]</c> enum keeps the parser's reading; its undefined bits are the options
    /// validator's to refuse.
    /// </summary>
    /// <typeparam name="TEnum">The enum type.</typeparam>
    /// <param name="text">The text.</param>
    /// <param name="value">The member.</param>
    /// <returns>Whether the text is readable.</returns>
    internal static bool TryEnum<TEnum>(string text, out TEnum value)
        where TEnum : struct, Enum
    {
        if (!Enum.TryParse(text, ignoreCase: true, out value))
        {
            return false;
        }

        return IsFlags<TEnum>()
            || (!text.Contains(',', StringComparison.Ordinal) && Enum.IsDefined(value));
    }

    /// <summary>
    /// Reads an enum like <see cref="TryEnum{TEnum}"/>, and a <c>[Flags]</c> value must also hold defined
    /// bits only. The parser takes any number, so <c>"256"</c> would otherwise become a set that names no
    /// member and checks nothing, where the operator meant a field.
    /// </summary>
    /// <typeparam name="TEnum">The enum type.</typeparam>
    /// <param name="text">The text.</param>
    /// <param name="value">The member, or the set of members.</param>
    /// <returns>Whether the text is readable.</returns>
    internal static bool TryDefinedEnum<TEnum>(string text, out TEnum value)
        where TEnum : struct, Enum
    {
        if (!TryEnum(text, out value))
        {
            return false;
        }

        if (!IsFlags<TEnum>())
        {
            return true;
        }

        var defined = 0L;

        foreach (var member in Enum.GetValues<TEnum>())
        {
            defined |= Convert.ToInt64(member, CultureInfo.InvariantCulture);
        }

        return (Convert.ToInt64(value, CultureInfo.InvariantCulture) & ~defined) == 0;
    }

    /// <summary>Describes the members of an enum, completing "which is not …".</summary>
    /// <typeparam name="TEnum">The enum type.</typeparam>
    /// <returns>The description.</returns>
    internal static string EnumExpectation<TEnum>()
        where TEnum : struct, Enum
        => Enum.GetNames<TEnum>() is [var only]
            ? $"the name '{only}'"
            : (IsFlags<TEnum>() ? "a comma-separated list of " : "one of ") + string.Join(", ", Enum.GetNames<TEnum>());

    private static bool IsFlags<TEnum>()
        where TEnum : struct, Enum
        => typeof(TEnum).IsDefined(typeof(FlagsAttribute), inherit: false);
}
