namespace AgentPrism;

/// <summary>Tek bir <see cref="EvalCase"/> eklemek icin tasiyici.</summary>
public sealed record EvalCaseDraft
{
    /// <summary>Agent'a gonderilecek sorgu metni.</summary>
    public required string Query { get; init; }

    /// <summary>Beklenen cikti.</summary>
    public string? ExpectedOutput { get; init; }

    /// <summary>Beklenen tool adlari.</summary>
    public IReadOnlyList<string> ExpectedTools { get; init; } = [];

    /// <summary>Modele ek baglam olarak verilecek metin.</summary>
    public string? Context { get; init; }

    /// <summary>Vakanin uretildigi calistirma. Elle eklenen vakalarda <see langword="null"/>.</summary>
    public Guid? SourceRunId { get; init; }

    /// <summary>Terfi sebebi. Elle eklenen vakalarda <see langword="null"/>.</summary>
    public EvalCaseSource? SourceKind { get; init; }
}
