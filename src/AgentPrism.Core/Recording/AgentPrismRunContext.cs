namespace AgentPrism;

/// <summary>
/// Suren calistirmanin kimligini, agactaki yerini ve butcesini calistirma
/// yolunun icindeki yardimci bilesenlere tasir.
/// </summary>
/// <remarks>
/// <para>
/// Iki tuketicisi vardir. Skill script calistirmasi MAF'in icinden, kayit
/// sarmalayicisinin <em>altinda</em> tetiklenir; alt agent cagrisi ise MAF'in
/// arka plan gorev tool'undan tetiklenir. Ikisinde de calistirma kimligini
/// parametre olarak gecirmenin yolu yoktur: cagri zinciri MAF'a aittir.
/// </para>
/// <para>
/// 🚨 Deger bir <see cref="AsyncLocal{T}"/> icinde tutulur. Bu, atamanin
/// <strong>cagirana geri akmadigi</strong> anlamina gelir: <c>Set</c> cagrisi
/// calistirmayi baslatan metodun <em>kendi govdesinde</em> yapilmalidir.
/// Ayni tuzak <see cref="System.Diagnostics.Activity.Current"/> ile Faz 6'da
/// yasandi.
/// </para>
/// <para>
/// Deger asagi dogru <strong>akar</strong>: Microsoft Agent Framework'un arka
/// plan agent gorevi <c>ExecutionContext</c>'i yakaladigi icin baska bir is
/// parcaciginda calisan alt agent de ayni kapsami gorur. Faz 12'de olculdu.
/// </para>
/// </remarks>
public static class AgentPrismRunContext
{
    private static readonly AsyncLocal<AgentRunScope?> ScopeHolder = new();

    /// <summary>Suren calistirmanin kapsami. Calistirma disinda <see langword="null"/>.</summary>
    public static AgentRunScope? Current => ScopeHolder.Value;

    /// <summary>Suren calistirmanin kimligi. Calistirma disinda <see langword="null"/>.</summary>
    public static Guid? CurrentRunId => ScopeHolder.Value?.RunId;

    /// <summary>Suren calistirmanin kapsamini ayarlar.</summary>
    /// <param name="scope">Kapsam. <see langword="null"/> ise kapsam temizlenir.</param>
    public static void SetCurrent(AgentRunScope? scope) => ScopeHolder.Value = scope;
}

/// <summary>
/// Suren bir calistirmanin, calistirma yolundaki yardimci bilesenlere acilan
/// gorunumu.
/// </summary>
/// <remarks>
/// Kapsam <see cref="RunRecordingAgent"/> tarafindan acilir. Alt agent cagrisi
/// buradan okudugu degerlerle kendi <see cref="AgentPrismRunOptions"/> nesnesini
/// kurar; boylece agac baglantisi, derinlik ve butce cagri zinciri boyunca
/// tasinir.
/// </remarks>
public sealed record AgentRunScope
{
    /// <summary>Suren calistirmanin kimligi.</summary>
    public required Guid RunId { get; init; }

    /// <summary>Agacin kokundeki calistirmanin kimligi. Kokte <see cref="RunId"/> ile aynidir.</summary>
    public required Guid RootRunId { get; init; }

    /// <summary>Agactaki derinlik. Kok calistirma 0'dir.</summary>
    public int Depth { get; init; }

    /// <summary>Bu calistirmayi yuruten agent'in adi.</summary>
    public string? AgentName { get; init; }

    /// <summary>Calistirmanin kiracisi. Alt calistirma bu kiracidan cikamaz.</summary>
    public string? TenantId { get; init; }

    /// <summary>Agac boyunca paylasilan butce.</summary>
    public AgentRunBudget? Budget { get; init; }

    /// <summary>Bu calistirmanin olctugu tanim surumu. Bilinmiyorsa <see langword="null"/>.</summary>
    public int? AgentVersion { get; init; }

    /// <summary>Bu calistirmanin bagli oldugu deneyin kimligi. Deney disi calistirmada <see langword="null"/>.</summary>
    public Guid? ExperimentId { get; init; }

    /// <summary>Bu calistirmanin atandigi deney kolunun adi. Deney disi calistirmada <see langword="null"/>.</summary>
    public string? Variant { get; init; }

    /// <summary>
    /// Bu calistirmanin olay yazicisi. Alt calistirma ozet olaylari buraya yazilir.
    /// </summary>
    /// <remarks>
    /// Sira numarasi <strong>tek bir yazicidan</strong> uretilir (karar K-014).
    /// Alt cagri kendi yazicisini kursaydi ayni calistirmada iki bagimsiz sayac
    /// olur ve sira numaralari cakisirdi.
    /// </remarks>
    public RunEventWriter? Writer { get; init; }

    /// <summary>
    /// Baglam sikistirmasinin (ozetleme) urettigi ek token kullanimini
    /// toplayan sayac. Calistirma sonunda nihai kullanima katilir.
    /// </summary>
    internal CompactionUsageAccumulator? ExtraUsage { get; init; }
}
