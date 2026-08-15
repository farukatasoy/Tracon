namespace AgentPrism;

/// <summary>Settings for the built-in pattern-based guard — Phase 48.</summary>
/// <remarks>
/// <para>
/// Read from the <c>AgentPrism:ContentGuard:Pattern</c> configuration section.
/// </para>
/// <para>
/// 🚨 This class deliberately <strong>has no</strong> <c>Enabled</c> flag. K1's
/// gate is the registration itself: <c>AddAgentPrism()</c> does not register the
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
    public const string SectionName = "AgentPrism:ContentGuard:Pattern";

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
    public PiiPatterns MaskedPii { get; set; } = PiiPatterns.None;

    /// <summary>
    /// The text written in place of a masked match. Default <c>[redacted]</c>.
    /// </summary>
    public string MaskReplacement { get; set; } = "[redacted]";
}
