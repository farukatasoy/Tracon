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

> ### ⚗️ Damıtılmış koşum kaydı
> Geçen ve **hiçbir düzeltme/kusur işareti taşımayan** case'lerin
> `Gerçek sonuç` blokları düştü — bir koşumun ortam çıktısı, koşum
> bittiği anda değerini kaybeder. **Geçmeyen** ve **işaret taşıyan**
> her case'in bloğu AYNEN durur. Tam metin — kopyala, çalıştır:
>
> ```bash
> git show 45cfed58:docs/manuel-test/kosumlar/2026-09-16/04-KALICILIK-DIGER.md
> ```

---

## Temiz geçen case'ler (35)

| Case | Durum | Başlık |
|---|---|---|
| MT-SQL-003 | ☑ | `CommandTimeoutSeconds` sınırları |
| MT-SQL-004 | ☑ | `AutoApplyMigrations=false` migration uygulamaz |
| MT-SQL-006 | ☑ | Yazılamayan bir dizinde başlatma reddedilir |
| MT-SQL-007 | ☑ | Üç `UseSqlite` aşırı yüklemesi aynı sonucu üretir (izlek C) |
| MT-SQL-011 | ☑ | Şema adı `dbo` olamaz |
| MT-SQL-012 | ☑ | Geçersiz şema adı biçimleri ve enjeksiyon denemesi reddedilir |
| MT-SQL-013 | ☑ | `CommandTimeoutSeconds` sınırları |
| MT-SQL-014 | ☑ | `AutoApplyMigrations=false` migration uygulamaz |
| MT-SQL-015 | ☑ | Yanlış host ile başlatma migration adımında çöker (fail-fast) |
| MT-SQL-016 | ☑ | Üç `UseSqlServer` aşırı yüklemesi aynı sonucu üretir (izlek C) |
| MT-SQL-021 | ☑ | SQLite: yeniden başlatma migration'ları tekrar uygulamaz |
| MT-SQL-022 | ☑ | SQLite: uygulanmış migration'ın checksum'ı bozulursa başlama reddedilir |
| MT-SQL-023 | ☑ | SQLite: iki eşzamanlı örnek dosya kilidiyle çakışmadan migration uygular |
| MT-SQL-025 | ☑ | SQL Server: yeniden başlatma migration'ları tekrar uygulamaz |
| MT-SQL-026 | ☑ | SQL Server: uygulanmış migration'ın checksum'ı bozulursa başlama reddedilir |
| MT-SQL-027 | ☑ | SQL Server: iki eşzamanlı örnek `sp_getapplock` ile çakışmadan migration uygular |
| MT-SQL-030 | ☑ | WAL modu dosyaları oluşur, `busy_timeout` eşzamanlı yazmayı bekletir |
| MT-SQL-031 | ☑ | Yabancı anahtar zorlaması açıktır: agent silindiğinde sürüm satırları CASCADE ile gider |
| MT-SQL-032 | ☑ | `TablePrefix` değiştirildiğinde aynı dosyada bağımsız bir tablo seti oluşur |
| MT-SQL-040 | ☑ | Maliyet ondalık hassasiyeti kesilmeden geri döner |
| MT-SQL-041 | ☑ | `SchemaName` değiştirildiğinde aynı veritabanında bağımsız bir tablo seti oluşur |
| MT-SQL-042 | ☑ | `mcr.microsoft.com/mssql/server` bu makinede başlar |
| MT-SQL-050 | ☑ | Hiçbir `Use*()` çağrılmadığında uygulama sorunsuz açılır |
| MT-SQL-051 | ☑ | Bellek içi izlekte de tüm temel CRUD uçları çalışır |
| MT-SQL-052 | ☑ | Bellek içi izlekte yeniden başlatma TÜM veriyi kaybeder |
| MT-SQL-053 | ☑ | Bellek içi izlekte konuşma dallandırma ucu 501 döner |
| MT-SQL-061 | ☑ | Şema/önek adı doğrulama kuralı üç sağlayıcıda da birebir aynıdır (izlek C) |
| MT-SQL-062 | ☑ | `/api/diagnostics` her sağlayıcıda doğru `persistenceProvider` adını bildirir |
| MT-SQL-063 | ☑ | `secret` hiçbir zaman veritabanına veya dosyaya yazılmaz |
| MT-SQL-070 | ☑ | SQLite: 20 eşzamanlı agent kaydı veri bozulmadan tamamlanır |
| MT-SQL-072 | ☑ | SQL Server: 20 eşzamanlı agent kaydı veri bozulmadan tamamlanır |
| MT-SQL-076 | ☑ | SQL Server ve SQLite'ta dış `DataSource`: simetri, sahiplik ve çakışma |
| MT-SQL-077 | ☑ | SQL Server: `runs_v1` içindeki toplam maliyet store'un raporladığıyla eşleşir |
| MT-SQL-075 | ☑ | Boşluk kapısı: bir sorgu `string.Empty`'ye ezilirse `SqlQueryCompletenessTests` düşer |
| MT-SQL-078 | ☑ | SQLite: `{prefix}runs_v1` nokta olmadan kurulur |

## Ayrıntı taşıyan case'ler (10)

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

## MT-SQL-010 — Boş bağlantı dizesiyle başlatma reddedilir — spec düzeltildi

**Gerçek sonuç**
`MT-SQL-001` ile aynı düzeltme (spec dosyasında gerekçeli). Boş
`Tracon__SqlServer__ConnectionString` ile uygulama BAŞLADI, `/api/diagnostics`
→ `persistenceProvider: "InMemory", registeredPersistenceProviders: 0`.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-SQL-020 — SQLite: boş DB'de migration'lar sırayla uygulanır — sayı düzeltildi

**Gerçek sonuç — bu dosyanın HER "15 migration/44 tablo" referansı bayat
(gerekçe spec dosyasında, MT-SQL-020'de).** Konsol: `"Tracon applied 38
migration(s). Schema: tracon_."` `count(*)` → **38**. Ad listesi
`0001_initial`'dan `0038_run_score_evaluator_version`'a sırayla gider
(tam liste devir notunda/kanıt dosyalarında). `sqlite_master` tablo
sayısı **48**.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-SQL-024 — SQL Server: boş DB'de migration'lar sırayla uygulanır — sayı düzeltildi

**Gerçek sonuç**
`Tracon_S1` sıfırlandı. Konsol: `"Tracon applied 39 migration(s). Schema:
tracon."` — SQLite'tan (38) **1 fazla** (bu turda ölçüldü, MT-SQL-060'ta
karşılaştırılacak). `tracon.__migrations` → **39**. `sys.tables`
(`tracon` şeması) → **48** — SQLite ile AYNI tablo sayısı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-SQL-060 — Üç sağlayıcının migration/tablo sayısı ölçümü tutarlıdır — sayı düzeltildi

**Gerçek sonuç**
Güncel sayılarla (bkz. MT-SQL-020/024): SQLite **48**, SQL Server **48**,
PostgreSQL (`mt_s1`) **49**. Fark tam **1**. Tablo adı KÜMELERİ karşılaştırıldı
(`comm`): SQLite ile SQL Server BİREBİR aynı 48 ad; PostgreSQL'in tek fazlası
`document_embeddings` (pgvector'a özgü) — spec'in iddiası (granülerlik farkı
değil, tek pgvector eklentisi) sayılar değişmiş olsa da AYNEN doğrulandı.

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

**Yeniden koşum — 2026-09-19 (kapanış, §5 turu 14) · ☐ AÇIK KALIYOR**

🚨 **Case'in tetikleyicisi çalışan bir süreç üzerinde ÇALIŞMIYOR — ve sebebi
işletim sistemidir, Tracon değil.** SQLite arka uçlu bir örnek ayakta
bırakılıp dosya salt-okunur yapıldı; yazma **başarılı** oldu:

```
chmod 444 mt_sql071.db
POST /api/agents   → 201   (yazıldı)
health             → 200
```

İki mekanik sebep ölçüldü:

1. **Açık dosya tanıtıcısı.** Unix izinleri `open()` anında denetlenir, her
   `write()`'ta değil. Süreç tanıtıcıyı zaten açık tuttuğu için sonradan
   yapılan `chmod` ona ulaşmıyor.
2. **WAL modu.** Yazmalar ana dosyaya değil `-wal` dosyasına gidiyor
   (ölçüldü: `mt_sql071.db` 4 KB iken `mt_sql071.db-wal` **1,6 MB**).
   Üçünü birden (`db` · `-wal` · `-shm`) `444` yapmak da sonucu
   değiştirmedi — aynı birinci sebep.

∴ `chmod` tabanlı hiçbir yaklaşım bu koşulu üretemez. Case'in kendi
düzyazısı aslında doğru senaryoyu yazıyor — *"disk salt-okunur **bağlanır**"* —
ve bir yeniden bağlama (remount) VFS seviyesinde yazma yolunu geçersiz kılardığı
için **işe yarardı**. Bu ortamda ayrı bir disk/imaj gerekir.

Spec'in `Girilecek veri`'si bu ölçümle düzeltildi; `00-INDEKS.md` açık kalem
tablosuna yazılır.

**Durum:** ☑ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

---

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

## MT-SQL-074 — SQLite: SQL tek kaynak sonrası tablo nitelendirmesi noktasız kalır

**Gerçek sonuç — bu turda taze koşuldu (spec'in 2026-08-24 kanıtı hâlâ
geçerli, sayı büyüdü).** `dotnet test tests/Tracon.Sqlite.IntegrationTests
-c Release --no-build` (gerçek dosya veritabanına karşı): **825/825 geçti**
(Ağustos'ta 592'ydi — set büyümüş, bayat sayı değil kusur). `Table("...")`
kullanan sorgular noktasız ad üretiyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı
