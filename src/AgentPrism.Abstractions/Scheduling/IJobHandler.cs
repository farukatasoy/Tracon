namespace AgentPrism;

/// <summary>
/// Belirli bir <see cref="JobKind"/> icin is yurutme mantigini saglayan
/// genisleme noktasi.
/// </summary>
/// <remarks>
/// <c>AddJobHandler&lt;T&gt;()</c> ile kaydedilir. AgentPrism.Core iki
/// uygulama getirir (<see cref="JobKind.AgentBatch"/>,
/// <see cref="JobKind.Workflow"/>); Faz 18 (eval) kendi isleyicisini
/// (<see cref="JobKind.Eval"/>) ayni sekilde ekler. Arka plan iscisi kayitli
/// isleyiciler arasindan <see cref="Kind"/> alanina gore secim yapar.
/// </remarks>
public interface IJobHandler
{
    /// <summary>Bu isleyicinin yurutebildigi is turu.</summary>
    JobKind Kind { get; }

    /// <summary>Isi yurutur.</summary>
    /// <param name="context">Is baglami: kayit, ogeler, raporlama ve iptal kontrolu.</param>
    /// <param name="cancellationToken">Iptal belirteci (isci kapanirken tetiklenir).</param>
    /// <returns>Tamamlanma gorevi.</returns>
    /// <remarks>
    /// Isleyici bir istisna firlatirsa is <see cref="IJobStore.ReleaseForRetryAsync"/>
    /// ile yeniden denenir (deneme sayisi asilmadiysa) veya
    /// <see cref="JobStatus.Failed"/> olarak isaretlenir.
    /// </remarks>
    ValueTask ExecuteAsync(JobContext context, CancellationToken cancellationToken = default);
}

/// <summary>
/// Bir <see cref="IJobHandler"/> yurutmesi sirasinda kullanilabilecek baglam.
/// </summary>
/// <remarks>
/// Isleyici <see cref="IJobStore"/>'a dogrudan erismez; bu, isleyicinin yalnizca
/// yurutme mantigina odaklanmasini saglar ve birim testinde sahte bir depo
/// kurmayi gereksiz kilar.
/// </remarks>
public sealed class JobContext
{
    /// <summary>Yurutulen isin kaydi.</summary>
    public required JobRecord Job { get; init; }

    /// <summary>Isin ogeleri, sira numarasina gore.</summary>
    public required IReadOnlyList<JobItemRecord> Items { get; init; }

    /// <summary>
    /// Bir ogenin isleme sonucunu bildirir. Isleyici her oge islendikce
    /// bunu cagirmalidir; sonuc kalici depoya ve is sayaclarina yansir.
    /// </summary>
    public required Func<JobItemResult, CancellationToken, ValueTask> ReportItemAsync { get; init; }

    /// <summary>
    /// Isin dis bir istekle (<c>POST .../cancel</c>) iptal edilip
    /// edilmedigini denetler. Isleyici ogeler arasinda bunu kontrol ederek
    /// isbirlikci iptali destekler.
    /// </summary>
    public required Func<CancellationToken, ValueTask<bool>> IsCancelledAsync { get; init; }
}
