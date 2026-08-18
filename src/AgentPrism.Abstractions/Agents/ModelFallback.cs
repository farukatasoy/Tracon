namespace AgentPrism;

/// <summary>One link of a <see cref="ModelBinding.Fallbacks"/> chain.</summary>
/// <remarks>
/// Deliberately narrow: only the provider and the model name, not a full
/// <see cref="ModelBinding"/>. A fallback binding embedding another fallback
/// chain would produce a recursive type that is hard to validate and to
/// reason about; the fallback call uses the fallback model's own defaults for
/// every other setting.
/// </remarks>
public sealed record ModelFallback
{
    /// <summary>Gets the provider name, for example <c>anthropic</c>.</summary>
    public required string Provider { get; init; }

    /// <summary>Gets the model name, for example <c>claude-opus-5</c>.</summary>
    public required string Model { get; init; }
}
