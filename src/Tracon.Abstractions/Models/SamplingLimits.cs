namespace Tracon;

/// <summary>
/// The value ranges a provider's API surface accepts for the <see cref="SamplingFields"/> it sends.
/// </summary>
/// <remarks>
/// <para>
/// A provider states them through <see cref="ISamplingSupport.SamplingLimits"/>. A definition that sets a
/// value outside a stated range stops with an error that names the field, the value and the range, on the
/// primary provider and on every <see cref="ModelBinding.Fallbacks"/> link. Without the check the request
/// either returns the provider's <c>400</c> at the first run, or runs with another value: a client library
/// that narrows a 64-bit seed to 32 bits wraps a larger one into a different number and says nothing.
/// </para>
/// <para>
/// A member left <see langword="null"/> states no limit and checks nothing. State only a limit that you
/// measured or that the provider documents: a range that is too narrow rejects a definition that would run.
/// The limits describe the provider as registered, so every tenant gets the same ones, including a tenant
/// that brings its own credential or endpoint.
/// </para>
/// </remarks>
public sealed record SamplingLimits
{
    /// <summary>Gets the limits of a provider that states none: no value is checked.</summary>
    public static SamplingLimits None { get; } = new();

    /// <summary>
    /// Gets the smallest <see cref="ModelBinding.Seed"/> the surface sends unchanged, or
    /// <see langword="null"/> for no lower limit.
    /// </summary>
    public long? MinSeed { get; init; }

    /// <summary>
    /// Gets the largest <see cref="ModelBinding.Seed"/> the surface sends unchanged, or
    /// <see langword="null"/> for no upper limit.
    /// </summary>
    public long? MaxSeed { get; init; }

    /// <summary>
    /// Gets the largest number of <see cref="ModelBinding.StopSequences"/> a request may carry, or
    /// <see langword="null"/> for no limit.
    /// </summary>
    public int? MaxStopSequences { get; init; }
}
