# 03 — Kalıcılık: PostgreSQL (`PG`) — Koşum Kaydı (2026-08-13)

> **Bu dosya bir koşum kaydıdır, spesifikasyon değildir** (Faz 58.3 ayrımı).
> Spesifikasyon: [`../../03-KALICILIK-POSTGRESQL.md`](../../03-KALICILIK-POSTGRESQL.md) — `Ön koşul`, `Adımlar`,
> `Beklenen sonuç` oradadır ve yeniden koşulabilir.
>
> Aşağısı yalnız **2026-08-13** koşumunun `Gerçek sonuç` ve `Durum`
> kayıtlarıdır. İkinci bir koşum bu dosyayı **ezmez**; kardeş bir
> `kosumlar/<tarih>/` dizini açar.

---

> ### ⚗️ Damıtılmış koşum kaydı
> Geçen ve **hiçbir düzeltme/kusur işareti taşımayan** case'lerin
> `Gerçek sonuç` blokları düştü — bir koşumun ortam çıktısı, koşum
> bittiği anda değerini kaybeder. **Geçmeyen** ve **işaret taşıyan**
> her case'in bloğu AYNEN durur. Tam metin — kopyala, çalıştır:
>
> ```bash
> git show efd5247:docs/manuel-test/kosumlar/2026-08-13/03-KALICILIK-POSTGRESQL.md
> ```

---

## Temiz geçen case'ler (26)

| Case | Durum | Başlık |
|---|---|---|
| MT-PG-002 | ☑ | Şema adı `public` olamaz |
| MT-PG-003 | ☑ | Geçersiz şema adı biçimleri ve enjeksiyon denemesi reddedilir |
| MT-PG-004 | ☑ | `CommandTimeoutSeconds` sınırları |
| MT-PG-005 | ☑ | Üç `UsePostgreSql` aşırı yüklemesi aynı sonucu üretir |
| MT-PG-006 | ☑ | Bağlantı dizesi hiç verilmezse hangi denetim önce tetiklenir |
| MT-PG-007 | ☑ | Yanlış host ile başlatma migration adımında çöker (fail-fast) |
| MT-PG-020 | ☑ | Boş DB'de 28 migration sırayla uygulanır |
| MT-PG-021 | ☑ | Yeniden başlatma migration'ları tekrar uygulamaz (idempotent) |
| MT-PG-022 | ☑ | Var olan (kısmi) şema üzerine devam |
| MT-PG-023 | ☑ | Uygulanmış migration'ın checksum'ı bozulursa başlama reddedilir |
| MT-PG-024 | ☑ | İki eşzamanlı örnek çakışmadan migration uygular |
| MT-PG-026 | ☑ | Şema adı değiştirildiğinde bağımsız bir migration seti oluşur |
| MT-PG-027 | ☑ | `Dimensions` değişikliği uygulanmış `vector` sütununun boyutunu DEĞİŞTİRMEZ |
| MT-PG-033 | ☑ | Diğer depolar `Replace` ile kayıtlıdır: tüketici önce kaydetse de PostgreSQL kazanır |
| MT-PG-035 | ☑ | Sağlayıcı çağrı sırası değişirse kazanan değişir |
| MT-PG-040 | ☑ | `pgvector` eklentisi ve HNSW indeksi migration sonrası kuruludur |
| MT-PG-041 | ☑ | Embedding uzunluğu depo boyutuyla eşleşmezse `UpsertAsync` reddedilir |
| MT-PG-042 | ☑ | Aynı kaynak yeniden yazılırsa eski parçalar silinir (upsert-üzerine-yazma) |
| MT-PG-043 | ☑ | Arama kosinüs mesafesine göre artan sıralı döner |
| MT-PG-044 | ☑ | İki kiracı aynı koleksiyon/kaynak kimliğini paylaşsa da birbirini görmez |
| MT-PG-045 | ☑ | Kaynak silindiğinde tüm parçaları kaybolur |
| MT-PG-046 | ☑ | `MaxDistance` filtresi uzak sonuçları eler |
| MT-PG-047 | ☑ | Koleksiyon adı geçersiz karakter taşıyorsa HTTP ucu 400 döner |
| MT-PG-052 | ☑ | Teşhis ucu migration UYGULAMAZ (salt okunur) |
| MT-PG-060 | ☑ | 20 eşzamanlı yazma isteği veri bozulmadan tamamlanır |
| MT-PG-061 | ☑ | PostgreSQL koşum sırasında durursa çalışan bir istek anlaşılır hatayla başarısız olur, uygulama çökmez |

## Ayrıntı taşıyan case'ler (10)

## MT-PG-001 — Boş bağlantı dizesiyle başlatma reddedilir

**Gerçek sonuç**
> **Kaldı — ama kök neden ürün kusuru değil, case'in İzlek B ile test edilemez olması.**
> `samples/AgentPrism.Api/Program.cs:639` şu korumayı taşır:
> `else if (!string.IsNullOrWhiteSpace(postgreSql["ConnectionString"])) { agentPrism.UsePostgreSql(postgreSql); }`.
> Bağlantı dizesi boş bırakıldığında bu koşul `false` döner ve `UsePostgreSql()`
> **hiç çağrılmaz** — validator'a hiçbir zaman ulaşılmaz. Uygulama normal
> başladı (`Now listening on: http://localhost:5080`), `OptionsValidationException`
> ATILMADI. `GET /agentprism/api/diagnostics`: `"persistenceProvider": "InMemory"`,
> `"registeredPersistenceProviders": 0`. `/health` **200 Degraded** döndü (kalıcılık
> değil, model sağlayıcı sağlığı nedeniyle — bkz. `25-SAGLIK-TESHIS-OPENAPI.md`).
>
> Ayrı bir yardımcı harness ile (bu case'in adımı DEĞİL, doğrulama amaçlı,
> `src/AgentPrism.PostgreSql`'e `ProjectReference` veren bir konsol) doğrudan
> `UsePostgreSql(...)` çağrıldığında validator'ın TAM DA belgelenen gibi
> çalıştığı doğrulandı:
> - `UsePostgreSql("")` (string aşırı yüklemesi) çağrı ANINDA
>   `ArgumentException: The value cannot be an empty string or composed
>   entirely of whitespace. (Parameter 'connectionString')` fırlatır — belgelenenden
>   daha erken/sert bir hata.
> - `UsePostgreSql(IConfiguration)` (örnek uygulamanın kullandığı aşırı yükleme)
>   çağrının kendisinde atmaz, ama `IOptions<AgentPrismPostgreSqlOptions>.Value`
>   erişiminde TAM OLARAK belgelenen mesajı taşıyan
>   `Microsoft.Extensions.Options.OptionsValidationException` fırlatır:
>   `"AgentPrismPostgreSqlOptions.ConnectionString bos olamaz. ..."`.
>
> **Sonuç:** `AgentPrismPostgreSqlOptionsValidator` doğru çalışıyor. Kusur,
> bu case'in İzlek B (örnek uygulama) üzerinden yazılmış olmasında — örnek
> uygulamanın kasıtlı "bağlantı dizesi yoksa bellek içi çalış" davranışı
> (`Program.cs:622` yorumu) bu senaryoyu YAPISAL OLARAK erişilemez kılıyor.
> Öneri: case'in adımları İzlek A/C'ye (doğrudan `UsePostgreSql()` çağıran bir
> konsol) taşınmalı; İzlek B için ayrı ve doğru bir case ("boş bağlantı dizesiyle
> örnek uygulama bellek içi depoya sessizce düşer") eklenmeli.

---

**Doküman düzeltmesi (2026-08-15, KAPANIS-PLANI §8):** Ürün kusuru yok —
`AgentPrismPostgreSqlOptionsValidator` İzlek A/C harness'ında belgelenen
davranışı tam olarak sergiliyor. Beklenti yukarıda koda göre düzeltildi.
Case'in İzlek A/C'ye taşınması ayrı bir doküman görevi olarak açık kalır.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

> **Temizlik:** `dotnet user-secrets set "AgentPrism:PostgreSql:ConnectionString" "Host=localhost;Port=55432;Database=agentprism;Username=postgres;Password=agentprism"` — uygulandı.

---

## MT-PG-008 — `secret` hiçbir zaman veritabanına veya dosyaya yazılmaz

**Gerçek sonuç**
> Beklendiği gibi (bu case önceki case'lerden agent kaydı birikince koşuldu —
> `manuel-denetim` dahil). `grep` sıfır satır döndü (exit code 1). Her iki SQL
> sorgusu da `count = 0`.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

# 2 — Migration'lar

`MigrationRunner`/`MigrationHostedService` `AgentPrism.Sql.Shared` içinde
yaşar ama davranışları PostgreSQL diyalektinin (`pg_advisory_lock`, gömülü
`.sql` dosyaları) üzerinden gözlenir. Kapsam kararı (bkz.
[`PROMPT.md`](../../../arsiv/manuel-test-kosum-2026-08/PROMPT.md) §3): migration üç
yoldan test edilir — boş DB, yeniden çalıştırma (idempotent), var olan şema
üzerine.

> 🚨 **Bu bölümdeki `28` sayısı 2026-08-13 koşumuna aittir ve donmuştur.**
> `0029_idempotency_response_headers.sql` o koşumdan sonra eklendi (bugün
> **29**). Sayı kaydı bozmamak için değiştirilmedi. **İkinci koşumda sayı
> yeniden türetilir**, ezberden alınmaz:
> `ls src/AgentPrism.PostgreSql/Migrations/*.sql | wc -l`

---

## MT-PG-025 — `AutoApplyMigrations=false` migration uygulamaz, sorumluluk operatöre kalır

**Gerçek sonuç**
> **Kaldı — gerçek, iki bağımsız denemede TUTARLI biçimde tekrar üretildi.**
> Log beklenen `AgentPrism migration'lari otomatik uygulanmiyor (AutoApplyMigrations
> kapali). Semanin guncel olmasi cagiranin sorumlulugundadir.` satırını taşıdı
> ve `Now listening on: http://localhost:5080` bile yazdı — ama birkaç saniye
> içinde uygulama KENDİ KENDİNİ KAPATTI:
> ```
> crit: AgentPrism.A2AApprovalGuardFilter[0]
>       A2A disa acik yuzey denetimi basarisiz oldu; uygulama durduruluyor.
>       Npgsql.PostgresException (0x80004005): 42P01: relation "agentprism.agent_definitions" does not exist
> info: Microsoft.Hosting.Lifetime[0]
>       Application is shutting down...
> ```
> İki bağımsız denemede de (temiz şema, `AutoApplyMigrations=false` sabit)
> aynı çöküş oluştu — rastgele bir yarış değil, tutarlı bir davranış.
> `/health` ve agent kaydı isteklerine ULAŞILAMADI (`HTTP: 000`) çünkü süreç
> çökme sürecindeydi.
>
> **Kök neden** (`src/AgentPrism.AspNetCore/A2A/A2AApprovalGuardFilter.cs`,
> `src/AgentPrism.Abstractions/Diagnostics/SchemaReadyGate.cs`): örnek uygulama
> `UseA2A(o => o.ExposedAgents.Add("ozetleyici"))` çağırıyor (`Program.cs:99`,
> VARSAYILAN yapılandırma). `SchemaReadyGate.MarkReady()`, `AutoApplyMigrations`
> kapalıyken de `MigrationHostedService` tarafından BİLEREK hemen çağrılıyor
> (kod yorumu: "o durumda semanin hazir olmasi tuketicinin sorumlulugundadir").
> Kapı açılır açılmaz `A2AApprovalGuardFilter`'ın arka plan denetimi
> `IAgentCatalog.ListAsync()` çağırıyor, bu da var olmayan
> `agentprism.agent_definitions` tablosuna çarpıyor, `catch (Exception)` bloğu
> bunu `LogCritical` + `lifetime.StopApplication()` ile karşılıyor. Aynı desen
> `McpApprovalGuardFilter`'da da var (kod yorumu: "AYNI gerekce ve AYNI
> tasarim") — A2A kapalı olsa MCP'nin de aynı şekilde çökertmesi beklenir.
>
> **Etki:** K-354'ün belgelediği sözleşme ("uygulama başlar, sorumluluk
> operatöre kalır") A2A/MCP açıkken (örnek uygulamanın VARSAYILANI) TAMAMEN
> geçersiz — uygulama Degraded modda hizmet vermek yerine kendini kapatıyor.
> Gerçek bir dağıtımda (ör. K8s: API pod, migration Job'undan önce ayağa
> kalkarsa) bu, `AutoApplyMigrations=false` seçmenin amacını tam tersine
> çeviren bir crash-loop üretir. Belgede **Yüksek** işaretli; gözlenen etkinin
> (dokümante edilen sözleşmenin A2A/MCP açıkken TAMAMEN işlevsiz olması,
> Degraded değil TAM KESİNTİ) **Kritik**'e yükseltilmesi önerilir.
>
> ---
>
> **2026-08-14 yeniden koşum — Geçti.** Kusur `2caa423` (S1-8 dalgası) ile
> kapanmış: `ExternalSurfaceGuard.ListCatalogWithRetryAsync` katalog sorgusunu
> artık üstel geri çekilmeyle yeniden dener ve hiçbir koşulda
> `StopApplication` çağırmaz; log satırı bunu açıkça söylüyor ("bu arada
> uygulamanin geri kalani calisir durumda kalir"). Bu case'in dört beklentisi
> de ampirik olarak doğrulandı (temiz `mt_fin` şeması,
> `AutoApplyMigrations=false`):
> - `migration'lari otomatik uygulanmiyor` log satırı: **1 kez** yazıldı.
> - `Application is shutting down`: **0 kez** — uygulama ayakta kaldı.
> - `/health`: **Unhealthy**, `HTTP 503`.
> - Agent kaydı: `HTTP 500` (tablo yok). Peş peşe ikinci bir kayıt isteği de
>   aynı `500`'ü verdi ve `/health` hâlâ yanıt verdi — çökme yok.
>
> Not: gövdede `provider:"echo"` kullanılırsa `400 Tanim gecersiz` alınır
> (gerçek sağlayıcı anahtarları kayıtlıyken `echo` kayıtlı değildir);
> veritabanına ulaşan yolu görmek için kayıtlı bir sağlayıcı (`openai`)
> kullanıldı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

> **Temizlik:** `dotnet user-secrets remove "AgentPrism:PostgreSql:AutoApplyMigrations"`, reset yordamı — uygulandı.

---

## MT-PG-030 — Yönetimsel yazmalar denetim izine düşer, yürütme yan ürünleri düşmez

**Gerçek sonuç**
> Beklendiği gibi. `agent:manuel-denetim` / `agent.create` için tek audit
> kaydı var. `runs`/`tool_invocations` için `count(*) = 0`.
>
> **Yan not (ortam, kusur değil):** `echo` sağlayıcısı yalnız OpenAI anahtarı
> BOŞ olduğunda kayıtlı oluyor (`samples/AgentPrism.Api/Program.cs:128,149` —
> `openAiEnabled = !string.IsNullOrWhiteSpace(...)`). Bu makinenin
> `user-secrets`'ında gerçek bir OpenAI anahtarı zaten tanımlıydı; ilk deneme
> bu yüzden `'echo' adinda bir model saglayicisi kayitli degil` ile 400 döndü.
> Bu dosyanın geri kalanı (İzlek B, `echo` kullanan tüm case'ler: 030/060/061)
> için OpenAI anahtarını GEÇİCİ olarak kaldırdım; MT-PG-047'ye gelindiğinde
> geri ekleyip o case sonrası yine kaldıracağım. `dotnet user-secrets` dışında
> hiçbir yere yazılmadı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-PG-031 — `IConversationBranchStore` `TryAdd` önceliği

**Gerçek sonuç**
> Beklendiği gibi — çıktı `Cozumlenen tip: SahteDalStore`. Doc'un anlattığı
> gibi, script iki noktada düzeltme gerektirdi (imza sürüklenmesi, kanıtlanan
> davranışı etkilemiyor): gerçek imza `BranchAsync(...)`/`ConversationBranch?`
> (`CreateBranchAsync`/`ConversationBranchInfo` DEĞİL), ve `UsePostgreSql(...)`
> `IServiceCollection` üzerinde değil `IAgentPrismBuilder` üzerinde bir
> extension — `var builder = services.AddAgentPrism();` yakalanıp
> `builder.UsePostgreSql(...)` çağrılması gerekti (`builder.Services` alttaki
> AYNI `IServiceCollection`'ı taşıyor, bu yüzden `TryAddSingleton` `services`
> üzerinden önce çağrılabiliyor).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-PG-032 — `IVectorSearchStore` aynı `TryAdd` önceliğine uyar

**Gerçek sonuç**
> Beklendiği gibi — `Cozumlenen tip: SahteVektorStore`. `IVectorSearchStore`
> imzası doc'la birebir eşleşti; tek düzeltme MT-PG-031'deki gibi
> `UsePostgreSql`'in `builder` üzerinden çağrılması oldu.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-PG-034 — Aynı zincirde iki kalıcılık sağlayıcısı kayıtlıysa uyarı loglanır

**Gerçek sonuç**
> **Kaldı — uyarı satırı doğru çıktı ama uygulama sonra çöktü; iki bağımsız
> denemede TUTARLI.** Log beklenen uyarıyı verdi: `AgentPrism'de birden fazla
> kalicilik saglayicisi kayitli: PostgreSQL, SQLite. Son kayit kazanir ve su an
> SQLite kullaniliyor. Yalnizca birini cagirin.` Ama birkaç saniye sonra AYNI
> `MT-PG-025` kusuruyla (`A2AApprovalGuardFilter`/`McpApprovalGuardFilter`)
> çöktü — `SQLite Error 1: 'no such table: agentprism_agent_definitions'` ile
> `crit` + `Application is shutting down...`. `Now listening` yazıldıktan hemen
> sonra süreç öldüğü için `/api/diagnostics` ve `/health` isteklerine hiç
> ULAŞILAMADI (`HTTP: 000`).
>
> **Kök neden — MT-PG-025'ten FARKLI bir tetikleyici, AYNI temel kusur:**
> burada `AutoApplyMigrations` açık; PostgreSQL şeması önceki case'lerden
> zaten TAM güncel olduğu için o sağlayıcının `MigrationHostedService`'i
> saniyeler içinde biter ve `SchemaReadyGate.MarkReady()`'yi HEMEN çağırır.
> Ama kazanan depo uygulamaları (`Replace` deseniyle) SQLite'a ait ve SQLite'ın
> KENDİ migration'ı (15 tablo, `agentprism_` şeması, sıfırdan) henüz
> BİTMEMİŞTİR — `SchemaReadyGate` tek, PAYLAŞILAN bir kapı, hangi sağlayıcının
> "gerçekten kazanan" olduğunu bilmiyor. Kapı, EN HIZLI biten sağlayıcı
> (burada zaten migrasyonlu PostgreSQL) tarafından açılıyor, ama guard filter'ın
> sorguladığı depo SQLite'a ait — tablo henüz yok. Aynı `A2AApprovalGuardFilter`/
> `McpApprovalGuardFilter` deseni (bkz. `MT-PG-025`) bunu `catch (Exception)` →
> `StopApplication()` ile karşılıyor.
>
> Bu, `MT-PG-025` ile AYNI temel tasarım kusurunun (guard filter'ların
> `SchemaReadyGate` açılmasını "sorguladığım tablo var" garantisi sanması)
> İKİNCİ, bağımsız bir tetikleyicisi. Çoklu sağlayıcı yanlış yapılandırması
> zaten "Kritik" işaretli bir negatif senaryo; gözlenen sonuç (Degraded yanıt
> yerine TAM çökme) doğrudan bu önem derecesini doğruluyor.
>
> ---
>
> **2026-08-14 yeniden koşum — Geçti.** İki düzeltme birlikte kapattı:
> 1. `2caa423` (S1-8) guard filter'ın `StopApplication` yolunu kaldırdı —
>    `MT-PG-025`'e bakın.
> 2. Asıl yarış bu koşumda düzeltildi: `MigrationHostedService.StartAsync`
>    **kaybeden** sağlayıcının dalında `_schemaReadyGate.MarkReady()`
>    çağırıyordu. Kapı paylaşılan TEK bir sinyaldir; kaybeden onu anında
>    açınca kazananın migration'ı daha bitmeden bekleyen arka plan servisleri
>    boş şemaya sorgu atıyordu. Kaybeden artık kapıyı **açmıyor**; kazanan her
>    yolda (`AutoApplyMigrations` kapalı olsa bile) `MarkReady` çağırdığı için
>    kapı asla açılmadan kalmaz.
>
> Ampirik (geçici `UseSqlite` satırı `UsePostgreSql`'den SONRA, PostgreSQL
> şeması zaten güncel):
> - Uyarı satırı birebir beklendiği gibi: `AgentPrism'de birden fazla kalicilik
>   saglayicisi kayitli: PostgreSQL, SQLite. Son kayit kazanir ve su an SQLite
>   kullaniliyor. Yalnizca birini cagirin.`
> - `registeredPersistenceProviders`: **2** · `persistenceProvider`: **SQLite**
>   (son çağrılan kazanır) · `/health`: **Degraded**, `HTTP 200`.
> - `no such table` hatası: **0** · `Application is shutting down`: **0**.
>
> Geçici kod değişikliği adım 4'e göre geri alındı (`git diff` boş).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-PG-050 — `/health` PostgreSQL erişilemezken Unhealthy döner

**Gerçek sonuç**
> **Kısmen Kaldı.** PostgreSQL'e özgü davranış TAM beklendiği gibi: container
> durdurulmuşken `/health` **503 Unhealthy** döndü; container yeniden
> başlayıp 3 saniye beklendikten sonra (uygulama YENİDEN BAŞLATILMADAN)
> `/health` **200**'e döndü — Npgsql havuzu kendiliğinden toparlandı, bu
> case'in asıl kanıtladığı şey budur.
>
> Ama gövde `Healthy` DEĞİL, `Degraded` gösterdi. Kök neden: `AgentPrismHealthCheck`
> (`src/AgentPrism.AspNetCore/Health/AgentPrismHealthCheck.cs:70-77`) `Healthy`
> için `report.ModelProviders.Any(status == Healthy)` şartını arıyor — ve
> `echo` sağlayıcısı (bu dosyanın tamamında ağa çıkmamak için kullanılan tek
> sağlayıcı) `ModelProviders` listesinde HİÇ YER ALMIYOR (yalnız
> openai/openai-responses/openrouter/anthropic/google izleniyor). Yani
> `echo`-only bir kurulumda `/health` YAPISAL OLARAK asla düz `Healthy`
> döndüremez — en iyi ihtimalle `Degraded` durur. Bu, PostgreSQL'in DEĞİL, bu
> dosyanın kendi sınır tablosunun "model sağlayıcı devre kesici durumu
> `25-SAGLIK-TESHIS-OPENAPI.md`'dedir, kapsam dışı" dediği bir alanın
> (§ "Bu dosya nerede biter") case metnine sızmasıdır — case'in beklenen
> sonucu kendi belirlediği sınırın dışına taşmış. PostgreSQL'e özgü kısım
> (`canConnect`, `migrationsUpToDate`) doğrulandı; `Healthy` etiketi doğrulanamadı.

---

**Doküman düzeltmesi (2026-08-15, KAPANIS-PLANI §8):** Gözlenen `Degraded`
sonucu, yukarıda düzeltilmiş beklentiyle **tam örtüşüyor** — ürün kusuru
yok. PostgreSQL'e özgü davranışın tamamı (503→200 geçişi, Npgsql havuzunun
kendiliğinden toparlanması) doğrulandı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-PG-051 — `/agentprism/api/diagnostics` bekleyen migration'ları listeler, hiçbir `secret` taşımaz

**Gerçek sonuç**
> _(2026-08-13 koşumu: Kaldı — `MT-PG-025` uygulamayı çökertiyordu, istek hiç
> gönderilemedi.)_
>
> **2026-08-14 yeniden koşum — Geçti.** İki ayrı düzeltme gerekti:
> 1. Çökme `2caa423` (S1-8) ile zaten kapanmıştı: `ExternalSurfaceGuard`
>    `ListCatalogWithRetryAsync` ile katalog sorgusunu üstel geri çekilmeyle
>    yeniden dener, uygulamayı durdurmaz. Ampirik: `shutdown` satırı **0**,
>    `Now listening` yazıldı, süreç ayakta.
> 2. Ama uç yine de **HTTP 500** dönüyordu — `AgentPrismDiagnosticsCollector`
>    `_agentCatalog.ListAsync`'i korumasız çağırıyor ve hata, YUKARIDA
>    toplanmış migration bilgisini de çöpe atıyordu. Teşhis ucunun birincil
>    kullanım anı tam da budur. Düzeltildi: katalog sorgusu artık `try/catch`
>    içinde, `AgentCount` `int?` oldu ve okunamayınca `null` gelir.
>
> Gözlenen gövde: `persistenceProvider: PostgreSQL`, `canConnect: true`,
> `migrationsUpToDate: false`, `pendingMigrations` **28 kalem**
> (`0001_initial` … `0028_experiment_canary`), `agentCount: null`,
> `toolCount: 6`. `Password=`/bağlantı dizesi/`sk-` taraması: **0 eşleşme**.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

> **Temizlik:** `dotnet user-secrets remove "AgentPrism:PostgreSql:AutoApplyMigrations"`, reset yordamı — uygulandı.

---

## MT-PG-053 — Migration tamamsa ve bir model sağlayıcı sağlıklıysa `/health` Healthy döner

**Gerçek sonuç**
> **200** döndü, gövde `Degraded` — düzeltilmiş beklentiyle **tam örtüşüyor**.
> Kod/veri kusuru yok; ürünün migration/bağlantı denetimi doğru çalışıyor.

---

**Doküman düzeltmesi (2026-08-15):** Beklenen sonuç yukarıda koda göre
düzeltildi. Ürün kusuru yok.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

# 6 — Sınır durumları: yük ve bağlantı kesintisi

---
