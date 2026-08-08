namespace AgentPrism;

/// <summary>Yazilacak tek bir parca.</summary>
public sealed record VectorChunk
{
    /// <summary>Kaynak icindeki sira numarasi (0 tabanli).</summary>
    public required int Index { get; init; }

    /// <summary>Parcanin metni.</summary>
    public required string Content { get; init; }

    /// <summary>
    /// Parcanin gomusu. 🚨 Uzunlugu <see cref="IVectorSearchStore.Dimensions"/>
    /// ile AYNI olmalidir; degilse yazma hata verir.
    /// </summary>
    public required ReadOnlyMemory<float> Embedding { get; init; }

    /// <summary>Istege bagli ustveri. Suzme icin kullanilabilir.</summary>
    public IReadOnlyDictionary<string, string>? Metadata { get; init; }
}
