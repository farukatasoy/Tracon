namespace Tracon;

/// <summary>
/// A non-token measurement and its cost: one tool call's, or one item a
/// workflow function node reported.
/// </summary>
/// <remarks>
/// <para>
/// The token cost model writes to the <c>runs</c> table. A tool or a function
/// node may spend no tokens but still incur a charge: text-to-speech is billed
/// by <em>character</em>, speech-to-text by <em>second</em>, a render by the
/// GPU second. The <see cref="Quantity"/> is never added across units — two
/// different units cannot be added. The <see cref="Cost"/> is: the amounts of one
/// currency add up into the run's <see cref="RunNonTokenCost"/>, which is a total
/// of its own beside the token cost. An item with no <see cref="Cost"/>, or one in
/// another currency, is listed and counted but not added.
/// </para>
/// <para>
/// A tool reports its measurement with <c>TraconToolUsage.Report(...)</c>; it
/// stays on the call's <see cref="ToolInvocationRecord"/>. A workflow function node
/// reports with <c>IWorkflowContext.ReportUsageAsync(...)</c>; every report is
/// an item of its own.
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

    /// <summary>
    /// The currency of <c>Cost</c>. Comes from <c>Tracon:Pricing:Currency</c>
    /// for Tracon's own tools.
    /// </summary>
    /// <remarks>
    /// An empty label means <c>Tracon:Pricing:Currency</c>. An amount adds to the run's
    /// total only when its currency is that one; any other currency is listed and
    /// counted as not added (<c>RunNonTokenCost.UnsummedItemCount</c>).
    /// </remarks>
    public string? Currency { get; init; }

    /// <summary>
    /// Whether the quantity was measured or estimated.
    /// </summary>
    /// <remarks>
    /// If the provider does not report the billed quantity, the tool produces
    /// an estimate (for example, the text's character count). Showing an
    /// estimate as a measurement fabricates a price, so a reader of the record must
    /// keep the two cases apart.
    /// </remarks>
    public bool IsEstimated { get; init; }

    /// <summary>
    /// The provider that did the work (for example <c>elevenlabs</c>), or
    /// <see langword="null"/> when the reporter does not name one.
    /// </summary>
    /// <remarks>
    /// A name for the reader of the record only. It takes no part in a total: the
    /// same unit at two providers is still one unit.
    /// </remarks>
    public string? Provider { get; init; }

    /// <summary>
    /// The model that did the work (for example <c>eleven_multilingual_v2</c>), or
    /// <see langword="null"/> when the reporter does not name one.
    /// </summary>
    public string? Model { get; init; }
}

/// <summary>
/// The known unit names for <see cref="ToolCallUsage.Unit"/>.
/// </summary>
/// <remarks>
/// The list is <strong>not</strong> closed: the field is free text, and a
/// tool may report its own unit. These constants only keep the names
/// Tracon's own tools use in one place.
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

    /// <summary>Calls. Used when a provider bills a hosted tool, such as a web search, by the call.</summary>
    public const string Calls = "calls";
}
