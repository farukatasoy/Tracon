# Faz 23 — SQL Server Desteği

> **Durum:** ✅ Tamam — 204/204 sözleşme testi `azure-sql-edge` (arm64) üzerinde yeşil (bkz. "Açık Kalan")
> **Kaynak:** [BEYIN-FIRTINASI.md](../BEYIN-FIRTINASI.md) · **F-06**
> **Paketler:** **`AgentPrism.SqlServer` (YENİ)** · `AgentPrism.PostgreSql` (yeniden yapılandırıldı) · `AgentPrism.Sql.Shared` (yeni, **paket değil**)
> **Migration:** Kendi migration seti — `0001_initial.sql`
> **Kararlar:** K-176 … K-189

---

> ### ⚗️ Damıtılmış kayıt
> Bu dosya fazın **planını** değil, fazın bıraktığı **kalıcı bilgiyi**
> taşır. Plan gövdesi, planlanan/gerçekleşen API, dosya listesi, risk ve
> açık soru bölümleri kapanışta düştü — **silinmedi, git geçmişindedir.**
>
> Tam metin — kopyala, çalıştır:
>
> ```bash
> git show 7f1833e:docs/arsiv/fazlar/23-SQL-SERVER.md
> ```
>
> Damıtıldı 2026-08-23 · `scripts/dokuman-bakim.py faz-damit`

---

## Ne Yapıldı

Faz 23 bir **tasarım sınavıydı**: üçüncü bir kalıcılık uygulaması eklemek kolay
olmalıydı. Sınavın sonucu, sözleşme testlerinin doğru kurulduğunu **ama**
paylaşım sınırının fazla dar çizildiğini gösterdi.

Doküman ilk hâlinde "paylaşılanlar: migration iskeleti, checksum, `SqlIdentifier`,
tanım yükleri; paylaşılmayanlar: SQL metinleri, komut yardımcıları, **depo
uygulamaları**" diyordu. Uygulama sırasında ölçülen bulgu bunu değiştirdi:

> 21 `ON CONFLICT ... RETURNING` upsert'ün **tamamı** SQL Server'da
> `UPDATE ... OUTPUT` + `IF @@ROWCOUNT = 0 INSERT ... OUTPUT` ile **aynı satır
> kümesini** döndürebiliyor.

Yani diyalekt farkı SQL metninin içinde kalıyor, C# akışına sızmıyor. Bu, depo
uygulamalarının da paylaşılabileceği anlamına geldi ve **kapsam genişletildi**
(K-176, kullanıcı kararı). Aksi hâlde ~4.000 satır depo mantığı ikinci, Faz
24'te üçüncü kez yazılacaktı.

---

## Açık Kalan — gerçek `mssql/server` hâlâ koşturulamadı (2026-08-12'de kapandı, K-386)

> **Çözüldü (2026-08-12, K-386):** Kök sebep Rosetta ayarı değil, kurulu
> Docker Desktop'ın (4.29.0) host macOS için çok eski olmasıydı. 4.86.0'a
> güncellemek gerçek `mcr.microsoft.com/mssql/server:2022-latest`'i çalışır
> hale getirdi; `AgentPrism.SqlServer.IntegrationTests` 479/479 yeşil koştu.
> Ayrıntı: `docs/hafiza/sql-server-yerel-test.md`. Aşağıdaki bölüm o günkü
> teşhisin ARŞİVİDİR.

**Geliştirme makinesinde `mcr.microsoft.com/mssql/server` hâlâ çalıştırılamıyor.**
İmaj yalnızca `linux/amd64`; makine Apple Silicon ve Docker'da amd64
emülasyonu kapalı (`rosetta error`, `alpine:amd64` bile başlamıyor).

**2026-08-05'te bu, kullanıcı onayıyla `mcr.microsoft.com/azure-sql-edge`
(arm64 native) ile aşıldı** — `SqlServerFixture` geçici olarak bu imaja
yönlendirildi, 204 sözleşme testi + diyalekt testlerinin tamamı koşturuldu,
sonra fixture gerçek `mssql/server` yapılandırmasına geri alındı (K-186).

**İlk koşu 204 testin 97'sini kırdı — üç gerçek üretim hatası bulundu ve
düzeltildi** (elle çevrilen 139 sorgu + ~500 satır DDL'nin ilk gerçek sınavıydı):

1. **K-187** — `@@ROWCOUNT` 17 sorguda `@@` önekini kaybetmişti (95 test)
2. **K-188** — `DbHelpers.ReadSingleAsync`/`ExecuteScalarAsync` iki dallı upsert
   deseninin ikinci sonuç kümesine hiç bakmıyordu (~15 test)
3. **K-189** — `SqlWebhookStore` dizi okumasında `Dialect.ReadTextArray`
   soyutlamasını atlayıp PostgreSQL'e özgü bir ADO.NET tipine bağımlıydı (8 test)

Düzeltmelerden sonra 204/204 yeşil; PostgreSQL tarafında regresyon yok
(416/416). Ayrıntı: `docs/hafiza/sql-saglayicilari.md`.

**Gerçek `mssql/server` ile doğrulama hâlâ açık.** `azure-sql-edge` T-SQL
yüzeyi neredeyse özdeş olsa da gerçek SQL Server değildir — motor farkları
(optimizer, kilitlenme davranışı, sürüm-özgü sözdizimi) kanıtlanmadı.

**Kapatmak için:** Docker Desktop → Settings → General → "Use Virtualization
framework" + "Use Rosetta for x86_64/amd64 emulation" → Apply & restart. Sonra:

```bash
dotnet test tests/AgentPrism.SqlServer.IntegrationTests -c Release
```

Alternatif: CI'yı Linux amd64 üzerinde koşturmak.

### Güncelleme (2026-08-07) — `azure-sql-edge` artık güvenilir bir yerel ikame

Yukarıdaki "Rosetta ayarını aç" tavsiyesi bu oturumda denendi ve **yeterli
değil**: ayar dosyada açık, Docker Desktop tam yeniden başlatıldı, VM içinde
Rosetta gerçekten devreye giriyor (`console.log`), ama container'ın kendi
amd64 çalıştırması yine de `exit 133` veriyor. Gerçek `mssql/server` bu
makinede hâlâ açık — ayrıntı: `docs/hafiza/sql-server-yerel-test.md`.

Bu kez `azure-sql-edge`'in Faz 25'te "artık her zaman çalışmayabilir" diye
kayda geçen arızası **teşhis edildi ve kalıcı olarak düzeltildi** (K-317):
kök sebep `Testcontainers.MsSql`'in hazır-olma denetiminin container içinde
`sqlcmd` araması, fixture'ın kendisi hiç `sqlcmd` kullanmıyor. Özel bir
`IWaitUntil` (gerçek `Microsoft.Data.SqlClient` bağlantısıyla `SELECT 1`)
bunu atlar. Bu teknikle artık **431/431** sözleşme testi (Faz 23'ten bu yana
büyüyen sözleşme paketi) `azure-sql-edge` üzerinde yeşil koştu ve dört
gerçek üretim hatası daha bulundu:

4. **K-318** — dört migration dosyasında (`0003`, `0009`, `0010`, `0011`)
   `ALTER TABLE ADD` ile eklenen bir sütun, AYNI toplu işlemdeki
   `CREATE INDEX`'te "Invalid column name" veriyordu; `EXEC(N'...')` ile
   sarıldı (CREATE SCHEMA'nın zaten kullandığı desen).
5. **K-319** — `EvalCaseResult.Scores` boşken `RawJson` `"null"` yazıyordu;
   SQL Server'ın `ISJSON` kısıtı bunu reddediyordu. `"[]"` yazılır (sütunun
   kendi `DEFAULT` değeriyle tutarlı).

PostgreSQL (844/844) ve SQLite (445/445) regresyonsuz geçti. Ayrıntı ve
tekrar dene rehberi: `docs/hafiza/sql-server-yerel-test.md`.

---

## Bitiş Ölçütleri (DoD)

- [x] `AgentPrism.SqlServer` paketi üretiliyor (`dotnet pack` sayısı arttı)
- [x] **Tüm store sözleşme testleri SQL Server üzerinde yeşil** — `azure-sql-edge` (arm64) ile 204/204 (Faz 23 kapanışında); gerçek `mssql/server` ile 479/479 (2026-08-12, K-386)
- [x] Migration'lar temiz veritabanında ve tekrar çalıştırmada doğru — `MigrationRunnerTests` `azure-sql-edge` üzerinde yeşil
- [x] Eşzamanlı iki süreçte migration bir kez uygulanıyor — `azure-sql-edge` üzerinde yeşil
- [ ] Örnek uygulama `UseSqlServer` ile uçtan uca çalışıyor — koşturulamadı (gerçek SQL Server gerektirir)
- [x] AOT durumu ölçüldü ve `MIMARI.md` bölüm 9 güncellendi (K-181)
- [x] Paket kontrol listesi tamam (README, slnx, meta paket kararı, csproj)
- [x] Dört doğrulama kapısı sıfır uyarı
- [x] PostgreSQL regresyonu yok: 416/416 entegrasyon testi, toplam 1235+ test yeşil

---

## Sonraki Faza Devir Notu

- **Faz 24 (SQLite) bu fazın kurduğu paylaşım modelini kullanır.** Model artık
  `azure-sql-edge` üzerinde 204/204 testle doğrulandı (K-186..K-189) — üçüncü
  sağlayıcı (SQLite), modelin gerçekten sağlayıcıdan bağımsız olup olmadığının
  asıl kanıtıdır. Beklenen iş: `SqliteQueries` + `SqliteDialect` + migration
  seti + test projesi. Depolara **dokunulmamalıdır**; dokunmak gerekiyorsa
  soyutlama eksiktir ve bu bir bulgudur.
- SQLite'ın kendine özgü noktaları: `uuid` yok (`BLOB`/`TEXT`), `datetimeoffset`
  yok (`TEXT` ISO-8601), eşzamanlı yazma tek yazar, `RETURNING` 3.35+ ile var,
  `sp_getapplock` karşılığı yok — dosya kilidi düşünülmeli.
- **`DbHelpers.ReadSingleAsync`/`ExecuteScalarAsync`'in çoklu-sonuç-kümesi
  düzeltmesi (K-188) SQLite'ta da geçerlidir.** SQLite tek ifadelik `INSERT ...
  ON CONFLICT ... RETURNING` (3.35+) kullanırsa PostgreSQL gibi tek kume
  üretir ve düzeltme zararsızdır; iki dallı bir desen seçilirse aynı tuzağa
  düşülebilir — `docs/hafiza/sql-saglayicilari.md`'deki tuzak notu okunmalıdır.
- **Gerçek `mssql/server` ile doğrulama hâlâ açık** (bkz. "Açık Kalan"). Bu,
  Faz 24'ü engellemez — paylaşım modeli `azure-sql-edge` ile kanıtlandı — ama
  CI'da Linux amd64 koşucusu eklenene kadar SQL Server tarafında motor-özgü
  bir fark keşfedilmemiş olabilir.
- Faz 25 (saklama) her sağlayıcı için temizleme SQL'i yazmak zorundadır;
  `IRetentionStore` sözleşmesi `SqlQueriesBase`'e yeni sorgular ekleyecektir —
  **her alt sınıfta** karşılığı yazılmalıdır.
