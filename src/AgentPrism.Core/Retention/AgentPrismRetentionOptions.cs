namespace AgentPrism;

/// <summary>Veri saklama ve arsivleme ayarlari — Faz 25.</summary>
/// <remarks>
/// <para>
/// <c>AgentPrism:Retention</c> yapilandirma bolumunden okunur. Bu sinif
/// yalnizca <strong>yapilandirma tabanli varsayilanlari</strong> tasir; asil
/// kaynak veritabanindaki <c>retention_policies</c> tablosudur
/// (<see cref="IRetentionPolicyStore"/>). Bir hedef icin veritabaninda kayit
/// varsa bu ayarlar tamamen yok sayilir.
/// </para>
/// <para>
/// 🚨 <see cref="Enabled"/> varsayilan <see langword="false"/>'dur. Bir paket
/// yukseltmesi, tuketici hicbir yapilandirma eklemeden veri silmemelidir —
/// yapilandirma tabanli varsayilanlarin devreye girmesi icin bu bayragin da
/// acikca <see langword="true"/> yapilmasi gerekir.
/// </para>
/// </remarks>
public sealed class AgentPrismRetentionOptions
{
    /// <summary>Yapilandirma bolumu adi.</summary>
    public const string SectionName = "AgentPrism:Retention";

    /// <summary>
    /// Yapilandirma tabanli varsayilanlar etkin mi. Kapaliyken asagidaki hedef
    /// ayarlari okunsa bile hicbir sey silinmez — yalniz veritabanindaki acik
    /// politikalar gecerli olur.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>Bir silme partisindeki en fazla satir sayisi.</summary>
    public int BatchSize { get; set; } = 5000;

    /// <summary>Partiler arasindaki bekleme suresi (uretim yukunu bogmamak icin).</summary>
    public TimeSpan BatchDelay { get; set; } = TimeSpan.FromMilliseconds(100);

    /// <summary><see cref="RetentionTargets.RunEvents"/> icin varsayilan.</summary>
    public RetentionTargetOptions RunEvents { get; } = new() { MaxAgeDays = 30 };

    /// <summary><see cref="RetentionTargets.ToolInvocations"/> icin varsayilan.</summary>
    public RetentionTargetOptions ToolInvocations { get; } = new() { MaxAgeDays = 90 };

    /// <summary><see cref="RetentionTargets.Traces"/> icin varsayilan (trace ve span'ler).</summary>
    public RetentionTargetOptions Spans { get; } = new() { MaxAgeDays = 14 };

    /// <summary><see cref="RetentionTargets.Jobs"/> icin varsayilan (yalniz tamamlananlar).</summary>
    public RetentionTargetOptions Jobs { get; } = new() { MaxAgeDays = 30 };

    /// <summary><see cref="RetentionTargets.WebhookDeliveries"/> icin varsayilan (yalniz teslim edilenler).</summary>
    public RetentionTargetOptions WebhookDeliveries { get; } = new() { MaxAgeDays = 7 };

    /// <summary><see cref="RetentionTargets.EvalCaseResults"/> icin varsayilan.</summary>
    public RetentionTargetOptions EvalCaseResults { get; } = new() { MaxAgeDays = 180 };

    /// <summary><see cref="RetentionTargets.WorkflowCheckpoints"/> icin varsayilan (tamamlanan calistirmadan sonra).</summary>
    public RetentionTargetOptions WorkflowCheckpoints { get; } = new() { MaxAgeDays = 7 };

    /// <summary><see cref="RetentionTargets.SkillScriptGrants"/> icin varsayilan (suresi dolan/iptal edilenler).</summary>
    public RetentionTargetOptions SkillScriptGrants { get; } = new() { MaxAgeDays = 30 };

    /// <summary><see cref="RetentionTargets.Attachments"/> icin varsayilan (yalniz sahipsizler).</summary>
    public RetentionTargetOptions Attachments { get; } = new() { MaxAgeDays = 7 };

    /// <summary>
    /// <see cref="RetentionTargets.Sessions"/> icin varsayilan. <see cref="RetentionTargetOptions.MaxAgeDays"/>
    /// varsayilan <see langword="null"/>'dur: kullanici verisi, sunulur ama KAPALI.
    /// </summary>
    public RetentionTargetOptions Sessions { get; } = new();

    /// <summary>
    /// <see cref="RetentionTargets.Conversations"/> icin varsayilan. <see cref="RetentionTargetOptions.MaxAgeDays"/>
    /// varsayilan <see langword="null"/>'dur: kullanici verisi, sunulur ama KAPALI.
    /// </summary>
    public RetentionTargetOptions Conversations { get; } = new();

    /// <summary>
    /// <see cref="RetentionTargets.IdempotencyKeys"/> icin varsayilan (Faz 43).
    /// Saklanan yanit istemciye zaten gonderilmis oldugu icin yeni bilgi acikca
    /// etmez; yine de omur sinirlanir (43.5).
    /// </summary>
    public RetentionTargetOptions IdempotencyKeys { get; } = new() { MaxAgeDays = 1 };

    /// <summary>Bir hedef adina karsilik gelen ayar nesnesini dondurur.</summary>
    /// <param name="target">Bkz. <see cref="RetentionTargets"/>.</param>
    /// <returns>Ayar nesnesi; bilinmeyen hedef icin <see langword="null"/>.</returns>
    internal RetentionTargetOptions? ForTarget(string target)
        => target switch
        {
            RetentionTargets.RunEvents => RunEvents,
            RetentionTargets.ToolInvocations => ToolInvocations,
            RetentionTargets.Traces => Spans,
            RetentionTargets.Jobs => Jobs,
            RetentionTargets.WebhookDeliveries => WebhookDeliveries,
            RetentionTargets.EvalCaseResults => EvalCaseResults,
            RetentionTargets.WorkflowCheckpoints => WorkflowCheckpoints,
            RetentionTargets.SkillScriptGrants => SkillScriptGrants,
            RetentionTargets.Attachments => Attachments,
            RetentionTargets.Sessions => Sessions,
            RetentionTargets.Conversations => Conversations,
            RetentionTargets.IdempotencyKeys => IdempotencyKeys,
            _ => null,
        };
}

/// <summary>Tek bir hedef icin yapilandirma tabanli saklama varsayilani.</summary>
public sealed class RetentionTargetOptions
{
    /// <summary>Bu yastan eski satirlar silinmeye adaydir. <see langword="null"/> = bu hedef icin varsayilan devre disi.</summary>
    public int? MaxAgeDays { get; set; }

    /// <summary>Silmeden once arsivlensin mi.</summary>
    public bool Archive { get; set; }
}
