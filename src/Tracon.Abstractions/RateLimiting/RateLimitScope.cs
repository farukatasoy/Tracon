namespace Tracon;

/// <summary>Where Tracon counts the requests of its rate limits.</summary>
public enum RateLimitScope
{
    /// <summary>
    /// The counter lives in the memory of each process. With several instances the limit applies per
    /// instance. This is the default; it adds no database call.
    /// </summary>
    Instance = 0,

    /// <summary>
    /// The counter lives in the <see cref="IRateLimitCounterStore"/>. With a SQL provider the limit
    /// applies to the whole deployment; each request costs one database write.
    /// </summary>
    Deployment = 1,
}
