using Microsoft.Extensions.AI;

namespace AgentPrism;

/// <summary>
/// Tamamlanmis bir uretim calistirmasini puanlayan genisleme noktasi (Faz 49).
/// </summary>
/// <remarks>
/// <para>
/// 🚨 Bu arayuz MAF'in <c>AIJudgeLoopEvaluator</c>'ini SARMALAMAZ. Olculdu
/// (MAF 1.16.0): <c>LoopEvaluation</c> bir PUAN dondurmez (yalnizca
/// <c>ShouldReinvoke</c> ve <c>Feedback</c>) ve <c>LoopContext</c> canli bir
/// <c>AIAgent</c> + <c>AgentSession</c> ister. Bitmis bir calistirmayi
/// puanlamak icin uygun degildir — bkz. <c>docs/KARARLAR.md</c>, K-140'in
/// yeniden acilma karari.
/// </para>
/// <para>K4: kayit <c>TryAddEnumerable</c> ile; birden fazla yargic ayni calistirmayi puanlayabilir.</para>
/// <para>
/// Bir kurulumda birden fazla <see cref="IRunJudge"/> kayitliysa cevrimici
/// degerlendirme isi hepsini calistirir; her biri kendi <see cref="RunScore"/>
/// satirini <c>Source = judge:{Name}</c> ile yazar.
/// </para>
/// </remarks>
public interface IRunJudge
{
    /// <summary>Yargicin adi. <see cref="RunScore.Source"/> alanina <c>judge:{Name}</c> yazilir.</summary>
    string Name { get; }

    /// <summary>Calistirmayi puanlar.</summary>
    /// <param name="context">Yargicin gordugu baglam.</param>
    /// <param name="cancellationToken">Iptal belirteci.</param>
    /// <returns>Yargicin karari.</returns>
    ValueTask<RunJudgment> JudgeAsync(
        RunJudgeContext context,
        CancellationToken cancellationToken = default);
}

/// <summary>Yargicin gordugu baglam.</summary>
public sealed record RunJudgeContext
{
    /// <summary>Puanlanan calistirmanin kimligi.</summary>
    public required Guid RunId { get; init; }

    /// <summary>Calistirmanin ait oldugu kiraci.</summary>
    public required string TenantId { get; init; }

    /// <summary>Calistirilan agent'in adi.</summary>
    public required string AgentName { get; init; }

    /// <summary>
    /// Calistirmanin girdisi.
    /// </summary>
    /// <remarks>
    /// 🚨 <c>run_inputs</c>'tan okunur (Faz 47, <see cref="IRunInputStore"/>);
    /// kayit yoksa calistirma orneklenmez ve bu tip hic uretilmez.
    /// </remarks>
    public required IReadOnlyList<ChatMessage> Input { get; init; }

    /// <summary>Calistirmanin cikti metni.</summary>
    public required string Output { get; init; }

    /// <summary>Cagrilan tool adlari. Bazi olcutler bunu ister.</summary>
    public IReadOnlyList<string> ToolNames { get; init; } = [];
}

/// <summary>Yargicin karari.</summary>
public sealed record RunJudgment
{
    /// <summary>Puan, 0-100. Yargic karar veremediyse <see langword="null"/>.</summary>
    /// <remarks>
    /// 🚨 Karar verilemedigi durumda <c>0</c> DEGIL <see langword="null"/>
    /// dondurulur. Sifir bir olcumdur; olcum yoklugu degildir.
    /// </remarks>
    public int? Score { get; init; }

    /// <summary>Kisa gerekce. <see cref="RunScore.Comment"/> alanina yazilir.</summary>
    public string? Reason { get; init; }

    /// <summary>Yargicin kendi model kullanimi. Maliyet raporuna girer.</summary>
    public RunUsage? JudgeUsage { get; init; }
}
