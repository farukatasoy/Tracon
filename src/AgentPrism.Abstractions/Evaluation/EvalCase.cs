namespace AgentPrism;

/// <summary>Bir <see cref="EvalSuite"/> icindeki tek bir test vakasi.</summary>
public sealed record EvalCase
{
    /// <summary>Vaka kimligi.</summary>
    public Guid Id { get; init; }

    /// <summary>Ait oldugu takimin kimligi.</summary>
    public required Guid SuiteId { get; init; }

    /// <summary>Takim icindeki sira numarasi (0'dan baslar).</summary>
    public required int Seq { get; init; }

    /// <summary>Agent'a gonderilecek sorgu metni.</summary>
    public required string Query { get; init; }

    /// <summary>
    /// Beklenen cikti. <c>containsExpected</c> denetiminde referans alinir.
    /// </summary>
    public string? ExpectedOutput { get; init; }

    /// <summary>
    /// <c>toolCalled</c> denetiminde aranan tool adlari. Bos liste, denetimin
    /// tum tool cagrilarini kabul edecegi anlamina gelmez — denetim yine de
    /// suite'in <c>checks</c> alaninda ayri ayri tanimlanir.
    /// </summary>
    public IReadOnlyList<string> ExpectedTools { get; init; } = [];

    /// <summary>Modele ek baglam olarak verilecek metin.</summary>
    public string? Context { get; init; }
}
