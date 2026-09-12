using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace Tracon;

/// <summary>
/// Tracon's built-in pattern-based content guard.
/// </summary>
/// <remarks>
/// <para>
/// The extension point alone is not enough: the rule is that an in-memory
/// implementation is first-class," and without a built-in implementation
/// <see cref="IContentGuard"/> would be an empty promise. It carries three
/// pattern families: a denied-term list (<see cref="ContentGuardAction.Block"/>),
/// PII patterns, and <c>secret</c> patterns (<see cref="ContentGuardAction.Mask"/>).
/// </para>
/// <para>
/// <strong>Every pattern is written with the source generator</strong>
/// (<see cref="GeneratedRegexAttribute"/>). <c>Tracon.Core</c> is
/// AOT-compatible; a <see cref="Regex"/> compiled at runtime would break that.
/// </para>
/// <para>
/// <strong>Every pattern carries a timeout</strong> (1000 ms). This is the
/// only defense against ReDoS and is mandatory on the hot path.
/// </para>
/// <para>
/// Card and Turkish national ID patterns are validated with <see cref="CheckDigits"/>.
/// Without it, every order number would be masked and the guard would get turned off.
/// </para>
/// <para>
/// Allocation order: a match is looked for first with <c>IsMatch</c> /
/// <c>EnumerateMatches</c>, and a new string is produced <strong>only if there
/// is a match</strong>. If no rule is defined, <see cref="ContentGuardResult.Allow"/>
/// is returned on the first line.
/// </para>
/// <para>
/// <strong>This guard deliberately ignores <see cref="ContentGuardContext.Source"/>.</strong>
/// The same denied-term and PII patterns apply no matter where the text came
/// from, including <see cref="ContentGuardSource.Unknown"/> — an unclassified
/// source is never treated as a reason to skip a check. A guard that wants
/// source-dependent strictness (for example, blocking a pattern only in
/// <see cref="ContentGuardSource.ToolResult"/>) reads <see cref="ContentGuardContext.Source"/>
/// in its own <see cref="IContentGuard.InspectAsync"/> implementation.
/// </para>
/// </remarks>
internal sealed partial class PatternContentGuard : IContentGuard
{
    private readonly IOptionsMonitor<PatternContentGuardOptions> _options;

    /// <summary>Creates a new built-in guard.</summary>
    /// <param name="options">Pattern and denied-term settings.</param>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is <see langword="null"/>.</exception>
    public PatternContentGuard(IOptionsMonitor<PatternContentGuardOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);

        _options = options;
    }

    /// <inheritdoc />
    public string Name => "pattern";

    /// <inheritdoc />
    public ValueTask<ContentGuardResult> InspectAsync(
        ContentGuardContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        var options = _options.CurrentValue;

        // If no rule is defined, no pattern runs. This is how you leave the
        // guard registered while temporarily disabling it.
        if (options.DeniedTerms.Count == 0 && options.MaskedPii == PiiPatterns.None)
        {
            return ValueTask.FromResult(ContentGuardResult.Allow);
        }

        var text = context.Text;

        foreach (var term in options.DeniedTerms)
        {
            if (!string.IsNullOrEmpty(term) &&
                text.Contains(term, StringComparison.OrdinalIgnoreCase))
            {
                // 🚨 The reason text carries neither the blocked content nor the
                // denied term itself: the term list is a corporate secret too
                // ("secret-project" could be a code name).
                return ValueTask.FromResult(ContentGuardResult.Block(
                    "denied-term",
                    "Content matched the configured denied-term list."));
            }
        }

        return ValueTask.FromResult(Mask(text, options));
    }

    private static ContentGuardResult Mask(string text, PatternContentGuardOptions options)
    {
        var masked = text;
        List<string>? rules = null;

        if (options.MaskedPii.HasFlag(PiiPatterns.ProviderApiKey))
        {
            Apply(ApiKeyPattern(), "provider-api-key", CheckDigitKind.None, options, ref masked, ref rules);
        }

        if (options.MaskedPii.HasFlag(PiiPatterns.CreditCard))
        {
            Apply(CreditCardPattern(), "credit-card", CheckDigitKind.Luhn, options, ref masked, ref rules);
        }

        // The Turkish national ID check runs AFTER the card check: a 16-digit
        // card number can contain a valid 11-digit ID sequence inside it, and
        // if the card is masked first, that false match never occurs.
        if (options.MaskedPii.HasFlag(PiiPatterns.TurkishNationalId))
        {
            Apply(TurkishNationalIdPattern(), "turkish-national-id", CheckDigitKind.TurkishNationalId, options, ref masked, ref rules);
        }

        if (options.MaskedPii.HasFlag(PiiPatterns.Iban))
        {
            Apply(IbanPattern(), "iban", CheckDigitKind.None, options, ref masked, ref rules);
        }

        if (options.MaskedPii.HasFlag(PiiPatterns.Email))
        {
            Apply(EmailPattern(), "email", CheckDigitKind.None, options, ref masked, ref rules);
        }

        return rules is null
            ? ContentGuardResult.Allow
            : ContentGuardResult.Mask(masked, string.Join(',', rules));
    }

    /// <summary>
    /// Applies a pattern. If there is no match, <strong>no string is produced</strong>.
    /// </summary>
    /// <remarks>
    /// If <paramref name="check"/> is not <see cref="CheckDigitKind.None"/>, a
    /// match must pass a check-digit validation; a match that does not pass is
    /// left as is. This is why "is there a valid match" is answered
    /// allocation-free first, and <c>Replace</c> is only called afterward.
    /// </remarks>
    private static void Apply(
        Regex pattern,
        string ruleName,
        CheckDigitKind check,
        PatternContentGuardOptions options,
        ref string text,
        ref List<string>? rules)
    {
        if (!HasMatch(pattern, text, check))
        {
            return;
        }

        var replacement = options.MaskReplacement;

        text = check == CheckDigitKind.None
            ? pattern.Replace(text, replacement)
            : pattern.Replace(text, match => IsValid(check, match.ValueSpan) ? replacement : match.Value);

        (rules ??= []).Add(ruleName);
    }

    private static bool HasMatch(Regex pattern, string text, CheckDigitKind check)
    {
        if (check == CheckDigitKind.None)
        {
            return pattern.IsMatch(text);
        }

        // EnumerateMatches allocates nothing: it returns only position and length.
        foreach (var match in pattern.EnumerateMatches(text))
        {
            if (IsValid(check, text.AsSpan(match.Index, match.Length)))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsValid(CheckDigitKind check, ReadOnlySpan<char> value)
        => check == CheckDigitKind.TurkishNationalId
            ? CheckDigits.IsValidTurkishNationalId(value)
            : CheckDigits.IsValidLuhn(value);

    [GeneratedRegex(
        @"[A-Za-z0-9._%+\-]+@[A-Za-z0-9\-]+(?:\.[A-Za-z0-9\-]+)*\.[A-Za-z]{2,}",
        RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: 1000)]
    private static partial Regex EmailPattern();

    [GeneratedRegex(
        @"\b[A-Z]{2}[0-9]{2}[A-Z0-9]{11,30}\b",
        RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: 1000)]
    private static partial Regex IbanPattern();

    // Two forms: 13-19 unbroken digits, or groups of four. Both must pass the
    // Luhn check; the pattern alone does not decide.
    [GeneratedRegex(
        @"\b[0-9]{4}(?:[ \-][0-9]{4}){2,4}\b|\b[0-9]{13,19}\b",
        RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: 1000)]
    private static partial Regex CreditCardPattern();

    [GeneratedRegex(
        @"\b[1-9][0-9]{10}\b",
        RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: 1000)]
    private static partial Regex TurkishNationalIdPattern();

    [GeneratedRegex(
        @"\bsk-[A-Za-z0-9_\-]{16,}|\bghp_[A-Za-z0-9]{20,}|\bAKIA[0-9A-Z]{16}\b",
        RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: 1000)]
    private static partial Regex ApiKeyPattern();
}
