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
/// <param name="TenantPredicate">
/// Satiri bir kiraciya baglayan SQL kosulu; <c>@tenant_id</c> parametresine
/// atifta bulunur. Tablonun kendi <c>tenant_id</c> sutunu varsa dogrudan bir
/// karsilastirmadir; yoksa (<c>run_events</c>, <c>tool_invocations</c>,
/// <c>eval_case_results</c>) sahibine bakan bir <c>EXISTS</c> ifadesidir.
/// 🚨 Faz 41: bu alan olmadan kiraci basina tanimlanmis bir politika BUTUN
/// kiracilarin satirlarini siliyordu.
/// </param>
/// <param name="RowLimitOrderExpression">
/// <c>MaxRows</c> (Faz 36) icin: en yeniden N. satiri bulmakta kullanilan SQL
/// ifadesi. Coğu hedefte <see cref="WherePredicate"/>'in <c>@cutoff</c> ile
/// karsilastirdigi SUTUNLA AYNIDIR (esik dogrudan ayni kosula beslenebilir).
/// Iliskili bir tabloya bakan hedeflerde (<c>eval_case_results</c>) korele bir
/// alt sorgudur. Bkz. 36.1.
/// </param>
internal readonly record struct RetentionTargetDefinition(
    string Table,
    string WherePredicate,
    string TenantPredicate,
    string OrderColumn,
    string RowLimitOrderExpression);

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
                $"EXISTS (SELECT 1 FROM {Table("runs")} rt "
                    + $"WHERE rt.id = {Table("run_events")}.run_id AND rt.tenant_id = @tenant_id)",
                "created_at",
                "created_at"),

            RetentionTargets.ToolInvocations => new RetentionTargetDefinition(
                Table("tool_invocations"),
                "created_at < @cutoff",
                $"EXISTS (SELECT 1 FROM {Table("runs")} rt "
                    + $"WHERE rt.id = {Table("tool_invocations")}.run_id AND rt.tenant_id = @tenant_id)",
                "created_at",
                "created_at"),

            // traces siliniyor; spans ON DELETE CASCADE ile birlikte gider.
            RetentionTargets.Traces => new RetentionTargetDefinition(
                Table("traces"),
                "started_at < @cutoff",
                "tenant_id = @tenant_id",
                "started_at",
                "started_at"),

            // Yalniz sonlanmis isler (Completed=3, Failed=4, Cancelled=5);
            // job_items ON DELETE CASCADE ile birlikte gider. Aktif islerin
            // completed_at'i NULL'dur; satir siniri sorgusu NULL'lari eler
            // (bkz. SqlDialect.BuildRetentionFindNthRowCutoffSql).
            RetentionTargets.Jobs => new RetentionTargetDefinition(
                Table("jobs"),
                "status IN (3, 4, 5) AND completed_at < @cutoff",
                "tenant_id = @tenant_id",
                "completed_at",
                "completed_at"),

            // Yalniz teslim edilmis (Delivered=1) kayitlar.
            RetentionTargets.WebhookDeliveries => new RetentionTargetDefinition(
                Table("webhook_deliveries"),
                "status = 1 AND delivered_at < @cutoff",
                "tenant_id = @tenant_id",
                "delivered_at",
                "delivered_at"),

            // eval_case_results'in kendi zaman sutunu yok; kosunun tamamlanma
            // zamanina EXISTS ile bakilir. Arsiv siralamasi icin uuid v7 kimligi
            // kullanilir (OrderColumn), ama MaxRows esigi WherePredicate'in
            // GERCEKTEN karsilastirdigi sutunla (er.completed_at) AYNI olmalidir
            // — bu yuzden RowLimitOrderExpression bagimsiz bir korele alt sorgudur.
            // Acik Soru 2 (Faz 36 plani) bu sekilde cozuldu: id bir zaman damgasi
            // DEGILDIR, dogrudan esik olamaz.
            //
            // 🚨 Korelasyon FULL NITELENDIRILMIS ada gore yazilir (Table(...)),
            // BARE hedef adina gore DEGIL: SQLite'ta QualifyTable onek + ad
            // BITISTIRIR (nokta yok, K-193) — bare "eval_case_results" FROM
            // yan tumcesindeki gercek nesneyle (ornegin "t_ab12cd34eval_case_results")
            // EsLESMEZ ve "no such column" ile calisma aninda patlar. Faz 36'da
            // MaxRows testleri bu tuzagi SQLite'a karsi kosarken YAKALADI; Faz
            // 25'in orijinal WherePredicate'i de AYNI hatayi tasiyordu, burada
            // birlikte duzeltildi.
            RetentionTargets.EvalCaseResults => new RetentionTargetDefinition(
                Table("eval_case_results"),
                $"EXISTS (SELECT 1 FROM {Table("eval_runs")} er " +
                $"WHERE er.id = {Table("eval_case_results")}.eval_run_id AND er.completed_at < @cutoff)",
                $"EXISTS (SELECT 1 FROM {Table("eval_runs")} ert " +
                $"WHERE ert.id = {Table("eval_case_results")}.eval_run_id AND ert.tenant_id = @tenant_id)",
                "id",
                $"(SELECT er.completed_at FROM {Table("eval_runs")} er WHERE er.id = {Table("eval_case_results")}.eval_run_id)"),

            // Kontrol noktasinin kendi created_at'i degil, BAGLI CALISTIRMANIN
            // tamamlanma zamani esas alinir (25.1: "Tamamlanan calistirmadan
            // 7 gun sonra"). MaxRows esigi de AYNI GEREKCEYLE runs.completed_at
            // uzerinden korele alt sorguyla hesaplanir (eval_case_results ile
            // ayni desen). Korelasyon burada da FULL NITELENDIRILMIS addir —
            // yukaridaki 🚨 notu gecerlidir.
            RetentionTargets.WorkflowCheckpoints => new RetentionTargetDefinition(
                Table("workflow_checkpoints"),
                $"EXISTS (SELECT 1 FROM {Table("runs")} r " +
                $"WHERE r.id = {Table("workflow_checkpoints")}.run_id AND r.completed_at < @cutoff)",
                "tenant_id = @tenant_id",
                "created_at",
                $"(SELECT r.completed_at FROM {Table("runs")} r WHERE r.id = {Table("workflow_checkpoints")}.run_id)"),

            // Suresi dolmus VEYA iptal edilmis izinler. Iki sutundan hangisi
            // doluysa (COALESCE) esik odur; ikisi de NULL ise satir siniri
            // sorgusu bu satiri eler (henuz uygun aday degil).
            RetentionTargets.SkillScriptGrants => new RetentionTargetDefinition(
                Table("skill_script_grants"),
                "(expires_at IS NOT NULL AND expires_at < @cutoff) " +
                "OR (revoked_at IS NOT NULL AND revoked_at < @cutoff)",
                "tenant_id = @tenant_id",
                "granted_at",
                "COALESCE(expires_at, revoked_at)"),

            // Sahipsiz: hic oturumu olmayan VEYA oturumu artik var olmayan ekler.
            // Korelasyon burada da FULL NITELENDIRILMIS addir — yukaridaki 🚨 notu
            // gecerlidir.
            RetentionTargets.Attachments => new RetentionTargetDefinition(
                Table("attachments"),
                "created_at < @cutoff AND (session_id IS NULL " +
                $"OR NOT EXISTS (SELECT 1 FROM {Table("sessions")} s WHERE s.id = {Table("attachments")}.session_id))",
                "tenant_id = @tenant_id",
                "created_at",
                "created_at"),

            // Kullanici verisi; varsayilan KAPALI (bkz. AgentPrismRetentionOptions).
            RetentionTargets.Sessions => new RetentionTargetDefinition(
                Table("sessions"),
                "updated_at < @cutoff",
                "tenant_id = @tenant_id",
                "updated_at",
                "updated_at"),

            // conversations siliniyor; conversation_items ve responses ON DELETE
            // CASCADE ile birlikte gider. Kullanici verisi; varsayilan KAPALI.
            RetentionTargets.Conversations => new RetentionTargetDefinition(
                Table("conversations"),
                "updated_at < @cutoff",
                "tenant_id = @tenant_id",
                "updated_at",
                "updated_at"),

            // Yalniz KAPANMIS baglantilar. Acik bir baglanti (ended_at IS NULL)
            // silinemez; sunucu cokerse kalan NULL, kapanmamis bir baglantinin
            // izidir ve saklama politikasi onu sessizce yok etmemelidir. Satir
            // siniri sorgusu NULL ended_at'i eler (acik baglanti aday olamaz).
            RetentionTargets.VoiceSessions => new RetentionTargetDefinition(
                Table("voice_sessions"),
                "ended_at IS NOT NULL AND ended_at < @cutoff",
                "tenant_id = @tenant_id",
                "started_at",
                "ended_at"),

            // Puanin kendi olusturulma zamani esas alinir; runs'a FK olmadigi
            // icin (diger olay/ozet tablolariyla ayni gerekce) EXISTS gerekmez.
            RetentionTargets.RunScores => new RetentionTargetDefinition(
                Table("run_scores"),
                "created_at < @cutoff",
                "tenant_id = @tenant_id",
                "created_at",
                "created_at"),

            // Saklanan idempotency yanitlari (Faz 43). Kendi olusturulma zamani
            // esas alinir; runs'a FK olmadigi icin EXISTS gerekmez.
            RetentionTargets.IdempotencyKeys => new RetentionTargetDefinition(
                Table("idempotency_keys"),
                "created_at < @cutoff",
                "tenant_id = @tenant_id",
                "created_at",
                "created_at"),

            // Calistirma girdileri (Faz 47). Kendi tenant_id ve created_at
            // sutunlarini tasir; runs'a FK'si CASCADE oldugu icin calistirma
            // silinince zaten gider, ama kendi omru de sinirlanabilmelidir.
            RetentionTargets.RunInputs => new RetentionTargetDefinition(
                Table("run_inputs"),
                "created_at < @cutoff",
                "tenant_id = @tenant_id",
                "created_at",
                "created_at"),

            // Bilgi tabani parcalari (Faz 51). 🚨 Tablo YALNIZ PostgreSQL
            // migration setinde vardir; bu hedefi SQL Server/SQLite'ta bir
            // politikaya baglamak calisma aninda "tablo yok" hatasi verir —
            // bu bilinclidir (bkz. docs/51-VEKTOR-BELLEK-VE-RAG.md, 51.3).
            RetentionTargets.DocumentEmbeddings => new RetentionTargetDefinition(
                Table("document_embeddings"),
                "created_at < @cutoff",
                "tenant_id = @tenant_id",
                "created_at",
                "created_at"),

            _ => throw new ArgumentException($"Bilinmeyen saklama hedefi: '{target}'.", nameof(target)),
        };
    }
}
