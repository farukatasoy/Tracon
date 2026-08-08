namespace AgentPrism;

/// <summary>
/// Sema adina gore olusturulmus SQL metinlerinin saglayicidan bagimsiz yuzeyi.
/// </summary>
/// <remarks>
/// <para>
/// Bu sinif yalnizca <em>hangi</em> sorgularin var oldugunu bildirir; metinleri
/// saglayici alt siniflari yazar (<c>PostgresQueries</c>, <c>SqlServerQueries</c>).
/// Boylece paylasilan depo kodu tek bir yuzeye bagimli kalir ve diyalekt farki
/// SQL metninin icinde kapali kalir.
/// </para>
/// <para>
/// 🚨 <strong>Buraya yeni bir sorgu eklendiginde her alt sinifta karsiligi
/// yazilmalidir.</strong> Yazilmazsa alan <c>string.Empty</c> kalir ve hata
/// yalnizca calisma aninda gorunur. Yeni sorgu ekleyen faz, sozlesme testinin
/// her saglayicida kostugunu dogrulamalidir.
/// </para>
/// <para>
/// Sema adi bir tanimlayicidir ve parametre olarak gonderilemez; SQL metnine
/// dogrudan yerlestirilir. Ad, <see cref="SqlIdentifier.RequireSchemaName"/> ile
/// kati bicimde dogrulandiktan sonra kullanilir.
/// </para>
/// <para>
/// Tum sorgu metinleri kurucuda bir kez kurulur ve alan olarak saklanir; her
/// cagrida yeniden birlestirme yapilmaz.
/// </para>
/// </remarks>
internal abstract class SqlQueriesBase
{
    /// <summary>Bir tool cagrisi kaydi ekler.</summary>
    public string InsertToolInvocation { get; protected set; } = string.Empty;

    /// <summary>Bir calistirmanin tool cagrilarini listeler.</summary>
    public string SelectToolInvocations { get; protected set; } = string.Empty;

    /// <summary>Tool bazinda kullanim ozetini cikarir.</summary>
    public string SelectToolUsage { get; protected set; } = string.Empty;

    /// <summary>Kiracinin tum deneylerini listeler.</summary>
    public string SelectExperiments { get; protected set; } = string.Empty;

    /// <summary>Adi verilen deneyi getirir.</summary>
    public string SelectExperiment { get; protected set; } = string.Empty;

    /// <summary>Bir agent icin Running durumundaki deneyi getirir.</summary>
    public string SelectRunningExperiment { get; protected set; } = string.Empty;

    /// <summary>Bir deneyi olusturur veya (yalniz Draft ise) gunceller.</summary>
    public string UpsertExperiment { get; protected set; } = string.Empty;

    /// <summary>Bir deneyi siler (yalniz Running degilse).</summary>
    public string DeleteExperiment { get; protected set; } = string.Empty;

    /// <summary>Bir deneyi Running durumuna gecirir.</summary>
    public string StartExperiment { get; protected set; } = string.Empty;

    /// <summary>Bir deneyi Stopped durumuna gecirir.</summary>
    public string StopExperiment { get; protected set; } = string.Empty;

    /// <summary>Bir deneyin kol bazinda calistirma sonuclarini cikarir.</summary>
    public string SelectExperimentResults { get; protected set; } = string.Empty;

    /// <summary>Trace basligini ekler veya gunceller ve kimligini dondurur.</summary>
    public string UpsertTrace { get; protected set; } = string.Empty;

    /// <summary>Bir span'i ekler veya gunceller.</summary>
    public string UpsertSpan { get; protected set; } = string.Empty;

    /// <summary>Bir calistirmanin trace basligini getirir.</summary>
    public string SelectTraceByRun { get; protected set; } = string.Empty;

    /// <summary>Bir trace'in span'lerini getirir.</summary>
    public string SelectSpans { get; protected set; } = string.Empty;

    /// <summary>Bir kiracinin onay kurallarini listeler.</summary>
    public string SelectToolApprovalRules { get; protected set; } = string.Empty;

    /// <summary>Bir onay kurali ekler; ayni kapsam varsa mevcut kaydi dondurur.</summary>
    public string InsertToolApprovalRule { get; protected set; } = string.Empty;

    /// <summary>Bir onay kuralini siler.</summary>
    public string DeleteToolApprovalRule { get; protected set; } = string.Empty;

    /// <summary>Bir kiracinin MCP sunucularini listeler.</summary>
    public string SelectMcpServers { get; protected set; } = string.Empty;

    /// <summary>Tek bir MCP sunucusunu getirir.</summary>
    public string SelectMcpServer { get; protected set; } = string.Empty;

    /// <summary>Bir MCP sunucusunu ekler veya gunceller.</summary>
    public string UpsertMcpServer { get; protected set; } = string.Empty;

    /// <summary>Bir MCP sunucusunu siler.</summary>
    public string DeleteMcpServer { get; protected set; } = string.Empty;

    /// <summary>Kayitli kiracilari listeler.</summary>
    public string SelectTenants { get; protected set; } = string.Empty;

    /// <summary>Bir kiraci kaydini ekler veya gunceller.</summary>
    public string UpsertTenantDescriptor { get; protected set; } = string.Empty;

    /// <summary>Bir kiraci kaydini siler.</summary>
    public string DeleteTenant { get; protected set; } = string.Empty;

    /// <summary>Yeni bir ek ekler.</summary>
    public string InsertAttachment { get; protected set; } = string.Empty;

    /// <summary>Bir ekin ustverisini okur.</summary>
    public string SelectAttachment { get; protected set; } = string.Empty;

    /// <summary>Bir ekin ham icerigini okur.</summary>
    public string SelectAttachmentContent { get; protected set; } = string.Empty;

    /// <summary>Ekleri filtreleyerek listeler.</summary>
    public string SelectAttachments { get; protected set; } = string.Empty;

    /// <summary>Bir eki siler ve harici depo konumunu dondurur.</summary>
    public string DeleteAttachment { get; protected set; } = string.Empty;

    /// <summary>Bir oturuma ait tum ekleri siler.</summary>
    public string DeleteAttachmentsBySession { get; protected set; } = string.Empty;

    /// <summary>Kalici agent dosyasinin icerigini okur.</summary>
    public string SelectAgentFile { get; protected set; } = string.Empty;

    /// <summary>Kalici agent dosyasini ekler veya gunceller.</summary>
    public string UpsertAgentFile { get; protected set; } = string.Empty;

    /// <summary>Kalici agent dosyasini siler.</summary>
    public string DeleteAgentFile { get; protected set; } = string.Empty;

    /// <summary>
    /// Bir agent'in dosyalarini yol oneki, istege bagli derinlik siniri, istege
    /// bagli glob suzgeci ve (yalniz PostgreSQL) istege bagli regex on suzgeci ile
    /// SQL'de daraltarak okur (Faz 51, Is A). Onceki <c>SelectAgentFiles</c>'in
    /// (tum dosyalari belleğe alan) yerini alir; onek her zaman verilir (kok
    /// dizin icin <c>"/"</c>), bu yuzden ayri bir "hepsini getir" sorgusuna
    /// gerek kalmadi.
    /// </summary>
    public string SelectAgentFilesFiltered { get; protected set; } = string.Empty;

    /// <summary>Bir workflow tanimini kaydeder ve surumunu artirir.</summary>
    public string UpsertWorkflow { get; protected set; } = string.Empty;

    /// <summary>Tek bir workflow tanimini getirir.</summary>
    public string SelectWorkflow { get; protected set; } = string.Empty;

    /// <summary>Bir kiracinin workflow tanimlarini listeler.</summary>
    public string SelectWorkflows { get; protected set; } = string.Empty;

    /// <summary>Bir workflow tanimini siler.</summary>
    public string DeleteWorkflow { get; protected set; } = string.Empty;

    /// <summary>Bir kontrol noktasi yazar.</summary>
    public string InsertWorkflowCheckpoint { get; protected set; } = string.Empty;

    /// <summary>Tek bir kontrol noktasinin durumunu okur.</summary>
    public string SelectWorkflowCheckpoint { get; protected set; } = string.Empty;

    /// <summary>Bir oturumun kontrol noktalarinin ustverisini listeler.</summary>
    public string SelectWorkflowCheckpoints { get; protected set; } = string.Empty;

    /// <summary>Bir calistirmanin kontrol noktalarinin ustverisini listeler.</summary>
    public string SelectWorkflowCheckpointsByRun { get; protected set; } = string.Empty;

    /// <summary>Bir oturumun tum kontrol noktalarini siler.</summary>
    public string DeleteWorkflowCheckpoints { get; protected set; } = string.Empty;

    /// <summary>Bir denetim izi kaydi ekler.</summary>
    public string InsertAuditEntry { get; protected set; } = string.Empty;

    /// <summary>Denetim izi kayitlarini filtreleyerek okur.</summary>
    public string SelectAuditLog { get; protected set; } = string.Empty;

    /// <summary>Bir zamanlamayi ekler veya gunceller.</summary>
    public string UpsertJobSchedule { get; protected set; } = string.Empty;

    /// <summary>Tek bir zamanlamayi getirir.</summary>
    public string SelectJobSchedule { get; protected set; } = string.Empty;

    /// <summary>Bir kiracinin zamanlamalarini listeler.</summary>
    public string SelectJobSchedules { get; protected set; } = string.Empty;

    /// <summary>Sirasi gelmis tum kiracilarin zamanlamalarini listeler.</summary>
    public string SelectDueJobSchedules { get; protected set; } = string.Empty;

    /// <summary>Bir zamanlamayi siler.</summary>
    public string DeleteJobSchedule { get; protected set; } = string.Empty;

    /// <summary>Bir zamanlamanin bir sonraki calisma zamanini atomik olarak ilerletmeye calisir.</summary>
    public string TryClaimJobScheduleNextRun { get; protected set; } = string.Empty;

    /// <summary>Yeni bir is ekler.</summary>
    public string InsertJob { get; protected set; } = string.Empty;

    /// <summary>Bir isin ogelerini toplu ekler.</summary>
    public string InsertJobItems { get; protected set; } = string.Empty;

    /// <summary><c>FOR UPDATE SKIP LOCKED</c> ile calismaya hazir en eski isi kiralar.</summary>
    public string LeaseJob { get; protected set; } = string.Empty;

    /// <summary>Devam eden bir isin kirasini uzatir.</summary>
    public string RenewJobLease { get; protected set; } = string.Empty;

    /// <summary>Kiralanmis bir isi yurutuluyor durumuna gecirir.</summary>
    public string MarkJobRunning { get; protected set; } = string.Empty;

    /// <summary>Bir isi sonlandirir.</summary>
    public string CompleteJob { get; protected set; } = string.Empty;

    /// <summary>Bir isi yeniden deneme icin beklemeye alir.</summary>
    public string ReleaseJobForRetry { get; protected set; } = string.Empty;

    /// <summary>Bir isi iptal etmeye calisir.</summary>
    public string CancelJob { get; protected set; } = string.Empty;

    /// <summary>Tek bir is kaydini getirir.</summary>
    public string SelectJob { get; protected set; } = string.Empty;

    /// <summary>Isleri filtreleyerek listeler.</summary>
    public string SelectJobs { get; protected set; } = string.Empty;

    /// <summary>Bir isin ogelerini listeler.</summary>
    public string SelectJobItems { get; protected set; } = string.Empty;

    /// <summary>Bir is ogesinin sonucunu bildirir ve is sayaclarini gunceller.</summary>
    public string ReportJobItem { get; protected set; } = string.Empty;

    /// <summary>Dogrulanmis sema adi.</summary>
    public string Schema { get; protected set; } = string.Empty;

    /// <summary>Semayi olusturur.</summary>
    public string CreateSchema { get; protected set; } = string.Empty;

    /// <summary>Migration defterini olusturur.</summary>
    public string CreateMigrationsTable { get; protected set; } = string.Empty;

    /// <summary>Uygulanmis migration'lari okur.</summary>
    public string SelectAppliedMigrations { get; protected set; } = string.Empty;

    /// <summary>Uygulanan bir migration'i deftere yazar.</summary>
    public string InsertMigration { get; protected set; } = string.Empty;

    /// <summary>Kiraci kaydini yoksa ekler.</summary>
    public string UpsertTenant { get; protected set; } = string.Empty;

    /// <summary>Agent tanimini ekler veya surumunu artirir.</summary>
    public string UpsertAgentDefinition { get; protected set; } = string.Empty;

    /// <summary>Tanimin degismez surum kaydini ekler.</summary>
    public string InsertAgentDefinitionVersion { get; protected set; } = string.Empty;

    /// <summary>Bir tanimin guncel surumunu okur.</summary>
    public string SelectAgentDefinition { get; protected set; } = string.Empty;

    /// <summary>Kiracinin tum tanimlarini okur.</summary>
    public string SelectAgentDefinitions { get; protected set; } = string.Empty;

    /// <summary>Tanimi ve gecmisini siler.</summary>
    public string DeleteAgentDefinition { get; protected set; } = string.Empty;

    /// <summary>Bir tanimin tum surumlerini okur.</summary>
    public string SelectAgentDefinitionVersions { get; protected set; } = string.Empty;

    /// <summary>Bir tanimin belirli bir surumunu okur.</summary>
    public string SelectAgentDefinitionVersion { get; protected set; } = string.Empty;

    /// <summary>Kiracinin skill'lerini listeler.</summary>
    public string SelectAgentSkills { get; protected set; } = string.Empty;

    /// <summary>Tek bir skill'i okur.</summary>
    public string SelectAgentSkill { get; protected set; } = string.Empty;

    /// <summary>Skill'i ekler veya gunceller.</summary>
    public string UpsertAgentSkill { get; protected set; } = string.Empty;

    /// <summary>Skill'i siler.</summary>
    public string DeleteAgentSkill { get; protected set; } = string.Empty;

    /// <summary>Skill'in kaynaklarini siler.</summary>
    public string DeleteAgentSkillResources { get; protected set; } = string.Empty;

    /// <summary>Skill kaynagini ekler.</summary>
    public string InsertAgentSkillResource { get; protected set; } = string.Empty;

    /// <summary>Skill kaynaklarini listeler.</summary>
    public string SelectAgentSkillResources { get; protected set; } = string.Empty;

    /// <summary>Bir skill'in tum script'lerini siler.</summary>
    public string DeleteAgentSkillScripts { get; protected set; } = string.Empty;

    /// <summary>Bir skill script'i ekler.</summary>
    public string InsertAgentSkillScript { get; protected set; } = string.Empty;

    /// <summary>Bir skill'in script'lerini okur.</summary>
    public string SelectAgentSkillScripts { get; protected set; } = string.Empty;

    /// <summary>Bir kiracinin tum script calistirma izinlerini okur.</summary>
    public string SelectSkillScriptGrants { get; protected set; } = string.Empty;

    /// <summary>Belirli bir script icin gecerli izni okur.</summary>
    public string SelectActiveSkillScriptGrant { get; protected set; } = string.Empty;

    /// <summary>Bir script calistirma izni ekler veya yeniler.</summary>
    public string UpsertSkillScriptGrant { get; protected set; } = string.Empty;

    /// <summary>Bir script calistirma iznini iptal eder.</summary>
    public string RevokeSkillScriptGrant { get; protected set; } = string.Empty;

    /// <summary>Oturumu ekler veya gunceller.</summary>
    public string UpsertSession { get; protected set; } = string.Empty;

    /// <summary>Oturumu okur.</summary>
    public string SelectSession { get; protected set; } = string.Empty;

    /// <summary>Oturumu siler.</summary>
    public string DeleteSession { get; protected set; } = string.Empty;

    /// <summary>Oturumlari filtreleyerek okur.</summary>
    public string SelectSessions { get; protected set; } = string.Empty;

    /// <summary>Yeni calistirma kaydi acar.</summary>
    public string InsertRun { get; protected set; } = string.Empty;

    /// <summary>Calistirmayi sonlandirir.</summary>
    public string UpdateRunCompletion { get; protected set; } = string.Empty;

    /// <summary>Bir calistirmanin maliyetini gunceller (yalniz bakim ucu).</summary>
    public string UpdateRunCost { get; protected set; } = string.Empty;

    /// <summary>Bir calistirmayi okur.</summary>
    public string SelectRun { get; protected set; } = string.Empty;

    /// <summary>Calistirmalari filtreleyerek okur.</summary>
    public string SelectRuns { get; protected set; } = string.Empty;

    /// <summary>Calistirma ozetini ve agent kirilimini iki sonuc kumesi olarak dondurur.</summary>
    public string SelectRunStatistics { get; protected set; } = string.Empty;

    /// <summary>Kova basina calistirma, hata, token ve maliyet zaman serisi. Bos kovalar da doner.</summary>
    public string SelectRunTimeSeries { get; protected set; } = string.Empty;

    /// <summary>Calistirma olayi ekler.</summary>
    public string InsertRunEvent { get; protected set; } = string.Empty;

    /// <summary>Calistirma olaylarini sira numarasina gore okur.</summary>
    public string SelectRunEvents { get; protected set; } = string.Empty;

    /// <summary>Konusmayi ekler veya gunceller.</summary>
    public string UpsertConversation { get; protected set; } = string.Empty;

    /// <summary>Konusmadaki siradaki sira numarasini dondurur.</summary>
    public string SelectNextConversationSequence { get; protected set; } = string.Empty;

    /// <summary>Konusmaya mesaj ekler.</summary>
    public string InsertConversationItem { get; protected set; } = string.Empty;

    /// <summary>Konusmanin mesajlarini sirali okur.</summary>
    public string SelectConversationItems { get; protected set; } = string.Empty;

    /// <summary>
    /// Dal noktasini olcer: kopyalanacak son sira numarasi ve oge sayisi
    /// (Faz 47). Hic oge yoksa sira numarasi <c>-1</c> doner.
    /// </summary>
    public string SelectConversationBranchPoint { get; protected set; } = string.Empty;

    /// <summary>
    /// Kaynak konusmanin ustverisini kopyalayarak yeni bir dal konusmasi acar
    /// (Faz 47). Kaynak yoksa veya baska bir kiraciya aitse hicbir satir yazilmaz.
    /// </summary>
    public string InsertBranchConversation { get; protected set; } = string.Empty;

    /// <summary>
    /// Dallandirmada kopyalanacak ogeleri sirali okur (Faz 47).
    /// </summary>
    public string SelectConversationItemsForBranch { get; protected set; } = string.Empty;

    /// <summary>
    /// Bir calistirmanin girdi mesajlarini yazar (Faz 47). Ayni calistirma icin
    /// ikinci yazim <strong>yok sayilir</strong>.
    /// </summary>
    public string InsertRunInput { get; protected set; } = string.Empty;

    /// <summary>Bir calistirmanin kayitli girdisini okur (Faz 47).</summary>
    public string SelectRunInput { get; protected set; } = string.Empty;

    /// <summary>Bir kiracinin eval takimlarini listeler.</summary>
    public string SelectEvalSuites { get; protected set; } = string.Empty;

    /// <summary>Tek bir eval takimini getirir.</summary>
    public string SelectEvalSuite { get; protected set; } = string.Empty;

    /// <summary>Eval takimini olusturur veya gunceller.</summary>
    public string UpsertEvalSuite { get; protected set; } = string.Empty;

    /// <summary>Eval takimini siler (vakalar ve kosular cascade silinir).</summary>
    public string DeleteEvalSuite { get; protected set; } = string.Empty;

    /// <summary>Bir takimin vakalarini sira numarasina gore getirir.</summary>
    public string SelectEvalCases { get; protected set; } = string.Empty;

    /// <summary>Bir takimin tum vakalarini siler (yerine yenileri yazilmadan once).</summary>
    public string DeleteEvalCases { get; protected set; } = string.Empty;

    /// <summary>Bir eval vakasi ekler.</summary>
    public string InsertEvalCase { get; protected set; } = string.Empty;

    /// <summary>
    /// Takima <c>seq</c>'i atomik olarak hesaplayarak TEK bir eval vakasi ekler
    /// (uretimden terfi, Faz 45).
    /// </summary>
    public string InsertEvalCaseWithComputedSeq { get; protected set; } = string.Empty;

    /// <summary>Bir takimda verilen kaynak calistirmadan terfi edilmis vakayi getirir.</summary>
    public string SelectEvalCaseBySourceRun { get; protected set; } = string.Empty;

    /// <summary>Yeni bir eval kosu kaydi acar.</summary>
    public string InsertEvalRun { get; protected set; } = string.Empty;

    /// <summary>Kosuyu calisiyor durumuna gecirir ve olculen surum/modeli yazar.</summary>
    public string MarkEvalRunRunning { get; protected set; } = string.Empty;

    /// <summary>Kosuyu sonlandirir ve ozet sayaclarini yazar.</summary>
    public string CompleteEvalRun { get; protected set; } = string.Empty;

    /// <summary>Bir eval kosusunu okur.</summary>
    public string SelectEvalRun { get; protected set; } = string.Empty;

    /// <summary>Bir is kaydinin urettigi eval kosusunu okur.</summary>
    public string SelectEvalRunByJobId { get; protected set; } = string.Empty;

    /// <summary>Eval kosularini filtreleyerek okur.</summary>
    public string SelectEvalRuns { get; protected set; } = string.Empty;

    /// <summary>Bir eval vaka sonucu ekler.</summary>
    public string InsertEvalCaseResult { get; protected set; } = string.Empty;

    /// <summary>Bir kosunun vaka sonuclarini okur.</summary>
    public string SelectEvalCaseResults { get; protected set; } = string.Empty;

    /// <summary>Bir kota kuralini ekler veya gunceller (kapsam catismasinda).</summary>
    public string UpsertQuota { get; protected set; } = string.Empty;

    /// <summary>Bir kiracinin kota kurallarini listeler.</summary>
    public string SelectQuotas { get; protected set; } = string.Empty;

    /// <summary>Tek bir kota kuralini getirir.</summary>
    public string SelectQuota { get; protected set; } = string.Empty;

    /// <summary>Bir kota kuralini siler.</summary>
    public string DeleteQuota { get; protected set; } = string.Empty;

    /// <summary>Kota tuketimini atomik olarak artirir.</summary>
    public string AddQuotaUsage { get; protected set; } = string.Empty;

    /// <summary>Kota tuketim sayaclarini okur.</summary>
    public string SelectQuotaUsage { get; protected set; } = string.Empty;

    /// <summary>Bir webhook aboneligini ekler veya gunceller.</summary>
    public string UpsertWebhookSubscription { get; protected set; } = string.Empty;

    /// <summary>Bir kiracinin webhook aboneliklerini listeler.</summary>
    public string SelectWebhookSubscriptions { get; protected set; } = string.Empty;

    /// <summary>Tek bir webhook aboneligini adiyla getirir.</summary>
    public string SelectWebhookSubscription { get; protected set; } = string.Empty;

    /// <summary>Belirli bir olaya abone olan etkin abonelikleri getirir.</summary>
    public string SelectWebhookSubscriptionsForEvent { get; protected set; } = string.Empty;

    /// <summary>Bir webhook aboneligini siler.</summary>
    public string DeleteWebhookSubscription { get; protected set; } = string.Empty;

    /// <summary>Bir aboneligin basarisizlik sayacini gunceller ve esikte kapatir.</summary>
    public string UpdateWebhookSubscriptionOutcome { get; protected set; } = string.Empty;

    /// <summary>Bir webhook teslim kaydi ekler.</summary>
    public string InsertWebhookDelivery { get; protected set; } = string.Empty;

    /// <summary>Tek bir teslim kaydini getirir.</summary>
    public string SelectWebhookDelivery { get; protected set; } = string.Empty;

    /// <summary>Bir teslim denemesinin sonucunu yazar.</summary>
    public string UpdateWebhookDeliveryResult { get; protected set; } = string.Empty;

    /// <summary>Teslim gecmisini filtreleyerek listeler.</summary>
    public string SelectWebhookDeliveries { get; protected set; } = string.Empty;

    /// <summary>Yeni bir API anahtari ekler.</summary>
    public string InsertApiKey { get; protected set; } = string.Empty;

    /// <summary>Bir kiracinin API anahtarlarini listeler.</summary>
    public string SelectApiKeys { get; protected set; } = string.Empty;

    /// <summary>Bir API anahtarini ozetiyle arar. Kiraci suzgeci YOKTUR (bolum 53.5).</summary>
    public string SelectApiKeyByHash { get; protected set; } = string.Empty;

    /// <summary>Bir API anahtarini kiraci sinirinda iptal eder.</summary>
    public string RevokeApiKey { get; protected set; } = string.Empty;

    /// <summary>Bir API anahtarinin son kullanim damgasini gunceller.</summary>
    public string TouchApiKeyLastUsed { get; protected set; } = string.Empty;

    /// <summary>Sistemde verilen kapsami tasiyan gecerli bir anahtar var mi.</summary>
    public string HasApiKeyWithScope { get; protected set; } = string.Empty;

    /// <summary>Bir saklama politikasini ekler veya gunceller (kapsam catismasinda).</summary>
    public string UpsertRetentionPolicy { get; protected set; } = string.Empty;

    /// <summary>Bir kiracinin saklama politikalarini listeler.</summary>
    public string SelectRetentionPolicies { get; protected set; } = string.Empty;

    /// <summary>Tek bir hedefin politikasini kiraci icinde getirir.</summary>
    public string SelectRetentionPolicy { get; protected set; } = string.Empty;

    /// <summary>Bir saklama politikasini siler.</summary>
    public string DeleteRetentionPolicy { get; protected set; } = string.Empty;

    /// <summary>Yeni bir temizleme kosusu acar.</summary>
    public string InsertRetentionRun { get; protected set; } = string.Empty;

    /// <summary>Devam eden bir kosunun sayaclarini atomik olarak artirir.</summary>
    public string UpdateRetentionRunProgress { get; protected set; } = string.Empty;

    /// <summary>Bir kosuyu sonlandirir.</summary>
    public string CompleteRetentionRun { get; protected set; } = string.Empty;

    /// <summary>Kosu gecmisini filtreleyerek listeler.</summary>
    public string SelectRetentionRuns { get; protected set; } = string.Empty;

    /// <summary>Bir konusma baglantisinin ozet kaydini ekler veya gunceller.</summary>
    public string UpsertVoiceSession { get; protected set; } = string.Empty;

    /// <summary>Konusma kayitlarini en yeniden eskiye listeler.</summary>
    public string SelectVoiceSessions { get; protected set; } = string.Empty;

    /// <summary>Bir calistirma/mesaj puanini ekler veya (ayni yazar/hedefse) gunceller.</summary>
    public string UpsertRunScore { get; protected set; } = string.Empty;

    /// <summary>Bir calistirmanin tum puanlarini listeler.</summary>
    public string SelectRunScores { get; protected set; } = string.Empty;

    /// <summary>Bir puani siler.</summary>
    public string DeleteRunScore { get; protected set; } = string.Empty;

    /// <summary>Tek yurutucu kirasini alir (bos ise ekler, sahibi/suresi uygunsa gunceller).</summary>
    public string AcquireSingletonLease { get; protected set; } = string.Empty;

    /// <summary>Elde tutulan tek yurutucu kirasini uzatir.</summary>
    public string RenewSingletonLease { get; protected set; } = string.Empty;

    /// <summary>Tek yurutucu kirasini birakir.</summary>
    public string ReleaseSingletonLease { get; protected set; } = string.Empty;

    /// <summary>
    /// Bir idempotency anahtarini <c>Reserved</c> olarak eklemeyi dener. Anahtar
    /// zaten varsa <see cref="SqlDialect.IsUniqueViolation"/> ile yakalanan bir
    /// ihlal firlatir; cagiran taraf o zaman <see cref="SelectIdempotencyKey"/>
    /// ile mevcut kaydi okur.
    /// </summary>
    public string InsertIdempotencyKey { get; protected set; } = string.Empty;

    /// <summary>Bir idempotency anahtarini kiraci+anahtar ile getirir.</summary>
    public string SelectIdempotencyKey { get; protected set; } = string.Empty;

    /// <summary>Ayrilmis bir idempotency anahtarini <c>Completed</c> yapar ve yaniti yazar.</summary>
    public string CompleteIdempotencyKey { get; protected set; } = string.Empty;

    /// <summary>Bir idempotency anahtarini siler (basarisiz istekten sonra serbest birakma).</summary>
    public string DeleteIdempotencyKey { get; protected set; } = string.Empty;

    /// <summary>Gomulu migration metnindeki sema yer tutucusunu gercek adla degistirir.</summary>
    /// <param name="sql">Ham migration metni.</param>
    /// <returns>Calistirilabilir SQL.</returns>
    public string ApplySchema(string sql)
        => sql.Replace(SchemaPlaceholder, Schema, StringComparison.Ordinal);

    /// <summary>Gomulu SQL dosyalarinda sema adinin yerine gecen isaret.</summary>
    public const string SchemaPlaceholder = "{schema}";
}
