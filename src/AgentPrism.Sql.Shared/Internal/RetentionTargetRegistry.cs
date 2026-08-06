namespace AgentPrism;

/// <summary>Bir saklama hedefinin tablosunu ve "eski" sayilma kosulunu tanimlar.</summary>
/// <param name="Table">Nitelendirilmis (sema/onekli) tablo adi.</param>
/// <param name="WherePredicate">
/// <c>@cutoff</c> parametresine atifta bulunan SQL kosulu. Bagimsiz bir sutun
/// karsilastirmasi olabilecegi gibi (<c>created_at &lt; @cutoff</c>), iliskili
/// bir tabloya bakan bir <c>EXISTS</c> ifadesi de olabilir (ornegin bir
/// calistirmanin tamamlanma zamanina bakan <c>workflow_checkpoints</c>).
/// </param>
/// <param name="OrderColumn">
/// Arsiv okumasinda determinizm icin kullanilan siralama sutunu. Hedefin kendi
/// zaman sutunu yoksa (<c>eval_case_results</c>) UUID v7 kimligi zaman sirali
/// oldugu icin (K-015) <c>id</c> kullanilir.
/// </param>
internal readonly record struct RetentionTargetDefinition(string Table, string WherePredicate, string OrderColumn);

/// <summary>
/// <see cref="RetentionTargets"/> beyaz listesindeki her hedefin tablo/kosul
/// eslemesini tutan sabit defter.
/// </summary>
/// <remarks>
/// <para>
/// Bu defter, 25.1'deki saklama tablosunun SQL karsiligidir. Yeni bir hedef
/// eklemek icin once <c>RetentionTargets</c>'a sabit eklenir, sonra buraya
/// tablosu/kosulu yazilir — SQL metni <strong>saglayici basina</strong>
/// COPYALANMAZ; <see cref="SqlDialect"/> yalniz 3 sablon yontemi (say/sil/oku)
/// ve tablo nitelendirmesini (<see cref="SqlDialect.QualifyTable"/>) saglar,
/// tablo/kosul burada TEK yerde tanimlanir. Gerekce: 30 elle yazilmis sorgu
/// yerine tek bir veri tablosu (docs/KARARLAR.md, karar K-198).
/// </para>
/// <para>
/// 🚨 <c>audit_log</c> burada KASITLI OLARAK yoktur ve asla eklenmemelidir.
/// </para>
/// </remarks>
internal static class RetentionTargetRegistry
{
    /// <summary>Bir hedefin tablo/kosul tanimini cozer.</summary>
    /// <param name="dialect">
    /// Tablo adlarini nitelendirmek icin kullanilan saglayici diyalekti
    /// (bkz. <see cref="SqlDialect.QualifyTable"/> — PostgreSQL/SQL Server
    /// nokta ile, SQLite onek bitistirerek nitelendirir).
    /// </param>
    /// <param name="target">Bkz. <see cref="RetentionTargets"/>.</param>
    /// <returns>Tanim.</returns>
    /// <exception cref="ArgumentException">Hedef beyaz listede yoksa.</exception>
    public static RetentionTargetDefinition Resolve(SqlDialect dialect, string target)
    {
        ArgumentNullException.ThrowIfNull(dialect);

        string Table(string name) => dialect.QualifyTable(name);

        return target switch
        {
            RetentionTargets.RunEvents => new RetentionTargetDefinition(
                Table("run_events"),
                "created_at < @cutoff",
                "created_at"),

            RetentionTargets.ToolInvocations => new RetentionTargetDefinition(
                Table("tool_invocations"),
                "created_at < @cutoff",
                "created_at"),

            // traces siliniyor; spans ON DELETE CASCADE ile birlikte gider.
            RetentionTargets.Traces => new RetentionTargetDefinition(
                Table("traces"),
                "started_at < @cutoff",
                "started_at"),

            // Yalniz sonlanmis isler (Completed=3, Failed=4, Cancelled=5);
            // job_items ON DELETE CASCADE ile birlikte gider.
            RetentionTargets.Jobs => new RetentionTargetDefinition(
                Table("jobs"),
                "status IN (3, 4, 5) AND completed_at < @cutoff",
                "completed_at"),

            // Yalniz teslim edilmis (Delivered=1) kayitlar.
            RetentionTargets.WebhookDeliveries => new RetentionTargetDefinition(
                Table("webhook_deliveries"),
                "status = 1 AND delivered_at < @cutoff",
                "delivered_at"),

            // eval_case_results'in kendi zaman sutunu yok; kosunun tamamlanma
            // zamanina EXISTS ile bakilir. Siralama icin uuid v7 kimligi kullanilir.
            RetentionTargets.EvalCaseResults => new RetentionTargetDefinition(
                Table("eval_case_results"),
                $"EXISTS (SELECT 1 FROM {Table("eval_runs")} er " +
                "WHERE er.id = eval_case_results.eval_run_id AND er.completed_at < @cutoff)",
                "id"),

            // Kontrol noktasinin kendi created_at'i degil, BAGLI CALISTIRMANIN
            // tamamlanma zamani esas alinir (25.1: "Tamamlanan calistirmadan
            // 7 gun sonra").
            RetentionTargets.WorkflowCheckpoints => new RetentionTargetDefinition(
                Table("workflow_checkpoints"),
                $"EXISTS (SELECT 1 FROM {Table("runs")} r " +
                "WHERE r.id = workflow_checkpoints.run_id AND r.completed_at < @cutoff)",
                "created_at"),

            // Suresi dolmus VEYA iptal edilmis izinler.
            RetentionTargets.SkillScriptGrants => new RetentionTargetDefinition(
                Table("skill_script_grants"),
                "(expires_at IS NOT NULL AND expires_at < @cutoff) " +
                "OR (revoked_at IS NOT NULL AND revoked_at < @cutoff)",
                "granted_at"),

            // Sahipsiz: hic oturumu olmayan VEYA oturumu artik var olmayan ekler.
            RetentionTargets.Attachments => new RetentionTargetDefinition(
                Table("attachments"),
                "created_at < @cutoff AND (session_id IS NULL " +
                $"OR NOT EXISTS (SELECT 1 FROM {Table("sessions")} s WHERE s.id = attachments.session_id))",
                "created_at"),

            // Kullanici verisi; varsayilan KAPALI (bkz. AgentPrismRetentionOptions).
            RetentionTargets.Sessions => new RetentionTargetDefinition(
                Table("sessions"),
                "updated_at < @cutoff",
                "updated_at"),

            // conversations siliniyor; conversation_items ve responses ON DELETE
            // CASCADE ile birlikte gider. Kullanici verisi; varsayilan KAPALI.
            RetentionTargets.Conversations => new RetentionTargetDefinition(
                Table("conversations"),
                "updated_at < @cutoff",
                "updated_at"),

            // Yalniz KAPANMIS baglantilar. Acik bir baglanti (ended_at IS NULL)
            // silinemez; sunucu cokerse kalan NULL, kapanmamis bir baglantinin
            // izidir ve saklama politikasi onu sessizce yok etmemelidir.
            RetentionTargets.VoiceSessions => new RetentionTargetDefinition(
                Table("voice_sessions"),
                "ended_at IS NOT NULL AND ended_at < @cutoff",
                "started_at"),

            // Puanin kendi olusturulma zamani esas alinir; runs'a FK olmadigi
            // icin (diger olay/ozet tablolariyla ayni gerekce) EXISTS gerekmez.
            RetentionTargets.RunScores => new RetentionTargetDefinition(
                Table("run_scores"),
                "created_at < @cutoff",
                "created_at"),

            _ => throw new ArgumentException($"Bilinmeyen saklama hedefi: '{target}'.", nameof(target)),
        };
    }
}
