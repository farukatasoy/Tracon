using System.Text.Json;

namespace AgentPrism;

/// <summary>Bir eval takimi olusturma/guncelleme istegi.</summary>
/// <remarks>Ad <em>yoldan</em> gelir, govdeden degil — <see cref="JobScheduleSaveRequest"/> ile ayni gerekce.</remarks>
public sealed record EvalSuiteSaveRequest
{
    /// <summary>Kisa aciklama.</summary>
    public string? Description { get; init; }

    /// <summary>Bu takimin olctugu agent'in adi.</summary>
    public required string AgentName { get; init; }

    /// <summary>Denetim tanimlari. Bkz. <see cref="EvalSuite.Checks"/>.</summary>
    public JsonElement Checks { get; init; }
}

/// <summary>Bir eval vakasinin girdi bicimi (istekte).</summary>
public sealed record EvalCaseInput
{
    /// <summary>Agent'a gonderilecek sorgu metni.</summary>
    public required string Query { get; init; }

    /// <summary>Beklenen cikti.</summary>
    public string? ExpectedOutput { get; init; }

    /// <summary><c>toolCalled</c> denetiminde aranan tool adlari.</summary>
    public IReadOnlyList<string> ExpectedTools { get; init; } = [];

    /// <summary>Modele ek baglam olarak verilecek metin.</summary>
    public string? Context { get; init; }
}

/// <summary>Bir eval kosusunu hemen tetikleme istegi.</summary>
public sealed record EvalRunTriggerRequest
{
    /// <summary>
    /// Bu kosu icin kaydedilecek model kimligi. Verilmezse agent'in guncel
    /// tanimindaki model kullanilir.
    /// </summary>
    public string? ModelId { get; init; }

    /// <summary>
    /// Her vakanin kararliligini olcmek icin kac kez tekrarlanacagi. Verilmezse 1.
    /// </summary>
    public int? NumRepetitions { get; init; }
}

/// <summary>Tek bir eval kosusunun ayrintili gorunumu: ozet ve vaka sonuclari birlikte.</summary>
public sealed record EvalRunDetailResponse
{
    /// <summary>Kosu ozeti.</summary>
    public required EvalRun Run { get; init; }

    /// <summary>Vaka bazinda sonuclar.</summary>
    public required IReadOnlyList<EvalCaseResult> Results { get; init; }
}
