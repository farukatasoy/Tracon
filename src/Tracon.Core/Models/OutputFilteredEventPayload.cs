namespace Tracon;

/// <summary>The <see cref="RunEventCustomTypes.OutputFiltered"/> event payload.</summary>
/// <remarks>
/// Names the provider only. The provider adapters report every filter stop as the one common
/// finish reason before the detector sees it, so the filter's own word (Anthropic <c>refusal</c>,
/// Gemini <c>SAFETY</c>) is not available here, and the answer text never is.
/// </remarks>
internal sealed record OutputFilteredEventPayload
{
    /// <summary>Gets the provider of the model binding whose call the content filter stopped.</summary>
    public required string Provider { get; init; }
}
