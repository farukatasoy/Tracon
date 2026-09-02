# Faz 133 — İş Kuyruğu Metrikleri

> **Durum:** ✅ Tamamlandı (2026-09-02)
> **Kaynak:** [ADAYLAR.md](../../ADAYLAR.md) — **F-178** (job/kuyruk metrikleri yarısı; model deneme telemetrisi yarısı adaylıkta kalır)
> **Önkoşul:** [Faz 129](129-IS-KUYRUGU-LANELERI.md) — `lane` kimliği olmadan metrik etiketlenemez
> **Paketler:** `AgentPrism.Abstractions`, `.Core`, `.Sql.Shared`, `.PostgreSql`, `.SqlServer`, `.Sqlite`, `.Testing.Contracts.Xunit`
> **Yeni paket:** Yok · **Migration:** **Yok** — ölçüldü: derinlik sorgusu Faz 129'un `jobs_claim_idx (lane, status, scheduled_for) WHERE status IN (0,1,2)` index'i tarafından zaten kapsanıyor
> **Public API:** büyüyor — `IJobStore`'a bir aggregate metot, iki options alanı, bir `record`. `wc -l src/*/PublicAPI.Shipped.txt` → her dosya 1 satır; shipped giriş **sıfır**, bugün eklemek hâlâ ucuz
> **Tüketici yüzeyi:** `docs-site/`: `guides/observability.md` (metrik **ve** etiket tabloları), `guides/background-work.md`, `reference/configuration.md`, `guides/write-your-own-store.md` (yeni store metodu) · sevk edilen: `IJobStore` XML dokümanı, `AgentPrismDiagnostics` sabitleri
> **Manuel test alanı:** [`docs/manuel-test/16-IS-KUYRUGU-VE-ZAMANLAMA.md`](../../manuel-test/16-IS-KUYRUGU-VE-ZAMANLAMA.md) — 🚨 **Önce 133.0'ı uygula**, bütçe 97 bayt boş

---

> ### ⚗️ Damıtılmış kayıt
> Bu dosya fazın **planını** değil, fazın bıraktığı **kalıcı bilgiyi**
> taşır. Plan gövdesi, planlanan/gerçekleşen API, dosya listesi, risk ve
> açık soru bölümleri kapanışta düştü — **silinmedi, git geçmişindedir.**
>
> Tam metin — kopyala, çalıştır:
>
> ```bash
> git show 5cc190d:docs/arsiv/fazlar/133-IS-KUYRUGU-METRIKLERI.md
> ```
>
> Damıtıldı 2026-09-02 · `scripts/dokuman-bakim.py faz-damit`

---

## Amaç

Faz 129 `lane`'i sevk etti: iş artık ayrılabiliyor ve `lane` başına eşzamanlılık alabiliyor. Ama **hiçbiri ölçülmüyor.** `AgentPrismMetrics` on enstrüman taşıyor ve **hiçbiri job hakkında değil**. Operatör şu üç soruyu bugün yanıtlayamaz: - Hangi `lane`'de iş birikiyor? - Bir `lane`'in eşzamanlılık bütçesi dar mı, geniş mi?

## Bitiş Ölçütleri (DoD)

- [x] Ayar yapılmayan kurulumda `agentprism.job.executions` ve `agentprism.job.duration` yazılır; gauge **yazılmaz** (case 1)
- [x] `EnableJobQueueDepthGauge: true` iken derinlik `lane` × `status` ile raporlanır (case 2)
- [x] `RefreshInterval` içinde ikinci scrape veritabanına gitmez (case 3)
- [x] `retry` bırakması sayaca girmez; yalnız terminal durum sayılır (case 4)
- [x] Derinlik sorgusu `jobs_claim_idx` kullanır; `EXPLAIN ANALYZE` çıktısı belgeye yazıldı (case 5)
- [x] Kardinalite sınırı aşılınca `other` etiketi kullanılır, var olan `lane`'ler adını korur (case 6)
- [x] `JobStoreContract` dört koşumun dördünde de yeşil
- [x] Metrik yazımı hata verse bile job tamamlanır
- [x] Dört doğrulama kapısı sıfır uyarı verir
- [x] `samples/AgentPrism.Api` ile gerçek `run` yapıldı, çıktı belgeye yazıldı
- [x] `secret` taraması boş döndü
- [x] **133.0 uygulandı:** `dokuman-bakim.py:177` `2_250_000`'e kalibre edildi, `--denetle` `docs/manuel-test` için `ok` döndürüyor
- [x] Manuel kabul case'leri `docs/manuel-test/16-IS-KUYRUGU-VE-ZAMANLAMA.md` içine eklendi; otomatikleştirilebilenler koşuldu
- [x] `faz-denetim` koşuldu; 🔴 bulgu kalmadı
- [x] `docs-site/` güncellendi — `observability.md`'nin **hem** metrik **hem** etiket tablosu; `npm run build` + `check-links.mjs` temiz
- [x] 🚨 `dotnet test AgentPrism.slnx` TAM log dosyasından teyit edildi — `| tail` ile **değil** (Faz 130 devir notu)

### Doğrulama komutları

```bash
# Metrik çıktısı (samples/AgentPrism.Api OTel konsol exporter'ı ile)
grep -E "agentprism\.job\.(executions|duration|queue\.depth)" <otel-log>

# Derinlik sorgusunun index kullanımı
psql -c "EXPLAIN ANALYZE <GetQueueDepth sorgusu>"

# Bütçe (case eklemeden ÖNCE ve SONRA)
python3 scripts/dokuman-bakim.py --denetle | grep manuel-test
```

---

## Plandan Sapmalar

**1. Manuel test bütçesi 2 250 000 değil, 2 300 000'e kalibre edildi (133.0).**
Planın formülü ("ölçülen + %15") `dokuman-bakim.py`'nin kendi eşiğiyle
çelişiyordu: `BOSLUK_ORANI = 0.15` bir bütçenin **en az %15'inin boş** kalmasını
ister, yani doğru formül `ölçülen / 0.85`'tir — dosyanın o satırdaki mevcut
yorumu da zaten bunu yazıyordu (`olculen/0.85 = 2.05M olurdu`). 2 250 000
yalnız %13,3 boşluk bırakır ve DoD'nin *"`--denetle` `ok` döndürüyor"*
satırını sağlayamazdı. 1 949 903 / 0,85 = 2 294 003 → **2 300 000**.

**2. `AgentPrismMetrics` kurucusu ikinci bir isteğe bağlı parametre aldı.**
Plan kardinalite muhafızını `AgentPrismMetrics`'in içine koyuyordu ama sınırın
(`MaxJobLaneCardinality`) oraya nasıl ulaşacağını söylemiyordu. `RecordJob`'a
parametre olarak taşımak her çağıranı sınırdan haberdar etmeyi gerektirirdi;
bunun yerine kurucu `IOptionsMonitor<AgentPrismOptions>?` alır —
`QuotaUsageObserver`'ın deseni, ve `new AgentPrismMetrics()` çağıran mevcut
testler bozulmadan çalışmaya devam eder. Kök tip enjekte edilir, iç içe tip
**değil** (`docs/hafiza/olcum-kota-ve-secenekler.md`'nin standalone-options
tuzağı).

**3. Sayaç iki ek terminal yolda da yazılıyor.** Plan diyagramı üç yol
gösteriyordu (`Completed` · `Failed` · `Cancelled`). Kod okununca iki yol daha
terminal çıktı ve ikisi de sayılmalıydı, yoksa sayaç işi sessizce kaybederdi:
- **`IJobHandler` bulunamadı** → `CompleteAsync(Failed)` ve erken `return`.
- **İş dışarıdan iptal edildi** → handler `IsCancelledAsync` ile görüp erken
  çıkar, `CompleteAsync` **çağrılmaz** (durum zaten `Cancelled`'dır).

**4. `InMemoryJobStore.GetQueueDepthAsync` `[TenantAgnostic]` işareti
ALMADI.** Öznitelik `AgentPrism.Sql.Shared` içinde `internal`'dır (K-176 linked
source) ve `AgentPrism.Core`'dan erişilemez — `CS0246`. Gerekçe XML yorumunda
duruyor; kapı olan `TenantCoverageTests` zaten yalnız SQL sağlayıcılarının
`Stores/` ağacını tarar ve `SqlJobStore` işareti taşır.

**5. Derinlik sorgusu `CAST(COUNT(*) AS bigint)` yazar, çıplak `COUNT(*)`
değil.** SQL Server'da `COUNT(*)` `int` döner (canlı sunucuda
`SQL_VARIANT_PROPERTY` ile ölçüldü); `GetInt64` `InvalidCastException` atardı.
`CAST` üç dialektte de geçerlidir, böylece sorgu `BuildSharedQueries()`'te tek
metin olarak kalabildi. **Aynı kusur var olan bir sorguda gerçekten yaşıyordu**
— aşağıya bakın.

**6. Fazın dışından bir kusur kapatıldı: `SelectConversationBranchPoint`.**
5'teki tuzağı `SqlQueriesBase`'de ararken, paylaşılan
`SELECT COALESCE(MAX(seq), -1), COUNT(*)` sorgusunun `reader.GetInt64(1)` ile
okunduğu görüldü. **Konuşma dallandırma (Faz 47) SQL Server'da hiç
çalışmıyordu** — özellik sevk edildiğinden beri. Görünmemesinin sebebi
`ConversationBranchTests`'in yalnız SQLite'ta var olmasıydı; gerekçe "sorgular
paylaşılan katmanda, dialektten bağımsız" idi ve o gerekçe sorgu **metni** için
doğru, **okuyucu** için yanlıştı. `kusur-giderme` uygulandı: beş case SQL
Server'a yazıldı, **kırmızı görüldü**
(`InvalidCastException: Unable to cast 'System.Int32' to 'System.Int64'`),
`CAST` eklendi, yeşile döndü. **Sınıf taraması:** 103 paylaşılan sorgunun
tamamı tarandı; kalan iki toplama (`MAX(seq)`) güvenlidir çünkü sütunun kendi
tipini döner ve `seq` üç dialektte de `bigint`. Başka vaka yok.

**7. `ManualTimeProvider` artık monotonik saati de sahteler.** Taban
`TimeProvider.GetTimestamp()` gerçek `Stopwatch`'a düşüyordu, bu yüzden
"süre duvar saatinden değil monotonik saatten gelir" iddiası test edilemezdi.
Fake'e `GetTimestamp`/`TimestampFrequency` eklendi: `Advance` ileri giderken
her iki saati, geri giderken **yalnız duvar saatini** oynatır.

**8. Bir alan hafızası dosyası bölündü.** 6'daki not `sql-saglayicilari.md`'yi
15 990/16 000 B'den taşırdı. K-214 merdiveni bu aşımda büyütmeyi değil bölmeyi
zorunlu kılar: migration konusu (`MigrationRunner`, `__migrations` defteri,
geçici çakışma) [`hafiza/sql-migration.md`](../../hafiza/sql-migration.md)'ye
taşındı — `sqlite.md`'nin Faz 36'daki emsali. İçerik silinmedi.

## Bu Fazda Verilen Kararlar

| Karar | Gerekçe |
|---|---|
| **K-651 — Manuel test bütçesi ölçüme yeniden bağlanabilir; formül `ölçülen / (1 − BOSLUK_ORANI)`** | K-214'ün amacı **sınırsız büyümeyi** engellemekti, sabit bir sayıyı korumak değil. Bütçe büyütmek serbest değildir; yeniden kalibrasyon yalnız ölçülen değere göre, **kullanıcı kararıyla** yapılır ve yorumda ölçüm tarihi/değeri yazılır. |
| **K-652 — Paylaşılan bir SQL sorgusunda toplama fonksiyonunun dönüş tipi `CAST` ile sabitlenir** | `COUNT(*)` SQL Server'da `int`, PostgreSQL/SQLite'ta `bigint`'tir. Paylaşılan katmanın tek bir okuyucusu vardır; tip dialekte göre değişirse o okuyucu bir sağlayıcıda çöker. Sorgu **metninin** paylaşılabilir olması **okuyucunun** taşınabilir olduğunu kanıtlamaz. |
| **K-653 — Kuyruk derinliği gauge'ında kiracı etiketi yoktur ve `GetQueueDepthAsync` kiracı parametresi almaz** | Kuyruk derinliği, her kiracıdan iş kiralayan **işçi havuzu** hakkında bir operatör sinyalidir. Kiracı etiketi hem kardinaliteyi kiracı sayısıyla çarpar hem de var olmayan bir kiracı sınırı ima eder. |

## Denetim Bulguları

`faz-denetim` bağımsız, taze bağlamlı bir denetçiyle koşuldu. **Bir 🔴, altı
🟡, üç 🟢** bulgu üretti. Hepsi kapatıldı; hiçbiri devredilmedi.

### 🔴 1 — Gauge kardinalite muhafızını atlıyordu · **DÜZELTİLDİ**

Sayaç `lane` etiketini `ResolveLaneTag`'ten geçiriyordu, gauge ham
`depth.Lane`'i yazıyordu. Sevk edilen XML (*"Once a process has seen
`MaxJobLaneCardinality` distinct lanes, every further lane is written as
`other`"*) ve site tablosu (`agentprism.job.lane` · **Every job signal**)
muhafızın her job sinyali için geçerli olduğunu söylüyordu — **yanlıştı**.
Kullanıcı başına `lane` üreten bir tüketicide gauge her scrape'te açık iş
sayısı kadar seri yayardı; muhafızın var olma sebebi tam olarak budur.

Düzeltme: `AgentPrismMetrics.ResolveLaneTag` `private` → `internal`,
`JobQueueDepthObserver` **aynı** `AgentPrismMetrics` örneğini alır (DI kaydı
`GetRequiredService<AgentPrismMetrics>()` geçirir). Ayrı bir muhafız örneği
**kasıtlı olarak reddedildi**: iki küme, aynı `lane`'i bir enstrümanda adıyla
diğerinde `other` ile yazardı — muhafızın önlemeye çalıştığı okunamaz serinin
ta kendisi. Kapı: `JobQueueDepthGaugeTests.The_gauge_applies_the_same_lane_cardinality_guard_as_the_counter`.

### 🟡 — altısı da kapatıldı

| # | Bulgu | Sonuç |
|---|---|---|
| 2 | `Tags.JobStatus` XML'i *"Only terminal statuses are written"* diyordu; gauge açık durum yazıyor | XML düzeltildi: sayaç/histogram terminal, gauge açık durum taşır — ikisi hiç kesişmez |
| 3 | *"Metrik yazımı hata verse bile job tamamlanır"* DoD satırının testi yoktu | `JobMetricsTests.A_throwing_metric_listener_does_not_stop_the_job_from_completing` yazıldı. **Ayırt ediciliği ölçüldü:** `RecordJobMetric`'in `try/catch`'i kaldırılınca test kırmızı oldu (job `Completed` yerine `Failed`'a düşüyor, çünkü metrik çağrısı `ExecuteJobAsync`'in `catch`'inin İÇİNDE) |
| 4 | Örnek uygulama çıktısı yoktu; ölçüm tablosu fonksiyonel test projesini listelemiyordu | İkisi de eklendi — aşağıdaki *Ölçümler* bölümü |
| 5 | İki plan iddiası kodla çelişiyordu (gauge hata yolunun "boş döner" iddiası; "worker kapanırken `Cancelled` sayılır") | Faz dokümanının **133.4** ve **Hata Modları** bölümleri koda göre düzeltildi. Doküman ile kod çelişirse doküman yanlıştır |
| 6 | `docs-site/capabilities.md` sevk edilen yetenek haritası job metriklerini bilmiyordu | `capabilities.md` ve `reference/glossary.md` güncellendi |
| 7 | Sapma 8 *"içerik silinmedi"* diyordu ama bölme sırasında K-247 maddesi düşmüştü | Madde geri kondu. Programatik doğrulama: özgün 31 madde, şimdi 31 madde, **kayıp 0** |

🚨 **7 numaralı bulgu bu fazın kendi dersini tekrarladı.** Dosya bölme işlemi
0-tabanlı liste indeksiyle 1-tabanlı satır numarasını karıştırdı ve komşu
maddeyi de sildi. Ders: **bir taşıma işleminin kayıpsızlığı göz kararıyla değil
sayarak doğrulanır.**

### 🟢 — üçü de bu fazda kapatıldı (aday listesine gitmedi)

| # | Bulgu | Sonuç |
|---|---|---|
| 1 | `MT-JOB-120`'nin ön koşulu "taze süreç" demiyordu; daha önce koşmuş bir `default` işi beklenen sırayı bozar | Ön koşul eklendi; case ayrıca gauge'ın aynı eşlemeyi kullandığını da doğruluyor |
| 2 | `JobQueueDepthObserver.RefreshAsync` store'a `CancellationToken` geçirmiyor | **Kapatılmadı, gerekçelendi:** `QuotaUsageObserver` ile birebir aynı; senkron gauge geri çağrımının sınıf sorunudur, tek bir gözlemciyi düzeltmek deseni ikiye böler |
| 3 | Sözleşme testinde anlamsız kalıntı `leased.ShouldNotBeNull()` | Silindi |

**Denetçinin temiz bulduğu başlıklar:** 3.2 (test tiyatrosu yok) · 3.3 (test
seviyeleri doğru) · 3.5 (imza-gövde kayması yok) · 3.6 (plan dışı public API
yok) · 3.7 (repo kuralları) · tüketici doküman sözleşmesi (hiçbir muafiyet
listesi veya taban çizgisi büyümedi).

## Ölçümler

**Derinlik sorgusunun index kullanımı (DoD case 5).** PostgreSQL, 60 007 satır
(3 000'i açık, geri kalanı terminal):

```
HashAggregate  (cost=1280.05..1280.25 rows=20) (actual time=1.168..1.169 rows=3)
  Group Key: lane, status
  ->  Bitmap Heap Scan on jobs  (actual time=0.199..0.824 rows=3000)
        Recheck Cond: (status = ANY ('{0,1,2}'::integer[]))
        ->  Bitmap Index Scan on jobs_claim_idx  (actual time=0.123..0.123 rows=3000)
              Buffers: shared hit=4
Execution Time: 1.180 ms
```

`Seq Scan` **yok**; `jobs_claim_idx` kullanılıyor ve taranan satır sayısı
**açık iş** sayısına eşit (3 000), tablonun tamamına değil. Migration
gerekmediği doğrulandı.

**Test koşumu (tam log dosyasından teyit edildi, `| tail` ile değil).**

| Paket | Sonuç |
|---|---|
| `AgentPrism.Core.UnitTests` | 2273/2273 ✅ |
| `AgentPrism.Sql.Shared.UnitTests` | 20/20 ✅ |
| `AgentPrism.Sqlite.IntegrationTests` | 641/641 ✅ |
| `AgentPrism.PostgreSql.IntegrationTests` | 697/697 ✅ |
| `AgentPrism.SqlServer.IntegrationTests` | 632/632 ✅ (627 → +5, Sapma 6) |
| `AgentPrism.AspNetCore.FunctionalTests` — `JobMetricEndToEndTests` | 2/2 ✅ |

**Örnek uygulama ile gerçek koşum.** `samples/AgentPrism.Api`,
`EnableJobQueueDepthGauge=true` ve `PollInterval=1s` ile başlatıldı; iş HTTP
üzerinden kuyruğa atıldı (`PUT /api/schedules/faz133-probe` →
`POST .../trigger`, `lane: "media"`, var olmayan bir agent hedefleniyor):

```
status=Failed  attempt=3  lane=media   (× 5 iş)
errorMessage: The agent named 'no-such-agent' was not found...
```

Her iş **üç deneme** harcadı ve **tek bir terminal `Failed`**'a düştü — DoD
case 4'ün gerçek uygulamadaki karşılığı. Gauge açıkken uygulama log'unda
`Could not record the job metric` veya `Could not refresh the job queue-depth
gauge cache` **hiç görülmedi**.

🚨 **Sayaç DEĞERLERİ örnek uygulamadan okunamadı.** Örnek uygulama bir OTel
exporter'ı taşımıyor ve `dotnet-counters` 9.0 net10.0 sürecinin metre'sini
yüzeye çıkarmadı (CSV yalnız başlık satırıyla döndü). Ölçüm değerlerinin kanıtı
bu yüzden `JobMetricEndToEndTests`'tedir: **gerçek DI + gerçek HTTP + gerçek
worker**, ölçüm aynı süreçteki `MeterListener` ile okunuyor. Örnek uygulama
koşumu davranışı (deneme sayısı, terminal durum, `lane`) kanıtlar; fonksiyonel
test yayılan ölçümü kanıtlar.

**Fonksiyonel testin ayırt ediciliği ölçüldü.** `RecordJobMetric` geçici olarak
erken dönecek şekilde değiştirildi; `JobMetricEndToEndTests` **kırmızı** oldu
(`TimeoutException: No 'agentprism.job.executions' measurement was published`),
düzeltme geri alınınca yeşile döndü. Planın *"`AgentPrismMetrics?` eklemek
hiçbir hata üretmez"* uyarısının gerçekten kapatıldığı böyle kanıtlandı.

## Sonraki Faza Devir Notu

- **`agentprism.job.duration` bir DENEMEYİ ölçer, işin ömrünü değil.** Bir işin
  toplam ömrü (ilk kiralamadan nihai duruma) hiçbir yerde ölçülmüyor. İkisi
  farklı sorulardır ve ikincisi bir gösterge panelinde daha sık istenir; ama
  onu yazmak `jobs` satırına yeni bir sütun ya da `StartedAt`'e dayanan bir
  hesap gerektirir — bu fazın "migration yok" kısıtının dışındaydı.
- **Kardinalite kümesi süreç ömürlüdür ve süreçler arasında paylaşılmaz.** Çok
  örnekli bir kurulumda iki işçi aynı 65. `lane`'i farklı etiketlerle
  yazabilir (biri adıyla, diğeri `other`). Sınır 64 iken bu pratikte
  görülmeyecek bir uçtur; gerçekten sorun olursa çözüm paylaşılan bir kayıt
  defteridir, daha büyük bir varsayılan değil.
- **🚨 Sapma 6'nın sınıfı yalnız `SqlQueriesBase`'de tarandı.** Sağlayıcıya
  özgü `*Queries.cs` dosyalarındaki okuyucular taranmadı — orada her dialekt
  kendi metnini yazdığı için tip uyuşmazlığı yapısal olarak daha az olası, ama
  **kanıtlanmadı**. Bir sonraki SQL fazı `reader.GetInt64`/`GetInt32`
  çağrılarını sorgu metinleriyle karşılaştıran bir tarama yapabilir.
- **Gauge yalnız `IJobStore`'u okur; `ITenantStore` gibi ikinci bir kaynağı
  yoktur.** `QuotaUsageObserver`'ın "yalnız KAYITLI kiracıları tarar" sınırının
  buradaki karşılığı yoktur — derinlik kiracıdan bağımsızdır (K-653).
