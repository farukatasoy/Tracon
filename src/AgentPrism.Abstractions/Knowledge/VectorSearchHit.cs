namespace AgentPrism;

/// <summary>Bir arama sonucu.</summary>
public sealed record VectorSearchHit
{
    /// <summary>Parcanin ait oldugu kaynak kimligi.</summary>
    public required string SourceId { get; init; }

    /// <summary>Kaynak icindeki sira numarasi.</summary>
    public required int ChunkIndex { get; init; }

    /// <summary>Parcanin metni.</summary>
    public required string Content { get; init; }

    /// <summary>Kosinus mesafesi. Kucuk deger daha yakin demektir.</summary>
    public required double Distance { get; init; }

    /// <summary>Yazilirken verilen istege bagli ustveri.</summary>
    public IReadOnlyDictionary<string, string>? Metadata { get; init; }
}
