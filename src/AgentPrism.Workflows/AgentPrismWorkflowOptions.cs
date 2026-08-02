namespace AgentPrism;

/// <summary>Workflow yurutmesini yoneten ayarlar.</summary>
public sealed class AgentPrismWorkflowOptions
{
    /// <summary>Yapilandirma bolumunun adi.</summary>
    public const string SectionName = "AgentPrism:Workflows";

    /// <summary>Workflow calistirma acik mi.</summary>
    /// <remarks>
    /// Kapatildiginda katalog yine listelenir; yalnizca calistirma reddedilir.
    /// Boylece bir sorun aninda tanimlar silinmeden yurutme durdurulabilir.
    /// </remarks>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Kontrol noktasi yazma acik mi.
    /// </summary>
    /// <remarks>
    /// Varsayilan <see langword="true"/>'dur: sürdürme kutudan ciktigi gibi
    /// calisir. Bedeli her super-step'te bir yazmadir; <see cref="MaxSuperSteps"/>
    /// ve bellek ici depodaki saklama siniri bunu dizginler.
    /// </remarks>
    public bool EnableCheckpointing { get; set; } = true;

    /// <summary>
    /// Ayni anda calisabilecek en fazla workflow sayisi.
    /// </summary>
    /// <remarks>
    /// Sinir <em>tum kiracilar icin ortaktir</em>. Tek bir workflow onlarca
    /// model cagrisi yapar; sinirsiz birakmak, bir kullanicinin tum saglayici
    /// kotasini tuketmesine izin verirdi.
    /// </remarks>
    public int MaxConcurrentRuns { get; set; } = 4;

    /// <summary>Tek bir workflow calistirmasinin en fazla suresi.</summary>
    public TimeSpan RunTimeout { get; set; } = TimeSpan.FromMinutes(10);

    /// <summary>
    /// Tek bir calistirmada islenebilecek en fazla super-step sayisi.
    /// </summary>
    /// <remarks>
    /// Sonsuz donguye karsi tek yapisal korumadir. Handoff ve GroupChat
    /// desenlerinde devretme kararini model verir; kotu yazilmis bir talimat
    /// iki agent'i sonsuza kadar birbirine devrettirebilir.
    /// </remarks>
    public int MaxSuperSteps { get; set; } = 100;

    /// <summary>
    /// Sonuclanmis bir calistirmanin kontrol noktalarinin saklanip
    /// saklanmayacagi.
    /// </summary>
    /// <remarks>
    /// Varsayilan <see langword="true"/>'dur: bir workflow bittikten sonra da
    /// ortadan devam ettirilebilmesi bu ozelligin varlik sebebidir. Depolama
    /// maliyetinden kacinan kurulumlar bunu kapatabilir; o zaman noktalar
    /// yalnizca calistirma surerken yasar.
    /// </remarks>
    public bool KeepCheckpointsAfterCompletion { get; set; } = true;
}
