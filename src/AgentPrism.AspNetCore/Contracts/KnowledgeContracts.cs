namespace AgentPrism;

/// <summary>Bir belge yukleme istegi.</summary>
/// <remarks>
/// Ya <see cref="Text"/> ya <see cref="Chunks"/> verilir; ikisi birden ya da
/// hicbiri verilemez (bkz. <see cref="KnowledgeIngestionService.IngestAsync"/>).
/// </remarks>
public sealed record UploadDocumentRequest
{
    /// <summary>Kaynak kimligi. Ayni kimlikle yeniden yukleme eskiyi degistirir.</summary>
    public required string SourceId { get; init; }

    /// <summary>
    /// Ham metin. Verilirse sunucu parcalar ve gomuler.
    /// </summary>
    public string? Text { get; init; }

    /// <summary>
    /// Hazir parcalar. <see cref="UploadDocumentChunk.Embedding"/> bos birakilirsa
    /// sunucu gomuler; doldurulmussa OLDUGU GIBI yazilir.
    /// </summary>
    public IReadOnlyList<UploadDocumentChunk>? Chunks { get; init; }
}

/// <summary>Bir yukleme istegindeki tek bir hazir parca.</summary>
public sealed record UploadDocumentChunk
{
    /// <summary>Kaynak icindeki sira numarasi.</summary>
    public required int Index { get; init; }

    /// <summary>Parcanin metni.</summary>
    public required string Content { get; init; }

    /// <summary>
    /// Parcanin gomusu. Bos ise sunucu gomuler; doluysa oldugu gibi yazilir ve
    /// depo boyutuyla eslesmiyorsa 400 doner.
    /// </summary>
    public float[]? Embedding { get; init; }

    /// <summary>Istege bagli ustveri.</summary>
    public IReadOnlyDictionary<string, string>? Metadata { get; init; }
}

/// <summary>Bir belge yukleme isteginin sonucu.</summary>
/// <param name="SourceId">Yuklenen kaynagin kimligi.</param>
/// <param name="ChunkCount">Yazilan parca sayisi.</param>
public sealed record UploadDocumentResponse(string SourceId, int ChunkCount);

/// <summary>Bir anlamsal arama istegi.</summary>
public sealed record SearchKnowledgeRequest
{
    /// <summary>Aranacak dogal dil sorgusu.</summary>
    public required string Query { get; init; }

    /// <summary>Kac sonuc dondurulecegi. Verilmezse yapilandirmadaki varsayilan kullanilir.</summary>
    public int? Top { get; init; }
}

/// <summary>Bir anlamsal arama sonucu.</summary>
public sealed record SearchKnowledgeHit
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
