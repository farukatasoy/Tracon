namespace Tracon;

/// <summary>Summarizes diagnostics for a model provider.</summary>
public sealed record ProviderDiagnostic
{
    /// <summary>Gets the provider name.</summary>
    public required string Name { get; init; }

    /// <summary>
    /// Gets the last known model provider health cache status as a
    /// <see cref="ModelProviderHealthStatus"/> value in text form. Returns
    /// <c>"Unknown"</c> when the cache is empty.
    /// </summary>
    public required string Status { get; init; }

    /// <summary>Gets whether the circuit breaker is open for this provider.</summary>
    public required bool CircuitOpen { get; init; }
}
