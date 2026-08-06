namespace AgentPrism;

/// <summary>Bir model saglayicisinin teshis ozeti.</summary>
public sealed record ProviderDiagnostic
{
    /// <summary>Saglayici adi.</summary>
    public required string Name { get; init; }

    /// <summary>
    /// Model saglayicisi saglik onbelleginin son bilinen durumu
    /// (<see cref="ModelProviderHealthStatus"/> degeri, metin olarak). Onbellek
    /// bosysa <c>"Unknown"</c>.
    /// </summary>
    public required string Status { get; init; }

    /// <summary>Devre kesici bu saglayici icin acik mi.</summary>
    public required bool CircuitOpen { get; init; }
}
