namespace Tracon;

/// <summary>Settings for the built-in pattern-based guard.</summary>
/// <remarks>
/// <para>
/// Read from the <c>Tracon:ContentGuard:Pattern</c> configuration section.
/// </para>
/// <para>
/// This class deliberately <strong>has no</strong> <c>Enabled</c> flag. The no-surprises rule's
/// gate is the registration itself: <c>AddTracon()</c> does not register the
/// built-in guard, so in a default setup the inspection wrapper is never added
/// to the pipeline and the cost is <em>exactly</em> zero. Adding an
/// <c>Enabled</c> flag would require the guard to be registered but turned off;
/// then the claim "if no guard is registered the cost is zero" would become
/// unmeasurable.
/// </para>
/// <para>
/// If you need to keep the guard running but temporarily disable it, empty
/// <see cref="DeniedTerms"/> and set <see cref="MaskedPii"/> to
/// <see cref="PiiPatterns.None"/>: the guard then sees no rule and returns
/// <see cref="ContentGuardResult.Allow"/> on the first inspection.
/// </para>
/// </remarks>
public sealed class PatternContentGuardOptions
{
    /// <summary>The configuration section name.</summary>
    public const string SectionName = "Tracon:ContentGuard:Pattern";

    /// <summary>
    /// Denied terms. A match produces <see cref="ContentGuardAction.Block"/>.
    /// </summary>
    /// <remarks>
    /// The comparison is case-insensitive and searches for a word fragment
    /// (<c>IndexOf</c>); it is not a regex, so a value the consumer writes
    /// carries no ReDoS risk.
    /// </remarks>
    public IList<string> DeniedTerms { get; } = [];

    /// <summary>
    /// The built-in PII patterns to turn on. A match produces
    /// <see cref="ContentGuardAction.Mask"/>. Default <see cref="PiiPatterns.None"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// In configuration, a comma-separated list of names (<c>"Email,CreditCard"</c>).
    /// An empty value leaves the setting unset. A value that is not such a list stops
    /// the application at startup with an <see cref="Microsoft.Extensions.Options.OptionsValidationException"/>
    /// that names the value as written; one misspelled name used to turn masking off
    /// for the whole list without a word.
    /// </para>
    /// <para>
    /// A value with a bit that no <see cref="PiiPatterns"/> member defines (for
    /// example <c>(PiiPatterns)64</c>) stops the application too, whether it comes
    /// from configuration or from code. A value set in code after binding replaces
    /// the configured one, so it also clears a configuration error.
    /// </para>
    /// </remarks>
    public PiiPatterns MaskedPii { get; set; } = PiiPatterns.None;

    /// <summary>
    /// The text written in place of a masked match. Default <c>[redacted]</c>.
    /// </summary>
    public string MaskReplacement { get; set; } = "[redacted]";
}
