namespace AgentPrism;

/// <summary>
/// A single tool call's non-token measurement and cost.
/// </summary>
/// <remarks>
/// <para>
/// The cost model assumes tokens and writes to the <c>runs</c> table. A tool may spend
/// no tokens but still incur a charge: text-to-speech is billed by <em>character</em>,
/// speech-to-text by <em>second</em>. These measurements are <strong>not
/// summed</strong> with token cost — two different units cannot be added. They are
/// shown as a separate line item in reports.
/// </para>
/// <para>
/// The measurement is reported by the tool itself:
/// <c>AgentPrismToolUsage.Report(...)</c>.
/// </para>
/// </remarks>
public sealed record ToolCallUsage
{
    /// <summary>The measurement unit. See <see cref="ToolUsageUnits"/> for known values.</summary>
    public required string Unit { get; init; }

    /// <summary>The billed quantity.</summary>
    public required decimal Quantity { get; init; }

    /// <summary>
    /// The computed amount. Stays <see langword="null"/> if the
    /// configuration has no price for this tool — <strong>not</strong> zero.
    /// </summary>
    public decimal? Cost { get; init; }

    /// <summary>The currency. Comes from <c>AgentPrism:Pricing:Currency</c>.</summary>
    public string? Currency { get; init; }

    /// <summary>
    /// Whether the quantity was measured or estimated.
    /// </summary>
    /// <remarks>
    /// If the provider does not report the billed quantity, the tool produces
    /// an estimate (for example, the text's character count). Showing an
    /// estimate as a measurement fabricates a price; the UI distinguishes the two cases.
    /// </remarks>
    public bool IsEstimated { get; init; }
}

/// <summary>
/// The known unit names for <see cref="ToolCallUsage.Unit"/>.
/// </summary>
/// <remarks>
/// The list is <strong>not</strong> closed: the field is free text, and a
/// tool may report its own unit. These constants only keep the names
/// AgentPrism's own tools use in one place.
/// </remarks>
public static class ToolUsageUnits
{
    /// <summary>Characters. Used in text-to-speech generation.</summary>
    public const string Characters = "characters";

    /// <summary>Seconds. Used in speech-to-text conversion.</summary>
    public const string Seconds = "seconds";

    /// <summary>Generated images. Used when an image provider bills per image.</summary>
    public const string Images = "images";

    /// <summary>Output tokens. Used when an image provider bills image generation by tokens.</summary>
    public const string Tokens = "tokens";
}
