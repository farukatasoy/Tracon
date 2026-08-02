namespace AgentPrism;

/// <summary>Denetim izi sorgusunu filtreleyen kriterler.</summary>
public sealed record AuditQuery
{
    /// <summary>Kiraci kimligi. <see langword="null"/> ise cagiranin kiracisi kullanilir.</summary>
    public string? TenantId { get; init; }

    /// <summary>Aktore gore filtre. <see langword="null"/> ise tumu.</summary>
    public string? Actor { get; init; }

    /// <summary>Eyleme gore filtre. Ornek: <c>agent.update</c>.</summary>
    public string? Action { get; init; }

    /// <summary>Varliga gore filtre. Ornek: <c>agent:support</c>.</summary>
    public string? Entity { get; init; }

    /// <summary>Bu zamandan sonraki kayitlar.</summary>
    public DateTimeOffset? After { get; init; }

    /// <summary>Bu zamandan onceki kayitlar.</summary>
    public DateTimeOffset? Before { get; init; }

    /// <summary>Donecek ust kayit sayisi.</summary>
    public int Limit { get; init; } = 100;
}
