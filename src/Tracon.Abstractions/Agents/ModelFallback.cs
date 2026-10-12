namespace Tracon;

/// <summary>One link of a <see cref="ModelBinding.Fallbacks"/> chain.</summary>
/// <remarks>
/// Deliberately narrow: only the provider and the model name, not a full
/// <see cref="ModelBinding"/>. A fallback binding embedding another fallback
/// chain would produce a recursive type that is hard to validate and to
/// reason about. The fallback call receives the same request options as the
/// primary call — temperature, top-p, <c>maxOutputTokens</c>, the response format
/// and the tools — with only the model name replaced. The primary binding's
/// <see cref="ModelBinding.ProviderSettings"/> do not carry over: they shape the
/// primary provider's client, and the fallback provider's client is built
/// without them.
/// </remarks>
public sealed record ModelFallback
{
    /// <summary>Gets the provider name, for example <c>anthropic</c>.</summary>
    public required string Provider { get; init; }

    /// <summary>Gets the model name, for example <c>claude-opus-5</c>.</summary>
    public required string Model { get; init; }
}
