using Microsoft.Extensions.AI;

namespace AgentPrism;

/// <summary>Bir calistirmanin kayitli girdisinin HTTP yaniti.</summary>
public sealed record RunInputResponse
{
    /// <summary>Calistirma kimligi.</summary>
    public required Guid RunId { get; init; }

    /// <summary>Kaydin olusturulma zamani (UTC).</summary>
    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>
    /// Girdi mesajlari. Polimorfik icerikleriyle birlikte, kaydedildikleri gibi.
    /// </summary>
    public required IReadOnlyList<ChatMessage> Messages { get; init; }
}

/// <summary>Bir yeniden oynatmanin sonucu.</summary>
public sealed record RunReplayResponse
{
    /// <summary>Acilan yeni calistirmanin kimligi.</summary>
    public required Guid RunId { get; init; }

    /// <summary>Kaynak calistirmanin kimligi. <c>runs.replay_of_run_id</c> ile aynidir.</summary>
    public required Guid SourceRunId { get; init; }

    /// <summary>Uygulanan tool modu.</summary>
    public required ReplayToolMode ToolMode { get; init; }

    /// <summary>Kullanilan tanim surumu. Kod agent'inda <see langword="null"/>.</summary>
    public int? AgentVersion { get; init; }

    /// <summary>Kullanilan model.</summary>
    public string? ModelId { get; init; }

    /// <summary>Modelin urettigi metin.</summary>
    public string? Output { get; init; }

    /// <summary>Iki calistirmayi yan yana koyan ucun adresi.</summary>
    public required string CompareLocation { get; init; }
}

/// <summary>Iki calistirmanin yan yana ozeti.</summary>
/// <remarks>
/// 🚨 Fark <strong>sunucuda hesaplanmaz</strong>; uc yalnizca iki ozeti dondurur
/// ve karsilastirmayi arayuz gosterir. Faz 19'un tanim surumu diff'i ayni deseni
/// izler ve arayuzde zaten bir diff bileseni vardir; ikinci bir hesap iki yerde
/// bakim demektir.
/// </remarks>
public sealed record RunComparisonResponse
{
    /// <summary>Soldaki calistirma.</summary>
    public required RunComparisonSide Left { get; init; }

    /// <summary>Sagdaki calistirma.</summary>
    public required RunComparisonSide Right { get; init; }
}

/// <summary>Karsilastirmanin bir tarafi.</summary>
public sealed record RunComparisonSide
{
    /// <summary>Calistirma kimligi.</summary>
    public required Guid RunId { get; init; }

    /// <summary>Agent adi.</summary>
    public required string AgentName { get; init; }

    /// <summary>Tanim surumu. Bilinmiyorsa <see langword="null"/>.</summary>
    public int? AgentVersion { get; init; }

    /// <summary>Kullanilan model.</summary>
    public string? ModelId { get; init; }

    /// <summary>Son durum.</summary>
    public required RunStatus Status { get; init; }

    /// <summary>Sure (milisaniye). Calistirma bitmediyse <see langword="null"/>.</summary>
    public long? DurationMs { get; init; }

    /// <summary>Token kullanimi.</summary>
    public RunUsage? Usage { get; init; }

    /// <summary>Maliyet.</summary>
    public RunCost? Cost { get; init; }

    /// <summary>Tool cagrisi sayisi.</summary>
    public required int ToolCallCount { get; init; }

    /// <summary>Hata sinifi. Basarili calistirmada <see langword="null"/>.</summary>
    public RunErrorClass? ErrorClass { get; init; }

    /// <summary>Hata mesaji. Basarili calistirmada <see langword="null"/>.</summary>
    public string? ErrorMessage { get; init; }

    /// <summary>Bu calistirma bir yeniden oynatma ise kaynagi.</summary>
    public Guid? ReplayOfRunId { get; init; }

    /// <summary>
    /// Modelin urettigi metin.
    /// </summary>
    /// <remarks>
    /// 🚨 Akissiz yol <c>MessageCompleted</c> yazar, akisli yol yalnizca
    /// <c>MessageDelta</c> uretir (bkz. <c>docs/hafiza/cekirdek-calistirma.md</c>).
    /// Ikisi TOPLANMAZ: <c>MessageCompleted</c> varsa o kullanilir, yoksa
    /// parcalar birlestirilir — aksi halde akissiz yolda metin iki kez sayilir.
    /// </remarks>
    public string? Output { get; init; }

    /// <summary>Bu calistirmaya yazilmis puanlar.</summary>
    public IReadOnlyList<RunScore> Scores { get; init; } = [];
}
