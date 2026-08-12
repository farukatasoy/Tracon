# 04 — Kalıcılık: SQLite, SQL Server ve Bellek İçi (`SQL`)

> **Alan kodu:** `SQL` · **Faz:** 23 (SQL Server), 24 (SQLite)
> **Kaynak:** `src/AgentPrism.Sqlite` (`AgentPrismSqliteBuilderExtensions.cs` ·
> `AgentPrismSqliteOptions.cs` · `AgentPrismSqliteOptionsValidator.cs` ·
> `Internal/SqliteDataSource.cs` · `Internal/SqliteDataSourceFactory.cs` ·
> `Internal/SqliteDialect.cs` · `Internal/SqliteQueries.cs` · `Migrations/*.sql`) ·
> `src/AgentPrism.SqlServer` (aynı dosya kümesi, `SqlServer` önekiyle) ·
> `src/AgentPrism.Sql.Shared` (`Migrations/MigrationRunner.cs` ·
> `Internal/SqlIdentifier.cs` · `Internal/SqlStoreContext.cs`) ·
> `src/AgentPrism.Abstractions/Diagnostics/SchemaReadyGate.cs` ·
> `src/AgentPrism.Core/Diagnostics/AgentPrismDiagnosticsCollector.cs` ·
> `src/AgentPrism.AspNetCore/Health/AgentPrismHealthCheck.cs`
>
> `AgentPrism.Sql.Shared` PostgreSQL ile de ORTAKTIR ve bu dosyayla
> [`03-KALICILIK-POSTGRESQL.md`](03-KALICILIK-POSTGRESQL.md) arasında paylaşılır;
> depo kayıt deseni (`Replace`/`TryAdd`), denetim izi dekoratörleri ve
> `MigrationRunner`'ın genel akışı orada tek sefer derinlemesine test edildi ve
> burada TEKRARLANMAZ. Bu dosya SQLite/SQL Server'a ÖZGÜ diyalekt davranışını,
> üç sağlayıcı arasındaki taşınabilirliği ve **bellek içi izleği** (hiçbir
> `Use*()` çağrılmadığında) kanıtlar.
>
> Ortam kurulumu, fixture verisi ve reset yordamı [`00-INDEKS.md`](00-INDEKS.md)'dedir.

---

## Bu dosya neyi kanıtlar

`UseSqlite()`/`UseSqlServer()` PostgreSQL ile AYNI zinciri kurar (bkz.
`03-KALICILIK-POSTGRESQL.md` diyagramı) — fark yalnız `SqlDialect` türevinde
yaşar. Bu dosya üç şeyi kanıtlar: (1) SQLite ve SQL Server'ın kendi ayar
doğrulama/migration kilit mekanizmaları PostgreSQL ile aynı garantiyi verir,
(2) iki sağlayıcının diyalekt farkları (dosya kilidi vs `sp_getapplock`,
`decimal` hassasiyeti, `:memory:` kalıcısızlığı) doğru yerde ortaya çıkar,
(3) **hiçbir** kalıcılık sağlayıcısı kayıtlı değilken AgentPrism'in tasarım
kuralı #1'i ("sıfır sürpriz") doğrudur — uygulama sorunsuz açılır, yalnızca
veri süreçle birlikte biter.

```mermaid
flowchart TD
    A["Use*() hic cagrilmadi"] --> B["Bellek ici store'lar (AddAgentPrism varsayilani)"]
    C["UseSqlite(connectionString)"] --> D["AgentPrismSqliteOptions + Validator"]
    E["UseSqlServer(connectionString)"] --> F["AgentPrismSqlServerOptions + Validator"]
    D --> G["SqliteDataSource + dosya kilidi"]
    F --> H["SqlServerDataSource + sp_getapplock"]
    G --> I["SqlStoreContext (SqliteDialect)"]
    H --> J["SqlStoreContext (SqlServerDialect)"]
    I --> K["MigrationHostedService: 15 migration"]
    J --> K
    K --> L["SchemaReadyGate acilir"]
    B --> L
    I --> M["Depo kayitlari: Replace / TryAdd (03'te test edildi)"]
    J --> M
    L --> N["/health, /agentprism/api/diagnostics"]
```

## Sınır: bu dosya nerede biter

| Konu | Nerede |
|---|---|
| Depo kayıt deseni (`Replace`/`TryAdd` önceliği), denetim izi dekoratörleri, iki sağlayıcı aynı zincirde kayıtlıysa uyarı (K-183) | [`03-KALICILIK-POSTGRESQL.md`](03-KALICILIK-POSTGRESQL.md) `MT-PG-030`–`035` |
| `pgvector`/`IVectorSearchStore`, RAG anlamsal kalitesi (PostgreSQL'e özgü — SQLite/SQL Server bu depoyu HİÇ uygulamaz) | `03-KALICILIK-POSTGRESQL.md` `MT-PG-040`–`047` |
| Saklama, arşivleme politikaları, kota | [`23-SAKLAMA-ARSIV-KOTA.md`](23-SAKLAMA-ARSIV-KOTA.md) |
| Kiracı/rol/API anahtarı HTTP güvenlik sınırları | [`13-KIRACI-VE-GUVENLIK.md`](13-KIRACI-VE-GUVENLIK.md) |
| Genel teşhis/sağlık ucu sözleşmesi (sağlayıcıdan bağımsız alanlar) | [`25-SAGLIK-TESHIS-OPENAPI.md`](25-SAGLIK-TESHIS-OPENAPI.md) |
| AOT publish smoke testi | [`01-KURULUM-VE-PAKETLEME.md`](01-KURULUM-VE-PAKETLEME.md) |
| Agent dosya belleği arama (`SqlAgentFileStore`, SQLite `LIKE` büyük/küçük harf duyarlılığı) | [`19-COK-MODLULUK-VE-SES.md`](19-COK-MODLULUK-VE-SES.md) |

## Koşmadan önce

1. [`00-INDEKS.md`](00-INDEKS.md) §4 reset yordamı uygulanır.
2. SQL Server bölümü için `ap-mssql` container'ı çalışır durumdadır (bkz.
   `00-INDEKS.md` §2.2). Donanım notu: bu makine arm64 ise ve Rosetta emülasyonu
   kapalıysa `mcr.microsoft.com/mssql/server` başlamaz — bkz. **MT-SQL-042**.
3. SQLite bölümü hiçbir container istemez; `sqlite3` komut satırı aracı gerekir
   (macOS'ta önceden kuruludur; yoksa `brew install sqlite`).
4. `AgentPrism:Ui:AuthToken` `manuel-test-token-2026`'dır.
5. Bu dosyanın her case'i **kendi kalıcılık sağlayıcısını** açıkça seçer —
   `00-INDEKS.md` §2.4'teki kural geçerlidir: aynı anda yalnız BİR sağlayıcının
   bağlantı dizesi tanımlı olmalıdır (`samples/AgentPrism.Api/Program.cs`
   `SqlServer → PostgreSQL → SQLite` sırasıyla İLK doluyu seçer, satır 635–646).

Kısaltmalar — bu dosyadaki her `curl`/`sqlite3`/`sqlcmd` şunları kullanır:

```bash
export APB="Authorization: Bearer manuel-test-token-2026"
export APU="http://localhost:5080/agentprism"
export SQLITEDB="samples/AgentPrism.Api/agentprism-manuel.db"
export MSSQL="docker exec -i ap-mssql /opt/mssql-tools18/bin/sqlcmd -C -S localhost -U sa -P AgentPrism!2026 -d AgentPrism"
```

---

# 1 — SQLite: Bağlantı, ayarlar ve doğrulama

Bu bölüm `AgentPrismSqliteOptions`, `AgentPrismSqliteOptionsValidator` ve
`SqliteDataSourceFactory`'yi sınar. `03-KALICILIK-POSTGRESQL.md`'nin MT-PG-001–007
ile aynı garanti seviyesi; farklar yalnız SQLite'ın şema kavramının olmaması
(`TablePrefix` `SchemaName`'in yerini alır) ve dosya tabanlı olmasıdır.

### MT-SQL-001 — Boş bağlantı dizesiyle başlatma reddedilir

| | |
|---|---|
| **İzlek** | B |
| **Önem** | **Kritik** |
| **İlgili faz** | Faz 24 |
| **İlgili karar** | K-006 |

**Ön koşul**
- Yok.

**Adımlar**
1. Bağlantı dizesini boş bir değere ayarla.
2. Uygulamayı başlat.

**Girilecek veri**
```bash
dotnet user-secrets set "AgentPrism:Sqlite:ConnectionString" ""
cd samples/AgentPrism.Api && dotnet run
```

**Beklenen sonuç**
- Uygulama başlamayı **reddeder** (`ValidateOnStart`); konsolda
  `OptionsValidationException` görünür, mesaj
  `AgentPrismSqliteOptions.ConnectionString bos olamaz` metnini taşır.
- `/health` hiçbir zaman yanıt vermez.

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

### MT-SQL-002 — Geçersiz `TablePrefix` biçimleri ve enjeksiyon denemesi reddedilir

| | |
|---|---|
| **İzlek** | B |
| **Önem** | **Kritik** |
| **İlgili faz** | Faz 24 |
| **İlgili karar** | K-029, K-179 |

Negatif senaryo. `TablePrefix` SQL metnine doğrudan yerleştirilir (parametre
olamaz); PostgreSQL'in şema adı kuralıyla BİREBİR aynı doğrulamadan geçer
(`SqlIdentifier.IsValidUnquoted`, paylaşılan kod).

**Ön koşul**
- Yok.

**Adımlar**
1. Büyük harf içeren bir önek dene.
2. Boşluk içeren bir önek dene.
3. SQL enjeksiyonu deneyen bir önek dene.

**Girilecek veri**
```bash
# 1) Buyuk harf
dotnet user-secrets set "AgentPrism:Sqlite:TablePrefix" "Agentprism_"
cd samples/AgentPrism.Api && dotnet run
# Ctrl+C ile durdur

# 2) Bosluk
dotnet user-secrets set "AgentPrism:Sqlite:TablePrefix" "agent prism_"
dotnet run
# Ctrl+C ile durdur

# 3) Enjeksiyon denemesi
dotnet user-secrets set "AgentPrism:Sqlite:TablePrefix" "x_; DROP TABLE agentprism_tenants;--"
dotnet run
# Ctrl+C ile durdur
```

**Beklenen sonuç**
- Üçü de başlamayı reddeder; hata mesajı
  `AgentPrismSqliteOptions.TablePrefix gecerli bir AgentPrism tablo oneki degil`
  metnini taşır.
- 3. denemede `DROP TABLE` **hiçbir zaman çalıştırılmaz**.

**Doğrulama sorgusu**
```bash
sqlite3 "$SQLITEDB" ".tables"
```
```
-- Beklenen: 'agentprism_tenants' hala listede.
```

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

### MT-SQL-003 — `CommandTimeoutSeconds` sınırları

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 24 |
| **İlgili karar** | — |

**Ön koşul**
- Yok.

**Adımlar**
1. `-1` dene (sınır dışı, negatif).
2. `3601` dene (sınır dışı, üst).
3. `0` dene (geçerli — sınırsız anlamına gelir).

**Girilecek veri**
```bash
dotnet user-secrets set "AgentPrism:Sqlite:CommandTimeoutSeconds" "-1"
cd samples/AgentPrism.Api && dotnet run
# Ctrl+C ile durdur

dotnet user-secrets set "AgentPrism:Sqlite:CommandTimeoutSeconds" "3601"
dotnet run
# Ctrl+C ile durdur

dotnet user-secrets set "AgentPrism:Sqlite:CommandTimeoutSeconds" "0"
dotnet run
```

**Beklenen sonuç**
- İlk iki değer başlamayı reddeder: `CommandTimeoutSeconds 0 ile 3600 arasinda
  olmalidir`.
- `0` kabul edilir, uygulama normal açılır.

**Gerçek sonuç**
- `-1` ve `3601` başlamayı reddetti; `Now listening` yazılmadı. Mesaj beklendiği
  gibi, reddedilen değeri de taşıyor: `AgentPrismSqliteOptions.CommandTimeoutSeconds
  0 ile 3600 arasinda olmalidir. Gelen deger: -1.` (aynısı `3601` için).
- `0` kabul edildi; uygulama açıldı ve `Now listening` yazdı.
- Sınırların kendisi (`0` ve `3600`) kabul tarafında — doğrulama kapsayıcı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

> **Temizlik:** `dotnet user-secrets set "AgentPrism:Sqlite:CommandTimeoutSeconds" "30"`

---

### MT-SQL-004 — `AutoApplyMigrations=false` migration uygulamaz, sorumluluk operatöre kalır

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 24 |
| **İlgili karar** | — |

**Ön koşul**
- Reset yordamı uygulanmış, `$SQLITEDB` dosyası yok.

**Adımlar**
1. `AutoApplyMigrations`'ı kapat.
2. Uygulamayı başlat.
3. Tablo sayısını kontrol et.

**Girilecek veri**
```bash
dotnet user-secrets set "AgentPrism:Sqlite:AutoApplyMigrations" "false"
cd samples/AgentPrism.Api && dotnet run
```
```bash
sqlite3 "$SQLITEDB" "SELECT count(*) FROM sqlite_master WHERE type='table';"
```

**Beklenen sonuç**
- Uygulama **açılır** (migration eksikliği başlatmayı engellemez).
- Konsolda "AgentPrism migration'lari otomatik uygulanmiyor" bilgi satırı görünür.
- SQLite dosyası ya hiç oluşmaz ya da boştur — `count(*)` **0** döner.
- `/health` bu durumda `Unhealthy` döner (bekleyen migration listesi dolu).

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

**Durum:** ☐ Beklemede · ☐ Geçti · ☑ Kaldı · ☐ Atlandı → `HATA-S1-002` (Yüksek)

> **Temizlik:** `dotnet user-secrets set "AgentPrism:Sqlite:AutoApplyMigrations" "true"`, reset yordamı.

---

### MT-SQL-005 — `Data Source=:memory:` desteklenir ama KALICI DEĞİLDİR

| | |
|---|---|
| **İzlek** | B |
| **Önem** | **Kritik** |
| **İlgili faz** | Faz 24 |
| **İlgili karar** | — |

Negatif/sınır senaryosu. `AgentPrismSqliteOptions.ConnectionString`'in XML
dokümanı açıktır: `:memory:` desteklenir ama bağlantı kapanınca veri gider.
Bu case iddiayı doğrular ve migration kilidinin `:memory:`'de ATLANDIĞINI
(dosya kilidi kurulamaz çünkü dosya yoktur) doğrular.

**Ön koşul**
- Yok.

**Adımlar**
1. Bağlantı dizesini `:memory:` yap.
2. Uygulamayı başlat, bir agent kaydet.
3. Kaydı doğrula.
4. Uygulamayı durdur, yeniden başlat.
5. Aynı kaydı tekrar sorgula.

**Girilecek veri**
```bash
dotnet user-secrets set "AgentPrism:Sqlite:ConnectionString" "Data Source=:memory:"
cd samples/AgentPrism.Api && dotnet run
```
```bash
curl -s -X POST "$APU/api/agents" -H "$APB" -H "content-type: application/json" \
  -d '{"name":"manuel-bellek-test","model":{"provider":"echo","model":"echo-1"}}'
curl -s "$APU/api/agents/manuel-bellek-test" -H "$APB" -w "\nHTTP: %{http_code}\n"
```
```bash
# Ctrl+C, sonra yeniden:
cd samples/AgentPrism.Api && dotnet run
```
```bash
curl -s "$APU/api/agents/manuel-bellek-test" -H "$APB" -w "\nHTTP: %{http_code}\n"
```

**Beklenen sonuç**
- Uygulama migration'ları normal uygular (konsolda "AgentPrism 15 migration
  uyguladi." görünür — kilit atlanır ama migration'lar YİNE çalışır).
- İlk sorgu agent'ı **bulur** (HTTP 200).
- Yeniden başlatma SONRASI aynı sorgu **404** döner — veri süreçle birlikte
  bitmiştir.

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

**Durum:** ☐ Beklemede · ☐ Geçti · ☑ Kaldı · ☐ Atlandı → `HATA-S1-003` (Yüksek)

> **Temizlik:** `dotnet user-secrets set "AgentPrism:Sqlite:ConnectionString" "Data Source=agentprism-manuel.db"`

---

### MT-SQL-006 — Yazılamayan bir dizinde başlatma reddedilir

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 24 |
| **İlgili karar** | — |

Negatif/sınır senaryosu. Dosya sistemi izin hatası yutulmamalı, anlaşılır bir
hatayla başlatmayı durdurmalıdır.

**Ön koşul**
- Yok.

**Adımlar**
1. Yazılamayan bir dizin oluştur.
2. Bağlantı dizesini o dizine yönlendir.
3. Uygulamayı başlat.

**Girilecek veri**
```bash
mkdir -p /tmp/agentprism-salt-okunur
chmod 555 /tmp/agentprism-salt-okunur
dotnet user-secrets set "AgentPrism:Sqlite:ConnectionString" \
  "Data Source=/tmp/agentprism-salt-okunur/agentprism.db"
cd samples/AgentPrism.Api && dotnet run
```

**Beklenen sonuç**
- Uygulama başlamayı reddeder; konsolda `SqliteException` ("unable to open
  database file" veya benzeri) görünür.
- Süreç "Now listening on" satırını hiç yazmadan sonlanır.

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

### MT-SQL-007 — Üç `UseSqlite` aşırı yüklemesi aynı sonucu üretir

| | |
|---|---|
| **İzlek** | C |
| **Önem** | Düşük |
| **İlgili faz** | Faz 24 |
| **İlgili karar** | — |

`UseSqlite(string)`, `UseSqlite(IConfiguration)`, `UseSqlite(Action<Options>)`
aynı DI kaydını üretir. Bu, kod okuma ile doğrulanan bir sözleşmedir — koşum
gerektirmez, izlek **C** işaretlenmiştir çünkü tek doğrulama kaynağı kaynak
kodun kendisidir.

**Ön koşul**
- `src/AgentPrism.Sqlite/AgentPrismSqliteBuilderExtensions.cs` açık.

**Adımlar**
1. Üç aşırı yüklemenin gövdesini karşılaştır.

**Girilecek veri**
```bash
grep -n "public static IAgentPrismBuilder UseSqlite" -A5 \
  src/AgentPrism.Sqlite/AgentPrismSqliteBuilderExtensions.cs
```

**Beklenen sonuç**
- `UseSqlite(string)` ve `UseSqlite(IConfiguration)` ikisi de `UseSqlite(Action<Options>)`'a
  yönlendirir; tek kayıt yolu vardır.

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

### MT-SQL-010 — Boş bağlantı dizesiyle başlatma reddedilir

| | |
|---|---|
| **İzlek** | B |
| **Önem** | **Kritik** |
| **İlgili faz** | Faz 23 |
| **İlgili karar** | K-006 |

**Ön koşul**
- `ap-mssql` container'ı çalışıyor.

**Adımlar**
1. Bağlantı dizesini boş bir değere ayarla.
2. Uygulamayı başlat.

**Girilecek veri**
```bash
dotnet user-secrets set "AgentPrism:SqlServer:ConnectionString" ""
cd samples/AgentPrism.Api && dotnet run
```

**Beklenen sonuç**
- Uygulama başlamayı reddeder; hata mesajı
  `AgentPrismSqlServerOptions.ConnectionString bos olamaz` metnini taşır.

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

### MT-SQL-011 — Şema adı `dbo` olamaz

| | |
|---|---|
| **İzlek** | B |
| **Önem** | **Kritik** |
| **İlgili faz** | Faz 23 |
| **İlgili karar** | K-013 |

PostgreSQL'in `public` yasağının SQL Server karşılığı. `dbo`, SQL Server'ın
varsayılan tüketici şemasıdır; AgentPrism ona hiçbir koşulda dokunmaz.

**Ön koşul**
- `ap-mssql` container'ı çalışıyor, bağlantı dizesi geçerli.

**Adımlar**
1. Şema adını `dbo` olarak ayarla.
2. Uygulamayı başlat.

**Girilecek veri**
```bash
dotnet user-secrets set "AgentPrism:SqlServer:SchemaName" "dbo"
cd samples/AgentPrism.Api && dotnet run
```

**Beklenen sonuç**
- Uygulama başlamayı reddeder.
- Hata mesajı `'dbo' olamaz` ve `K-013` ifadelerini taşır.

**Doğrulama sorgusu**
```bash
$MSSQL -Q "SELECT COUNT(*) FROM sys.tables WHERE schema_id = SCHEMA_ID('dbo');"
```
```
-- Beklenen: denemeden ONCEKI ile AYNI sayi (dbo semasina hicbir tablo yazilmadi).
```

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

### MT-SQL-012 — Geçersiz şema adı biçimleri ve enjeksiyon denemesi reddedilir

| | |
|---|---|
| **İzlek** | B |
| **Önem** | **Kritik** |
| **İlgili faz** | Faz 23 |
| **İlgili karar** | K-029, K-179 |

Negatif senaryo. SQL Server tanımlayıcıları için PostgreSQL'den daha geniş bir
karakter kümesine izin verir, ama AgentPrism taşınabilirlik için **aynı katı
kuralı** (`SqlIdentifier`, paylaşılan kod) her iki sağlayıcıda da uygular.

**Ön koşul**
- `ap-mssql` container'ı çalışıyor.

**Adımlar**
1. Büyük harf içeren bir şema adı dene.
2. Boşluk içeren bir şema adı dene.
3. SQL enjeksiyonu deneyen bir şema adı dene.

**Girilecek veri**
```bash
# 1) Buyuk harf
dotnet user-secrets set "AgentPrism:SqlServer:SchemaName" "Agentprism"
cd samples/AgentPrism.Api && dotnet run
# Ctrl+C ile durdur

# 2) Bosluk
dotnet user-secrets set "AgentPrism:SqlServer:SchemaName" "agent prism"
dotnet run
# Ctrl+C ile durdur

# 3) Enjeksiyon denemesi
dotnet user-secrets set "AgentPrism:SqlServer:SchemaName" "agentprism]; DROP TABLE sys.tables;--"
dotnet run
# Ctrl+C ile durdur
```

**Beklenen sonuç**
- Üçü de başlamayı reddeder; hata mesajı
  `AgentPrismSqlServerOptions.SchemaName gecerli bir AgentPrism sema adi degil`
  metnini taşır.
- 3. denemede hiçbir DDL **çalıştırılmaz**.

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

### MT-SQL-013 — `CommandTimeoutSeconds` sınırları

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 23 |
| **İlgili karar** | — |

**Ön koşul**
- `ap-mssql` container'ı çalışıyor.

**Adımlar**
1. `-1` dene (sınır dışı).
2. `3601` dene (sınır dışı).

**Girilecek veri**
```bash
dotnet user-secrets set "AgentPrism:SqlServer:CommandTimeoutSeconds" "-1"
cd samples/AgentPrism.Api && dotnet run
# Ctrl+C ile durdur

dotnet user-secrets set "AgentPrism:SqlServer:CommandTimeoutSeconds" "3601"
dotnet run
```

**Beklenen sonuç**
- İkisi de başlamayı reddeder: `CommandTimeoutSeconds 0 ile 3600 arasinda
  olmalidir`.

**Gerçek sonuç**
- `-1` ve `3601` başlamayı reddetti; `Now listening` yazılmadı. Mesaj:
  `AgentPrismSqlServerOptions.CommandTimeoutSeconds 0 ile 3600 arasinda
  olmalidir. Gelen deger: -1.` (aynısı `3601` için).
- SQLite karşılığı MT-SQL-003 ile birebir aynı sınır ve aynı mesaj kalıbı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

> **Temizlik:** `dotnet user-secrets set "AgentPrism:SqlServer:CommandTimeoutSeconds" "30"`

---

### MT-SQL-014 — `AutoApplyMigrations=false` migration uygulamaz

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 23 |
| **İlgili karar** | — |

**Ön koşul**
- Reset yordamı uygulanmış, `AgentPrism` veritabanı boş.

**Adımlar**
1. `AutoApplyMigrations`'ı kapat.
2. Uygulamayı başlat.
3. Tablo sayısını kontrol et.

**Girilecek veri**
```bash
dotnet user-secrets set "AgentPrism:SqlServer:AutoApplyMigrations" "false"
cd samples/AgentPrism.Api && dotnet run
```
```bash
$MSSQL -Q "SELECT COUNT(*) FROM sys.tables WHERE schema_id = SCHEMA_ID('agentprism');"
```

**Beklenen sonuç**
- Uygulama açılır; konsolda otomatik migration'ın kapalı olduğu bilgisi görünür.
- Tablo sayısı **0**'dır (şema bile oluşmamış olabilir).
- `/health` `Unhealthy` döner.

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

**Durum:** ☐ Beklemede · ☐ Geçti · ☑ Kaldı · ☐ Atlandı → `HATA-S1-002` (Yüksek)

> **Temizlik:** `dotnet user-secrets set "AgentPrism:SqlServer:AutoApplyMigrations" "true"`, reset yordamı.

---

### MT-SQL-015 — Yanlış host ile başlatma migration adımında çöker (fail-fast)

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 23 |
| **İlgili karar** | — |

Negatif senaryo. `MigrationHostedService`'in davranışı sağlayıcıdan
bağımsızdır: "Hata uygulamayi baslatmaz" — bkz. `03-KALICILIK-POSTGRESQL.md`
`MT-PG-007`'nin aynısı, SQL Server ile.

**Ön koşul**
- Hiçbir şey dinlemeyen bir port biliniyor (örnek: `1`).

**Adımlar**
1. Bağlantı dizesini geçersiz bir porta ayarla.
2. Uygulamayı başlat ve bekle.
3. `/health`'e erişmeyi dene.

**Girilecek veri**
```bash
dotnet user-secrets set "AgentPrism:SqlServer:ConnectionString" \
  "Server=localhost,1;Database=AgentPrism;User Id=sa;Password=AgentPrism!2026;TrustServerCertificate=true;Connect Timeout=5"
cd samples/AgentPrism.Api && dotnet run
```
```bash
# Baska bir terminalde, uygulama hala "baslarken":
curl -s -m 3 -w "\nHTTP: %{http_code}\n" "http://localhost:5080/health"
```

**Beklenen sonuç**
- `dotnet run` süreci bir `SqlException` zincirini konsola yazar ve **sonlanır**.
- `/health` isteği bağlantı reddi veya zaman aşımıyla başarısız olur.

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

### MT-SQL-016 — Üç `UseSqlServer` aşırı yüklemesi aynı sonucu üretir

| | |
|---|---|
| **İzlek** | C |
| **Önem** | Düşük |
| **İlgili faz** | Faz 23 |
| **İlgili karar** | — |

MT-SQL-007'nin SQL Server karşılığı — kod okuma ile doğrulanır, koşum
gerektirmez.

**Ön koşul**
- `src/AgentPrism.SqlServer/AgentPrismSqlServerBuilderExtensions.cs` açık.

**Adımlar**
1. Üç aşırı yüklemenin gövdesini karşılaştır.

**Girilecek veri**
```bash
grep -n "public static IAgentPrismBuilder UseSqlServer" -A5 \
  src/AgentPrism.SqlServer/AgentPrismSqlServerBuilderExtensions.cs
```

**Beklenen sonuç**
- `UseSqlServer(string)` ve `UseSqlServer(IConfiguration)` ikisi de
  `UseSqlServer(Action<Options>)`'a yönlendirir.

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

### MT-SQL-020 — SQLite: boş DB'de 15 migration sırayla uygulanır

| | |
|---|---|
| **İzlek** | B |
| **Önem** | **Kritik** |
| **İlgili faz** | Faz 24 |
| **İlgili karar** | — |

**Ön koşul**
- Reset yordamı uygulanmış, `$SQLITEDB` dosyası yok.

**Adımlar**
1. Uygulamayı başlat.
2. Açılış logunu oku.
3. Uygulanmış migration sayısını sorgula.

**Girilecek veri**
```bash
cd samples/AgentPrism.Api && dotnet run
```
```bash
sqlite3 "$SQLITEDB" "SELECT count(*) FROM agentprism___migrations;"
sqlite3 "$SQLITEDB" "SELECT name FROM agentprism___migrations ORDER BY id;"
```

**Beklenen sonuç**
- Konsol `AgentPrism 15 migration uyguladi.` yazar.
- `count(*)` **15** döner.
- Ad listesi `0001_initial`'dan `0015_experiment_canary`'e sırayla gider.
- `sqlite_master`'daki tablo sayısı **44**'tür.

**Doğrulama sorgusu**
```bash
sqlite3 "$SQLITEDB" "SELECT count(*) FROM sqlite_master WHERE type='table';"
```

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

### MT-SQL-021 — SQLite: yeniden başlatma migration'ları tekrar uygulamaz (idempotent)

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 24 |
| **İlgili karar** | — |

**Ön koşul**
- MT-SQL-020 geçti.

**Adımlar**
1. Uygulamayı durdur.
2. Yeniden başlat.
3. Açılış logunu oku.

**Girilecek veri**
```bash
cd samples/AgentPrism.Api && dotnet run
```

**Beklenen sonuç**
- Konsolda migration uygulama satırı **görünmez** (0 migration uygulanır,
  bilgi satırı yalnızca `count > 0` iken yazılır).
- `sqlite3 "$SQLITEDB" "SELECT count(*) FROM agentprism___migrations;"` hâlâ **15** döner.

**Gerçek sonuç**
- Yeniden başlatmada konsolda `AgentPrism.MigrationRunner` satırı hiç görünmedi.
- `/api/diagnostics` `migrationsUpToDate`=true, `pendingMigrations`=[] doğruladı.
- `agentprism___migrations` hâlâ **15** satır taşıyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-SQL-022 — SQLite: uygulanmış migration'ın checksum'ı bozulursa başlama reddedilir

| | |
|---|---|
| **İzlek** | B |
| **Önem** | **Kritik** |
| **İlgili faz** | Faz 24 |
| **İlgili karar** | — |

Negatif senaryo. Uygulanmış bir migration asla düzenlenmez; dosya bütünlüğü
bir özet ile korunur.

**Ön koşul**
- MT-SQL-020 geçti, uygulama durdurulmuş.

**Adımlar**
1. `__migrations` defterindeki bir satırın `checksum` sütununu elle boz.
2. Uygulamayı başlat.

**Girilecek veri**
```bash
sqlite3 "$SQLITEDB" "UPDATE agentprism___migrations SET checksum = 'bozuk' WHERE id = 1;"
cd samples/AgentPrism.Api && dotnet run
```

**Beklenen sonuç**
- Uygulama başlamayı reddeder; `AgentPrismException` mesajı
  `'0001_initial' migration'i veritabaninda uygulanmis ancak dosyanin icerigi
  degismis` metnini taşır.

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

### MT-SQL-023 — SQLite: iki eşzamanlı örnek dosya kilidiyle çakışmadan migration uygular

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 24 |
| **İlgili karar** | K-192 |

Sınır senaryosu. SQLite'ta `pg_advisory_lock`/`sp_getapplock` karşılığı
yoktur; kilit `<veritabani>.agentprism-migration-lock` adlı bir sidecar dosya
üzerinden alınır (`FileShare.None`).

**Ön koşul**
- Reset yordamı uygulanmış, `$SQLITEDB` dosyası yok.

**Adımlar**
1. İki terminalde uygulamayı EŞ ZAMANLI başlat (biri farklı portta).
2. İkisinin de loglarını oku.
3. Uygulanmış migration sayısını kontrol et.

**Girilecek veri**
```bash
# Terminal 1
cd samples/AgentPrism.Api && dotnet run

# Terminal 2 (mumkun oldugunca ayni anda)
cd samples/AgentPrism.Api && dotnet run --urls http://localhost:5090
```
```bash
sqlite3 "$SQLITEDB" "SELECT count(*) FROM agentprism___migrations;"
```

**Beklenen sonuç**
- Yalnız BİR terminalin logu `AgentPrism 15 migration uyguladi.` yazar; diğeri
  kilidi ikinci sırada alır ve 0 migration uygular.
- Hiçbir terminalde checksum hatası veya çökme olmaz.
- `count(*)` tam olarak **15** döner (30 değil).

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

### MT-SQL-024 — SQL Server: boş DB'de 15 migration sırayla uygulanır

| | |
|---|---|
| **İzlek** | B |
| **Önem** | **Kritik** |
| **İlgili faz** | Faz 23 |
| **İlgili karar** | — |

**Ön koşul**
- Reset yordamı uygulanmış, `AgentPrism` veritabanı boş.

**Adımlar**
1. Uygulamayı başlat.
2. Açılış logunu oku.
3. Uygulanmış migration sayısını sorgula.

**Girilecek veri**
```bash
cd samples/AgentPrism.Api && dotnet run
```
```bash
$MSSQL -Q "SELECT COUNT(*) FROM agentprism.__migrations;"
$MSSQL -Q "SELECT name FROM agentprism.__migrations ORDER BY id;"
```

**Beklenen sonuç**
- Konsol `AgentPrism 15 migration uyguladi.` yazar.
- Sorgu **15** döner, `0001_initial`'dan `0015_experiment_canary`'e sırayla.
- `sys.tables` içinde `agentprism` şemasına ait **44** tablo vardır.

**Doğrulama sorgusu**
```bash
$MSSQL -Q "SELECT COUNT(*) FROM sys.tables WHERE schema_id = SCHEMA_ID('agentprism');"
```

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

### MT-SQL-025 — SQL Server: yeniden başlatma migration'ları tekrar uygulamaz (idempotent)

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 23 |
| **İlgili karar** | — |

**Ön koşul**
- MT-SQL-024 geçti.

**Adımlar**
1. Uygulamayı durdur.
2. Yeniden başlat.
3. Açılış logunu oku.

**Girilecek veri**
```bash
cd samples/AgentPrism.Api && dotnet run
```

**Beklenen sonuç**
- Konsolda migration uygulama satırı görünmez.
- `$MSSQL -Q "SELECT COUNT(*) FROM agentprism.__migrations;"` hâlâ **15** döner.

**Gerçek sonuç**
- Yeniden başlatmada konsolda `MigrationRunner` satırı hiç görünmedi
  (`grep -c` → 0); 0 migration uygulandı.
- `agentprism.__migrations` hâlâ **15** satır.
- `/api/diagnostics`: `persistenceProvider` = `SQL Server`,
  `migrationsUpToDate` = `True`, `pendingMigrations` = [].

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-SQL-026 — SQL Server: uygulanmış migration'ın checksum'ı bozulursa başlama reddedilir

| | |
|---|---|
| **İzlek** | B |
| **Önem** | **Kritik** |
| **İlgili faz** | Faz 23 |
| **İlgili karar** | — |

Negatif senaryo. MT-SQL-022'nin SQL Server karşılığı.

**Ön koşul**
- MT-SQL-024 geçti, uygulama durdurulmuş.

**Adımlar**
1. `__migrations` defterindeki bir satırın `checksum` sütununu elle boz.
2. Uygulamayı başlat.

**Girilecek veri**
```bash
$MSSQL -Q "UPDATE agentprism.__migrations SET checksum = 'bozuk' WHERE id = 1;"
cd samples/AgentPrism.Api && dotnet run
```

**Beklenen sonuç**
- Uygulama başlamayı reddeder; `AgentPrismException` mesajı
  `'0001_initial' migration'i veritabaninda uygulanmis ancak dosyanin icerigi
  degismis` metnini taşır.

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

### MT-SQL-027 — SQL Server: iki eşzamanlı örnek `sp_getapplock` ile çakışmadan migration uygular

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 23 |
| **İlgili karar** | — |

Sınır senaryosu. `sp_getapplock` oturum kapsamlı **Exclusive** kilit alır;
dönüş değeri negatifse kilit alınamamış demektir ve `AgentPrismException`
fırlatılır (bkz. `SqlServerDialect.AcquireMigrationLockAsync`).

**Ön koşul**
- Reset yordamı uygulanmış, `AgentPrism` veritabanı boş.

**Adımlar**
1. İki terminalde uygulamayı EŞ ZAMANLI başlat.
2. İkisinin de loglarını oku.
3. Uygulanmış migration sayısını kontrol et.

**Girilecek veri**
```bash
# Terminal 1
cd samples/AgentPrism.Api && dotnet run

# Terminal 2 (mumkun oldugunca ayni anda)
cd samples/AgentPrism.Api && dotnet run --urls http://localhost:5090
```
```bash
$MSSQL -Q "SELECT COUNT(*) FROM agentprism.__migrations;"
```

**Beklenen sonuç**
- Yalnız BİR terminal migration uygular; diğeri kilidi bekler ve boş döner.
- `COUNT(*)` tam olarak **15** döner.
- Hiçbir terminalde `sp_getapplock donus degeri` hatası (kilit süresi aşımı)
  görünmez — 30 saniyelik kilit zaman aşımı bu kısa migration seti için
  yeterlidir.

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

### MT-SQL-030 — WAL modu dosyaları oluşur, `busy_timeout` eşzamanlı yazmayı bekletir

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 24 |
| **İlgili karar** | — |

Her bağlantı açıldığında `PRAGMA journal_mode = WAL; PRAGMA busy_timeout =
5000;` çalıştırılır (`SqliteDataSource.OnStateChange`). Bu, tek yazıcı/çoklu
okuyucu kısıtını gevşetir ve kısa süreli kilitlenmelerde hata yerine bekleme
üretir.

**Ön koşul**
- Uygulama normal çalışıyor, SQLite aktif.

**Adımlar**
1. WAL yardımcı dosyalarının varlığını kontrol et.
2. 20 agent kaydını EŞ ZAMANLI gönder.
3. Sonuç kodlarını ve kayıtlı sayıyı doğrula.

**Girilecek veri**
```bash
ls -la samples/AgentPrism.Api/*.db-wal samples/AgentPrism.Api/*.db-shm
```
```bash
for i in $(seq 1 20); do
  curl -s -o /dev/null -w "%{http_code}\n" -X POST "$APU/api/agents" -H "$APB" \
    -H "content-type: application/json" -d "{
    \"name\": \"manuel-sqlite-esz-$i\", \"model\": { \"provider\": \"echo\", \"model\": \"echo-1\" }
  }" &
done
wait
```
```bash
sqlite3 "$SQLITEDB" "SELECT count(*) FROM agentprism_agent_definitions WHERE name LIKE 'manuel-sqlite-esz-%';"
```

**Beklenen sonuç**
- `-wal` ve `-shm` dosyaları var olur (WAL modu etkindir).
- 20 HTTP kodunun tümü 2xx'tir.
- SQL sorgusu **20** döner.
- Uygulama loglarında `SQLITE_BUSY`/`database is locked` hatası **görünmez**
  (`busy_timeout=5000` çakışmaları bekleterek çözer).

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

### MT-SQL-031 — Yabancı anahtar zorlaması açıktır: agent silindiğinde sürüm satırları CASCADE ile gider

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 24 |
| **İlgili karar** | — |

SQLite'ta yabancı anahtar zorlaması **varsayılan kapalıdır**; her bağlantıda
`PRAGMA foreign_keys = ON` açıkça çalıştırılmazsa `agent_definition_versions.agent_id
REFERENCES agent_definitions(id) ON DELETE CASCADE` sessizce yok sayılır ve
silinen bir agent'ın sürüm satırları geride kalır (yetim satır).

**Ön koşul**
- Uygulama normal çalışıyor, SQLite aktif.

**Adımlar**
1. Bir agent kaydet, bir kez güncelle (iki sürüm satırı oluşur).
2. Agent'ın `id`'sini SQL'den al.
3. Agent'ı API'den sil.
4. Sürüm tablosunu sorgula.

**Girilecek veri**
```bash
curl -s -X POST "$APU/api/agents" -H "$APB" -H "content-type: application/json" \
  -d '{"name":"manuel-fk-test","model":{"provider":"echo","model":"echo-1"}}'
curl -s -X PUT "$APU/api/agents/manuel-fk-test" -H "$APB" -H "content-type: application/json" \
  -d '{"name":"manuel-fk-test","model":{"provider":"echo","model":"echo-1"},"description":"guncellendi"}'
```
```bash
AGENT_ID=$(sqlite3 "$SQLITEDB" "SELECT id FROM agentprism_agent_definitions WHERE name='manuel-fk-test';")
sqlite3 "$SQLITEDB" "SELECT count(*) FROM agentprism_agent_definition_versions WHERE agent_id='$AGENT_ID';"
```
```bash
curl -s -X DELETE "$APU/api/agents/manuel-fk-test" -H "$APB" -w "\nHTTP: %{http_code}\n"
```
```bash
sqlite3 "$SQLITEDB" "SELECT count(*) FROM agentprism_agent_definition_versions WHERE agent_id='$AGENT_ID';"
```

**Beklenen sonuç**
- Silme öncesi sürüm sayısı **2**'dir.
- `DELETE` isteği 2xx döner.
- Silme sonrası sürüm sayısı **0**'dır — `ON DELETE CASCADE` gerçekten
  çalışmıştır (`PRAGMA foreign_keys = ON` etkindir).

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

### MT-SQL-032 — `TablePrefix` değiştirildiğinde aynı dosyada bağımsız bir tablo seti oluşur

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 24 |
| **İlgili karar** | K-190, K-193 |

SQLite'ta indeks/tetikleyici adları veritabanı GENELİNDE tektir (şemaya göre
`scope`'lanmaz); farklı `TablePrefix` değerleri aynı fiziksel `.db` dosyasını
güvenle paylaşabilmelidir çünkü her migration dosyasındaki İNDEKS adları da
tablo önekini taşır (K-193).

**Ön koşul**
- MT-SQL-020 geçti (varsayılan `agentprism_` öneki tabloları var).

**Adımlar**
1. Öneki değiştir.
2. Uygulamayı AYNI dosyaya karşı başlat.
3. Her iki önekin de tablolarının var olduğunu doğrula.

**Girilecek veri**
```bash
dotnet user-secrets set "AgentPrism:Sqlite:TablePrefix" "ikinci_"
cd samples/AgentPrism.Api && dotnet run
```
```bash
sqlite3 "$SQLITEDB" ".tables" | tr ' ' '\n' | grep -c "^agentprism_tenants$"
sqlite3 "$SQLITEDB" ".tables" | tr ' ' '\n' | grep -c "^ikinci_tenants$"
```

**Beklenen sonuç**
- Uygulama açılır, `ikinci_` önekli 44 tabloyu oluşturur; `AgentPrism 15
  migration uyguladi.` tekrar görünür (bu, öneke göre AYRI bir migration
  defteridir — `ikinci___migrations`).
- İki sorgu da **1** döner: eski (`agentprism_`) ve yeni (`ikinci_`) tablo
  setleri aynı dosyada ÇAKIŞMADAN bir arada durur.

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

### MT-SQL-040 — Maliyet ondalık hassasiyeti kesilmeden geri döner

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 23 |
| **İlgili karar** | — |

Negatif riskin doğrulanması. Tipi verilmemiş bir `decimal` parametresini SQL
Server `decimal(18,0)` sayar ve ondalık kısmı sessizce keser;
`SqlServerDialect.AddDecimal` her zaman `Precision=20, Scale=10` yazarak bunu
önler. Bu case gerçek bir model çağrısıyla üretilen küçük bir maliyetin
tam sayıya YUVARLANMADIĞINI doğrular.

**Ön koşul**
- SQL Server aktif, OpenAI anahtarı tanımlı (`00-INDEKS.md` §2.4).

**Adımlar**
1. `support` agent'ını `FIX-PROMPT-02` ile, `FIX-SESSION-01` oturumunda çalıştır.
2. SSE akışının bitmesini bekle (çalıştırma tamamlanır).
3. Aynı agent/oturum için en son çalıştırma kaydını listeden al.
4. Aynı satırı SQL'den doğrudan oku.

**Girilecek veri**
```bash
curl -s -N -X POST "$APU/api/agents/support/run" -H "$APB" -H "content-type: application/json" \
  -d '{"message":"Merhaba","sessionId":"musteri-42"}' > /dev/null
```
```bash
curl -s "$APU/api/runs?agentName=support&sessionId=musteri-42&take=1" -H "$APB" | python3 -c \
  "import json,sys; r=json.load(sys.stdin)[0]; print(r['id']); print(r['cost'])"
```
```bash
$MSSQL -Q "SELECT input_cost, output_cost FROM agentprism.runs WHERE id = '<yukaridaki id>';"
```

**Beklenen sonuç**
- `cost.inputCost` ve `cost.outputCost` **NULL değildir ve pozitiftir**
  (`RunPricingResolver` modeli tanıyorsa).
- API yanıtındaki değer SQL'den okunan değerle **birebir eşleşir** — hiçbir
  basamak tam sayıya yuvarlanmamıştır (`decimal(20,10)` 10 ondalık basamak
  taşır).

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

### MT-SQL-041 — `SchemaName` değiştirildiğinde aynı veritabanında bağımsız bir tablo seti oluşur

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 23 |
| **İlgili karar** | K-013 |

PostgreSQL `MT-PG-026`'nın SQL Server karşılığı: `SchemaName` gerçek bir SQL
Server şemasıdır (`CREATE SCHEMA`), SQLite'ın tek ad alanını paylaşan önek
deseninden FARKLIDIR.

**Ön koşul**
- MT-SQL-024 geçti (varsayılan `agentprism` şeması var).

**Adımlar**
1. Şema adını değiştir.
2. Uygulamayı AYNI veritabanına karşı başlat.
3. İki şemanın da var olduğunu doğrula.

**Girilecek veri**
```bash
dotnet user-secrets set "AgentPrism:SqlServer:SchemaName" "ikinci"
cd samples/AgentPrism.Api && dotnet run
```
```bash
$MSSQL -Q "SELECT COUNT(*) FROM sys.tables WHERE schema_id = SCHEMA_ID('agentprism');"
$MSSQL -Q "SELECT COUNT(*) FROM sys.tables WHERE schema_id = SCHEMA_ID('ikinci');"
```

**Beklenen sonuç**
- Uygulama açılır, `ikinci` şemasını oluşturur ve 15 migration uygular.
- İki sorgu da **44** döner — eski (`agentprism`) ve yeni (`ikinci`) şemalar
  birbirinden bağımsız, tam tablo setleri taşır.

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

### MT-SQL-042 — `mcr.microsoft.com/mssql/server` bu makinede başlamazsa `azure-sql-edge` ikamesi kaydedilir

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 23 |
| **İlgili karar** | K-186, K-317, K-386 |

> **Güncelleme (2026-08-12, K-386):** Bu makinede kök sebep bulundu — kurulu
> Docker Desktop (4.29.0) host macOS için çok eskiydi, Rosetta VM'e hiç
> kurulmuyordu. `brew install --cask docker` ile 4.86.0'a güncellendikten
> sonra gerçek `mcr.microsoft.com/mssql/server:2022-latest` bu makinede
> BAŞARIYLA çalışıyor (`AgentPrism.SqlServer.IntegrationTests` 479/479).
> **Beklenen sonuç artık `mssql/server`'dır** — aşağıdaki adımlar hâlâ
> geçerlidir (Docker Desktop güncel değilse veya başka bir makinede tekrar
> `exit 133` görülürse `azure-sql-edge` ikamesine düşülür). Ayrıntı:
> `docs/hafiza/sql-server-yerel-test.md`.

Ortam gözlem case'i — bir kusur değil, bilinen bir platform kısıtının
kaydıdır. `docs/hafiza/sql-server-yerel-test.md`: Apple Silicon'da eski bir
Docker Desktop sürümünde, `useVirtualizationFrameworkRosetta` ayarı açık olsa
bile `mcr.microsoft.com/mssql/server` konteyner İÇİNDE amd64 çalıştırırken
`exit 133` ile düşebilir. `README.md`'nin "sözleşme testleri `azure-sql-edge`
ile doğrulandı" notu bu case'in kaydettiği eski DURUMU yansıtır — kod HER
ZAMAN `mssql/server`'ı hedefler (`SqlServerFixture.cs:28`), imaj yalnız yerel
doğrulamada değişir.

**Ön koşul**
- `00-INDEKS.md` §2.2'deki `docker run` komutu denenmiş.

**Adımlar**
1. `ap-mssql` container'ının sağlıklı başladığını doğrula.
2. Başlamadıysa Rosetta ayarını kontrol et, `azure-sql-edge` ile yeniden dene.
3. Sonucu kaydet.

**Girilecek veri**
```bash
docker logs ap-mssql --tail 30
docker inspect ap-mssql --format '{{.State.Status}} {{.State.ExitCode}}'
```
```bash
# Basarisizsa (exit 133 veya benzeri):
docker rm -f ap-mssql
docker run -d --name ap-mssql -p 51433:1433 \
  -e ACCEPT_EULA=Y -e MSSQL_SA_PASSWORD='AgentPrism!2026' \
  mcr.microsoft.com/azure-sql-edge:latest
```

**Beklenen sonuç**
- `mssql/server` başlarsa: bu bölümün geri kalanı GERÇEK SQL Server'a karşı
  koşar; "Gerçek sonuç" alanına `mssql/server` yazılır.
- Başlamazsa: `azure-sql-edge` ikamesiyle devam edilir; bu dosyanın SQL Server
  bölümündeki HER case'in "Gerçek sonuç" alanına **hangi imajla koşulduğu**
  açıkça yazılır. `azure-sql-edge` "gerçek SQL Server'ı kanıtlamaz" (T-SQL
  yüzeyi neredeyse özdeştir ama motor farklıdır) — bu bir bilinen sapmadır,
  kusur değildir.

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

### MT-SQL-050 — Hiçbir `Use*()` çağrılmadığında uygulama sorunsuz açılır

| | |
|---|---|
| **İzlek** | B |
| **Önem** | **Kritik** |
| **İlgili faz** | Faz 2 |
| **İlgili karar** | — |

**Ön koşul**
- Yok.

**Adımlar**
1. Üç kalıcılık sağlayıcısının da bağlantı dizesini kaldır.
2. Uygulamayı başlat.
3. `/health` ve teşhis ucunu çağır.

**Girilecek veri**
```bash
dotnet user-secrets remove "AgentPrism:PostgreSql:ConnectionString"
dotnet user-secrets remove "AgentPrism:SqlServer:ConnectionString"
dotnet user-secrets remove "AgentPrism:Sqlite:ConnectionString"
cd samples/AgentPrism.Api && dotnet run
```
```bash
curl -s -w "\nHTTP: %{http_code}\n" "http://localhost:5080/health"
curl -s "$APU/api/diagnostics" -H "$APB" | python3 -m json.tool
```

**Beklenen sonuç**
- Uygulama normal açılır — hiçbir `OptionsValidationException` görünmez
  (hiçbir `AgentPrismSqlite/SqlServer/PostgreSqlOptionsValidator` tetiklenmez,
  çünkü hiçbiri kayıtlı değildir).
- `/health` **Healthy** döner (en az bir model sağlayıcısı sağlıklıysa).
- `/api/diagnostics`: `persistenceProvider` = `"InMemory"`,
  `registeredPersistenceProviders` = **0**, `migrationsUpToDate` = `true`,
  `pendingMigrations` = `[]`.

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

### MT-SQL-051 — Bellek içi izlekte de tüm temel CRUD uçları çalışır

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 2 |
| **İlgili karar** | — |

**Ön koşul**
- MT-SQL-050 geçti, uygulama bellek içi izlekte çalışıyor.

**Adımlar**
1. Bir agent oluştur.
2. Agent'ı çalıştır.
3. Agent'ı sil.

**Girilecek veri**
```bash
curl -s -X POST "$APU/api/agents" -H "$APB" -H "content-type: application/json" \
  -d '{"name":"manuel-bellekici","model":{"provider":"echo","model":"echo-1"}}' \
  -w "\nHTTP: %{http_code}\n"
curl -s "$APU/api/agents/manuel-bellekici" -H "$APB" -w "\nHTTP: %{http_code}\n"
curl -s -X DELETE "$APU/api/agents/manuel-bellekici" -H "$APB" -w "\nHTTP: %{http_code}\n"
```

**Beklenen sonuç**
- Üç isteğin de HTTP kodu 2xx'tir — kalıcılık katmanı olmadan da
  `InMemoryAgentDefinitionStore` (veya benzeri) tam işlevseldir.

**Gerçek sonuç**
- Üç istek de 2xx döndü: `POST` **201**, `GET` **200**, `DELETE` **204**.
- Kalıcılık katmanı olmadan bellek içi depolar tam işlevsel.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-SQL-052 — Bellek içi izlekte yeniden başlatma TÜM veriyi kaybeder

| | |
|---|---|
| **İzlek** | B |
| **Önem** | **Kritik** |
| **İlgili faz** | Faz 2 |
| **İlgili karar** | — |

Negatif/tasarım gereği senaryo. Bu, bir kusur DEĞİL, bellek içi izleğin
tanımıdır — case bu sınırın belgelenmiş ve beklenen olduğunu doğrular.

**Ön koşul**
- MT-SQL-050 geçti.

**Adımlar**
1. Bir agent kaydet.
2. Kaydın var olduğunu doğrula.
3. Uygulamayı yeniden başlat.
4. Aynı kaydı tekrar sorgula.

**Girilecek veri**
```bash
curl -s -X POST "$APU/api/agents" -H "$APB" -H "content-type: application/json" \
  -d '{"name":"manuel-gecici","model":{"provider":"echo","model":"echo-1"}}'
curl -s "$APU/api/agents/manuel-gecici" -H "$APB" -w "\nHTTP: %{http_code}\n"
```
```bash
# Ctrl+C, sonra yeniden:
cd samples/AgentPrism.Api && dotnet run
```
```bash
curl -s "$APU/api/agents/manuel-gecici" -H "$APB" -w "\nHTTP: %{http_code}\n"
```

**Beklenen sonuç**
- İlk sorgu **200** döner.
- Yeniden başlatma sonrası aynı sorgu **404** döner.

**Gerçek sonuç**
- İlk açılışta `GET /api/agents/manuel-gecici` → **200**.
- Uygulama durdurulup yeniden başlatıldıktan sonra aynı sorgu → **404**.
- Veri süreçle birlikte bitti; bellek içi izleğin tanımı doğrulandı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-SQL-053 — Bellek içi izlekte konuşma dallandırma ucu 501 döner

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 47 |
| **İlgili karar** | — |

Negatif senaryo. `ConversationBranchService.BranchAsync`
(`src/AgentPrism.Core/Sessions/ConversationBranchService.cs:87`)
`IConversationBranchStore is null` denetimini oturumun VAR OLUP OLMADIĞINI
kontrol etmeden ÖNCE yapar — bellek içi izlekte bu depo hiç kayıtlı değildir
(`ConversationBranchService`'in kurucusu `IConversationBranchStore? = null`
alır). Bu yüzden case var olmayan bir oturum kimliğiyle de çalışır.

**Ön koşul**
- MT-SQL-050 geçti (bellek içi izlek aktif).

**Adımlar**
1. Herhangi bir oturum kimliğini dallandırmayı dene.

**Girilecek veri**
```bash
curl -s -X POST "$APU/api/sessions/herhangi-bir-oturum/branch" -H "$APB" \
  -H "content-type: application/json" -d '{"upToSequence":1}' \
  -w "\nHTTP: %{http_code}\n"
```

**Beklenen sonuç**
- HTTP **501** (Not Implemented) döner.
- Yanıt gövdesi "Konusma dallandirma yalnizca kalici bir SQL saglayicisi
  acikken calisir" metnini taşır — oturumun var olup olmadığı hiç
  kontrol edilmez.

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

### MT-SQL-060 — Üç sağlayıcının migration/tablo sayısı ölçümü tutarlıdır

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | 2, 23, 24 |
| **İlgili karar** | — |

Ölçüm doğrulama case'i. `00-INDEKS.md`'deki önceki bir şüphe ("migration
sayısı sağlayıcılar arasında eşit değil, eksik yetenek olabilir") bu dosyanın
üretimi sırasında kaynak kodun tamamı grep'lenerek **çürütüldü**: SQLite ve
SQL Server aynı 44 tabloyu tek, birleştirilmiş bir `0001_initial.sql`
migration'ında kurar; PostgreSQL aynı özellikleri 28 daha granüler
migration'a böler ve yalnız `document_embeddings` (pgvector) fazlasını taşır.
Bu case koşum sırasında bu ölçümü ÜÇ canlı veritabanına karşı doğrular.

**Ön koşul**
- MT-SQL-020 (SQLite) ve MT-SQL-024 (SQL Server) geçti;
  `03-KALICILIK-POSTGRESQL.md` `MT-PG-020` geçti.

**Adımlar**
1. Her üç veritabanında tablo sayısını say.
2. Tablo adı kümelerini karşılaştır.

**Girilecek veri**
```bash
sqlite3 "$SQLITEDB" "SELECT count(*) FROM sqlite_master WHERE type='table';"
$MSSQL -Q "SELECT COUNT(*) FROM sys.tables WHERE schema_id = SCHEMA_ID('agentprism');"
docker exec -i ap-pg psql -U postgres -d agentprism -t -c \
  "SELECT count(*) FROM information_schema.tables WHERE table_schema = 'agentprism';"
```

**Beklenen sonuç**
- SQLite: **44**. SQL Server: **44**. PostgreSQL: **45**.
- Fark tam olarak **1**'dir ve yalnız `document_embeddings` tablosundan gelir
  (`03-KALICILIK-POSTGRESQL.md` §4, `pgvector`'a özgü).
- Hiçbir sağlayıcıda diğerinde olmayan BAŞKA bir tablo yoktur (özellik
  eksikliği değil, granülerlik farkı doğrulanmış olur).

**Gerçek sonuç**
> _(koşum sırasında doldurulur)_

**Durum:** ☐ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-SQL-061 — Şema/önek adı doğrulama kuralı üç sağlayıcıda da birebir aynıdır

| | |
|---|---|
| **İzlek** | C |
| **Önem** | Düşük |
| **İlgili faz** | 2, 23, 24 |
| **İlgili karar** | K-029, K-179 |

Kod okuma ile doğrulanan bir sözleşme — `SqlIdentifier.IsValidUnquoted`
`AgentPrism.Sql.Shared` içinde TEK bir yerde tanımlıdır ve üç `*OptionsValidator`
sınıfı da onu çağırır. Bu case MT-SQL-002/012'nin ve `MT-PG-003`'ün AYNI kod
yoluna gittiğini teyit eder.

**Ön koşul**
- Yok.

**Adımlar**
1. Üç validator dosyasının `SqlIdentifier` çağrısını karşılaştır.

**Girilecek veri**
```bash
grep -n "SqlIdentifier.IsValidUnquoted" \
  src/AgentPrism.Sqlite/AgentPrismSqliteOptionsValidator.cs \
  src/AgentPrism.SqlServer/AgentPrismSqlServerOptionsValidator.cs \
  src/AgentPrism.PostgreSql/AgentPrismPostgreSqlOptionsValidator.cs
```

**Beklenen sonuç**
- Üç dosya da aynı statik metodu çağırır — 63 karakter sınırı, küçük harf/alt
  çizgi kuralı üç sağlayıcıda da AYNIDIR.

**Gerçek sonuç**
> _(koşum sırasında doldurulur)_

**Durum:** ☐ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-SQL-062 — `/api/diagnostics` her sağlayıcıda doğru `persistenceProvider` adını bildirir

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 33 |
| **İlgili karar** | — |

**Ön koşul**
- SQLite aktifken bir koşu, SQL Server aktifken bir koşu (sırayla).

**Adımlar**
1. SQLite aktifken teşhis ucunu çağır.
2. SQL Server aktifken tekrar çağır.

**Girilecek veri**
```bash
curl -s "$APU/api/diagnostics" -H "$APB" | python3 -c \
  "import json,sys; print(json.load(sys.stdin)['persistenceProvider'])"
```

**Beklenen sonuç**
- SQLite aktifken tam olarak `SQLite` döner.
- SQL Server aktifken tam olarak `SQL Server` döner.
- Her iki durumda da `registeredPersistenceProviders` = **1**.

**Gerçek sonuç**
> _(koşum sırasında doldurulur)_

**Durum:** ☐ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-SQL-063 — `secret` hiçbir zaman veritabanına veya dosyaya yazılmaz

| | |
|---|---|
| **İzlek** | B |
| **Önem** | **Kritik** |
| **İlgili faz** | 23, 24 |
| **İlgili karar** | K-059 |

Negatif senaryo. `03-KALICILIK-POSTGRESQL.md` `MT-PG-008`'in SQLite/SQL Server
karşılığı.

**Ön koşul**
- Uygulama normal çalışıyor (herhangi bir sağlayıcı), önceki case'lerden en az
  birkaç agent kaydedilmiş.

**Adımlar**
1. `appsettings*.json` dosyalarını bağlantı dizesi için tara.
2. Agent tanım tablosunu ve denetim izini parola alt dizgisi için tara.

**Girilecek veri**
```bash
grep -rn "Password=AgentPrism\|Password=agentprism" samples/AgentPrism.Api/appsettings*.json
```
```bash
sqlite3 "$SQLITEDB" "SELECT count(*) FROM agentprism_audit_log WHERE before LIKE '%Password=%' OR after LIKE '%Password=%';"
```
```sql
SELECT COUNT(*) FROM agentprism.audit_log WHERE before LIKE '%Password=%' OR [after] LIKE '%Password=%';
```

**Beklenen sonuç**
- `grep` sıfır satır döner.
- Her iki sorgu da **0** döner.

**Gerçek sonuç**
> _(koşum sırasında doldurulur)_

**Durum:** ☐ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

---

# 8 — Yük ve sınır durumları

### MT-SQL-070 — SQLite: 20 eşzamanlı agent kaydı veri bozulmadan tamamlanır

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 24 |
| **İlgili karar** | — |

Bu case MT-SQL-030'un yük ölçümünü tekrarlar; burada amaç SQL Server'a
paralel bir referans noktası bırakmaktır (bkz. MT-SQL-072). Zaten
MT-SQL-030'u koşmuşsan bu case'i **atla** ve sonucu buraya kopyala.

**Ön koşul**
- Uygulama normal çalışıyor, SQLite aktif.

**Adımlar**
1. 20 agent kaydını EŞ ZAMANLI gönder.
2. Kaydedilen sayıyı doğrula.

**Girilecek veri**
```bash
for i in $(seq 1 20); do
  curl -s -o /dev/null -w "%{http_code}\n" -X POST "$APU/api/agents" -H "$APB" \
    -H "content-type: application/json" -d "{
    \"name\": \"manuel-sqlite-yuk-$i\", \"model\": { \"provider\": \"echo\", \"model\": \"echo-1\" }
  }" &
done
wait
```
```bash
sqlite3 "$SQLITEDB" "SELECT count(*) FROM agentprism_agent_definitions WHERE name LIKE 'manuel-sqlite-yuk-%';"
```

**Beklenen sonuç**
- 20 HTTP kodunun tümü 2xx'tir.
- SQL sorgusu **20** döner.

**Gerçek sonuç**
> _(koşum sırasında doldurulur)_

**Durum:** ☐ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-SQL-071 — SQLite: dosya koşum sırasında salt-okunur yapılırsa yazma istekleri anlaşılır hatayla başarısız olur, uygulama çökmez

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 24 |
| **İlgili karar** | — |

Negatif/sınır senaryosu. MT-SQL-006'dan FARKLIDIR: burada uygulama zaten
çalışırken dosya izni değişir (örnek: disk salt-okunur bağlanır, izin
yönetimi bozulur) — başlangıç değil, ÇALIŞMA ANI dayanıklılığı sınanır.

**Ön koşul**
- Uygulama normal çalışıyor, SQLite aktif, migration'lar tamam.

**Adımlar**
1. Veritabanı dosyasını salt-okunur yap.
2. Yazma isteği gönder.
3. Uygulamanın hâlâ ayakta olduğunu doğrula.
4. İzni geri ver, isteği tekrarla.

**Girilecek veri**
```bash
chmod 444 "$SQLITEDB"
curl -s -o /dev/null -w "%{http_code}\n" -X POST "$APU/api/agents" -H "$APB" \
  -H "content-type: application/json" \
  -d '{"name":"manuel-salt-okunur","model":{"provider":"echo","model":"echo-1"}}'
```
```bash
curl -s -w "\nHTTP: %{http_code}\n" "http://localhost:5080/health"
```
```bash
chmod 644 "$SQLITEDB"
curl -s -o /dev/null -w "%{http_code}\n" -X POST "$APU/api/agents" -H "$APB" \
  -H "content-type: application/json" \
  -d '{"name":"manuel-salt-okunur","model":{"provider":"echo","model":"echo-1"}}'
```

**Beklenen sonuç**
- İlk yazma isteği **5xx** döner (örnek: 500), uygulama süreci ÇÖKMEZ.
- `/health` isteği bu sırada hâlâ yanıt verir (Kestrel ayaktadır; salt-okunur
  dosya yalnız YAZMA yollarını etkiler).
- İzin geri verildikten sonra aynı istek **2xx** döner.

**Gerçek sonuç**
> _(koşum sırasında doldurulur)_

**Durum:** ☐ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

> **Temizlik:** `chmod 644 "$SQLITEDB"` (adım 4'te zaten yapıldıysa gerek yok).

---

### MT-SQL-072 — SQL Server: 20 eşzamanlı agent kaydı veri bozulmadan tamamlanır

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 23 |
| **İlgili karar** | — |

`03-KALICILIK-POSTGRESQL.md` `MT-PG-060`'ın SQL Server karşılığı.

**Ön koşul**
- Uygulama normal çalışıyor, SQL Server aktif.

**Adımlar**
1. 20 agent kaydını EŞ ZAMANLI gönder.
2. Kaydedilen sayıyı doğrula.

**Girilecek veri**
```bash
for i in $(seq 1 20); do
  curl -s -o /dev/null -w "%{http_code}\n" -X POST "$APU/api/agents" -H "$APB" \
    -H "content-type: application/json" -d "{
    \"name\": \"manuel-mssql-yuk-$i\", \"model\": { \"provider\": \"echo\", \"model\": \"echo-1\" }
  }" &
done
wait
```
```bash
$MSSQL -Q "SELECT COUNT(*) FROM agentprism.agent_definitions WHERE name LIKE 'manuel-mssql-yuk-%';"
```

**Beklenen sonuç**
- 20 HTTP kodunun tümü 2xx'tir.
- SQL sorgusu **20** döner.
- Uygulama loglarında bağlantı havuzu tükenmesi hatası görünmez.

**Gerçek sonuç**
> _(koşum sırasında doldurulur)_

**Durum:** ☐ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-SQL-073 — SQL Server: container koşum sırasında durursa çalışan bir istek anlaşılır hatayla başarısız olur, uygulama çökmez

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 23 |
| **İlgili karar** | — |

Negatif/sınır senaryosu. `03-KALICILIK-POSTGRESQL.md` `MT-PG-061`'in SQL
Server karşılığı — "kopan bağlantı" simülasyonu.

**Ön koşul**
- MT-SQL-072 geçti (`manuel-mssql-yuk-1` agent'ı var).

**Adımlar**
1. Container'ı durdur.
2. Var olan bir agent'ı sorgulamayı dene.
3. Container'ı yeniden başlat, kısa süre bekle.
4. Aynı isteği tekrarla.

**Girilecek veri**
```bash
docker stop ap-mssql
curl -s -w "\nHTTP: %{http_code}\n" "$APU/api/agents/manuel-mssql-yuk-1" -H "$APB"
```
```bash
docker start ap-mssql
sleep 15
curl -s -w "\nHTTP: %{http_code}\n" "$APU/api/agents/manuel-mssql-yuk-1" -H "$APB"
```

**Beklenen sonuç**
- Container durduğunda istek **5xx** döner (bağlantı reddi/zaman aşımı),
  uygulama süreci ÇÖKMEZ, `/health` isteği (aynı sırada) `Unhealthy` döner
  ama yanıt VERİR.
- Container geri geldikten sonra aynı istek **2xx** döner — `SqlServerDataSource`
  yeni bir bağlantı kurar, süreç yeniden başlatmaya gerek duymaz.

**Gerçek sonuç**
> _(koşum sırasında doldurulur)_

**Durum:** ☐ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

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
