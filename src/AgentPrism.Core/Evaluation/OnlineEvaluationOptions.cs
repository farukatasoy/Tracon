namespace AgentPrism;

/// <summary>Cevrimici degerlendirme ayarlari — Faz 49.</summary>
/// <remarks>
/// <para>
/// <c>AgentPrism:OnlineEvaluation</c> yapilandirma bolumunden okunur.
/// </para>
/// <para>
/// 🚨 <strong>Iki kapili varsayilan.</strong> <see cref="Enabled"/> varsayilan
/// <see langword="false"/>'dur (Faz 48 ile ayni duz K1 okumasi) VE
/// <see cref="SampleRate"/> varsayilan <c>0.0</c>'dir. <see cref="Enabled"/>
/// acilsa bile oran ayrica verilmedikce hicbir calistirma orneklenmez, yargic
/// modeli hic cagrilmaz ve tek kurus harcanmaz. Ucuncu savunma
/// <see cref="MaxScoresPerHour"/>: orneklem orani yanlis hesaplansa bile bir
/// ust sinir vardir.
/// </para>
/// </remarks>
public sealed class OnlineEvaluationOptions
{
    /// <summary>Yapilandirma bolumu adi.</summary>
    public const string SectionName = "AgentPrism:OnlineEvaluation";

    /// <summary>🚨 Cevrimici degerlendirme etkin mi. Varsayilan <see langword="false"/>.</summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Orneklenecek tamamlanan calistirma orani, 0.0-1.0.
    /// </summary>
    /// <remarks>
    /// 🚨 Varsayilan <c>0.0</c>: <see cref="Enabled"/> acilsa bile oran
    /// verilmedikce hicbir sey puanlanmaz. Orneklemenin kendisi
    /// <strong>deterministiktir</strong> — calistirma kimliginin ozetinden
    /// turetilir; ayni calistirma iki kez degerlendirilmez ve yeniden deneme
    /// yeni bir zar atmaz.
    /// </remarks>
    public double SampleRate { get; set; }

    /// <summary>
    /// Bir kiracida saatte en fazla kac calistirma orneklenir.
    /// </summary>
    /// <remarks>
    /// Orneklemenin ikinci savunmasidir: <see cref="SampleRate"/> yanlis
    /// hesaplansa veya trafik patlasa bile mutlak maliyet bu tavanla sinirlanir.
    /// </remarks>
    public int MaxScoresPerHour { get; set; } = 100;

    /// <summary>Yalnizca bu agent'lar puanlanir. Bos ise hepsi.</summary>
    public IList<string> AgentNames { get; } = [];

    /// <summary>Dusuk puan esigi, 0-100 olceginde.</summary>
    public int LowScoreThreshold { get; set; } = 60;

    /// <summary>
    /// Alarm icin gereken asgari ornek sayisi. Tek bir dusuk puan alarm uretmez.
    /// </summary>
    /// <remarks>
    /// Model gurultuludur; tek ornek uzerinden alarm uretmek nobetci
    /// muhendisi egitir ve bildirimler yok sayilmaya baslar.
    /// </remarks>
    public int MinSampleSize { get; set; } = 20;

    /// <summary>Ortalama hesabinin penceresi.</summary>
    public TimeSpan EvaluationWindow { get; set; } = TimeSpan.FromHours(1);
}
