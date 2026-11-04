namespace Tracon;

/// <summary>
/// The check every options validator makes of a text option: a value made only of white space is a mistake.
/// </summary>
/// <remarks>
/// <para>
/// A number, a flag or a duration whose key holds only white space is "not given" and keeps its default. A text option
/// used to keep the blanks as the value: <c>Audit:ChainKeyId = "   "</c> named a key called blanks, and a mask
/// replacement of blanks hid a match behind spaces. The value looks applied and means nothing, so the host refuses to
/// start and names the member.
/// </para>
/// <para>
/// Empty is a different value and is left to each option: the binder reads an empty text as "not given", and a few
/// options give the empty string a meaning of their own in code. The check is on the value the options hold, not on the
/// configuration text, so a value assigned in code is checked too.
/// </para>
/// </remarks>
internal static class OptionText
{
    /// <summary>Whether the text has characters and every one of them is white space.</summary>
    /// <param name="value">The option value.</param>
    /// <returns><see langword="true"/> for <c>"   "</c>; <see langword="false"/> for <see langword="null"/>, <c>""</c> and any text with a visible character.</returns>
    internal static bool IsOnlyWhiteSpace(string? value)
        => !string.IsNullOrEmpty(value) && string.IsNullOrWhiteSpace(value);

    /// <summary>Adds the failure for a text option made only of white space.</summary>
    /// <param name="value">The option value.</param>
    /// <param name="typeName">The options type name, for the message.</param>
    /// <param name="memberName">The member name, for the message.</param>
    /// <param name="failures">The failure list, created on the first failure.</param>
    internal static void AddWhenOnlyWhiteSpace(string? value, string typeName, string memberName, ref List<string>? failures)
    {
        if (IsOnlyWhiteSpace(value))
        {
            (failures ??= []).Add(Failure($"{typeName}.{memberName}", value!.Length));
        }
    }

    private static string Failure(string member, int length)
        => $"{member} is only white space ({length} characters). Write a value, or remove the key to keep the default.";
}
