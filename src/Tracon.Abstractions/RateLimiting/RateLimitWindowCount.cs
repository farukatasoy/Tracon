namespace Tracon;

/// <summary>The counter of one rate-limit partition after an <see cref="IRateLimitCounterStore.IncrementAsync"/> call.</summary>
public sealed record RateLimitWindowCount
{
    /// <summary>The start of the window the count belongs to (UTC).</summary>
    public required DateTimeOffset WindowStart { get; init; }

    /// <summary>The number of requests counted in the window, including the one that was just added.</summary>
    public required long Count { get; init; }
}
