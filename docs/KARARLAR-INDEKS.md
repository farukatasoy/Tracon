# KARARLAR — İndeks

> **Üretilen, elle düzenlenmez.** Kaynak: `KARARLAR.md` · üretim: `scripts/dokuman-bakim.py`

Bul: `grep -n 'K-059\|jsonb' docs/KARARLAR.md`; oku: `sed -n 'N,Np' docs/KARARLAR.md`. Tarih yok (K-214). Reddedilenler: [`arsiv/KARARLAR-INDEKS-REDDEDILEN.md`](arsiv/KARARLAR-INDEKS-REDDEDILEN.md). En eski 327 karar: [`arsiv/KARARLAR-INDEKS-ARSIV.md`](arsiv/KARARLAR-INDEKS-ARSIV.md). 👤 kullanıcı kararı · 🔁 yeniden açılmış.

---

## En Yeni Kalıcı Kararlar (115 / 442 kalem)

| K | Satır | Karar |
|---|---|---|
| K-328 | 373 | Yargıç maliyeti `RunKind.Eval` dışlamasıyla ayrılır; yeni bir sütun açılmadı (Faz 49) |
| K-329 | 374 | İki kapılı varsayılan: `OnlineEvaluationOptions.Enabled = false` VE `SampleRate = 0.0` (Faz 49) |
| K-330 | 375 | Yargıcın `IChatClient`'ı guard boru hattından GEÇER; engelleme özel olarak ele alınmadı (Faz 49, D1) |
| K-331 | 376 | `RunScore.Author` yargıç puanlarında `judge:{ad}` ile BİLEREK DOLU yazılır (Faz 49) |
| K-332 | 377 | Cevrimiçi değerlendirme pencere özeti BELLEK İÇİDİR; yeni bir SQL sorgu yüzeyi açılmadı (Faz 49) |
| K-333 | 378 | `OnlineEvalJobHandler` DI'da hem `IJobHandler` hem KENDİ somut tipiyle kayıtlıdır (Faz 49) |
| K-334 | 379 | K-057 güncellendi: AgentPrism artık MCP istemcisi VE sunucusudur (Faz 50) |
| K-335 | 380 | A2A sunucu maliyeti yeniden ölçüldü: "+2 paket" değil "+4 paket"; iki paket plan taslağında hiç yoktu (Faz 50) |
| K-336 | 381 | A2A her disa acik agent icin AYRI bir alt yol ve AYRI bir agent karti kullanir; tekil kart varsayimi terk edildi (Faz 50) |
| K-337 | 382 | MCP/A2A dış çağrısı `ChildAgentInvoker`'ı KULLANMAZ; `ExternalAgentProxy`/`CatalogToolCallHandler` her zaman YENİ bir kök çalıştırma açar (Faz 50) |
| K-338 | 383 | MCP/A2A dış yüzeyleri erişim ayarlarını `IApplicationBuilder.Properties` üzerinden `MapAgentPrism`'den DEVRALIR; `MapAgentPrism` önce çağrılmalıdır (Faz 50) |
| K-339 | 384 | `ChildRunApproval` public yapıldı: üçüncü tüketici MCP/A2A dış çağrı katmanıdır (Faz 50) |
| K-340 | 385 | Dışa açılan bir agent'ın `AgentRunBudget.MaxDepth` değeri, kaç seviye TORUN çağrısına izin verildiğidir; "0" = hiç, "1" = bir seviye (Faz 50) |
| K-341 | 386 | Vektör gömüsü metin biçiminde (`::vector` cast) yazılır, hiçbir vektör paketi alınmaz (Faz 51) |
| K-342 | 387 | K-105 güncellenir: `ChatHistoryMemoryProvider` yine bağlanmadı, sebep artık ölçülmüş (Faz 51) |
| K-343 | 388 | Anlamsal arama yalnız PostgreSQL'de uygulanır (Faz 51) 👤 |
| K-344 | 389 | Sql.Shared'in cross-provider katmanı `IVectorSearchStore` için kullanılmaz (Faz 51) |
| K-345 | 390 | `document_embeddings.metadata` sütunu `jsonb`'dir, `json` değil (Faz 51) |
| K-346 | 391 | Migration şablonlama genelleştirildi: `SqlStoreContext.MigrationTemplateValues` (Faz 51) |
| K-347 | 392 | K-218 kapatıldı: `ToolMethodScanner` örnek metotları TARAMA ANINDA reddeder (Faz 52) |
| K-348 | 393 | Kaynak üreteci ayrı bir NuGet paketi değildir; `AgentPrism.Core` nupkg'sinde `analyzers/dotnet/cs/` altında taşınır (Faz 52) |
| K-349 | 394 | Kaynak üretecinin Roslyn sürümü `Microsoft.CodeAnalysis.CSharp` `4.8.0`'dır (Faz 52) |
| K-350 | 395 | `AddGeneratedTools()` `IAgentPrismBuilder`'a EKLENMEDİ; derlemeye özel üretilmiş bir uzantı metodudur (Faz 52) |
| K-351 | 396 | Parametre tipi beyaz listesi `record`/`class` (composite) tipleri KAPSAMAZ; `APG0003` ile reddedilir (Faz 52) |
| K-352 | 398 | F-76 (OpenAPI 500) YALNIZ `ProjectReference` tüketicisini etkiler; düzeltme kütüphanede değil, örnek uygulamanın derlemesindedir (2026-08-08 denetimi) |
| K-353 | 399 | `.UseMcp()` yapılandırmayı AÇIKÇA alır; AgentPrism kendiliğinden `IConfiguration` okumaz (2026-08-08 denetimi) |
| K-354 | 400 | SQL'e dokunan arka plan servisleri `SchemaReadyGate`'i bekler; hosted service kayıt sırası ZORLANMAZ (2026-08-08 denetimi) |
| K-355 | 401 | Çalıştırmanın alt yazmaları BEKLENEN kiracıyı taşır; ambient kiracı KULLANILMAZ ve alan HTTP sözleşmesine girmez (2026-08-08 denetimi) |
| K-356 | 402 | API anahtarı ozeti SHA-256'dır; Argon2 DEĞİL (Faz 53, Açık Soru 2) |
| K-357 | 403 | API anahtarı doğrulaması önbelleklenmez; her istekte `key_hash` ile SQL aranır (Faz 53, Açık Soru 3, seçenek A) |
| K-358 | 404 | `last_used_at` her istekte değil, en az bir dakikada bir yazılır (Faz 53, Açık Soru 4, seçenek B) |
| K-359 | 405 | `Authorization` başlığı sunulduğunda, statik `AuthToken` tanımsız olsa bile doğrulanmalıdır; eşleşmezse 401 (Faz 53, bilinçli davranış değişikliği) |
| K-360 | 406 | API anahtarı kapsam (`scope`) denetimi yalnız agents/runs/external-invoke uçlarına uygulandı; tam taksonomi ERTELENDİ (Faz 53) |
| K-361 | 407 | `docs/MIMARI.md` sıcak yol bütçesi 42.000 → 44.000 bayt (Faz 53) |
| K-362 | 408 | Heartbeat toplu yazılır: `IRunStore.TouchHeartbeatAsync` tek `Guid` değil `IReadOnlyCollection<Guid>` alır (Faz 54, Açık Soru 1, seçenek B) |
| K-363 | 409 | `RunErrorClass.Infrastructure` yeni değer olarak eklendi (Faz 54, Açık Soru 3, seçenek A'nın düzeltilmiş hâli) |
| K-364 | 410 | Oksuz hata parmak izi SABİT bir dize (`"orphaned"`); `ErrorFingerprint.Compute` ÇAĞRILMAZ (Faz 54) |
| K-365 | 411 | Heartbeat/oksuz-kapama sorguları dizi parametresi (`WHERE id IN (@array)`) KULLANMAZ; tekil `UPDATE` döngüsü ve alt-sorgulu tek `UPDATE` tercih edildi (Faz 54) |
| K-366 | 412 | `ClaimOrphanedRunsAsync` kapattığı çalıştırmanın `RunFailed` olayını da KENDİSİ yazar; ayrı bir yazma turu yok (Faz 54) |
| K-367 | 413 | `MapAgentPrismMcpServer`/`MapAgentPrismA2A` onay-yüzeyi denetimi, `Map*()` sırasında senkron çalışan bir kontrolden istek-bazlı `IEndpointFilter`e taşındı (kullanıcı kararı) 👤 |
| K-368 | 414 | `RunStatus.AwaitingApproval` eklendi; onay kararından sonra AYNI `RunId` devam ETMEZ, `AwaitingInput` emsaliyle birebir aynı şekilde YENİ bir çalıştırma açılır (Faz 55, kullanıcı kararı) 👤 |
| K-369 | 415 | `pending_approvals.run_id`, `runs(id)`e `ON DELETE CASCADE` ile bağlı; sözleşme testleri gerçek bir `runs` satırı önceden kuran bir `PrepareRunAsync` kancası kazandı (Faz 55) |
| K-370 | 416 | `ApprovalEndpoints.DecideAsync`, `audit` kaydını mutasyondan ÖNCE ve `AuditRecorder.WriteAsync` (hataları yutan sarmalayıcı) DEĞİL doğrudan `IAuditLog.WriteAsync` ile yazar (Faz 55) |
| K-371 | 417 | `IPendingApprovalStore.ExpireAsync`, planın taslak imzası `ValueTask<int>` yerine `ValueTask<IReadOnlyList<PendingApproval>>` döner (Faz 55, plandan sapma) |
| K-372 | 418 | Senkron/MCP/A2A çalıştırma yolu `pending_approvals`'a HİÇ yazmaz; yalnız kuyruktan koşan (`Prefer: respond-async`) çalıştırmalar yazar (Faz 55, plandan sapma — planın Açık Soru 1 önerisi "B: her ikisi" idi, uygulanan "A: yalnız kuyruk") |
| K-373 | 419 | Kanarya kuralı yalnızca İKİ kollu deneylerde tanımlanabilir; planın "kalan kollar kontrol sayılır" (çoğul) ifadesi UYGULANMADI (Faz 56, plandan sapma) |
| K-374 | 420 | `ExperimentAssignmentResolver.SelectVariant`, kanarya kuralı tanımlıyken kanarya kolunu HER ZAMAN `[0, ağırlık)` aralığına yerleştirir — bu, `Experiment.Variants`'ın FİZİKSEL sırasından bağımsızdır (Faz 56) |
| K-375 | 421 | `CanaryPolicy`'ye ayrı bir `RampRequiresSampleSize` alanı AÇILMADI; kademeli artırma da `MinSampleSize`'ı AYNEN kullanır (Faz 56, plandan sapma) |
| K-376 | 422 | `CanaryPolicy`'ye Faz 49'unkine benzer ayrı bir `EvaluationWindow` eklenmedi; kanarya kararı `ExperimentVariantResult`'ın TÜM-ZAMANLI (deney başından beri biriken) sonuçlarına dayanır (Faz 56, plandan sapma, Açık Soru 5) |
| K-377 | 423 | `ExperimentVariantResult`e `AverageScore` eklendi; `SelectExperimentResults` sorgusu `run_scores`'a (önce çalıştırma başına ortalama, sonra kol başına o ortalamaların ortalaması) genişletildi (Faz 56) |
| K-378 | 424 | Kademeli artırma adımı denetim izine YAZILMAZ; yalnız otomatik GERİ ALMA `IAuditLog.WriteAsync` ile mutasyondan ÖNCE (K-089 emsali) yazılır (Faz 56) |
| K-379 | 425 | `IExperimentStore.SetCanaryPolicyAsync`, `SaveAsync`'in Draft-yalnız kısıtından MUAFTIR; kanarya kuralı deney Running iken de tanımlanabilir veya kaldırılabilir (Faz 56) |
| K-380 | 426 | `CompiledAgentCache`'in anahtarına `TenantId` eklendi; iki farklı kiracının aynı ad+sürüm+bağımlılık parmak izinde bir tanımı olması artık BİRİNCİ kiracının derlenmiş agent'ını İKİNCİ kiracıya sızdırmaz (manuel kabul testi hazırlığı, kullanıcı talebiyle bulundu) |
| K-381 | 427 | Paylaşılan `AgentFileStore` (dosya belleği/metin araması) her kiracı için `TenantPrefixingAgentFileStore` ile sarmalanır; kiracı sınırı MAF'ın kendi deposuna dokunmadan bir vekil (proxy) katmanında uygulanır (manuel kabul testi hazırlığı) |
| K-382 | 428 | `AgentPrismEndpointFilter`'a `CheckTenancyWhitelist` eklendi; `AllowedTenants` doluyken listede olmayan bir aday artık istek endpoint'e ulaşmadan 403 ile reddedilir, varsayılan kiracıya SESSİZCE düşmez (manuel kabul testi hazırlığı) |
| K-383 | 429 | `AgentPrism.Core.csproj`'un üreteç-paketleme hedefi `@(Analyzer)` yerine Generators projesinin `GetTargetPath` çıktısını MSBuild görevi ile okur; `dotnet pack --no-build` artık `analyzers/dotnet/cs/AgentPrism.Generators.dll`'i İÇERİR (manuel kabul testi hazırlığı — daha önce Kritik olarak not düşülmüştü) |
| K-384 | 430 | SSE akış uçlarındaki (`AgentEndpoints`, `OpenAIResponsesEndpoints`, `OpenAIChatCompletionsEndpoints`) dar istisna filtresi kaldırıldı; `OperationCanceledException` dışındaki HER istisna artık bir `error` çerçevesine dönüşür (K-296'nın tamamlanması, manuel kabul testi hazırlığı) |
| K-385 | 431 | K-302 güncellendi: `AddCaseAsync`'in yeniden deneme döngüsüne jitter eklendi, üst sınır 5 → 10'a çıkarıldı |
| K-386 | 432 | K-317 kapandı: Docker Desktop 4.29.0 → 4.86.0 güncellemesi gerçek `mssql/server`'daki Rosetta hatasını çözdü; SQL Server sözleşme testleri bu makinede artık `azure-sql-edge` ikamesi OLMADAN, gerçek imajla koşuyor |
| K-387 | 433 | `SqlServerTestContext.DisposeAsync` artık test şemasını GERÇEKTEN bırakır (dokümantasyon iddia ediyordu, kod yapmıyordu); deadlock'a çarparsa 5 denemeli backoff ile yeniden dener |
| K-388 | 434 | `MigrationRunner.ApplyOneAsync` migration SQL'ini ve `INSERT INTO __migrations` kaydını TEK round-trip'te birleştirir; `CreateSchema`+`CreateMigrationsTable` birleşimi ve `SET XACT_ABORT ON` ile tam tek-round-trip BİLEREK yapılmadı |
| K-389 | 435 | Migration kilidi VERİTABANI genelinden SEMAYA/ONEĞE kapsandı: `SqlServerDialect`, `PostgresDialect`, `SqliteDialect` artık `sp_getapplock`/`pg_advisory_lock`/kilit dosyası anahtarını sema (veya SQLite'ta tablo öneki) adından türetir |
| K-390 | 436 | Sözleşme test izolasyonu "her test kendi şeması" modelinden "her test SINIFI kendi şeması + her test verisini `ResetDataAsync` ile sıfırlar" modeline geçti (K-389 ile birlikte) |
| K-391 | 437 | PostgreSQL: `CREATE EXTENSION IF NOT EXISTS vector` içeren `0024_vector.sql`, K-389 sonrası eş zamanlı ilk-kez migrasyonlarda benzersizlik ihlaline (`pg_extension_name_index`, SQLSTATE 23505) düşebilir — `MigrationRunner.ApplyOneAsync` bunu jitter'lı yeniden deneme ile karşılar, test fixture'ı ise uzantıyı bir kez ÖNCEDEN kurarak yarışı tamamen önler |
| K-392 | 438 | `InvariantGlobalization` hem örnek uygulamadan (`samples/AgentPrism.Api`) hem paket şablonundan (`AgentPrism.Starter`) KALDIRILDI; SQL Server desteğiyle bağdaşmıyor |
| K-393 | 439 | `A2AApprovalGuardFilter`/`McpApprovalGuardFilter`: şema hazır değilken (`AutoApplyMigrations=false`) katalog sorgusu HEMEN uygulamayı durdurmak yerine sınırsız sayıda, üstel gecikmeli (5 sn'de tavanlanan) yeniden dener; deneme SAYISI değil yalnız uygulama kapanışı sınırlar (HATA-S1-002, manuel kabul testi S1) |
| K-394 | 440 | Workflow çalıştırmaları artık kota muhasebesinden geçiyor: `WorkflowEndpoints.RunAsync` agent'larla AYNI `QuotaGate.CheckAsync` 429 kapısından geçer, `WorkflowRunner.CompleteAsync` workflow'un TAMAMINI (Depth 0, `TreeUsage`/`TreeCost` toplamından) TEK bir "run" olarak `QuotaEnforcer.RecordAsync`'e yazar (HATA-S1-006, Kritik, manuel kabul testi S1) |
| K-395 | 441 | `AgentPrismEndpointFilter`: `requireBearerToken:false` gruplarına (eşlenmemiş `api/*` yolları, gerçek zamanlı ses ucu) AgentPrism'in kendi statik `AuthToken`'ıyla eşleşen bir başlık artık REDDEDİLMİYOR — başlık YOKMUŞ gibi nötr davranılıyor (HATA-S1-014, manuel kabul testi S1) |
| K-396 | 442 | `AgentDefinitionCompiler.SearchFileStoreAsync`: `TextSearchProvider`'ın doğal dil sorgusu artık `AgentFileStore.SearchAsync`'e ham regex olarak DEĞİL, boşluğa göre ayrılmış ≥3 karakterlik tokenlerin kaçışlanıp "VEYA" ile birleştirildiği bir desen olarak geçiyor (HATA-S1-009, manuel kabul testi S1) |
| K-397 | 443 | `ApiKeyScope`'a `KnowledgeRead`/`KnowledgeAdmin` eklendi; `KnowledgeEndpoints`'in tüm uçları artık `RequireApiKeyScope` çağırıyor (HATA-S1-011, Yüksek, manuel kabul testi S1) |
| K-398 | 444 | `RunRecordingAgent.RunCoreStreamingAsync`: terminal durum artık tüketicinin ERKEN `DisposeAsync()`'i (dogal bitiş değil, istisna da değil) durumunda da yazılıyor — `Canceled`, o ana kadar biriken kısmi `usage` ile (HATA-S1-015, Yüksek, manuel kabul testi S1) |
| K-399 | 445 | `AgentPrismRetentionOptions`'a `RunInputs`/`VoiceSessions`/`RunScores` (varsayılan sırasıyla 30/30/180 gün) ve `DocumentEmbeddings` (varsayılan KAPALI, `MaxAgeDays=null`) eklendi — dört hedef daha önce `ForTarget`'ta `_ => null` dalına düşüp config varsayılanını SESSİZCE yok sayıyordu (HATA-S1-005, Düşük, manuel kabul testi S1) |
| K-400 | 446 | Skill script çalıştırma iki ayrı kök nedenle TAMAMEN çalışmıyordu: (1) resolver'sız `JsonSerializerOptions`, (2) MAF'ın nullable-ama-required arguman semasını `null` ile reddetmesi (HATA-K-002, Kritik, manuel kabul testi Ortak Kuyruk) |
| K-401 | 447 | `WorkflowRunner.ToRunError` artık `TargetInvocationException`/tek-elemanlı `AggregateException` sarmalayıcılarını soyar; gerçek neden `RunError.Message`'a yazılır (HATA-K-003, Kritik, manuel kabul testi Ortak Kuyruk) |
| K-402 | 448 | `UseWorkflows()` artık `AgentPrismWorkflowOptions`'ı `AgentPrism:Workflows` bölümünden `IConfiguration`'a BAĞLIYOR (HATA-K-004, Kritik, manuel kabul testi Ortak Kuyruk) |
| K-403 | 449 | `WorkflowRunner.RunStreamingAsync` gerçek bir `async IAsyncEnumerable` yineleyicisi yapıldı; `sessionId` doğrulaması artık SSE `event: error` üretiyor, düz `HTTP 500`'e düşmüyor (HATA-K-005, Kritik, manuel kabul testi Ortak Kuyruk) |
| K-404 | 450 | `POST/PUT /api/agents` artık `AgentDefinitionValidator.ValidateAsync`'i SAVE ZAMANINDA çağırıyor; bilinmeyen skill/tool/callable-agent adı `400` ile reddediliyor (HATA-K-001, Yüksek, manuel kabul testi Ortak Kuyruk) |
| K-405 | 451 | `WorkflowEndpoints` artık `ApiKeyScope.WorkflowsRead`/`WorkflowsAdmin`/`RunsRead`/`RunsWrite` uyguluyor (HATA-K-006, Yüksek, manuel kabul testi Ortak Kuyruk) |
| K-406 | 452 | `BindRunRecording` artık `RecordRunInput`'ı config'ten okuyor (HATA-K-007, Yüksek, manuel kabul testi Ortak Kuyruk — aynı kök neden HATA-S2-002/HATA-S4-015'te de bağımsız bulunmuştu) |
| K-407 | 453 | `EvalEndpoints`/`ExperimentEndpoints`/`RunEndpoints` artık Eval/Experiment/`RunsRead`/`RunsWrite` kapsamlarını uyguluyor (HATA-K-008, Yüksek, manuel kabul testi Ortak Kuyruk) |
| K-408 | 454 | Kaynak dili sınırı: pakete giren veya çalışma anında çalışan her şey İngilizce'dir; geliştirme aparatı (`docs/`, `.agents/skills/`, `scripts/`) Türkçe kalır 👤 |
| K-409 | 455 | Migration `.sql` yorumları İngilizce'ye çevrildi; 61 dosyanın tamamının checksum'ı değişti — var olan bir veritabanına karşı çalıştırmadan önce şema düşürülüp yeniden kurulmalıdır 👤 |
| K-410 | 456 | `SourceLanguageTests` eklendi: taban çizgisi güdümlü, kelime sınırlı Türkçe sözlük + aksan taraması; `ProblemDetailsLanguageTests`in genelleştirilmiş kardeşi |
| K-411 | 457 | Senkronizasyon kopyası (`<ad> 2.<uzantı>`) taraması `faz-tamamlama` skill'ine KAPI olarak eklendi; kapsam `src tests samples` |
| K-412 | 458 | Doküman dizin bütçesi `docs/arsiv/` ve `docs/manuel-test/kosumlar/` HARİÇ ölçülür 👤 |
| K-413 | 459 | `docs/YOL-HARITASI.md` ÜRETİLEN dosyadır; kaynak her fazın kendi `> |
| K-414 | 460 | Manuel test koşum kaydı ile spesifikasyon AYRI dosyalarda yaşar; ikinci koşum `kosumlar/<tarih>/` kardeşi açar, üzerine yazmaz |
| K-415 | 461 | Ürün dokümantasyonu ayrı bir Astro Starlight sitesindedir (`docs-site/`), İngilizce'dir ve `farukatasoy.github.io/AgentPrism` adresinde yayınlanır 👤 |
| K-416 | 462 | API referansı DocFX'in `outputFormat: markdown` çıktısından üretilir ve Starlight içine gömülür; DocFX'in kendi HTML sitesi kullanılmadı |
| K-417 | 463 | HTTP API sayfaları OpenAPI belgesinden ÜRETİLİR; Scalar gömülmedi |
| K-418 | 464 | 🚨 `AddOpenApi()` ÇIPLAK çağrılmalıdır; yapılandırma `Configure<OpenApiOptions>("v1", ...)` ile AYRI kaydedilir — aksi hâlde ŞEMA XML DOKÜMANI SESSİZCE DÜŞER |
| K-419 | 465 | Doküman sitesinin ekran görüntüleri E2E koşumundan üretilir ve commit edilir; elle alınan görsel kabul edilmez |
| K-420 | 466 | Yayınlanan `description` metinleri iç doküman referansı taşımaz |
| K-421 | 467 | Public API takibi (`EnablePublicApiTracking`) Faz 60'ta, yayın kararından (Faz 7, K-068) bağımsız olarak açıldı 👤 |
| K-422 | 468 | RS0026'nın 10 kalemi üç ayrı stratejiyle çözüldü: birleştirme yalnız `ResolveAsync` içindir, geri kalanı ayrıştırma veya statik fabrikadır |
| K-423 | 469 | RS0041 (oblivious reference type) `AgentPrism.Abstractions.csproj`'da tek satırlık `NoWarn` ile bastırıldı; kök neden ölçüldü |
| K-424 | 470 | `AgentPrism.Generators` ve `AgentPrism.Templates` public API takibinden yeni bir `AgentPrismPublicApiTrackingEnabled` MSBuild özelliğiyle hariç tutuldu |
| K-425 | 471 | Aday yeteneği üretimi `aday-kesfi` skill'iyle yazılı bir protokole bağlandı; oturum elemeli diyalog olarak koşar ve her bulgu üç kanala ayrışır 👤 |
| K-426 | 472 | Keşif turu kaydı `docs/kesif/<tarih>-<konu>.md` altında yaşar ve doküman dizin bütçesinden HARİÇ tutulur 👤 |
| K-427 | 473 | `docs/` kökü YALNIZ canlı dokümanı taşır; kapanmış her kayıt `docs/arsiv/`'e taşınır 👤 |
| K-428 | 474 | Aday listesi tur numarası taşımaz: `UCUNCU-FAZ-ADAYLARI.md` → `ADAYLAR.md` 👤 |
| K-429 | 475 | Manuel kabul testi PROTOKOLÜ `manuel-test-kosumu` skill'ine taşındı; `docs/manuel-test/` yalnız spec + koşum kaydı taşır 👤 |
| K-430 | 476 | `00-INDEKS.md` §7 `Koşum` sütunu koşum kaydından ÖLÇÜLÜR, elle işaretlenmez |
| K-431 | 477 | Örnek uygulama rol politikalarını `AgentPrism:Demo:Roles:Enabled` ile kaydeder ve o anda `RequireRolePolicies`'i AÇAR; şema gösterim amaçlı bir başlık okuyucusudur |
| K-432 | 478 | Workflow çalıştırması iptal istendiğinde `Completed` yerine `Canceled` yazılır; zorlama süper-adım sınırında VE pompa çıkışında yapılır |
| K-433 | 479 | Pompa, yanıtlanmış bir istekten sonra akışı yeniden açmadan ÖNCE MAF'ın kendi çalıştırma durumunu sorar |
| K-434 | 480 | Dosya belleği/metin araması alt ağacı kiracı VE agent adıyla öneklenir; oturumlar arası paylaşım BİLEREK korunur |
| K-435 | 481 | `IToolRegistry`/`AgentPrismToolRegistration` `AITool`'a değil `AIFunctionDeclaration`'a genişledi |
| K-436 | 482 | İstemci tool'u bildirimi `AIFunctionFactory.Create(...).AsDeclarationOnly()` değil `AIFunctionFactory.CreateDeclaration(...)` ile kurulur |
| K-437 | 483 | İstemciden gelen tool sonucu modele `ChatRole.Tool` altında gönderilir, `ChatRole.User` değil |
| K-438 | 484 | CORS, `services.AddCors()` OLMADAN `CorsService`/`CorsMiddleware`'in elle kurulmasıyla uygulanır |
| K-439 | 485 | `toolResults` eşleştirmesi bilerek İKİ KEZ yapılır: akış başlamadan önce doğrulama, sonra gerçek mesaj kurulumu |
| K-440 | 486 | `ClientToolResult.Result`/`ErrorMessage` 65.536 karakterle sınırlanır |
| K-441 | 487 | Gömülebilir bileşen yalnız akışsız (`Idempotency-Key`) istek kullanır; SSE ayrıştırıcı taşımaz |
| K-442 | 488 | Gömülebilir bileşen `wwwroot/embed/` altına ayrı bir Vite girişiyle derlenir ve VAR OLAN genel varlık sunum mekanizmasıyla, sıfır yeni C# `endpoint` koduyla sunulur |
