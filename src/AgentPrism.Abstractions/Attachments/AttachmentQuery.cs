namespace AgentPrism;

/// <summary>Ek listeleme filtresi.</summary>
public sealed record AttachmentQuery
{
    /// <summary>Sorgulanacak kiraci.</summary>
    public required string TenantId { get; init; }

    /// <summary>Verilmisse yalniz bu oturuma ait ekler.</summary>
    public string? SessionId { get; init; }

    /// <summary>Atlanacak kayit sayisi.</summary>
    public int Skip { get; init; }

    /// <summary>Donulecek en fazla kayit sayisi.</summary>
    public int Take { get; init; } = 50;
}
