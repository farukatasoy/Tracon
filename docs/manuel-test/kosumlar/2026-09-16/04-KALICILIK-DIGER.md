# 04 — Kalıcılık: SQLite, SQL Server ve Bellek İçi (`SQL`) — Koşum Kaydı (2026-09-16)

> **Bu dosya bir koşum kaydıdır, spesifikasyon değildir.**
> Spesifikasyon: [`../../04-KALICILIK-DIGER.md`](../../04-KALICILIK-DIGER.md)
> — `Ön koşul`, `Adımlar`, `Beklenen sonuç` oradadır ve yeniden koşulabilir.
>
> Aşağısı yalnız **2026-09-16** koşumunun `Gerçek sonuç` ve `Durum` kayıtlarıdır.

| | |
|---|---|
| **Şerit** | `ap-s1` (Faz B — sıradaki aile: `13 · 19 · 04 · 18 · 10 · 08`) |
| **Çalışma kopyası** | `/Users/farukatasoy/Desktop/projects/ap-s1` · dal `test/kosum-s1` |
| **Kod** | `f721b229` donuk |
| **Case sayısı** | 45 (MT-SQL-001..078, seyrek numaralı) |
| **Port** | 5081 |
| **Depo** | SQLite: `samples/Tracon.Api/tracon-manuel.db` (yerel, container yok) · SQL Server: `Tracon_S1` veritabanı, `ap-mssql` container (51433) — bu oturumda oluşturuldu (yoktu) |

**Sapma — `user-secrets` yazılmaz** (skill §1.2): spec'in her `dotnet
user-secrets set/remove` adımı, tek kullanımlık bir Python başlatıcı
(`launch2.py`) ile ortam değişkenine çevrildi — sağlayıcı anahtarları
`dotnet user-secrets list --json`'dan okunup doğrudan alt sürecin ortamına
enjekte edildi, hiçbir dosyaya/loga yazılmadı. Bağlantı dizeleri zaten
secret değildir (yerel/test kimlik bilgileri).

**Ortam düzeltmesi (oturum başı):** `ap-mssql`'de `Tracon_S1` veritabanı
yoktu (önceki oturumlarda hiç kullanılmamış) — `CREATE DATABASE Tracon_S1;`
ile oluşturuldu. Paylaşılan container'a dokunulmadı (durdurma/silme yok).

---

## Devir notu

**Aile KAPANDI: MT-SQL-001..078 koşuldu (44 Geçti · 1 Beklemede, toplam
45/45 case hesaba katıldı).** Tek açık kalem: `MT-SQL-071` (SQLite'ın
çalışma-anında-salt-okunur senaryosu) — macOS'ta `Microsoft.Data.Sqlite`
bağlantı havuzunun izin/bayrak değişikliğinden ÖNCE açılmış bir dosya
tanıtıcısını koruduğu ölçüldü (üç farklı yöntemle: `chmod` yalnız ana
dosya, `chmod` üç dosya birden, `chflags uchg` üç dosya birden — hiçbiri
çalışan sürecin yazmasını engelleyemedi, oysa YENİ bir kabuk `open()`'ı
doğru şekilde reddedildi). Bu bir ürün kusuru değil, test yönteminin bu
OS/dosya sistemi kombinasyonunda senaryoyu tetikleyememesi — Linux'ta
(gerçek üretim ortamı) farklı davranabilir. Ayrıntı case'in kendi kaydında.

**Kod düzeltmesi yok, yalnız spec düzeltmeleri:**
- `MT-SQL-001`/`MT-SQL-010`: `03-KALICILIK-POSTGRESQL.md`'nin `MT-PG-001`'de
  2026-08-15'te bulduğu AYNI öncül hatası (boş bağlantı dizesi İzlek B'de
  validator'a hiç ulaşmıyor) SQLite/SQL Server karşılıklarında da vardı,
  aynı şekilde düzeltildi.
- `MT-SQL-005`: çıplak `Data Source=:memory:` artık AÇIKÇA reddediliyor
  (paylaşımlı URI biçimi gerekiyor) — `XmlDoc` zaten güncellenmiş,
  spec bayattı.
- `MT-SQL-020`/`024` (ve bağımlı `021/023/025/027/032/041/060`): "15
  migration / 44 tablo" sabit sayısı bayat — güncel: **SQLite 38 migration/
  48 tablo, SQL Server 39 migration/48 tablo, PostgreSQL 49 tablo** (fark
  hâlâ tam 1, hâlâ yalnız `document_embeddings`).
- `MT-SQL-073`: canlı koşum, `CompositeAgentCatalog`'un DB kaynağı
  çökünce kod-tanımlı agent'larla SESSİZCE devam ettiğini ortaya çıkardı
  (belgeli, kasıtlı dayanıklılık — kusur DEĞİL); yalnız `sessions`/`runs`
  gibi saf-SQL uçları case'in beklediği `5xx`'i veriyor, agent uçları
  değil. Ayrıntı case'in kendi kaydında.
- `MT-SQL-074-078`: Ağustos kapanışının kanıtı hâlâ geçerliydi, bu turda
  TAZE yeniden koşuldu (sayılar büyüdü: 592→825, 8→22, 479→806 — set
  büyümüş, kusur değil).

**Ortam notu:** `Tracon_S1` SQL Server veritabanı bu oturumda oluşturuldu
(yoktu). Paylaşılan `ap-mssql`/`ap-pg` container'larına hiç dokunulmadı;
`MT-SQL-073`'ün "container durdurma" adımı şerit-yerel bir TCP
yönlendiriciyle taklit edildi (dosya 03'ün tarifi).

**Sıradaki ailenin işi:** `18-MCP-VE-A2A.md` açılmalı (58 case).

---

## MT-SQL-001 — Boş bağlantı dizesiyle başlatma reddedilir — spec düzeltildi

**Gerçek sonuç**
`03-KALICILIK-POSTGRESQL.md`'nin `MT-PG-001`'de 2026-08-15'te bulduğu AYNI
öncül hatası burada da geçerliydi — spec düzeltildi (gerekçe spec
dosyasında). Boş `Tracon__Sqlite__ConnectionString` ile uygulama BAŞLADI:
`Now listening on: http://localhost:5081`. `GET /health` → `200 Degraded`.
`GET /api/diagnostics` → `persistenceProvider: "InMemory",
registeredPersistenceProviders: 0` — `UseSqlite()` hiç çağrılmadı
(`Program.cs:867-870`), validator'a hiç ulaşılmadı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-SQL-002 — Geçersiz `TablePrefix` biçimleri ve enjeksiyon denemesi reddedilir

**Gerçek sonuç**
Üçü de `OptionsValidationException` ile reddedildi (büyük harf, boşluk,
enjeksiyon denemesi). Mesaj İngilizce ("It must start with a lowercase
letter or underscore...") — spec'in beklediği Türkçe metin (K-228'in bilinen
bayat-beklenti örüntüsü, yeni kusur değil). 3. denemede `$SQLITEDB` dosyası
hiç OLUŞMADI (üçü de dosya oluşmadan reddedildi) — `DROP TABLE`'ın
çalışabileceği bir veritabanı bile yoktu, enjeksiyon güvenle engellendi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-SQL-003 — `CommandTimeoutSeconds` sınırları

**Gerçek sonuç**
`-1` ve `3601` ikisi de `OptionsValidationException`
("must be between 0 and 3600") ile reddedildi. `0` kabul edildi, uygulama
normal açıldı (`/api/diagnostics` 200).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-SQL-004 — `AutoApplyMigrations=false` migration uygulamaz

**Gerçek sonuç**
Uygulama açıldı, konsolda "Tracon migrations are not applied automatically
(AutoApplyMigrations is off)..." bilgi satırı (İngilizce, K-228). SQLite
dosyası oluştu ama tablo sayısı **0**. `/health` → `503 Unhealthy`.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-SQL-005 — Paylaşımlı `:memory:` desteklenir ama kalıcı değildir

**Gerçek sonuç — spec iki yerde düzeltildi (gerekçe spec dosyasında).**
(1) Çıplak `Data Source=:memory:` denendi, `OptionsValidationException` ile
REDDEDİLDİ ("this library opens a new connection for every operation...");
paylaşımlı biçime (`Data Source=file::memory:?cache=shared`) geçildi.
(2) Spec'in `echo`/`echo-1` örneği bu ortamda kayıtlı değil (gerçek
anahtarlar varken `echo` kayıtlanmıyor — bilinen kural); `openai`/
`gpt-5.4-mini` kullanıldı. Paylaşımlı biçimle: migration'lar uygulandı
("Tracon applied 38 migration(s)" — spec'in "15" beklentisi bayat, bkz.
MT-SQL-020), agent oluşturuldu (`201`), aynı bağlantı havuzunda sorgulandı
(`200`). Uygulama YENİDEN başlatılınca (yeni bir paylaşımlı `:memory:`
havuzu) aynı sorgu `404` döndü — veri süreçle birlikte bitti, tam beklendiği
gibi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-SQL-006 — Yazılamayan bir dizinde başlatma reddedilir

**Gerçek sonuç**
`chmod 555` ile salt-okunur yapılan dizine karşı: `Microsoft.Data.Sqlite
.SqliteException (0x80004005): SQLite Error 14: 'unable to open database
file'` ile süreç sonlandı, "Now listening on" satırı hiç yazılmadı
(`http_code=000` — dinleyici hiç açılmadı).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-SQL-007 — Üç `UseSqlite` aşırı yüklemesi aynı sonucu üretir (izlek C)

**Gerçek sonuç**
`UseSqlite(string)` ve `UseSqlite(IConfiguration)` ikisi de
`UseSqlite(Action<TraconSqliteOptions>)`'a delege ediyor
(`TraconSqliteBuilderExtensions.cs:28-73`) — tek kayıt yolu doğrulandı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

# 2 — SQL Server

## MT-SQL-010 — Boş bağlantı dizesiyle başlatma reddedilir — spec düzeltildi

**Gerçek sonuç**
`MT-SQL-001` ile aynı düzeltme (spec dosyasında gerekçeli). Boş
`Tracon__SqlServer__ConnectionString` ile uygulama BAŞLADI, `/api/diagnostics`
→ `persistenceProvider: "InMemory", registeredPersistenceProviders: 0`.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-SQL-011 — Şema adı `dbo` olamaz

**Gerçek sonuç**
`OptionsValidationException`: "TraconSqlServerOptions.SchemaName cannot be
'dbo'. Tracon never touches the consumer's default schema. Rationale:
docs/KARARLAR.md, decision K-013." `dbo` şemasındaki tablo sayısı denemeden
önce/sonra **0** — hiçbir tablo yazılmadı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-SQL-012 — Geçersiz şema adı biçimleri ve enjeksiyon denemesi reddedilir

**Gerçek sonuç**
Üçü de `OptionsValidationException` ile reddedildi (büyük harf, boşluk,
enjeksiyon denemesi) — mesaj İngilizce (K-228), hiçbir DDL çalışmadı (süreç
migration adımına hiç ulaşmadan düştü).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-SQL-013 — `CommandTimeoutSeconds` sınırları

**Gerçek sonuç**
`-1` ve `3601` ikisi de `OptionsValidationException`
("must be between 0 and 3600") ile reddedildi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-SQL-014 — `AutoApplyMigrations=false` migration uygulamaz

**Gerçek sonuç**
`Tracon_S1` veritabanı sıfırlandı (`DROP`+`CREATE`). Uygulama açıldı,
konsolda "Tracon migrations are not applied automatically..." bilgi
satırı. `tracon` şemasındaki tablo sayısı **0** (şema bile oluşmadı).
`/health` → `503 Unhealthy`.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-SQL-015 — Yanlış host ile başlatma migration adımında çöker (fail-fast)

**Gerçek sonuç**
Port `1`'e karşı: `Microsoft.Data.SqlClient.SqlException (0x80131904): A
network-related or instance-specific error...` zinciri, `Unhandled
exception` ile süreç sonlandı. `/health` isteği hiçbir zaman yanıt vermedi
(`HTTP: 000`, bağlantı reddi).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-SQL-016 — Üç `UseSqlServer` aşırı yüklemesi aynı sonucu üretir (izlek C)

**Gerçek sonuç**
`UseSqlServer(string)` ve `UseSqlServer(IConfiguration)` ikisi de
`UseSqlServer(Action<TraconSqlServerOptions>)`'a delege ediyor
(`TraconSqlServerBuilderExtensions.cs:29-93`).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

# 3 — Migration'lar

## MT-SQL-020 — SQLite: boş DB'de migration'lar sırayla uygulanır — sayı düzeltildi

**Gerçek sonuç — bu dosyanın HER "15 migration/44 tablo" referansı bayat
(gerekçe spec dosyasında, MT-SQL-020'de).** Konsol: `"Tracon applied 38
migration(s). Schema: tracon_."` `count(*)` → **38**. Ad listesi
`0001_initial`'dan `0038_run_score_evaluator_version`'a sırayla gider
(tam liste devir notunda/kanıt dosyalarında). `sqlite_master` tablo
sayısı **48**.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-SQL-021 — SQLite: yeniden başlatma migration'ları tekrar uygulamaz

**Gerçek sonuç**
Yeniden başlatmada konsolda migration uygulama satırı GÖRÜNMEDİ.
`count(*)` hâlâ **38** (38'in gerçek sayı olduğu MT-SQL-020'de kurulmuştur).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-SQL-022 — SQLite: uygulanmış migration'ın checksum'ı bozulursa başlama reddedilir

**Gerçek sonuç**
`Tracon.TraconException: Migration '0001_initial' has been applied to the
database but the file's content has changed. Checksum in the database:
bozuk, checksum of the file: E56F3238...` ile başlamayı reddetti.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-SQL-023 — SQLite: iki eşzamanlı örnek dosya kilidiyle çakışmadan migration uygular

**Gerçek sonuç**
İki örnek (port 5081 ve 5091) eş zamanlı başlatıldı, AYNI dosyaya karşı.
Yalnız 5081 "Tracon applied 38 migration(s)" yazdı; 5091'in logunda migration
satırı HİÇ yok (kilidi ikinci sırada aldı, 0 uyguladı). Checksum hatası ya
da çökme yok. `count(*)` tam **38** (76 değil).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-SQL-024 — SQL Server: boş DB'de migration'lar sırayla uygulanır — sayı düzeltildi

**Gerçek sonuç**
`Tracon_S1` sıfırlandı. Konsol: `"Tracon applied 39 migration(s). Schema:
tracon."` — SQLite'tan (38) **1 fazla** (bu turda ölçüldü, MT-SQL-060'ta
karşılaştırılacak). `tracon.__migrations` → **39**. `sys.tables`
(`tracon` şeması) → **48** — SQLite ile AYNI tablo sayısı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-SQL-025 — SQL Server: yeniden başlatma migration'ları tekrar uygulamaz

**Gerçek sonuç**
Yeniden başlatmada migration satırı yok, `COUNT(*)` hâlâ **39**.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-SQL-026 — SQL Server: uygulanmış migration'ın checksum'ı bozulursa başlama reddedilir

**Gerçek sonuç**
`Tracon.TraconException: Migration '0001_initial' has been applied to the
database but the file's content has changed...` ile reddedildi — SQLite ile
aynı mesaj kalıbı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-SQL-027 — SQL Server: iki eşzamanlı örnek `sp_getapplock` ile çakışmadan migration uygular

**Gerçek sonuç**
İki örnek (5081, 5091) eş zamanlı başlatıldı. Yalnız 5081 "Tracon applied
39 migration(s)" yazdı; 5091 sessizce bekledi ve 0 uyguladı. Kilit zaman
aşımı hatası yok. `COUNT(*)` tam **39** (78 değil).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

# 4 — Diyalekt ve eşzamanlılık

## MT-SQL-030 — WAL modu dosyaları oluşur, `busy_timeout` eşzamanlı yazmayı bekletir

**Gerçek sonuç**
`-wal`/`-shm` dosyaları oluştu. 20 eş zamanlı `POST /api/agents` isteğinin
20'si de `201`. `SELECT count(*)` → **20**. Loglarda `SQLITE_BUSY`/
`database is locked` **sıfır** eşleşme.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-SQL-031 — Yabancı anahtar zorlaması açıktır: agent silindiğinde sürüm satırları CASCADE ile gider

**Gerçek sonuç**
Silme öncesi sürüm sayısı **2**. `DELETE` → `204`. Silme sonrası sürüm
sayısı **0** — `PRAGMA foreign_keys = ON` etkin, CASCADE çalıştı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-SQL-032 — `TablePrefix` değiştirildiğinde aynı dosyada bağımsız bir tablo seti oluşur

**Gerçek sonuç**
`TablePrefix=ikinci_` ile açıldı: "Tracon applied 38 migration(s). Schema:
ikinci_." — 38, MT-SQL-020'de kurulan güncel sayı. Hem `tracon_tenants`
hem `ikinci_tenants` sorguları **1** döndü — iki önek aynı dosyada
çakışmadan bir arada.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

# 5 — SQL Server'a özgü

## MT-SQL-040 — Maliyet ondalık hassasiyeti kesilmeden geri döner

**Gerçek sonuç — ortam eksiği tamamlandı.** İlk deneme `cost` alanının TÜM
alt alanlarını `null` verdi — kök neden `appsettings.json`'ın `Pricing`
bloğunda `openai`/`gpt-5.4-mini` için HİÇ fiyat tanımlı DEĞİL (yalnız Voice
ve Images fiyatları var) — bu, case'in kendi koşullu dalı ("RunPricingResolver
modeli tanıyorsa") zaten öngörüyor. `00-INDEKS.md`/`12-GOZLEMLENEBILIRLIK-
MALIYET.md`'nin kurduğu yerleşik desen kullanıldı:
`Tracon:Pricing:openai:gpt-5.4-mini:Input=0.15` ve `:Output=0.60` (env
değişkeni, `user-secrets`'a yazılmadı). Yeniden koşulunca: `cost.inputCost
= 5.22e-05`, `cost.outputCost = 1.2e-05`, ikisi de pozitif ve NULL DEĞİL.
SQL'den doğrudan okunan değer BİREBİR aynı: `input_cost=.0000522000,
output_cost=.0000120000` — hiçbir basamak tam sayıya yuvarlanmadı
(`decimal(20,10)` doğrulandı).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-SQL-041 — `SchemaName` değiştirildiğinde aynı veritabanında bağımsız bir tablo seti oluşur

**Gerçek sonuç**
`SchemaName=ikinci` ile açıldı: "Tracon applied 39 migration(s). Schema:
ikinci." Hem `tracon` hem `ikinci` şemalarında **48** tablo (MT-SQL-024'te
kurulan güncel sayı, spec'in "44"ü değil) — iki şema tam bağımsız, birbirinden
etkilenmedi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-SQL-042 — `mcr.microsoft.com/mssql/server` bu makinede başlar

**Gerçek sonuç**
`docker inspect ap-mssql` → `mcr.microsoft.com/mssql/server:2022-latest`,
`Status: running, ExitCode: 0` — K-386'nın güncellemesi doğrulandı, GERÇEK
`mssql/server` bu makinede sorunsuz çalışıyor. Bu dosyanın SQL Server
bölümündeki (`010`–`041`) HER case zaten bu gerçek imaja karşı koşuldu —
`azure-sql-edge` ikamesine hiç gerek kalmadı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

# 6 — Bellek içi izlek

## MT-SQL-050 — Hiçbir `Use*()` çağrılmadığında uygulama sorunsuz açılır

**Gerçek sonuç**
Üç sağlayıcının da bağlantı dizesi boş. `/health` → `200 Degraded` (model
sağlayıcı nedeniyle). `/api/diagnostics` →
`persistenceProvider:"InMemory", registeredPersistenceProviders:0,
migrationsUpToDate:true, pendingMigrations:[]`.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-SQL-051 — Bellek içi izlekte de tüm temel CRUD uçları çalışır

**Gerçek sonuç**
Oluştur `201`, oku `200`, sil `204` — üçü de 2xx.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-SQL-052 — Bellek içi izlekte yeniden başlatma TÜM veriyi kaybeder

**Gerçek sonuç**
İlk sorgu `200`. Uygulama yeniden başlatıldıktan sonra aynı sorgu `404`.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-SQL-053 — Bellek içi izlekte konuşma dallandırma ucu 501 döner

**Gerçek sonuç**
Var olmayan bir oturum kimliğiyle bile `501 "Branching not supported...
only works when a persistent SQL provider is enabled..."` — oturumun var
olup olmadığı hiç kontrol edilmedi (İngilizce metin, K-228).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

# 7 — Üç sağlayıcı arası tutarlılık

## MT-SQL-060 — Üç sağlayıcının migration/tablo sayısı ölçümü tutarlıdır — sayı düzeltildi

**Gerçek sonuç**
Güncel sayılarla (bkz. MT-SQL-020/024): SQLite **48**, SQL Server **48**,
PostgreSQL (`mt_s1`) **49**. Fark tam **1**. Tablo adı KÜMELERİ karşılaştırıldı
(`comm`): SQLite ile SQL Server BİREBİR aynı 48 ad; PostgreSQL'in tek fazlası
`document_embeddings` (pgvector'a özgü) — spec'in iddiası (granülerlik farkı
değil, tek pgvector eklentisi) sayılar değişmiş olsa da AYNEN doğrulandı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-SQL-061 — Şema/önek adı doğrulama kuralı üç sağlayıcıda da birebir aynıdır (izlek C)

**Gerçek sonuç**
Üç validator dosyası da `SqlIdentifier.IsValidUnquoted` çağırıyor
(`TraconSqlServerOptionsValidator.cs:37`, `TraconSqliteOptionsValidator.cs:51`,
`TraconPostgreSqlOptionsValidator.cs:39`) — aynı statik metot, tek kaynak.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-SQL-062 — `/api/diagnostics` her sağlayıcıda doğru `persistenceProvider` adını bildirir

**Gerçek sonuç**
SQLite aktifken → `"SQLite"`. SQL Server aktifken → `"SQL Server"`. Her
ikisinde de `registeredPersistenceProviders: 1`.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-SQL-063 — `secret` hiçbir zaman veritabanına veya dosyaya yazılmaz

**Gerçek sonuç**
`appsettings*.json`'da `Password=` alt dizgisi **sıfır** eşleşme.
SQLite `tracon_audit_log` ve SQL Server `tracon.audit_log`'da
`before`/`after` alanlarında `Password=` araması ikisinde de **0**.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

# 8 — Yük ve dayanıklılık

## MT-SQL-070 — SQLite: 20 eşzamanlı agent kaydı veri bozulmadan tamamlanır

**Gerçek sonuç**
Spec'in kendi notu gereği MT-SQL-030 ile aynı ölçüm — sonuç oradan
kopyalandı: 20/20 `2xx`, `count(*)=20`, `SQLITE_BUSY` yok.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-SQL-071 — SQLite: çalışma anında salt-okunur yapılırsa yazma istekleri anlaşılır hatayla başarısız olur

**Gerçek sonuç — SONUÇSUZ, ortam sınırı (kod kusuru DEĞİL).** Üç deneme
yapıldı: (1) yalnız ana `.db` dosyası `chmod 444`, (2) `.db`+`.db-wal`+
`.db-shm` üçü birden `chmod 444`, (3) üçü birden `chflags uchg`
(değiştirilemez bayrak — açık dosya tanıtıcılarında bile POSIX'te
zorlanması beklenir). ÜÇÜNDE DE yazma isteği `201` ile BAŞARILI oldu —
uygulama izin/bayrak değişikliğini hiç fark etmedi. Doğrudan bir kabuk
`echo >> .db-wal` denemesi AYNI `chflags uchg` altında doğru şekilde
`operation not permitted` verdi — bayrak gerçekten uygulanmıştı. Bu, macOS
üzerinde `Microsoft.Data.Sqlite`'ın bağlantı havuzunun izin DEĞİŞİKLİĞİNDEN
ÖNCE açılmış bir dosya tanıtıcısını yeniden kullandığını gösteriyor —
POSIX izin/bayrak denetimi yalnız YENİ `open()` çağrılarında uygulanır,
zaten açık bir tanıtıcının sonraki `write()` çağrılarını etkilemez. Bu
case'in senaryosunu (çalışma ANINDA izin kaybı) bu OS/dosya sistemi
kombinasyonunda, süreci yeniden başlatmadan tetiklemenin bir yolu
bulunamadı — case'in kendisi MT-SQL-006'dan (başlangıçta reddetme, farklı
ve zaten doğrulanmış bir yol) kasıtlı olarak ayrılıyor. Linux'ta (gerçek
üretim/CI ortamı) farklı davranabilir; bu yalnız macOS'a özgü bir test
yöntemi sınırlamasıdır.

**Durum:** ☑ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-SQL-072 — SQL Server: 20 eşzamanlı agent kaydı veri bozulmadan tamamlanır

**Gerçek sonuç**
20/20 `201`. `COUNT(*)` → **20**. Loglarda bağlantı havuzu tükenmesi hatası
**sıfır**.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-SQL-073 — SQL Server: container koşum sırasında durursa çalışan bir istek anlaşılır hatayla başarısız olur

**Gerçek sonuç — paylaşılan container'a DOKUNULMADI, şerit-yerel TCP
yönlendirici kullanıldı** (dosya 03'ün MT-PG-050/061 tarifi): uygulama
`localhost:51533`'e bağlandı, küçük bir Python yönlendirici bunu gerçek
`ap-mssql:51433`'e aktardı. Yönlendirici öldürülünce:
- `GET /api/diagnostics` → `canConnect:false`.
- `GET /api/sessions` → **`500`** — case'in beklediği tam senaryo bu.
- `/health` aynı anda **`503 Unhealthy`** ama YANIT VERDİ — süreç ÇÖKMEDİ.
- 🚨 **Ek gözlem (kusur DEĞİL, kaynakla doğrulandı):** `GET
  /api/agents/{ad}` ve `GET /api/agents` DB kapalıyken `500` yerine
  `404`/`200` döndü (kod-tanımlı agent'lar hâlâ görünür kaldı, DB'li
  agent'lar sessizce KAYBOLDU). Kök neden: `CompositeAgentCatalog.cs:54-65`
  her `IAgentSource`'u ayrı `try/catch` içinde çağırıyor, biri (`DB
  kaynağı`) patlarsa `RecordSourceFailure` ile loglayıp DİĞER kaynaklarla
  (kod-tanımlı agent'lar) devam ediyor — bilinçli, belgeli bir dayanıklılık
  tasarımı ("sources are tried... the losing source is omitted"). Bu
  yalnız agent kataloğuna özgü (kod-tanımlı bir yedek kaynağı OLAN tek
  şey); `sessions`/`runs` gibi salt-SQL uçları böyle bir yedeğe sahip
  DEĞİL ve doğru şekilde `500` veriyor.

Yönlendirici geri getirilince (uygulama YENİDEN BAŞLATILMADAN): `GET
/api/sessions` → `200`, `/health` → `200`. `SqlServerDataSource` yeni bir
bağlantı kurdu, süreç yeniden başlatmaya gerek duymadı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

# 9 — Kaynak-doğrulanan sözleşmeler (izlek C, bu turda taze koşuldu)

## MT-SQL-076 — SQL Server ve SQLite'ta dış `DataSource`: simetri, sahiplik ve çakışma

**Gerçek sonuç — bu turda taze koşuldu.** `dotnet test
tests/Tracon.SqlServer.IntegrationTests -c Release --no-build` (kendi
izole testcontainer'ına karşı, `ap-mssql`'e hiç dokunmadı): **806/806
geçti** (Ağustos'ta 479'du). `ExternalDataSourceTests.cs` hem
`Tracon.SqlServer.IntegrationTests` hem `Tracon.Sqlite.IntegrationTests`
(MT-SQL-074/078'in 825/825'i, aynı koşum) içinde mevcut — dört senaryo da
(dış kaynakla doğrulama geçer + `OwnsDataSource=false`, iki alan birden
verilince `OptionsValidationException`, dispose sonrası `ObjectDisposedException`
YOK, kendi kurduğu kaynakta `OwnsDataSource=true`) kapsanıyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-SQL-077 — SQL Server: `runs_v1` içindeki toplam maliyet store'un raporladığıyla eşleşir

**Gerçek sonuç — bu turda taze koşuldu.** `ReadViewContractTests.cs`
`tests/Tracon.SqlServer.IntegrationTests/`'te mevcut, aynı 806/806 geçen
koşumun parçası (kendi izole testcontainer'ına karşı, gerçek `CREATE OR
ALTER VIEW` + `EXEC(N'...')` sarmalaması dahil).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-SQL-074 — SQLite: SQL tek kaynak sonrası tablo nitelendirmesi noktasız kalır

**Gerçek sonuç — bu turda taze koşuldu (spec'in 2026-08-24 kanıtı hâlâ
geçerli, sayı büyüdü).** `dotnet test tests/Tracon.Sqlite.IntegrationTests
-c Release --no-build` (gerçek dosya veritabanına karşı): **825/825 geçti**
(Ağustos'ta 592'ydi — set büyümüş, bayat sayı değil kusur). `Table("...")`
kullanan sorgular noktasız ad üretiyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-SQL-075 — Boşluk kapısı: bir sorgu `string.Empty`'ye ezilirse `SqlQueryCompletenessTests` düşer

**Gerçek sonuç — bu turda taze koşuldu.** `dotnet test
tests/Tracon.Sql.Shared.UnitTests -c Release --no-build`: **22/22 geçti**
(Ağustos'ta 8'di). Kod donuk olduğu için "bir sorguyu elle boz" adımı bu
turda TEKRARLANMADI (Ağustos'ta zaten ampirik olarak kanıtlanmıştı: `["UpsertMcpServer"]`
ile düştü, geri alınca yeşil) — yalnız GEÇERLİ hâlin hâlâ yeşil olduğu
doğrulandı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-SQL-078 — SQLite: `{prefix}runs_v1` nokta olmadan kurulur

**Gerçek sonuç — bu turda taze koşuldu.** `ReadViewContractTests.cs`
`tests/Tracon.Sqlite.IntegrationTests/` içinde mevcut ve MT-SQL-074 ile
AYNI 825/825 geçen koşumun parçası (gerçek dosya veritabanına karşı).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## Fiziksel eylem / ortam sınırı nedeniyle sonuçsuz kalan case

| Case | Neden | Kullanıcıdan istenen / kapanışta yapılacak |
|---|---|---|
| MT-SQL-071 | macOS'ta bağlantı havuzu, izin değişikliğinden önce açılmış bir dosya tanıtıcısını koruyor — üç farklı yöntem (chmod tek dosya, chmod üç dosya, chflags uchg) çalışan sürecin yazmasını engelleyemedi | Linux'ta (gerçek üretim/CI) tekrar denenmeli; macOS'ta yalnız süreç YENİDEN BAŞLATILDIKTAN sonra izin testi anlamlı olur ama bu MT-SQL-006 ile örtüşür |
