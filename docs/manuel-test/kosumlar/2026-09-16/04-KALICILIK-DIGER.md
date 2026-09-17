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

Aile açılıyor — bu ilk devir notu.

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

---
