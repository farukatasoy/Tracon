namespace Tracon;

/// <summary>
/// Endpoint metadata that carries <see cref="TraconEndpointOptions.StreamKeepAliveInterval"/>
/// to <see cref="SseWriter"/>.
/// </summary>
/// <param name="Interval">The longest silence the stream may have.</param>
/// <remarks>
/// Put on the route group by <c>MapTracon</c>, so every streaming endpoint
/// under it, including one added later, reads the interval without its own
/// plumbing.
/// </remarks>
internal sealed record SseKeepAliveMetadata(TimeSpan Interval);
