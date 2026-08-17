# 04 — Kalıcılık: SQLite, SQL Server ve Bellek İçi (`SQL`) — Koşum Kaydı (2026-08-13)

> **Bu dosya bir koşum kaydıdır, spesifikasyon değildir** (Faz 58.3 ayrımı).
> Spesifikasyon: [`../../04-KALICILIK-DIGER.md`](../../04-KALICILIK-DIGER.md) — `Ön koşul`, `Adımlar`,
> `Beklenen sonuç` oradadır ve yeniden koşulabilir.
>
> Aşağısı yalnız **2026-08-13** koşumunun `Gerçek sonuç` ve `Durum`
> kayıtlarıdır. İkinci bir koşum bu dosyayı **ezmez**; kardeş bir
> `kosumlar/<tarih>/` dizini açar.

---

## MT-SQL-001 — Boş bağlantı dizesiyle başlatma reddedilir

**Gerçek sonuç**
> **Test yöntemi bu senaryoyu tetikleyemiyor — kod kusuru DEĞİL.**
> `samples/AgentPrism.Api/Program.cs:632` `IsNullOrWhiteSpace(sqlite["ConnectionString"])`
> kontrolüyle boş bağlantı dizesini "sağlayıcı yapılandırılmamış" sayıp
> `UseSqlite(...)`'ı hiç çağırmıyor; uygulama bunun yerine sorunsuz açılıyor
> ve bellek içi izleğe düşüyor (`/health` → `Degraded`, HTTP 200, konsolda
> hata yok). `AgentPrismSqliteOptionsValidator.cs:21-27` doğrudan okunarak
> doğrulandı: validator gerçekten `"AgentPrismSqliteOptions.ConnectionString
> bos olamaz..."` mesajıyla reddediyor — ama yalnızca `UseSqlite("")` fiilen
> çağrılırsa. Bu örnek uygulamanın kasıtlı "boş = yapılandırılmamış" tasarımı
> yüzünden bu case'i bu harness üzerinden koşmanın yolu yok; doğrulamak için
> ya örnek dışında `UseSqlite("")` çağıran ayrı bir minimal host gerekir ya da
> case kod okumasıyla (izlek C) kapatılır. SQL Server karşılığı MT-SQL-010
> aynı sebeple etkilenir (bkz. o case).

**Durum:** ☐ Beklemede · ☐ Geçti · ☐ Kaldı · ☑ Atlandı (yöntem geçersiz — bkz. not)

> **Temizlik:** `dotnet user-secrets set "AgentPrism:Sqlite:ConnectionString" "Data Source=agentprism-manuel.db"`

---

## MT-SQL-002 — Geçersiz `TablePrefix` biçimleri ve enjeksiyon denemesi reddedilir

**Gerçek sonuç**
- Üç deneme de başlamayı reddetti. Hiçbirinde `Now listening` satırı yazılmadı
  (`grep -c` → 0); süreç `OptionsValidationException` ile sonlandı.
- Mesaj beklenen metni birebir taşıyor ve reddedilen değeri de yazıyor:
  `AgentPrismSqliteOptions.TablePrefix gecerli bir AgentPrism tablo oneki
  degil. Kucuk harf veya alt cizgi ile baslamali; kucuk harf, rakam ve alt
  cizgi icermeli; en cok 63 karakter olmalidir. Gelen deger: '<deger>'.`
- Doğrulama sorgusu: `agentprism_tenants` hâlâ `.tables` listesinde — 3.
  denemedeki `DROP TABLE` çalışmadı. Doğrulama `UseSqlite` içinde `IOptions<T>`
  ilk çözüldüğü anda olur (`AgentPrismSqliteBuilderExtensions.cs:80`), yani
  hiçbir SQL metni kurulmadan önce.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

> **Temizlik:** `dotnet user-secrets set "AgentPrism:Sqlite:TablePrefix" "agentprism_"`
>
> ⚠️ **Koşum notu:** Bu temizlik önceki oturumda **uygulanmamış**. Şerit
> açılırken paylaşılan `user-secrets` deposu `AgentPrism:Sqlite:TablePrefix` =
> `x_; DROP TABLE agentprism_tenants;--` taşıyordu ve uygulamanın açılmasını
> engelledi. KOSUM-PLANI §2.2 gereği depoya yazılmadı; şerit kendi ortam
> değişkenlerini açıkça sabitleyerek ilerledi. Bkz. `HATA-S1-001`.

---

## MT-SQL-003 — `CommandTimeoutSeconds` sınırları

**Gerçek sonuç**
- `-1` ve `3601` başlamayı reddetti; `Now listening` yazılmadı. Mesaj beklendiği
  gibi, reddedilen değeri de taşıyor: `AgentPrismSqliteOptions.CommandTimeoutSeconds
  0 ile 3600 arasinda olmalidir. Gelen deger: -1.` (aynısı `3601` için).
- `0` kabul edildi; uygulama açıldı ve `Now listening` yazdı.
- Sınırların kendisi (`0` ve `3600`) kabul tarafında — doğrulama kapsayıcı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

> **Temizlik:** `dotnet user-secrets set "AgentPrism:Sqlite:CommandTimeoutSeconds" "30"`

---

## MT-SQL-004 — `AutoApplyMigrations=false` migration uygulamaz, sorumluluk operatöre kalır

**Gerçek sonuç**
- Bilgi satırı beklendiği gibi çıktı: `AgentPrism migration'lari otomatik
  uygulanmiyor (AutoApplyMigrations kapali). Semanin guncel olmasi cagiranin
  sorumlulugundadir.`
- `.db` dosyası oluştu (4096 bayt) ama boş: `count(*)` **0**.
- **Uygulama açılmadı — kendini kapattı.** `Now listening` satırı yazıldı, hemen
  ardından `Application is shutting down...` geldi ve süreç sonlandı. `/health`
  isteği bağlantı kuramadı (`HTTP:000`), yani beklenen `Unhealthy` yanıtı
  **hiç okunamıyor**.
- Kapatmayı iki dış yüzey denetimi tetikledi:
  - `crit: AgentPrism.A2AApprovalGuardFilter — A2A disa acik yuzey denetimi
    basarisiz oldu; uygulama durduruluyor.` →
    `SqliteException: no such table: agentprism_agent_definitions`
    (`A2AApprovalGuardFilter.cs:58`)
  - `crit: AgentPrism.McpApprovalGuardFilter` — aynı hata
    (`McpApprovalGuardFilter.cs:86`)
- Kök neden: `MigrationHostedService.StartAsync` `AutoApplyMigrations` kapalıyken
  `SchemaReadyGate`'i **bilerek açar** ("semanin hazir olmasi tuketicinin
  sorumlulugundadir"). İki onay denetimi bu kapıyı bekler ve açılır açılmaz
  `catalog.ListAsync` ile `agent_definitions` tablosunu sorgular. Şema gerçekte
  hazır değilse sorgu patlar, `catch (Exception)` bloğu
  `lifetime.ApplicationStarted.Register(lifetime.StopApplication)` çağırır ve
  uygulama iner.
- **Ayırt edici kontrol:** Şema önce `AutoApplyMigrations=true` ile kurulup
  (45 tablo) sonra `false` ile açıldığında uygulama sorunsuz ayakta kaldı
  (`kapanma satiri: 0`, `/health` → `Degraded`). Yani kusur `AutoApplyMigrations`
  ayarında değil, **kapının hazır olmayan şema üzerinde açılmasında**.
- Sonuç: dokümante edilen "operatör migration'ı dışarıdan uygular" sözleşmesi
  yalnızca şema ZATEN hazırsa çalışır. İlk kurulumda ya da migration adımı
  atlandığında operatörün durumu görmesi için tasarlanmış
  `/health` → `Unhealthy` + bekleyen migration listesi sinyali erişilemez;
  yerine "no such table" ile kapanma döngüsü oluşur. Örnek uygulama
  `UseMcpServer()` ve `UseA2A()` çağırdığı için (`Program.cs:98-99`) iki denetim
  de etkindir.

 **Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı → `HATA-S1-002` (Yüksek) — S1-8'de düzeltmesiyle (K-393) yeniden koşuldu: ayrı sunucu + boş şema ile uygulama açık kaldı, /health→Unhealthy. Bkz. SONUCLAR-S1-2026-08-13.md.

> **Temizlik:** `dotnet user-secrets set "AgentPrism:Sqlite:AutoApplyMigrations" "true"`, reset yordamı.

---

## MT-SQL-005 — `Data Source=:memory:` desteklenir ama KALICI DEĞİLDİR

**Gerçek sonuç**
- **`Data Source=:memory:` ile uygulama hiç açılmıyor.** İddia edilenden daha
  ağır bir durum: veri "bağlantı kapanınca gitmiyor", uygulama başlatmayı
  tamamlayamıyor. Her iki açılış denemesinde de süreç sonlandı; `POST`/`GET`
  istekleri bağlantı kuramadı (`HTTP:000`).
- Log sırası:
  1. `AgentPrism 15 migration uyguladi. Sema: agentprism_.` — migration'lar
     gerçekten uygulandı.
  2. `fail: Microsoft.Extensions.Hosting.Internal.Host[11] — Hosting failed to start`
     `SqliteException: no such table: agentprism_tenants`
  3. `Unhandled exception` → süreç ölür.
- Kök neden: `SqliteDataSource.CreateDbConnection()` her çağrıda **yeni** bir
  `SqliteConnection` üretir (`Internal/SqliteDataSource.cs:44`). Çıplak
  `:memory:` veritabanı **bağlantıya özeldir**: `MigrationRunner` kendi
  bağlantısında 15 migration uygular, bağlantı kapanır, o veritabanı yok olur.
  Hemen ardından `EnsureDefaultTenantAsync` YENİ bir bağlantı açar — bu boş bir
  in-memory veritabanıdır ve `agentprism_tenants` orada yoktur
  (`MigrationHostedService.StartAsync`). Yani `:memory:`, bağlantı başına
  bağlantı açan bir `DbDataSource` ile yapısal olarak bağdaşmıyor.
- **Doğrulanan çalışan biçim:** `Data Source=file:apmem?mode=memory&cache=shared`
  ile uygulama sorunsuz açıldı (15 migration, `/health` → HTTP 200). Paylaşımlı
  önbellek aynı süreçteki tüm bağlantılara tek bir in-memory veritabanı verir.
- Bu bir **public API doküman kusurudur** ve tüketiciye IntelliSense'te
  gösterilir: `AgentPrismSqliteOptions.ConnectionString` XML dokümanı
  (`AgentPrismSqliteOptions.cs:17-18`) `Data Source=:memory: desteklenir ama
  kalici DEGILDIR: baglanti kapaninca veri gider` diyor. Gerçekte çıplak
  `:memory:` hiç desteklenmiyor. Doküman ya `cache=shared` biçimini önermeli ya
  da `:memory:` desteği kaldırılmalı.

 **Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı → `HATA-S1-003` (Yüksek) — S1-8'de düzeltmesiyle yeniden koşuldu: validator artik ciplak :memory:'yi acik bir OptionsValidationException ile reddediyor. Bkz. SONUCLAR-S1-2026-08-13.md.

> **Temizlik:** `dotnet user-secrets set "AgentPrism:Sqlite:ConnectionString" "Data Source=agentprism-manuel.db"`

---

## MT-SQL-006 — Yazılamayan bir dizinde başlatma reddedilir

**Gerçek sonuç**
- Uygulama başlamayı reddetti. `Now listening` satırı yazılmadı (`grep -c` → 0).
- Konsolda beklenen hata: `SqliteException (0x80004005): SQLite Error 14:
  'unable to open database file'.` — ardından `Hosting failed to start` ve
  `Unhandled exception` geldi, süreç sonlandı.
- Dosya sistemi izin hatası yutulmuyor; başlatmayı anlaşılır bir hatayla
  durduruyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

> **Temizlik:**
> ```bash
> chmod 755 /tmp/agentprism-salt-okunur && rm -rf /tmp/agentprism-salt-okunur
> dotnet user-secrets set "AgentPrism:Sqlite:ConnectionString" "Data Source=agentprism-manuel.db"
> ```

---

## MT-SQL-007 — Üç `UseSqlite` aşırı yüklemesi aynı sonucu üretir

**Gerçek sonuç**
- `AgentPrismSqliteBuilderExtensions.cs:20-26` — `UseSqlite(string)` gövdesi
  `builder.UseSqlite(options => options.ConnectionString = connectionString)`.
- `AgentPrismSqliteBuilderExtensions.cs:38-46` — `UseSqlite(IConfiguration)`
  gövdesi `builder.UseSqlite(options => Bind(configurationSection, options))`.
- `AgentPrismSqliteBuilderExtensions.cs:58` — `UseSqlite(Action<Options>)` tek
  gerçek kayıt yoludur; DI kaydını yalnız o yapar.
- Üç aşırı yükleme tek kayıt yolunda buluşuyor; sözleşme doğrulandı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

# 2 — SQL Server: Bağlantı, ayarlar ve doğrulama

Bu bölüm `AgentPrismSqlServerOptions`, `AgentPrismSqlServerOptionsValidator` ve
`SqlServerDataSourceFactory`'yi sınar. SQL Server, PostgreSQL'in `public` şema
yasağına birebir denk düşen bir `dbo` yasağı taşır (K-013).

> ⚠️ **Bu bölümdeki her case `mcr.microsoft.com/mssql/server` başarıyla
> başlamış `ap-mssql` container'ını gerektirir.** Apple Silicon'da Rosetta
> emülasyonu kapalıysa container başlamaz — önce **MT-SQL-042**'yi uygula ve
> orada kaydedilen ikame imajla devam et.

> ✅ **`HATA-S1-004` ÇÖZÜLDÜ (2026-08-13, Şerit 1).** Bu bölüm ve §3 önce
> koşulamadı: `samples/AgentPrism.Api/AgentPrism.Api.csproj:6`
> `<InvariantGlobalization>true</InvariantGlobalization>` taşıyordu ve
> `Microsoft.Data.SqlClient` bu modu desteklemediği için `SqlConnection.Open`
> anında `System.NotSupportedException` fırlıyordu — uygulama SQL Server ile hiç
> açılmıyordu. Aynı satır `AgentPrism.Starter` şablonundaydı; şablon
> `UseSqlServer` seçeneği sunduğu için kusur doğrudan tüketiciye gidiyordu.
> Ayar **karar K-392** ile hem örnekten hem şablondan kaldırıldı, dört doğrulama
> kapısı yeşil koştu ve bu bölümün tüm case'leri gerçek bağlantıyla yeniden
> koşuldu. Ayrıntı: `HATA-S1-004`.

---

## MT-SQL-010 — Boş bağlantı dizesiyle başlatma reddedilir

**Gerçek sonuç**
> **Test yöntemi bu senaryoyu tetikleyemiyor — kod kusuru DEĞİL.** MT-SQL-001'in
> birebir aynısı, SQL Server tarafında. `samples/AgentPrism.Api/Program.cs:635`
> `IsNullOrWhiteSpace(sqlServer["ConnectionString"])` kontrolüyle boş değeri
> "sağlayıcı yapılandırılmamış" sayıp `UseSqlServer(...)`'ı hiç çağırmıyor.
> Koşuldu: üç bağlantı dizesi de boşken uygulama sorunsuz açıldı
> (`Now listening` yazıldı), `/api/diagnostics` → `persistenceProvider`
> = `InMemory`, `registeredPersistenceProviders` = 0, `/health` → `Degraded`.
> Yani validator'a hiç ulaşılmıyor. Doğrulamak için örnek dışında
> `UseSqlServer("")` çağıran ayrı bir minimal host gerekir ya da case kod
> okumasıyla (izlek C) kapatılır.

**Durum:** ☐ Beklemede · ☐ Geçti · ☐ Kaldı · ☑ Atlandı (yöntem geçersiz — bkz. not)

> **Temizlik:** `dotnet user-secrets set "AgentPrism:SqlServer:ConnectionString" "Server=localhost,51433;Database=AgentPrism;User Id=sa;Password=AgentPrism!2026;TrustServerCertificate=true"`

---

## MT-SQL-011 — Şema adı `dbo` olamaz

**Gerçek sonuç**
- Uygulama başlamayı reddetti; `Now listening` yazılmadı.
- Mesaj beklenen iki ifadeyi de taşıyor:
  `AgentPrismSqlServerOptions.SchemaName 'dbo' olamaz. AgentPrism tuketicinin
  varsayilan semasina dokunmaz. Gerekce: docs/KARARLAR.md, karar K-013.`
- Doğrulama sorgusu: `dbo` şemasındaki tablo sayısı **0** — denemeden önceki
  değerle aynı, `dbo`'ya hiçbir tablo yazılmadı.
- Not: bu case bağlantı kurulmadan, ayar doğrulamasında sonuçlandığı için
  `HATA-S1-004` (Invariant Globalization) engelinden **etkilenmedi**.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

> **Temizlik:** `dotnet user-secrets set "AgentPrism:SqlServer:SchemaName" "agentprism"`

---

## MT-SQL-012 — Geçersiz şema adı biçimleri ve enjeksiyon denemesi reddedilir

**Gerçek sonuç**
- Üç deneme de başlamayı reddetti; hiçbirinde `Now listening` yazılmadı.
- Mesaj beklenen metni taşıyor ve reddedilen değeri de yazıyor:
  `AgentPrismSqlServerOptions.SchemaName gecerli bir AgentPrism sema adi degil.
  Kucuk harf veya alt cizgi ile baslamali; kucuk harf, rakam ve alt cizgi
  icermeli; en cok 63 karakter olmalidir. Gelen deger: '<deger>'.`
- 3. denemeden sonra `SELECT COUNT(*) FROM sys.tables` normal yanıt verdi —
  `sys.tables` düşürülmedi, hiçbir DDL çalışmadı.
- Doğrulama, PostgreSQL ile aynı paylaşılan `SqlIdentifier` kuralını
  uyguluyor: SQL Server'ın kendi daha geniş tanımlayıcı kuralı değil.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

> **Temizlik:** `dotnet user-secrets set "AgentPrism:SqlServer:SchemaName" "agentprism"`

---

## MT-SQL-013 — `CommandTimeoutSeconds` sınırları

**Gerçek sonuç**
- `-1` ve `3601` başlamayı reddetti; `Now listening` yazılmadı. Mesaj:
  `AgentPrismSqlServerOptions.CommandTimeoutSeconds 0 ile 3600 arasinda
  olmalidir. Gelen deger: -1.` (aynısı `3601` için).
- SQLite karşılığı MT-SQL-003 ile birebir aynı sınır ve aynı mesaj kalıbı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

> **Temizlik:** `dotnet user-secrets set "AgentPrism:SqlServer:CommandTimeoutSeconds" "30"`

---

## MT-SQL-014 — `AutoApplyMigrations=false` migration uygulamaz

**Gerçek sonuç**
- Bilgi satırı çıktı: `AgentPrism migration'lari otomatik uygulanmiyor
  (AutoApplyMigrations kapali). Semanin guncel olmasi cagiranin
  sorumlulugundadir.`
- `agentprism` şemasındaki tablo sayısı **0** — şema bile oluşmadı.
- **Uygulama açılmadı — kendini kapattı.** `Now listening` yazıldı, hemen
  ardından `Application is shutting down...` geldi. `/health` bağlantı kuramadı
  (`HTTP:000`), yani beklenen `Unhealthy` yanıtı **okunamıyor**.
- `crit: AgentPrism.A2AApprovalGuardFilter — A2A disa acik yuzey denetimi
  basarisiz oldu; uygulama durduruluyor.` →
  `SqlException: Invalid object name 'agentprism.agent_definitions'.`
  (öncesinde `Invalid object name 'agentprism.tenants'` uyarısı).
- **SQLite karşılığı MT-SQL-004 ile birebir aynı davranış.** İki sağlayıcıda da
  aynı kök neden: `MigrationHostedService.StartAsync` `AutoApplyMigrations`
  kapalıyken `SchemaReadyGate`'i açıyor, onay denetimleri hazır olmayan şemayı
  sorguluyor ve uygulama iniyor. Kök neden `AgentPrism.Sql.Shared` içinde
  ortak olduğu için sağlayıcıdan bağımsız — bu koşum onu **doğruladı**.

 **Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı → `HATA-S1-002` (Yüksek) — S1-8'de düzeltmesiyle (K-393) yeniden koşuldu, SQL Server'da da aynı sonuç. Bkz. SONUCLAR-S1-2026-08-13.md.

> **Temizlik:** `dotnet user-secrets set "AgentPrism:SqlServer:AutoApplyMigrations" "true"`, reset yordamı.

---

## MT-SQL-015 — Yanlış host ile başlatma migration adımında çöker (fail-fast)

**Gerçek sonuç**
- `dotnet run` süreci sonlandı; `Now listening` hiç yazılmadı.
- Beklenen istisna geldi: `Microsoft.Data.SqlClient.SqlException (0x80131904):
  A network-related or instance-specific error occurred while establishing a
  connection to SQL Server. The server was not found or was not accessible…
  (provider: TCP Provider, error: 35 …)`, ardından `Hosting failed to start`.
- `/health` isteği bağlantı kuramadı (`HTTP:000`).
- Fail-fast sözleşmesi korunuyor: şema hazır değilken uygulama ayakta kalmıyor.
  `MT-PG-007`'nin SQL Server karşılığı olarak aynı davranış gözlendi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

> **Temizlik:** `dotnet user-secrets set "AgentPrism:SqlServer:ConnectionString" "Server=localhost,51433;Database=AgentPrism;User Id=sa;Password=AgentPrism!2026;TrustServerCertificate=true"`

---

## MT-SQL-016 — Üç `UseSqlServer` aşırı yüklemesi aynı sonucu üretir

**Gerçek sonuç**
- `AgentPrismSqlServerBuilderExtensions.cs:21-27` — `UseSqlServer(string)`
  gövdesi `builder.UseSqlServer(options => options.ConnectionString = connectionString)`.
- `AgentPrismSqlServerBuilderExtensions.cs:39-46` — `UseSqlServer(IConfiguration)`
  gövdesi `builder.UseSqlServer(options => Bind(configurationSection, options))`.
- `AgentPrismSqlServerBuilderExtensions.cs:68` — `UseSqlServer(Action<Options>)`
  tek gerçek kayıt yoludur.
- MT-SQL-007 ile birebir aynı desen; iki sağlayıcı da tek kayıt yolunda
  buluşuyor. Kod okuması olduğu için `HATA-S1-004`'ten etkilenmedi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

# 3 — Migration'lar

`MigrationRunner` her üç sağlayıcıda ORTAKTIR (`AgentPrism.Sql.Shared`);
kilit biçimi (`SqliteDialect`/`SqlServerDialect`) ve migration kaynak öneki
diyalekt üzerinden değişir. **Ölçüldü:** SQLite ve SQL Server her ikisi de
**15** migration dosyası taşır (`0001_initial`'dan `0015_experiment_canary`'e);
PostgreSQL'in 28 dosyası aynı özelliklerin DAHA GRANÜLER migration'lara
bölünmüş halidir — nihai şema PostgreSQL'de **45** tablo (`document_embeddings`
dâhil, yalnız `pgvector`), SQLite ve SQL Server'da **44** tablodur. Üçü de aynı
özellik setini taşır; SQLite/SQL Server'da eksik bir yetenek YOKTUR (bkz.
**MT-SQL-060**).

---

## MT-SQL-020 — SQLite: boş DB'de 15 migration sırayla uygulanır

**Gerçek sonuç**
- Konsol tam olarak `AgentPrism 15 migration uyguladi. Sema: agentprism_.` yazdı.
- `agentprism___migrations` içinde **15** satır, sırayla `0001_initial` → `0015_experiment_canary`.
- `sqlite_master` tablo sayısı **45**, doküman iddiası olan 44 ile ÇELİŞİYOR.
  Kök sebep kod kusuru değil, dokümanın kendi sorgusuyla tutarsız beklentisi:
  `SELECT count(*) FROM sqlite_master WHERE type='table'` `agentprism___migrations`
  defter tablosunu da sayar (44 özellik tablosu + 1 migration defteri = 45).
  `/api/diagnostics`: `persistenceProvider`="SQLite", `registeredPersistenceProviders`=1,
  `canConnect`=true, `migrationsUpToDate`=true, `pendingMigrations`=[] — hepsi doğru.
  **Doküman düzeltmesi önerilir:** bu case ve MT-SQL-024/060'taki "44" beklentisi
  "45 (44 özellik tablosu + 1 migration defteri)" olarak güncellenmeli.

**Durum:** ☐ Beklemede · ☑ Geçti (doküman sayı düzeltmesiyle) · ☐ Kaldı · ☐ Atlandı

---

## MT-SQL-021 — SQLite: yeniden başlatma migration'ları tekrar uygulamaz (idempotent)

**Gerçek sonuç**
- Yeniden başlatmada konsolda `AgentPrism.MigrationRunner` satırı hiç görünmedi.
- `/api/diagnostics` `migrationsUpToDate`=true, `pendingMigrations`=[] doğruladı.
- `agentprism___migrations` hâlâ **15** satır taşıyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-SQL-022 — SQLite: uygulanmış migration'ın checksum'ı bozulursa başlama reddedilir

**Gerçek sonuç**
- Uygulama başlamayı reddetti; `Now listening` yazılmadı.
- Mesaj beklenen metni taşıyor, üstelik iki özeti de karşılaştırmalı veriyor:
  `AgentPrismException: '0001_initial' migration'i veritabaninda uygulanmis
  ancak dosyanin icerigi degismis. Veritabanindaki ozet: bozuk, dosyanin ozeti:
  13CEA2CB…149A6. Uygulanmis bir migration duzenlenmez; degisiklik icin yeni bir
  migration dosyasi ekleyin.`
- Mesaj ne yapılacağını da söylüyor (yeni migration ekle) — teşhis için
  yeterli.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

> **Temizlik:** Reset yordamı (checksum elle düzeltilemez, dosya baştan kurulur).

---

## MT-SQL-023 — SQLite: iki eşzamanlı örnek dosya kilidiyle çakışmadan migration uygular

**Gerçek sonuç**
- İki süreç eş zamanlı başlatıldı (portlar 5081 ve 5091). Yalnız **ikincisi**
  `AgentPrism 15 migration uyguladi. Sema: agentprism_.` yazdı; birincinin
  logunda migration satırı hiç görünmedi (0 migration uyguladı).
- İkisi de sağlıklı açıldı — her iki logda da `Now listening` var.
- Hiçbir terminalde checksum hatası, `AgentPrismException` veya
  `Unhandled` yok (`grep -ci` → 0 / 0).
- `agentprism___migrations` **15** satır — 30 değil.
- Kilit sidecar dosyası gerçekten oluştu:
  `agentprism-manuel.db.agentprism_.agentprism-migration-lock` (0 bayt).
  Adın hem veritabanı dosyasını hem tablo önekini taşıması, aynı `.db`
  dosyasında farklı `TablePrefix` kullanan kurulumların birbirini
  kilitlememesini sağlıyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

> **Temizlik:** İkinci terminali durdur (`Ctrl+C`).

---

## MT-SQL-024 — SQL Server: boş DB'de 15 migration sırayla uygulanır

**Gerçek sonuç**
- Konsol `AgentPrism 15 migration uyguladi. Sema: agentprism.` yazdı.
- `agentprism.__migrations` **15** satır; sıra `0001_initial` → `0015_experiment_canary`.
- `sys.tables` içinde `agentprism` şemasına ait **45** tablo — doküman iddiası
  olan 44 ile ÇELİŞİYOR. Kök sebep kod kusuru değil, dokümanın kendi
  doğrulama sorgusuyla tutarsız beklentisi: sorgu `__migrations` defter
  tablosunu da sayar (44 özellik tablosu + 1 defter = 45). SQLite tarafında
  `MT-SQL-020` aynı sapmayı kaydetti — iki sağlayıcı da tutarlı.
- **Doküman düzeltmesi önerilir:** bu case ve `MT-SQL-020`/`060`'taki "44"
  beklentisi "45 (44 özellik tablosu + 1 migration defteri)" olmalı.

**Durum:** ☐ Beklemede · ☑ Geçti (doküman sayı düzeltmesiyle) · ☐ Kaldı · ☐ Atlandı

---

## MT-SQL-025 — SQL Server: yeniden başlatma migration'ları tekrar uygulamaz (idempotent)

**Gerçek sonuç**
- Yeniden başlatmada konsolda `MigrationRunner` satırı hiç görünmedi
  (`grep -c` → 0); 0 migration uygulandı.
- `agentprism.__migrations` hâlâ **15** satır.
- `/api/diagnostics`: `persistenceProvider` = `SQL Server`,
  `migrationsUpToDate` = `True`, `pendingMigrations` = [].

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-SQL-026 — SQL Server: uygulanmış migration'ın checksum'ı bozulursa başlama reddedilir

**Gerçek sonuç**
- Uygulama başlamayı reddetti; `Now listening` yazılmadı.
- Mesaj beklenen metni taşıyor ve iki özeti karşılaştırmalı veriyor:
  `'0001_initial' migration'i veritabaninda uygulanmis ancak dosyanin icerigi
  degismis. Veritabanindaki ozet: bozuk, dosyanin ozeti: ABA3542C…F271.
  Uygulanmis bir migration duzenlenmez; degisiklik icin yeni bir migration
  dosyasi ekleyin.`
- SQLite karşılığı `MT-SQL-022` ile aynı davranış ve aynı mesaj kalıbı;
  özet değeri sağlayıcıya göre farklı (ayrı migration dosya kümesi).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

> **Temizlik:** Reset yordamı.

---

## MT-SQL-027 — SQL Server: iki eşzamanlı örnek `sp_getapplock` ile çakışmadan migration uygular

**Gerçek sonuç**
- İki süreç eş zamanlı başlatıldı (portlar 5081 ve 5091). Yalnız **birincisi**
  `AgentPrism 15 migration uyguladi. Sema: agentprism.` yazdı; ikincinin
  logunda migration satırı yok (0 migration uyguladı).
- İkisi de sağlıklı açıldı — her iki logda da `Now listening` var.
- `sp_getapplock donus degeri` hatası, `AgentPrismException` veya `Unhandled`
  hiçbir terminalde yok (`grep -ci` → 0 / 0). 30 saniyelik kilit zaman aşımı
  bu migration seti için yeterli.
- `agentprism.__migrations` tam olarak **15** satır — 30 değil.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

> **Temizlik:** İkinci terminali durdur (`Ctrl+C`).

---

# 4 — SQLite'a özgü davranışlar

---

## MT-SQL-030 — WAL modu dosyaları oluşur, `busy_timeout` eşzamanlı yazmayı bekletir

**Gerçek sonuç**
- `agentprism-manuel.db-wal` ve `agentprism-manuel.db-shm` dosyalarının ikisi de
  var — WAL modu etkin.
- 20 eş zamanlı `POST` isteğinin **tümü 201** döndü.
- `agentprism_agent_definitions` içinde `manuel-sqlite-esz-%` desenine uyan
  **20** satır — hiçbir yazma kaybolmadı.
- Uygulama logunda `SQLITE_BUSY` veya `database is locked` **hiç yok**
  (`grep -ci` → 0). `busy_timeout=5000` çakışmaları hata yerine beklemeyle
  çözüyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-SQL-031 — Yabancı anahtar zorlaması açıktır: agent silindiğinde sürüm satırları CASCADE ile gider

**Gerçek sonuç**
- Kayıt + güncelleme sonrası sürüm sayısı **2** (agent id
  `019FF7E6-BBB4-79ED-842D-93BBA398988F`).
- `DELETE` isteği **204** döndü.
- Silme sonrası sürüm sayısı **0** — `ON DELETE CASCADE` gerçekten çalıştı,
  yetim satır kalmadı.
- Not: `sqlite3` komut satırından `PRAGMA foreign_keys;` **0** döner. Bu
  beklenen bir durumdur ve çelişki değildir — pragma **bağlantı başınadır**,
  uygulamanın kendi bağlantılarında `SqliteDataSource.OnStateChange` ile
  `PRAGMA foreign_keys = ON` çalıştırılır; CLI kendi ayrı bağlantısını açar.
  CASCADE'in gerçekten çalışması bunun kanıtı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-SQL-032 — `TablePrefix` değiştirildiğinde aynı dosyada bağımsız bir tablo seti oluşur

**Gerçek sonuç**
- Uygulama açıldı ve `AgentPrism 15 migration uyguladi. Sema: ikinci_.` yazdı —
  öneke göre AYRI bir migration defteri kuruldu.
- İki sorgu da **1** döndü: `agentprism_tenants` ve `ikinci_tenants` aynı `.db`
  dosyasında yan yana duruyor.
- İki ayrı defter, ikisi de dolu: `agentprism___migrations` 15 satır,
  `ikinci___migrations` 15 satır.
- Toplam tablo sayısı **90** = 2 × 45 (44 özellik tablosu + 1 defter). İndeks ve
  tetikleyici adları da öneki taşıdığı için (K-193) veritabanı genelindeki ad
  tekliği kuralı ihlal edilmedi.
- Kilit dosyaları da önek başına ayrı:
  `agentprism-manuel.db.agentprism_.agentprism-migration-lock` ve
  `agentprism-manuel.db.ikinci_.agentprism-migration-lock` (K-389).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

> **Temizlik:** `dotnet user-secrets set "AgentPrism:Sqlite:TablePrefix" "agentprism_"`, reset yordamı.

---

# 5 — SQL Server'a özgü davranışlar

---

## MT-SQL-040 — Maliyet ondalık hassasiyeti kesilmeden geri döner

**Gerçek sonuç**
- **İmaj:** gerçek `mcr.microsoft.com/mssql/server:2022-latest` (bkz. MT-SQL-042).
- İlk deneme fiyat üretmedi: `cost` = `{inputCost: null, outputCost: null,
  source: "Unknown"}`. Çalıştırma başarılıydı (`status`=1, 211 girdi / 13 çıktı
  token) — sorun model kataloğunun `gpt-5.4-mini` için fiyat taşımaması.
  `RunPricingResolver.Resolve` bu durumda `PricingSource.Unknown` döndürür
  (`RunPricingResolver.cs:70`). Case'in kendi beklentisi bunu zaten koşullu
  yazıyor ("`RunPricingResolver` modeli tanıyorsa"), ama fiyat olmadan
  `decimal` hassasiyeti ÖLÇÜLEMEZ.
- Bu yüzden fiyat **yapılandırmadan** verildi (kod değişikliği değil, komut
  satırı yapılandırması) ve case'in asıl amacı ölçüldü:
  `--AgentPrism:Pricing:openai:gpt-5.4-mini:Input=12345.6789`
  `--AgentPrism:Pricing:openai:gpt-5.4-mini:Output=98765.4321`
  ⚠️ Doğru anahtar yolu `AgentPrism:Pricing:<saglayici>:<model>:Input`'tur —
  `Providers` ara anahtarı **yoktur** (sınıf üyesi `Providers` olsa da
  `BindPricing` doğrudan `Pricing`'in çocuklarını dolaşır) ve alan adı
  `InputCostPerMillionTokens` değil **`Input`**'tur
  (`AgentPrismServiceCollectionExtensions.cs:807-861`).
- Sonuç — 211 girdi / 13 çıktı token ile:

  | | API yanıtı | SQL'den okunan | Beklenen (elle hesap) |
  |---|---|---|---|
  | `inputCost` | `2.6049382479` | `2.6049382479` | `2.6049382479` |
  | `outputCost` | `1.2839506173` | `1.2839506173` | `1.2839506173` |

- Üçü de **birebir eşleşiyor**; hiçbir basamak yuvarlanmadı. `source` =
  `Configuration`, `currency` = `USD`.
- Sütun tipi doğrudan katalogdan doğrulandı: `input_cost` ve `output_cost`
  **`decimal(20,10)`** — `SqlServerDialect.AddDecimal`'in `Precision=20,
  Scale=10` yazması etkili. Tipi verilmemiş bir parametrenin düşeceği
  `decimal(18,0)` durumunda iki değer de `3` ve `1`'e yuvarlanırdı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-SQL-041 — `SchemaName` değiştirildiğinde aynı veritabanında bağımsız bir tablo seti oluşur

**Gerçek sonuç**
- **İmaj:** gerçek `mcr.microsoft.com/mssql/server:2022-latest`.
- Uygulama açıldı ve `AgentPrism 15 migration uyguladi. Sema: ikinci.` yazdı.
- İki sorgu da **45** döndü (doküman 44 diyor — `MT-SQL-024` ile aynı sapma:
  doğrulama sorgusu `__migrations` defterini de sayar). `agentprism` ve `ikinci`
  şemaları aynı veritabanında bağımsız, tam tablo setleri taşıyor.
- İki ayrı defter, ikisi de dolu: `agentprism.__migrations` 15 satır,
  `ikinci.__migrations` 15 satır.
- SQLite'ın önek deseninden farkı doğrulandı: burada `ikinci` gerçek bir SQL
  Server şemasıdır (`SCHEMA_ID('ikinci')` çözülüyor), tek ad alanını paylaşan
  bir önek değil.
- Not: ilk koşumda `agentprism` şeması 0 tablo gösterdi — önceki case'in
  veritabanı reset'i yüzünden ön koşul (MT-SQL-024) bozulmuştu. Varsayılan şema
  yeniden kurulup case tekrar koşuldu.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

> **Temizlik:** `dotnet user-secrets set "AgentPrism:SqlServer:SchemaName" "agentprism"`, reset yordamı.

---

## MT-SQL-042 — `mcr.microsoft.com/mssql/server` bu makinede başlamazsa `azure-sql-edge` ikamesi kaydedilir

**Gerçek sonuç**
- **Gerçek `mcr.microsoft.com/mssql/server:2022-latest` çalışıyor — ikameye
  gerek yok.** K-386'nın öngördüğü sonuç doğrulandı.
- `docker inspect ap-mssql`: imaj `mcr.microsoft.com/mssql/server:2022-latest`,
  `platform=linux`, durum `running`.
- Container içi mimari: `uname -m` → **`x86_64`** (Rosetta emülasyonu çalışıyor,
  `exit 133` yok).
- Motor sürümü: `Microsoft SQL Server 2022 (RTM-CU26) (KB5093420) -
  16.0.4265.3 (X64)`.
- Bu dosyanın SQL Server bölümündeki **her case gerçek `mssql/server` ile
  koşuldu**; `azure-sql-edge` ikamesi hiç kullanılmadı. Dolayısıyla §2, §3 ve §5
  sonuçları gerçek motoru kanıtlar.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

# 6 — Bellek içi izlek (Tasarım Kuralı #1)

Bu bölüm, `PROMPT.md` §3'ün "sıfır sürpriz" kuralını doğrulayan **tek**
bölümdür: hiçbir `Use*()` çağrılmadığında AgentPrism bellek içi depolarla
çalışmaya devam eder. Küçük görünür, atlanmaz.

---

## MT-SQL-050 — Hiçbir `Use*()` çağrılmadığında uygulama sorunsuz açılır

**Gerçek sonuç**
- Uygulama normal açıldı. `OptionsValidationException` **hiç yok**
  (`grep -ci` → 0) — hiçbir validator tetiklenmedi, çünkü hiçbir sağlayıcı
  kayıtlı değil.
- `/api/diagnostics` beklenen dört alanı da doğru verdi:
  `persistenceProvider` = `InMemory`, `registeredPersistenceProviders` = **0**,
  `migrationsUpToDate` = `True`, `pendingMigrations` = `[]`.
- ⚠️ **`/health` açılışta `Healthy` DEĞİL, `Degraded` döner** — gerekçe
  `Henuz saglikli oldugu dogrulanmis bir model saglayicisi yok.` Bu bir kusur
  değil, ama case'in eksik yazdığı bir ön koşul var: sağlayıcı durumu
  `AgentPrismDiagnosticsCollector.cs:97` içinde `_healthCache.TryPeek(...)` ile
  okunur ve bu önbellek **yalnız `GET /api/models/health` çağrılınca dolar**.
  Başarılı bir gerçek agent çalıştırması onu doldurmaz — bir OpenAI çağrısı
  yapıldıktan sonra bile beş sağlayıcının beşi de `Unknown` kaldı ve `/health`
  `Degraded` döndü.
- Ön koşul uygulandığında beklenen sonuç **doğrulandı**: `GET /api/models/health`
  çağrıldıktan hemen sonra `/health` → **`Healthy`**.
- **Doküman düzeltmesi önerilir:** case'in adımlarına "3.5. `GET
  /api/models/health` çağır (sağlayıcı sağlık önbelleğini ısıt)" eklenmeli.
  Bu aynı zamanda işletme bilgisidir: yeni açılmış bir örnek, hazır olduğu
  hâlde readiness probe'a `Degraded` bildirir.

**Durum:** ☐ Beklemede · ☑ Geçti (doküman ön koşul eklemesiyle) · ☐ Kaldı · ☐ Atlandı

---

## MT-SQL-051 — Bellek içi izlekte de tüm temel CRUD uçları çalışır

**Gerçek sonuç**
- Üç istek de 2xx döndü: `POST` **201**, `GET` **200**, `DELETE` **204**.
- Kalıcılık katmanı olmadan bellek içi depolar tam işlevsel.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-SQL-052 — Bellek içi izlekte yeniden başlatma TÜM veriyi kaybeder

**Gerçek sonuç**
- İlk açılışta `GET /api/agents/manuel-gecici` → **200**.
- Uygulama durdurulup yeniden başlatıldıktan sonra aynı sorgu → **404**.
- Veri süreçle birlikte bitti; bellek içi izleğin tanımı doğrulandı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-SQL-053 — Bellek içi izlekte konuşma dallandırma ucu 501 döner

**Gerçek sonuç**
- HTTP **501** döndü, `ProblemDetails` gövdesiyle:
  `title` = `Dallandirma desteklenmiyor`, `status` = 501, `detail` =
  `Konusma dallandirma yalnizca kalici bir SQL saglayicisi acikken calisir.
  Bellek ici kurulumda sohbet gecmisi oturum durumunun opak blogunda yasar ve
  belirli bir noktaya kadar kopyalanamaz; sessizce tamamini kopyalamak istenen
  dali uretmezdi.`
- Var olmayan bir oturum kimliği (`herhangi-bir-oturum`) kullanıldı ve yine de
  501 geldi — depo denetimi oturum varlığından ÖNCE yapılıyor, case'in
  öngördüğü sıra doğrulandı.
- Mesaj yalnız "desteklenmiyor" demiyor, NEDEN desteklenmediğini de anlatıyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

# 7 — Sağlayıcılar arası karşılaştırma ve taşınabilirlik

---

## MT-SQL-060 — Üç sağlayıcının migration/tablo sayısı ölçümü tutarlıdır

**Gerçek sonuç**
Üçü de temiz kurulumdan ölçüldü (SQLite sıfırdan, SQL Server `agentprism`
şeması, PostgreSQL şerit şeması `mt_s1`):

| Sağlayıcı | Migration | Tablo |
|---|---|---|
| SQLite | 15 | **45** |
| SQL Server | 15 | **45** |
| PostgreSQL | 28 | **46** |

- **Fark tam olarak 1** ve doküman iddiasını birebir doğruluyor: tablo adı
  kümelerinin farkı `diff` ile alındı ve tek satır çıktı —
  `> document_embeddings`. Başka hiçbir tablo bir sağlayıcıda olup diğerinde
  eksik değil.
- **SQLite ile SQL Server tablo kümeleri BİREBİR AYNI** — `diff` boş döndü
  (SQLite adları `agentprism_` öneki soyularak karşılaştırıldı). Granülerlik
  farkı yalnız migration dosya sayısındadır (15'e karşı 28), nihai şemada
  değil; SQLite/SQL Server'da eksik bir yetenek YOK.
- ⚠️ Mutlak sayılar doküman iddiasından (**44/44/45**) 1 fazla: doğrulama
  sorguları `__migrations` defter tablosunu da sayar. `MT-SQL-020`,
  `MT-SQL-024` ve `MT-SQL-041` aynı sapmayı kaydetti. **İlişkisel iddia
  (fark = 1, yalnız `document_embeddings`) tamamen doğrulandı**; yalnız mutlak
  sayılar "45/45/46 (44 özellik tablosu + 1 migration defteri)" olarak
  güncellenmelidir.

**Durum:** ☐ Beklemede · ☑ Geçti (doküman sayı düzeltmesiyle) · ☐ Kaldı · ☐ Atlandı

---

## MT-SQL-061 — Şema/önek adı doğrulama kuralı üç sağlayıcıda da birebir aynıdır

**Gerçek sonuç**
Üç validator da AYNI statik metodu çağırıyor:

- `src/AgentPrism.Sqlite/AgentPrismSqliteOptionsValidator.cs:30` —
  `SqlIdentifier.IsValidUnquoted(options.TablePrefix)`
- `src/AgentPrism.SqlServer/AgentPrismSqlServerOptionsValidator.cs:30` —
  `SqlIdentifier.IsValidUnquoted(options.SchemaName)`
- `src/AgentPrism.PostgreSql/AgentPrismPostgreSqlOptionsValidator.cs:31` —
  `SqlIdentifier.IsValidUnquoted(options.SchemaName)`

Tek tanım yeri `src/AgentPrism.Sql.Shared/Internal/SqlIdentifier.cs:38`;
uzunluk sınırı orada `private const int MaxLength = 63` olarak sabit ve
yorumu gerekçeyi de yazıyor: "PostgreSQL siniri `NAMEDATALEN - 1` = 63'tur;
SQL Server 128'e izin verir" — yani en katı kural üç sağlayıcıya da
taşınabilirlik için uygulanıyor.

Koşum kanıtı da tutarlı: `MT-SQL-002` (SQLite `TablePrefix`) ve `MT-SQL-012`
(SQL Server `SchemaName`) **birebir aynı** hata metnini üretti — yalnız
sınıf/alan adı farklıydı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-SQL-062 — `/api/diagnostics` her sağlayıcıda doğru `persistenceProvider` adını bildirir

**Gerçek sonuç**
Dört kurulum sırayla koşuldu; `persistenceProvider` her birinde doğru:

| Kurulum | `persistenceProvider` | `registeredPersistenceProviders` |
|---|---|---|
| Hiçbir `Use*()` yok | `InMemory` | 0 |
| `UseSqlite()` | `SQLite` | 1 |
| `UsePostgreSql()` | `PostgreSQL` | 1 |
| `UseSqlServer()` | `SQL Server` | 1 |

Adlar insan tarafından okunabilir biçimde bildiriliyor (`SQL Server` boşluklu,
`PostgreSQL` kendi büyük/küçük harf düzeniyle). Case'in istediği iki kurulumun
ötesinde dördü de doğrulandı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-SQL-063 — `secret` hiçbir zaman veritabanına veya dosyaya yazılmaz

**Gerçek sonuç**
- `grep -rn "Password=AgentPrism\|Password=agentprism" samples/AgentPrism.Api/appsettings*.json`
  → **0 satır**. Dosyalar yalnız boş placeholder (`"ConnectionString": ""`,
  `"ApiKey": ""`, `"AuthToken": ""`) ve değeri nereye yazacağını anlatan `//`
  yorumları taşıyor.
- **Ön koşul kuruldu:** tarama boş tablo üzerinde anlamsız olacağı için önce
  SQL Server'da 3 agent oluşturuldu, 3'ü güncellendi, 1'i silindi →
  `audit_log` **7** satır, `agent_definitions` **2** satır.
- Dolu tablolar üzerinde tarama — dördü de **0**:

  | Aranan | Sonuç |
  |---|---|
  | `%Password=%` | 0 |
  | `%sk-%` (API anahtarı öneki) | 0 |
  | `%AgentPrism!2026%` (gerçek SQL Server parolası) | 0 |
  | `%manuel-test-token%` (gerçek `AuthToken`) | 0 |
- SQLite tarafında da `agentprism_audit_log` sorgusu **0** döndü.
- Case'in istediği iki desenin ötesinde, koşumda GERÇEKTEN kullanılan parola ve
  token değerleri de arandı — hiçbiri veritabanına sızmamış.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

# 8 — Yük ve sınır durumları

---

## MT-SQL-070 — SQLite: 20 eşzamanlı agent kaydı veri bozulmadan tamamlanır

**Gerçek sonuç**
- 20 eş zamanlı `POST` isteğinin **tümü 201** döndü.
- `agentprism_agent_definitions` içinde `yuk-sqlite-%` deseni **20** satır;
  `COUNT(DISTINCT name)` de **20** — hiçbir kayıt kaybolmadı, hiçbiri
  çiftlenmedi.
- Logda `SQLITE_BUSY`, `database is locked` veya `Unhandled` **yok**
  (`grep -ci` → 0).
- Uygulama yük sonrası ayakta: `/health` → HTTP 200.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-SQL-071 — SQLite: dosya koşum sırasında salt-okunur yapılırsa yazma istekleri anlaşılır hatayla başarısız olur, uygulama çökmez

**Gerçek sonuç**
> **Test yöntemi bu senaryoyu tetikleyemiyor — kod kusuru DEĞİL.**
> `chmod` çalışan bir sürecin ZATEN AÇIK dosya tanıtıcılarını etkilemez; Unix
> izni `open()` anında denetler, her yazmada değil.

Artan sıkılıkta dört deneme yapıldı, **dördünde de yazma 201 döndü**:

| Deneme | Sonuç |
|---|---|
| A) yalnız `.db` → `chmod 444` | POST **201** |
| B) `.db` + `-wal` + `-shm` → `chmod 444` | POST **201** |
| C) üstüne dizin → `chmod 555` | POST **201** |
| D) izinler geri verildi | POST **201** |

**Ayırt edici kanıt** — dosya gerçekten salt-okunurdu, uygulama açık fd'ler
üzerinden yazıyordu:

- Aynı anda YENİ bir süreçten (`sqlite3` CLI, yeni `open()`) yazma denendi:
  `Error: stepping, attempt to write a readonly database (8)`. Yani izinler
  gerçekten uygulanmıştı.
- `lsof` uygulamanın süreç kimliğinde `.db`, `-wal` ve `-shm` üzerinde **`u`
  (okuma/yazma) kipinde açık fd'ler** gösterdi (`312u`, `313u`, `314u`,
  `317u`, `320u`). `Microsoft.Data.Sqlite` bağlantı havuzu bu tanıtıcıları
  açık tutar.
- Uygulama hiçbir aşamada çökmedi (`Unhandled`/`shutting down` → 0),
  `/health` boyunca HTTP 200 döndü.

**Doğru yöntem ne olurdu:** dosya sistemini gerçekten salt-okunur bağlamak
(`mount -ur`) ya da bir dosya sistemi kotası/hata enjeksiyonu kullanmak —
`chmod` yetmez. Başlangıç anındaki izin hatası zaten **MT-SQL-006** ile
kapsanıyor ve orada beklendiği gibi başlatma reddediliyor.

**Durum:** ☐ Beklemede · ☐ Geçti · ☐ Kaldı · ☑ Atlandı (yöntem geçersiz — bkz. not)

> **Temizlik:** `chmod 644 "$SQLITEDB"` (adım 4'te zaten yapıldıysa gerek yok).

---

## MT-SQL-072 — SQL Server: 20 eşzamanlı agent kaydı veri bozulmadan tamamlanır

**Gerçek sonuç**
- **İmaj:** gerçek `mcr.microsoft.com/mssql/server:2022-latest`.
- 20 eş zamanlı `POST` isteğinin **tümü 201** döndü.
- `agentprism.agent_definitions` içinde `yuk-mssql-%` deseni **20** satır;
  `COUNT(DISTINCT name)` de **20** — kayıp ve çiftlenme yok.
- Logda `deadlock`, `SqlException` veya `Unhandled` **yok** (`grep -ci` → 0).
- Uygulama yük sonrası ayakta: `/health` → HTTP 200.
- SQLite karşılığı `MT-SQL-070` ile aynı sonuç; iki sağlayıcı da 20-yönlü eş
  zamanlı yazmayı bozulmadan karşılıyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-SQL-073 — SQL Server: container koşum sırasında durursa çalışan bir istek anlaşılır hatayla başarısız olur, uygulama çökmez

**Gerçek sonuç**
- **İmaj:** gerçek `mcr.microsoft.com/mssql/server:2022-latest`. Container
  durdurma KOSUM-PLANI §2.3'ten bir sapmadır; başka şerit çalışmadığı
  doğrulanarak kullanıcı onayıyla yapıldı.
- Başlangıç: `GET /api/agents/yuk-mssql-1` → **200**.
- **Container durdurulduğunda:**
  - İstek **500** döndü — anlaşılır bir hata, sessiz başarı değil.
  - `/health` **`Unhealthy`** (HTTP 503) döndü ama **yanıt verdi** — Kestrel
    ayakta kaldı.
  - Uygulama süreci **çökmedi** (`pgrep` → ayakta).
- **Container geri geldiğinde:** SQL Server tam hazır olana kadar beklendikten
  sonra art arda 5 deneme yapıldı; **beşi de `GET` 200 ve `/health` 200**
  döndü. `SqlServerDataSource` yeni bağlantı kurdu, süreç yeniden başlatmaya
  gerek kalmadı.
- ⚠️ **Koşum notu — ilk denemede yanlış "Kaldı" alınabilirdi.** `docker start`
  hemen ardından yapılan sorgu **500** döndü ve hata
  `A connection was successfully established with the server, but then an error
  occurred during the pre-login handshake.` idi. Bu AgentPrism kusuru değil:
  container TCP'yi kabul ediyor ama SQL Server motoru henüz açılmamış oluyor.
  Dokümandaki `sleep 15` bu makinede **yetersiz**; hazırlık `sqlcmd -Q "SELECT 1;"`
  başarılı olana kadar döngüyle beklenmelidir.
- **Doküman düzeltmesi önerilir:** `sleep 15` yerine hazırlık yoklaması
  (`until docker exec ... sqlcmd -Q "SELECT 1;"; do sleep 2; done`) kullanılmalı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## Özet — case sayımı

| Bölüm | Case aralığı | Adet |
|---|---|---|
| 1. SQLite bağlantı/ayar | MT-SQL-001–007 | 7 |
| 2. SQL Server bağlantı/ayar | MT-SQL-010–016 | 7 |
| 3. Migration'lar | MT-SQL-020–027 | 8 |
| 4. SQLite'a özgü | MT-SQL-030–032 | 3 |
| 5. SQL Server'a özgü | MT-SQL-040–042 | 3 |
| 6. Bellek içi izlek | MT-SQL-050–053 | 4 |
| 7. Sağlayıcılar arası karşılaştırma | MT-SQL-060–063 | 4 |
| 8. Yük ve sınır durumları | MT-SQL-070–073 | 4 |
| **Toplam** | | **40** |

Negatif/sınır case oranı: 001, 002, 005, 006, 010, 011, 012, 015, 021, 022,
023, 025, 026, 027, 030, 031, 040, 042, 052, 053, 063, 070, 071, 072, 073 —
**25/40 (%63)**, `PROMPT.md` §4.5'in %40 alt sınırının üzerinde.

---

## Koşum sonucu (2026-08-13, Şerit 1 — oturum S1-1 + S1-2)

**40/40 case koşuldu. Boş kalan yok.**

| Durum | Adet | Case |
|---|---|---|
| ☑ Geçti | **34** | — |
| ☑ Kaldı | **3** | `MT-SQL-004`, `MT-SQL-014` (→ `HATA-S1-002`) · `MT-SQL-005` (→ `HATA-S1-003`) |
| ☑ Atlandı | **3** | `MT-SQL-001`, `MT-SQL-010` (boş bağlantı dizesi örnek tarafından "yapılandırılmamış" sayılıyor) · `MT-SQL-071` (`chmod` çalışan sürecin açık fd'lerini etkilemiyor) |

Üç "Atlandı" da **yöntem geçersizliğidir, kod kusuru değildir**; gerekçe her
case'in kendi `Gerçek sonuç` alanında yazılıdır.

**Koşum sırasında düzeltilen kusur:** `HATA-S1-004` — `InvariantGlobalization`
SQL Server'ı tamamen kırıyordu (örnek **ve** paket şablonu). Karar `K-392` ile
kaldırıldı, dört doğrulama kapısı yeşil koştu, bloklanan 6 case gerçek SQL
Server bağlantısıyla yeniden koşuldu. Tüm SQL Server case'leri gerçek
`mcr.microsoft.com/mssql/server:2022-latest` ile koşuldu (`MT-SQL-042`).

**Bu dosyada düzeltilmesi gereken doküman sapmaları:**

| Nerede | Şu an | Olması gereken |
|---|---|---|
| `MT-SQL-020`, `024`, `041`, `060` | tablo sayısı **44** (PostgreSQL 45) | **45** (PostgreSQL **46**) — doğrulama sorgusu `__migrations` defterini de sayar |
| `MT-SQL-050` adımları | ön koşul yok | "`GET /api/models/health` çağır" adımı eklenmeli; önbellek ısıtılmadan `/health` `Degraded` döner |
| `MT-SQL-073` adım 3 | `sleep 15` | `until docker exec ... sqlcmd -Q "SELECT 1;"` hazırlık yoklaması — `sleep 15` yetersiz |
| `MT-SQL-040` | fiyatın katalogdan geleceği varsayılıyor | `gpt-5.4-mini` katalogda fiyatsız; `AgentPrism:Pricing:<saglayici>:<model>:Input` ile verilmeli (`Providers` ara anahtarı YOK, alan adı `Input`/`Output`) |

Ayrıntı ve hata kayıtları: [`SONUCLAR-S1-2026-08-13.md`](../../../arsiv/manuel-test-kosum-2026-08/SONUCLAR-S1-2026-08-13.md).

---
