namespace AgentPrism;

/// <summary>
/// Katalogda listelenen bir agent'in ozet gorunumu. Arayuzun agent listesini
/// cizmek icin ihtiyac duydugu her seyi tasir, agent'i derlemeye gerek birakmaz.
/// </summary>
public sealed record AgentDescriptor
{
    /// <summary>Agent'in benzersiz adi.</summary>
    public required string Name { get; init; }

    /// <summary>Arayuzde gosterilecek ad.</summary>
    public string? DisplayName { get; init; }

    /// <summary>Kisa aciklama.</summary>
    public string? Description { get; init; }

    /// <summary>Tanimin kaynagi.</summary>
    public required AgentDefinitionOrigin Origin { get; init; }

    /// <summary>
    /// Bu agent'i saglayan kaynagin adi. Ornek: <c>code</c>, <c>database</c>.
    /// Ayni <see cref="Origin"/> degerine sahip birden cok kaynak olabilir.
    /// </summary>
    public required string SourceName { get; init; }

    /// <summary>Tanim surumu. Kod agent'larinda her zaman 1'dir.</summary>
    public int Version { get; init; } = 1;

    /// <summary>Baglanan model. Kod agent'larinda bilinmeyebilir.</summary>
    public ModelBinding? Model { get; init; }

    /// <summary>Bu agent'in kullanabilecegi tool adlari.</summary>
    public IReadOnlyList<string> ToolNames { get; init; } = [];

    /// <summary>Harness yetenekleri acik mi.</summary>
    public bool UsesHarness { get; init; }

    /// <summary>Son degistirilme zamani (UTC).</summary>
    public DateTimeOffset? UpdatedAt { get; init; }
}
