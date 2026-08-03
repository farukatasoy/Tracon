namespace AgentPrism;

/// <summary>Toplu ve zamanlanmis calistirma (Faz 17) ayarlari.</summary>
/// <remarks>
/// <c>AgentPrism:Scheduling</c> yapilandirma bolumunden okunur. Bkz.
/// <c>AgentPrismServiceCollectionExtensions.UseScheduling</c>.
/// </remarks>
public sealed class AgentPrismSchedulingOptions
{
    /// <summary>Yapilandirma bolumu adi.</summary>
    public const string SectionName = "AgentPrism:Scheduling";

    /// <summary>Zamanlama alt sistemi etkin mi.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Arka plan iscisi bu surecte calissin mi. <see langword="false"/>
    /// yapilirsa kuyruk ve zamanlama depolari calismaya devam eder, ancak
    /// hicbir is bu surecte kiralanmaz veya yurutulmez — dagitim baska bir
    /// surece birakilir.
    /// </summary>
    public bool RunWorker { get; set; } = true;

    /// <summary>Bu surecte ayni anda yurutulebilecek en fazla is sayisi.</summary>
    public int MaxConcurrentJobs { get; set; } = 2;

    /// <summary>Yeni is ve sirasi gelen zamanlama aranma sikligi.</summary>
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>Bir isin kira suresi. Isci bu sure icinde bitirmezse is yeniden kiralanabilir.</summary>
    public TimeSpan LeaseDuration { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>Bir isin <see cref="JobStatus.Failed"/> olmadan once yapabilecegi en fazla deneme sayisi.</summary>
    public int MaxAttempts { get; set; } = 3;

    /// <summary>Tek bir iste izin verilen en fazla oge sayisi.</summary>
    public int MaxItemsPerJob { get; set; } = 1000;
}
