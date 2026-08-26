# Faz 111 — Okuma Sözleşmesi Görünümleri

> **Durum:** ✅ Tamamlandı (2026-08-26)
> **Kaynak:** [`kesif/2026-08-26-ef-core-uyum-olcumu.md`](kesif/2026-08-26-ef-core-uyum-olcumu.md) — kalem **P4**. Bu faz bir `F-NN` adayından gelmez
> **Önkoşul:** [Faz 110](arsiv/fazlar/110-TUKETICI-BAGLANTI-DUZLEMI.md) — `docs-site/.../guides/ef-core.md` sayfası orada doğar; bu faz ona okuma bölümünü ekler. Kod bağımlılığı yoktur
> **Paketler:** `AgentPrism.PostgreSql`, `.SqlServer`, `.Sqlite` — yalnız migration setleri
> **Yeni paket:** Yok · **Migration:** **Gerekli — üç set** (PostgreSQL + SQL Server + SQLite). Numaralar sağlayıcı başına bağımsızdır (K-178) ve uygulama anında alınır
> **Public API:** C# yüzeyi yalnız üç `EnableReadViews` bool alanıyla büyüdü (üç `Options` sınıfına birer tane — bkz. "Gerçekleşen Public API"). Asıl taahhüt **kalıcı bir veri sözleşmesidir**: bir görünüm yayımlandıktan sonra sütun kaybetmez. Bu, `Shipped.txt`'in kapsamadığı bir taahhüttür ve geri alması pahalıdır
> **Tüketici yüzeyi:** Site — yeni referans sayfası `docs-site/src/content/docs/reference/read-views.md` · değişen: `guides/ef-core.md`, `packages.md` · Sevk edilen: üç sağlayıcı `README.md`'si
> **Manuel test alanı:** [`manuel-test/03-KALICILIK-POSTGRESQL.md`](manuel-test/03-KALICILIK-POSTGRESQL.md) · [`manuel-test/04-KALICILIK-DIGER.md`](manuel-test/04-KALICILIK-DIGER.md)

---

## Bu Faza Başlarken

> `faz-baslangic` skill'ini uygula. Aşağıdaki liste o skill'in 2. adımıdır —
> **tamamını değil, yalnız işaret edilen bölümleri oku.**

1. Bu doküman
2. Kararlar — dosyanın tamamını **okuma**, yalnız bu kalemleri grep'le:
   ```bash
   grep -n "K-013\|K-178\|K-190\|K-483" docs/KARARLAR.md
   ```
   **K-013** (ayrı `agentprism` şeması), **K-178** (migration numaraları
   sağlayıcı başına bağımsız), **K-190** (SQLite'ta şema yerine tablo öneki),
   **K-483** (🚨 elle tekrarlanan toplama ifadesi kusur **sınıfı** üretir — bu
   fazın ana risk kaydı)
3. [Faz 110](arsiv/fazlar/110-TUKETICI-BAGLANTI-DUZLEMI.md) — yalnız devir notu:
   ```bash
   awk '/## Sonraki Faza Devir Notu/,0' docs/110-TUKETICI-BAGLANTI-DUZLEMI.md
   ```
   Site sayfasının hangi bölümleri doldurulmuş, hangi yer boş bırakılmış
4. Alan hafızası (bu faz üç alana dokunuyor):
   [`hafiza/sql-saglayicilari.md`](hafiza/sql-saglayicilari.md) (üç dialekt) ·
   [`hafiza/sqlite.md`](hafiza/sqlite.md) (tip afinitesi tuzakları) ·
   [`hafiza/olcum-kota-ve-secenekler.md`](hafiza/olcum-kota-ve-secenekler.md)
   (K-483'ün vaka kaydı — maliyet toplamı)
5. Gerektiğinde, tamamı değil ilgili bölümü:
   [`MIMARI-GUVENLIK.md`](MIMARI-GUVENLIK.md) — kiracı sınırı bölümü

---

## Amaç

Tüketici kendi raporunda koşu maliyetini kendi entity'sinin yanında göstermek
ister. Bugün iki yol var, ikisi de kötü: HTTP'den sayfa sayfa çekip bellekte
join, ya da iç tablolara doğrudan bağlanıp bir sonraki sürümde kırılmayı göze
alma. Faz, üçüncü yolu açar: **sürümlü, dar, salt-okunur bir görünüm
sözleşmesi.**

Görünüm yalnız ergonomi değildir. Bir kusur **sınıfını** tüketici için
imkânsız kılar:
[`0034_run_attribution.sql:36-40`](../src/AgentPrism.PostgreSql/Migrations/0034_run_attribution.sql)
`cached_input_cost`'un bir koşu toplamının **üçüncü terimi** olduğunu yazıyor.
`runs` tablosuna doğrudan bağlanan tüketici `input_cost + output_cost` yazar ve
**eksik hesaplar**. Aynı kusur K-483'te bizim içimizde doğdu ve 4241 test
yakalamadı. Görünüm toplamı bir kez, doğru, tek yerde hesaplar.

- **P4** — `runs` için sürümlü okuma görünümü, üç sağlayıcıda, EF keyless
  entity dokümanıyla birlikte

### Bugün ne çalışmıyor — doğrulanmış kanıt

| Kanıt | Gözlem |
|---|---|
| `grep -rli "CREATE VIEW" src/*/Migrations/*.sql` → **boş** | Hiçbir sağlayıcıda görünüm yok. Tüketicinin bağlanabileceği tek şey iç tablolardır |
| [`0034_run_attribution.sql:36-40`](../src/AgentPrism.PostgreSql/Migrations/0034_run_attribution.sql) | `cached_input_cost` **üçüncü terimdir**; `input_cost`'un alt kümesi değildir. Yorum satırı bunu açıkça uyarıyor |
| [`0001_initial.sql:163-166`](../src/AgentPrism.Sqlite/Migrations/0001_initial.sql) · [`0021_run_attribution.sql:16`](../src/AgentPrism.Sqlite/Migrations/0021_run_attribution.sql) | 🚨 SQLite'ta `input_cost`/`output_cost` **`TEXT`**, `cached_input_cost` **`NUMERIC`**. Aynı toplamda karışık afinite var |
| [`ProtectedColumn.cs:9-44`](../src/AgentPrism.Abstractions/Security/ProtectedColumn.cs) | On içerik sütunu at-rest korunuyor. Hiçbiri `runs` tablosunun üstverisinde değil — görünüm sınırı bu listeden türer |
| [`0001_initial.sql:133-148`](../src/AgentPrism.PostgreSql/Migrations/0001_initial.sql) | `runs` üstverisi (kimlik, kiracı, agent, durum, zaman, token) düz metindir; korunan sütun içermez |

> Kanıtlar 2026-08-26 tarihinde doğrulandı.

---

## 111.1 — Sözleşme ne, ne değil

| Kural | İçerik |
|---|---|
| **Adında sürüm vardır** | `runs_v1`. Sürüm son ekine sahip olmayan görünüm yayımlanmaz |
| **Yalnız eklenir** | Yayımlanmış bir `v_1` görünümü sütun **kaybetmez**, sütun **yeniden adlandırmaz**, sütun tipini **daraltmaz**. Yeni sütun eklemek serbesttir |
| **Kırıcı değişiklik yeni sürümdür** | `runs_v2` doğar; `runs_v1` en az bir major sürüm boyunca yaşar |
| **Yalnız üstveri ve türev** | Görünüme hiçbir `ProtectedColumn` sütunu girmez. Konuşma içeriği, tool argümanı, dosya içeriği **hiçbir sürümde** görünüme girmez |
| **🚨 Güvenlik sınırı değildir** | Görünüm kiracı **filtrelemez**; `tenant_id` sütununu taşır ve filtreleme tüketicinin sorgusundadır. Bu, dokümanda uyarı kutusudur |
| **Salt okunur** | Görünüm üzerinden yazma desteklenmez. Yazma yolu store arayüzleridir |

Sözleşmenin dışında kalanlar da yazılır: `run_events`, `tool_invocations`,
`sessions` ve `attachments` **görünüm almaz**. Gerekçe: içerikleri korunan
sütunlardır ve okuma yolları HTTP API'dir.

## 111.2 — `runs_v1` görünümü

Tek görünümle başlanır. İkinci bir görünüm (günlük toplam gibi) v1'e
girmez — tüketici bunu kendi LINQ'unda toplar.

| Sütun | Kaynak | Not |
|---|---|---|
| `run_id` | `runs.id` | |
| `tenant_id` | `runs.tenant_id` | 🚨 Filtreleme tüketicinin işidir |
| `agent_name` | `runs.agent_name` | |
| `session_id` | `runs.session_id` | `NULL` olabilir |
| `status` | `runs.status` | Sayısal; enum değerleri kararlıdır (`0001_initial.sql` başlık yorumu) |
| `status_name` | türev | Metin karşılık — tüketici eşlemeyi elle yazmasın diye |
| `started_at` · `completed_at` | `runs` | UTC |
| `is_streaming` | `runs` | |
| `input_tokens` · `output_tokens` · `cached_input_tokens` · `reasoning_tokens` · `total_tokens` | `runs` | 🚨 `input_tokens` ile `cached_input_tokens` toplanmaz — `0034` yorumu çift sayımı uyarıyor |
| `input_cost` · `output_cost` · `cached_input_cost` | `runs` | Ham terimler |
| **`total_cost`** | türev | **111.3** |
| `cost_currency` | `runs` | `total_cost` doluysa dolu |
| `error_type` | `runs` | Sınıflandırılmış tip |

Üç sağlayıcı farkı:

| Sağlayıcı | Ad | İdempotent kurulum |
|---|---|---|
| PostgreSQL | `{schema}.runs_v1` | `CREATE OR REPLACE VIEW` |
| SQL Server | `{schema}.runs_v1` | `CREATE OR ALTER VIEW` |
| SQLite | `{schema}runs_v1` — tablo öneki, nokta yok (K-190) | `DROP VIEW IF EXISTS` + `CREATE VIEW` |

## 111.3 — 🚨 Toplam maliyet: tek anlam, üç dialekt

Bu bölüm fazın en yüksek riskli parçasıdır. Üç kural bağlayıcıdır:

**1. Terim listesi tek yerden türetilir.** `total_cost`, `runs` tablosundaki
**her** `*_cost` sütununu toplar. Bugün üçtür: `input_cost`, `output_cost`,
`cached_input_cost`. Dördüncüsü eklenirse görünüm onunla birlikte
güncellenir — ve bunu unutmayı **imkânsız** kılan bir kapı testi yazılır
(111.5).

**2. `NULL` sıfır değildir.** Fiyat tanımsızsa `NULL` yazılır, `0` değil
(`0011_run_costs.sql` başlık yorumu). Toplam bu anlamı korur: üç terimin
tamamı `NULL` ise `total_cost` **`NULL`**'dur; en az biri doluysa dolu
terimler toplanır.

**3. 🚨 SQLite'ta afinite karışıktır.** `input_cost`/`output_cost` `TEXT`,
`cached_input_cost` `NUMERIC`. Açık `CAST` olmadan toplama sessizce yanlış
sonuç verir — sayı olmayan metin `0` sayılır. SQLite görünümü her terimi açıkça
dönüştürür.

**Doğrulama:** görünümün `total_cost` değeri, aynı koşu için store'un
döndürdüğü `RunStatistics.TotalCost` ile **birebir** eşleşmelidir. İkisinin
ayrışması bu fazın ana kusurudur ve testi üç sağlayıcıda birden koşar.

## 111.4 — EF tarafında kullanım

Referans sayfası tüketiciye keyless entity eşlemesini verir:

```csharp
modelBuilder.Entity<AgentPrismRun>()
    .HasNoKey()
    .ToView("runs_v1", "agentprism");
```

Sayfada üç uyarı yer alır:

- `.ToView(...)` eşlemesi tüketicinin `dotnet ef migrations add` çıktısına
  **girmez** — görünümü AgentPrism'in migration'ı kurar, tüketicinin migration'ı
  değil. Bu, iki migration hattının çakışmamasının nedenidir
- Kiracı filtresi tüketicinin sorgusundadır; görünüm filtrelemez
- Saklama politikası satır silerse görünüm satırı da kaybolur; rapor geçmişi
  saklama penceresiyle sınırlıdır

## 111.5 — Kapı: sözleşme kendini korusun

İki test kapısı yazılır. İkisi de bu fazın değil, **sonraki fazların** kusurunu
yakalar. İkisi de `tests/AgentPrism.Sql.Shared.UnitTests` içine girer: o proje
üç sağlayıcıyı birden referans eder ve gömülü SQL metnini **bağlantı açmadan**
okur — kapılar veritabanı gerektirmez:

| Kapı | Ne yapar |
|---|---|
| **Sütun kümesi kapısı** | Görünümün sütun listesi repo'daki beklenen listeyle karşılaştırılır. Bir sütun kaybolursa veya adı değişirse test kırmızıdır. Yeni sütun eklemek testi kırmaz — sözleşme eklemeye açıktır |
| **Toplam terim kapısı** | `runs` tablosunun `*_cost` desenine uyan sütunları taranır; görünümün `total_cost` ifadesi hepsini içermiyorsa test kırmızıdır. K-483'ün sınıf taraması, bir daha elle yapılmasın diye otomatik |

Ayrıca korunan sütun kapısı: görünüm sütun kümesi ile `ProtectedColumn`
sütunlarının kesişimi **boş** olmalıdır.

---

## Planlanan Public API

C# public yüzeyi büyümüyor. Yeni tip, yeni metot, yeni HTTP ucu yoktur.

**Veri sözleşmesi** (yeni ve kalıcı):

```sql
-- PostgreSQL / SQL Server
{schema}.runs_v1
-- SQLite
{schema}runs_v1
```

### HTTP `endpoint`'leri

Yok.

### Arayüz payı

Yok.

---

## Planlanan Dosya Listesi

```
src/AgentPrism.PostgreSql/Migrations/NNNN_read_views.sql
src/AgentPrism.SqlServer/Migrations/NNNN_read_views.sql
src/AgentPrism.Sqlite/Migrations/NNNN_read_views.sql

tests/AgentPrism.Sql.Shared.UnitTests/
├── ReadViewColumnSetTests.cs      (sütun kümesi + korunan sütun kapıları)
└── ReadViewCostTermTests.cs       (toplam terim kapısı — SQL metni üzerinden)
tests/AgentPrism.PostgreSql.IntegrationTests/ReadViewContractTests.cs
tests/AgentPrism.SqlServer.IntegrationTests/ReadViewContractTests.cs
tests/AgentPrism.Sqlite.IntegrationTests/ReadViewContractTests.cs

docs-site/src/content/docs/reference/read-views.md   (yeni)
docs-site/src/content/docs/guides/ef-core.md         (okuma bölümü)
src/AgentPrism.PostgreSql/README.md · .SqlServer · .Sqlite
```

---

## Hata Modları ve Testler

> Mutlu yoldan değil, **ne bozulabilir**den türetilir. Seviyeyi plan seçer.
> Sınır geçen davranış (DI · HTTP · kiracı · akış · depo · paket) birim
> testiyle kanıtlanamaz — [`.agents/ortak/test-seviyeleri.md`](../.agents/ortak/test-seviyeleri.md).

| Ne bozulabilir | Seviye | Test sınıfı |
|---|---|---|
| 🚨 SQLite'ta `TEXT` + `NUMERIC` toplamı sessizce yanlış çıkar | Fonksiyonel (üç sağlayıcı) | `ReadViewContractTests` |
| 🚨 Görünüm toplamı ile `RunStatistics.TotalCost` ayrışır | Fonksiyonel | `ReadViewContractTests` |
| 🚨 Sonraki faz dördüncü bir `*_cost` sütunu ekler, görünüm güncellenmez | Kapı (metin, bağlantısız) | `ReadViewCostTermTests` |
| Sonraki faz görünümden sütun düşürür veya adını değiştirir | Kapı (metin, bağlantısız) | `ReadViewColumnSetTests` |
| Görünüme korunan bir içerik sütunu girer | Kapı (metin, bağlantısız) | `ReadViewColumnSetTests` |
| Tüm maliyet terimleri `NULL` iken `total_cost` `0` döner (tanımsız fiyat, sıfır fiyata dönüşür) | Fonksiyonel | `ReadViewContractTests` |
| Migration ikinci kez koşunca görünüm kurulumu çöker | Fonksiyonel | `MigrationTests` (mevcut, idempotency zaten kapsanıyor) |
| SQLite'ta tablo öneki görünüm adına uygulanmaz; `{schema}` yer tutucusu kaçırılır | Fonksiyonel | `ReadViewContractTests` |
| Başka kiracının satırı görünümde görünür ve tüketici filtrelemeyi unutur | Doküman + Fonksiyonel | Görünümün `tenant_id` taşıdığını ve **filtrelemediğini** kanıtlayan test; uyarı `read-views.md`'de |
| Saklama politikası satır silince görünüm bayat kalır | Fonksiyonel | `ReadViewContractTests` — silinen koşu görünümden düşer |
| Referans sayfası kırık bağlantı taşır | Kapı | `check-links.mjs` |

**Beş sorunun cevabı.** *İptal:* görünüm salt okunurdur, iptal edilebilir bir
kod yolu üretmez. *Eşzamanlılık:* görünüm durum tutmaz; okuma anındaki tabloyu
yansıtır. *Boş/aşırı girdi:* `NULL` maliyet satırı ve sıfır koşulu kiracı.
*Başka kiracı:* tabloda ayrı satır — filtrelemenin tüketicide olduğunu
kanıtlayan test. *Alt sistem hatası:* görünüm yoksa (migration koşmamışsa)
tüketicinin sorgusu anlaşılır bir SQL hatası verir; AgentPrism'in kendi yolu
etkilenmez.

---

## Manuel Kabul Case'leri

| # | Ön koşul | Adımlar | Beklenen sonuç |
|---|---|---|---|
| 1 | PostgreSQL migrate edildi | `SELECT * FROM agentprism.runs_v1 LIMIT 5;` | Sütunlar 111.2 tablosuyla birebir; içerik sütunu yok |
| 2 | Maliyetli bir `run` koşuldu | Görünümdeki `total_cost` ile `GET /api/stats` toplamını karşılaştır | Aynı değer |
| 3 | Fiyatı tanımsız bir model ile `run` koşuldu | Aynı koşuyu görünümde oku | `total_cost` **`NULL`**, `0` değil |
| 4 | SQLite sağlayıcısı | `SELECT total_cost FROM agentprism_runs_v1;` | Değer sayısal ve PostgreSQL ile aynı anlamda |
| 5 | İki kiracının koşusu var | Görünümü filtresiz sorgula | İki kiracının satırı da görünür — bu **beklenen** davranıştır; doküman uyarısı bunu karşılar |

---

## Açık Sorular

| # | Soru | Seçenekler | Öneri |
|---|---|---|---|
| 1 | `error_message` v1'e girsin mi? | A: Girmesin, `error_type` yeter · B: Girsin | **A.** Sağlayıcı ham mesajı taşıyabilir; sözleşme dar başlar. Eklemek sonradan **additive** ve ucuzdur, çıkarmak değildir |
| 2 | `status_name` metnini görünüm mü üretsin, tüketici mi eşlesin? | A: Görünüm üretsin · B: Yalnız sayısal `status` | **A.** Elle eşleme tüketici tarafında kusur sınıfıdır; aynı gerekçe `total_cost` için de geçerli |
| 3 | Görünüm varsayılan olarak mı kurulsun, isteğe bağlı migration seti mi olsun? | A: Varsayılan · B: `EnabledMigrationSets` ile isteğe bağlı (`pgvector` deseni, Faz 67) | **B.** K1 "sıfır sürpriz": kalıcı bir sözleşme, isteyen tüketiciye açılır. Ölçüm: isteğe bağlı setin üç sağlayıcıdaki maliyeti uygulama anında doğrulanır |
| 4 | `runs_v1` dışında bir görünüm v1'e girsin mi? | A: Hayır · B: Günlük maliyet toplamı da girsin | **A.** YAGNI. Tüketici LINQ'da toplar; ikinci görünüm ikinci taahhüttür |

---

## Bitiş Ölçütleri (DoD)

- [x] Üç sağlayıcıda `runs_v1` görünümü kurulur ve 111.2 sütun tablosunu birebir karşılar — `ReadViewColumnSetTests` (6/6) + gerçek `\d+` çıktısı aşağıda
- [x] `total_cost` üç terimi de içerir; `NULL` anlamı korunur (case 3 kanıtlar) — `ReadViewContractTests.Total_cost_is_null_not_zero_when_pricing_is_undefined`, üç sağlayıcıda
- [x] Görünüm toplamı ile `RunStatistics.TotalCost` üç sağlayıcıda birebir eşleşir — `ReadViewContractTests.Total_cost_in_the_view_matches_the_stores_own_statistics`
- [x] Toplam terim kapısı çalışıyor: `runs` tablosuna sahte bir `*_cost` sütunu eklendiğinde `ReadViewCostTermTests` **kırmızı** olur — build-time, `CostAddends` reflection ile
- [x] Sütun kümesi kapısı çalışıyor: bir sütun düşürüldüğünde `ReadViewColumnSetTests` **kırmızı** olur — elle doğrulandı (`cost_currency` → `cost_currency_renamed` denemesi kırmızı çıktı, sonra geri alındı)
- [x] İki kapı da veritabanı **açmadan** koşar (`AgentPrism.Sql.Shared.UnitTests` deseni) — `ReadViewSqlText.ExtractMarkedBlock` gömülü kaynak metnini okur
- [x] Görünüm sütunları ile `ProtectedColumn` kesişimi boş — `ReadViewColumnSetTests` üç ayrı test, üç sağlayıcı
- [x] `reference/read-views.md` yayında; EF keyless entity örneği çalışıyor — sayfa `docs-site/src/content/docs/reference/read-views.md`, sidebar'a eklendi
- [x] `npm run build` + `check-links.mjs` temiz — `npm run check` (dördü birden) yeşil
- [x] Dört doğrulama kapısı sıfır uyarı verir — `python3 scripts/kapi.py kapanis --taban d20b46f`, iki ayrı koşumda
- [x] `samples/AgentPrism.Api` ile gerçek `run` yapıldı, çıktı belgeye yazıldı — bkz. "Gerçek Koşum Kanıtı" altbölümü
- [x] `secret` taraması boş döndü — `python3 scripts/kapi.py tarama`
- [x] Manuel kabul case'leri `docs/manuel-test/03-KALICILIK-POSTGRESQL.md` ve `04-KALICILIK-DIGER.md` içine eklendi; otomatikleştirilebilenler koşuldu — MT-PG-072..075, MT-SQL-077..078
- [x] `faz-denetim` koşuldu; 🔴 bulgu kalmadı — bkz. "Denetim Bulguları"

### Gerçek Koşum Kanıtı (2026-08-26, kapanış)

`samples/AgentPrism.Api`, gerçek bir PostgreSQL konteynerine (`ap-pg`) karşı
`EnableReadViews=true` ile çalıştırıldı. `support` agent'ına gerçek bir HTTP
isteği (`POST /agentprism/api/agents/support/run`) gönderildi; `EchoModelProvider`
ağ çağrısı yapmadan yanıtladı (K1: API anahtarı gerekmedi). Açılış logu **38
migration** uyguladığını yazdı (37 çekirdek + 1 `views`). Koşu tamamlandıktan
sonra `runs_v1` doğrudan sorgulandı:

```
run_id        | 01a03e53-c473-7826-b74c-8ee2582a52b2
agent_name    | support
status        | 1
status_name   | Completed
input_tokens  | 283
output_tokens | 16
total_tokens  | 299
input_cost    | (NULL)
output_cost   | (NULL)
total_cost    | (NULL)
cost_currency | (NULL)
error_type    | (NULL)
```

`EchoModelProvider`'ın fiyatı tanımsız olduğu için `total_cost` **`NULL`**
döndü — `0` değil; case 3'ün üretimdeki kanıtı budur. `\d+
agentprism_phase111_demo.runs_v1` çıktısı 111.2 sütun tablosuyla birebirdi
(20 sütun, sırası ve tipleri dahil). Demo şeması iş bitince
`DROP SCHEMA ... CASCADE` ile temizlendi; `ap-pg` konteyneri önceki durumuna
(durdurulmuş) geri döndürüldü.

### Doğrulama komutları

```bash
# Görünüm sütunları
psql "$AGENTPRISM_CONNECTION" -c "\d+ agentprism.runs_v1"

# Toplam, store ile eşleşiyor mu
./artifacts/bin/AgentPrism.PostgreSql.IntegrationTests/release/AgentPrism.PostgreSql.IntegrationTests \
  --filter-method "*ReadViewContract*"

# Sözleşme dışı sütun sızmış mı
grep -n "state\|payload\|arguments\|result\|content" src/AgentPrism.PostgreSql/Migrations/*_read_views.sql
```

---

## Riskler

| Risk | Önlem |
|------|-------|
| 🚨 Kalıcı taahhüt: yayımlanan görünüm geri alınamaz | Sözleşme **dar** başlar (tek görünüm, üstveri + türev). Açık Soru 3 ile isteğe bağlı sete konur |
| 🚨 K-483 sınıfının tekrarı — yeni maliyet terimi görünüme girmez | Toplam terim kapısı; elle tarama değil test |
| SQLite afinite karışımı sessiz yanlış toplam üretir | Açık `CAST`; üç sağlayıcıda karşılaştırmalı test |
| Tüketici kiracı filtresini unutur ve başka kiracının verisini raporlar | Uyarı kutusu + davranışı kanıtlayan test. Görünüm bir güvenlik sınırı **değildir** ve bu yazılır |
| Üç dialektte üç ayrı SQL — elle tekrarlanan ifade | Beklenen sütun listesi `AgentPrism.Sql.Shared.UnitTests` içinde **tek** yerde durur; üç sağlayıcının SQL metni aynı listeye karşı doğrulanır |
| Sonraki faz `runs` sütununu yeniden adlandırır, görünüm sessizce kırılır | Sütun kümesi kapısı derleme değil test seviyesinde yakalar |

---

<!-- ============================================================
     AŞAĞISI KAPANIŞTA DOLDURULUR — `faz-tamamlama` skill'i.
     Plan anında boş kalır. Başlıkları SİLME.
     ============================================================ -->

## Plandan Sapmalar

- **Dosya yolu planınkinden farklı.** Plan `src/AgentPrism.PostgreSql/Migrations/NNNN_read_views.sql` diyordu (çekirdek sette). Açık Soru 3'ün B seçeneği (isteğe bağlı set) kabul edilince bu, Faz 67'nin `MigrationsKnowledge/` deseniyle tutarlı olması için `MigrationsViews/0001_read_views.sql`'e taşındı — her sağlayıcı kendi `OptionalMigrationResourcePrefixes` sözlüğünde ayrı bir embedded-resource önekiyle kayıtlı.
- **SQL Server'da yeni bir tuzak keşfedildi ve dokümana yazıldı: `CREATE (OR ALTER) VIEW` tek başına bir toplu iş olmalıdır.** `MigrationRunner.ApplyOneAsync` migration metnini `__migrations` INSERT'iyle **tek** komut metninde gönderir (K-388); bu, `CREATE OR ALTER VIEW`'in T-SQL kısıtını doğrudan ihlal ederdi. Çözüm: view tanımı `EXEC(N'...')` ile sarmalandı — kendi toplu işini açar. Plan bunu öngörmüyordu; ilk yazılan SQL Server migration'ı gerçek konteynerde "'CREATE VIEW' must be the first statement in a query batch" hatasıyla düştü, EXEC sarmalamasıyla düzeltildi.
- **İki test-altyapısı kusuru bulundu ve düzeltildi** (fazın kapsamı dışında ama onun ürettiği ilk view tarafından ortaya çıkarıldı): `SqliteTestContext.DisposeAsync()` ve SQL Server `SqlServerTestContext.DropSchemaAsync()` yalnız TABLOLARI siliyordu; `runs_v1` bir VIEW olduğu için sarkan kalıyordu. SQLite'ta bu, sonraki bir testin `ALTER TABLE ... RENAME` migration'ını "no such table" ile düşürdü (SQLite'ın rename-fixup'ı dosyadaki HER görünümü yeniden çözer); SQL Server'da `DROP SCHEMA` "being referenced by object" ile başarısız oldu. İkisi de görünüm-önce-tablo sırasıyla düzeltildi. Vaka kaydı: `docs/hafiza/sqlite.md`.
- **Bağımsız denetimin 🟡 bulguları üzerine üç ek test dosyası eklendi** (planda yoktu, denetim sonrası): `ReadViewStatusNameTests.cs` (`status_name` eşlemesinin her `RunStatus` değerini kapsadığını build zamanında doğrular — `total_cost` kapısının eşdeğeri) ve üç sağlayıcıda birer `ReadViewOptionalSetTests.cs` (`EnableReadViews=false` iken görünümün gerçekten kurulmadığını doğrudan kanıtlar — "knowledge" setinin `Knowledge_disabled_by_default_creates_no_vector_objects` deseninin eşdeğeri).

## Bu Fazda Verilen Kararlar

- **K-626** — `runs_v1` okuma sözleşmesi görünümü üç sağlayıcıda `EnableReadViews` ile isteğe bağlı yayımlandı; yayımlanan sürüm sütun kaybetmez/yeniden adlandırmaz/daraltmaz, kırıcı değişiklik `runs_v2` olarak açılır; görünüm bir kiracı sınırı DEĞİLDİR. Tam gerekçe: `docs/KARARLAR.md`.

## Gerçekleşen Public API

C# yüzeyi üç yeni public üye ile büyüdü — planın "yüzey büyümüyor" iddiası
denetimde yanlış bulundu ve düzeltildi (bkz. Denetim Bulguları, 🟢 madde 1):

```csharp
// AgentPrismPostgreSqlOptions, AgentPrismSqlServerOptions, AgentPrismSqliteOptions
public bool EnableReadViews { get; set; }   // default: false
```

Veri sözleşmesi (`runs_v1`, 111.2'de planlanan sütun listesiyle birebir,
gerçek `\d+` çıktısıyla doğrulandı):

```sql
-- PostgreSQL / SQL Server
{schema}.runs_v1
-- SQLite
{prefix}runs_v1
```

Sütunlar: `run_id`, `tenant_id`, `agent_name`, `session_id`, `status`,
`status_name`, `started_at`, `completed_at`, `is_streaming`, `input_tokens`,
`output_tokens`, `cached_input_tokens`, `reasoning_tokens`, `total_tokens`,
`input_cost`, `output_cost`, `cached_input_cost`, `total_cost`,
`cost_currency`, `error_type` (20 sütun).

## Dosya Listesi (gerçekleşen)

```
src/AgentPrism.PostgreSql/MigrationsViews/0001_read_views.sql   (planda: Migrations/NNNN_read_views.sql)
src/AgentPrism.SqlServer/MigrationsViews/0001_read_views.sql
src/AgentPrism.Sqlite/MigrationsViews/0001_read_views.sql

src/AgentPrism.{PostgreSql,SqlServer,Sqlite}/AgentPrism*Options.cs        (+ EnableReadViews)
src/AgentPrism.{PostgreSql,SqlServer,Sqlite}/AgentPrism*BuilderExtensions.cs
src/AgentPrism.{PostgreSql,SqlServer,Sqlite}/Internal/*Dialect.cs         (+ OptionalMigrationResourcePrefixes["views"])
src/AgentPrism.{PostgreSql,SqlServer,Sqlite}/*.csproj                     (+ EmbeddedResource)
src/AgentPrism.{PostgreSql,SqlServer,Sqlite}/PublicAPI.Unshipped.txt
src/AgentPrism.{PostgreSql,SqlServer,Sqlite}/README.md

tests/AgentPrism.Sql.Shared.UnitTests/
├── ReadViewSqlText.cs             (paylaşılan yardımcı — marker'lar arası metni okur)
├── ReadViewColumnSetTests.cs      (sütun kümesi + korunan sütun kapıları, 6 test)
├── ReadViewCostTermTests.cs       (toplam terim kapısı, 3 test)
└── ReadViewStatusNameTests.cs     (status_name kapısı, 3 test — denetim sonrası eklendi)

tests/AgentPrism.{PostgreSql,SqlServer,Sqlite}.IntegrationTests/
├── ReadViewContractTests.cs       (4 test/sağlayıcı: toplam eşleşme, NULL koruması, status_name, kiracı filtresizliği)
└── ReadViewOptionalSetTests.cs    (2 test/sağlayıcı — denetim sonrası eklendi)

tests/AgentPrism.{PostgreSql,SqlServer,Sqlite}.IntegrationTests/Infrastructure/*TestContext.cs
                                   (+ enableReadViews parametresi; Sqlite/SqlServer'da ayrıca
                                    view-önce-tablo temizlik düzeltmesi)

docs-site/src/content/docs/reference/read-views.md   (yeni)
docs-site/src/content/docs/guides/ef-core.md         (okuma bölümü eklendi)
docs-site/src/content/docs/packages.md               (satır eklendi)
docs-site/src/content/docs/reference/compatibility.md (satır eklendi)
docs-site/src/sidebar.mjs                            (yeni sayfa kaydı)

docs/KARARLAR.md          (K-626)
docs/hafiza/sqlite.md     (view-önce-tablo tuzağı)
docs/manuel-test/03-KALICILIK-POSTGRESQL.md  (MT-PG-072..075)
docs/manuel-test/04-KALICILIK-DIGER.md       (MT-SQL-077..078)
```

## Denetim Bulguları

Bağımsız denetim (taze bağlamlı agent, 2026-08-26): **🔴 yok.**

| # | Seviye | Bulgu | Sonuç |
|---|---|---|---|
| 1 | 🟡 | `status_name` eşlemesinin `total_cost`'un eşdeğeri bir otomatik kapısı yoktu — yeni bir `RunStatus` değeri eklenip görünüme yansıtılmazsa hiçbir test kırmızı olmazdı | **Düzeltildi** — `ReadViewStatusNameTests.cs` eklendi (üç sağlayıcı, `RunStatus` enum'unu reflection ile okur) |
| 2 | 🟡 | `ProtectedRawColumnNames` listesi (`ReadViewColumnSetTests.cs`) `ProtectedColumn` enum'unun XML doc'undaki ham sütun adlarının elle kopyasıdır, reflection'la bağlı değil | **Gerekçelendi** — `ProtectedColumn` bugün 10 kalemli, nadiren değişen bir enum ve XML doc metnini reflection'la ayrıştırmak (tam otomasyon için tek yol) bu ölçekte orantısız karmaşıklık katardı. Liste kodda yorumla işaretli; yeni bir `ProtectedColumn` değeri eklenirse bu listenin güncellenmesi gerektiği hatırlatılıyor |
| 3 | 🟡 | `EnableReadViews=false` iken görünümün hiç kurulmadığını doğrudan kanıtlayan test yoktu — yalnız dolaylı kanıt (~40 sınıfın varsayılanla yeşil kalması) vardı | **Düzeltildi** — üç sağlayıcıda birer `ReadViewOptionalSetTests.cs` eklendi, "knowledge" setinin deseninin birebir eşdeğeri |
| 4 | 🟡 | DoD satırı "`samples/AgentPrism.Api` ile gerçek `run` yapıldı" kanıtsızdı | **Düzeltildi** — gerçek koşum yapıldı, çıktı DoD bölümüne yazıldı (bkz. "Gerçek Koşum Kanıtı") |
| 5 | 🟢 | "Planlanan Public API" bölümü "yüzey büyümüyor" diyordu ama üç `EnableReadViews` bool eklendi | **Düzeltildi** — plan başlığı ve "Gerçekleşen Public API" bölümü güncellendi |
| 6 | 🟢 | Planlanan dosya yolu (`Migrations/NNNN_read_views.sql`) gerçekleşenden (`MigrationsViews/0001_read_views.sql`) farklıydı | **Not düşüldü** — "Plandan Sapmalar"a yazıldı |

Denetimden sonra dört kapı **yeniden koştu** (yeni test dosyaları + SQL marker
değişiklikleri nedeniyle) ve yeşil kaldı.

## Sonraki Faza Devir Notu

- **`runs_v1` artık kalıcı bir sözleşmedir (K-626).** Bir sonraki faz `runs`
  tablosuna sütun eklerken bunun `runs_v1`'e de eklenip eklenmeyeceğini (additive,
  serbest) düşünmeli; sütun **yeniden adlandırma/daraltma/kaldırma** ise `runs_v2`
  gerekir, `runs_v1` elle düzenlenmez.
- **Yeni bir `*_cost` sütunu eklenirse** `ReadViewCostTermTests` onu otomatik
  ister (build zamanında kırmızı olur) — `SqlQueriesBase.CostAddends`'e eklemek
  yeterli değildir, üç `0001_read_views.sql`'in `total_cost: BEGIN/END` bloğuna
  da elle eklenmesi gerekir.
- **Yeni bir `RunStatus` değeri eklenirse** aynı şekilde `ReadViewStatusNameTests`
  kırmızı olur; üç `0001_read_views.sql`'in `status_name: BEGIN/END` bloğundaki
  `CASE`'e yeni dal eklenmesi gerekir.
- **🚨 SQL Server'da `CREATE VIEW`/`PROCEDURE`/`FUNCTION`/`TRIGGER` içeren bir
  migration yazarsan `EXEC(N'...')` ile sarmala.** `MigrationRunner` migration
  metnini `__migrations` INSERT'iyle tek komutta gönderir; bu dört DDL türü
  T-SQL'de "toplu işin tek deyimi olmalı" kısıtı taşır. Vaka: bu faz, ilk
  yazılan `CREATE OR ALTER VIEW`'de gerçek konteynerde patladı.
- **🚨 SQLite/SQL Server test context'lerinde VIEW temizliği artık TABLO
  temizliğinden ÖNCE gelir** (`SqliteTestContext.DisposeAsync`,
  `SqlServerTestContext.DropSchemaAsync`). Yeni bir test-context temizlik
  yordamı yazarsan bu sırayı koru — aksi hâlde sarkan bir view sonraki testi
  düşürür (vaka kaydı: `docs/hafiza/sqlite.md`).
- **İkinci bir görünüm (`runs_v2` dışında, örn. `run_events` için) açılmadı**
  (Açık Soru 4/A, YAGNI) — talep gelirse `ProtectedColumn` kesişim kapısı ilk
  kez gerçek bir korunan sütunla karşılaşacak, bugünkü boş-kesişim testi
  yalnız `runs_v1` için anlamlıydı.
