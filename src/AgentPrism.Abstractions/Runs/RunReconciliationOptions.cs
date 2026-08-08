namespace AgentPrism;

/// <summary>Oksuz calistirma uzlastirmasi (Faz 54) ayarlari.</summary>
/// <remarks>
/// <c>AgentPrism:RunReconciliation</c> yapilandirma bolumunden okunur. Bkz.
/// <c>AgentPrismServiceCollectionExtensions.AddAgentPrism</c>.
/// </remarks>
public sealed class RunReconciliationOptions
{
    /// <summary>Yapilandirma bolumu adi.</summary>
    public const string SectionName = "AgentPrism:RunReconciliation";

    /// <summary>
    /// Uzlastirma acik mi. Varsayilan <see langword="false"/>: tek ornekli bir
    /// gelistirme kurulumunda kimse bir arka plan yazicisi beklemez ve kira
    /// tablosuna hicbir sorgu gitmez.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Suren bir calistirmanin "hala buradayim" isaretini yazma araligi.
    /// </summary>
    public TimeSpan HeartbeatInterval { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Bu sureden uzun sure isaret vermeyen <c>Running</c> satir oksuz sayilir.
    /// </summary>
    /// <remarks>
    /// Varsayilan <see cref="HeartbeatInterval"/>'in on kati: tek bir GC
    /// duraklamasi veya kisa bir veritabani kesintisi calisan bir isi
    /// oldu ilan ETMEMELIDIR.
    /// </remarks>
    public TimeSpan OrphanThreshold { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>Uzlastirma turlari arasindaki bekleme.</summary>
    public TimeSpan ScanInterval { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>Bir turda kapatilacak ust satir sayisi.</summary>
    public int MaxRunsPerScan { get; set; } = 100;
}
