# Faz 154 — Skor Trendinin Kalıcı Sorgusu

> **Durum:** ✅ Tamamlandı (2026-09-07)
> **Kaynak:** [ADAYLAR.md](ADAYLAR.md) · **F-209**
> **Önkoşul:** 🚨 [Faz 152](arsiv/fazlar/152-SKORUN-ADI-VE-SEKLI.md) — aynı tabloya (`run_scores`) dokunur ve **önce koşmalıdır**. 152 skora bir **ad** getiriyor; kırılım o adı içermelidir, aksi hâlde kırılım iki kez elden geçer.
> **Paketler:** `AgentPrism.Abstractions`, `AgentPrism.Core`, `AgentPrism.AspNetCore`, `AgentPrism.PostgreSql`, `AgentPrism.Sqlite`, `AgentPrism.SqlServer`, `AgentPrism.Testing.Contracts.Xunit`, `AgentPrism.UI`
> **Yeni paket:** Yok · **Migration:** 🚨 **gerekli, üç sağlayıcıda** — yalnız indeks; numaralar uygulama anında alınır (K-178). Yeni tablo **yok**
> **Public API:** Büyüyor — `IRunScoreStore`'a bir okuma üyesi + bir sorgu/sonuç tipi çifti. `wc -l src/*/PublicAPI.Shipped.txt` → her dosya **1 satır** (ölçüldü 2026-09-07): depo arayüzüne üye eklemek üçüncü taraf uygulayıcıyı kırar ve **`1.0` öncesi** yapılmalıdır.
> **Tüketici yüzeyi:** site: `docs-site/src/content/docs/concepts/evaluation.md` · üretilen: `http-api/`, `api/agentprism.irunscorestore` · sevk edilen: `EvalEndpoints` online özet metni (bugün tüketiciyi **kendi tablomuza** yönlendiriyor), `IRunScoreStore` XML dokümanı
> **Manuel test alanı:** `docs/manuel-test/17-EVAL-VE-DENEYLER.md` · `docs/manuel-test/12-GOZLEMLENEBILIRLIK-MALIYET.md`

---

## Bu Faza Başlarken

> `faz-baslangic` skill'ini uygula. Aşağıdaki liste o skill'in 2. adımıdır —
> **tamamını değil, yalnız işaret edilen bölümleri oku.**

1. Bu doküman
2. Kararlar — dosyanın tamamını **okuma**, yalnız bu kalemleri grep'le:
   ```bash
   grep -n "K-178\|K-232\|K-421\|K-638" docs/KARARLAR.md
   ```
   **K-178** (migration numaraları sağlayıcı başına bağımsız), **K-232**
   (sunucu yanıtları çevrilmez), **K-421** (public API takibi açık), **K-638**
   (yargıç checkpoint'i yeni tablo AÇMADAN mevcut `run_scores` satırlarından
   okur — bu fazın emsali ve aynı kuralın devamı).
3. [`152-SKORUN-ADI-VE-SEKLI.md`](arsiv/fazlar/152-SKORUN-ADI-VE-SEKLI.md) — yalnız devir notu:
   ```bash
   awk '/## Sonraki Faza Devir Notu/,0' docs/152-SKORUN-ADI-VE-SEKLI.md
   ```
   🚨 **Bu faz Faz 152'nin çıktısına doğrudan oturur.** `RunScore.Name`,
   `Value`'nun `double?` olması ve `Categorical` şekli kırılımı ve
   toplulaştırmayı **tanımlar**. Faz 152 kapanmadan bu faza başlama.
4. Alan hafızası (bu faz üç alana dokunuyor):
   [`hafiza/sql-migration.md`](hafiza/sql-migration.md) (yalnız indeks migration'ı) ·
   [`hafiza/sql-saglayicilari.md`](hafiza/sql-saglayicilari.md) (toplulaştırma
   ifadelerinin sağlayıcı farkları) · [`hafiza/postgresql.md`](hafiza/postgresql.md)
   (indeks ve sorgu planı)
5. Gerektiğinde, tamamı değil ilgili bölümü:
   [`MIMARI.md`](MIMARI.md) — veri modeli · gözlemlenebilirlik bölümü

---

## Amaç

Skor özeti süreç yeniden başlayınca **sıfırlanıyor** ve ürün bunu tüketiciye
**kendi tablosunu sorgulayarak** çözmesini söylüyor. Bir NuGet paketinin
tüketiciyi kendi şemasına yönlendirmesi bir sözleşme boşluğudur: `run_scores`
public bir yüzey değildir, migration'la değişebilir — nitekim
[Faz 152](arsiv/fazlar/152-SKORUN-ADI-VE-SEKLI.md) tam olarak onu değiştiriyor.

Bu faz **tasarımı değiştirmez**. Mevcut "no durable counter store" kuralı
korunur; canlı gösterge bellekte kalır. Eklenen tek şey eksik olan **okuma
üyesidir**.

- **F-209** — `IRunScoreStore`'a zaman aralığı ve kırılımla toplulaştıran bir
  okuma üyesi, onu sunan bir uç ve `created_at` üzerinde bir indeks.

### Bugün ne çalışmıyor — doğrulanmış kanıt

| Kanıt | Gözlem |
|---|---|
| [`EvalEndpoints.cs:167-170`](../src/AgentPrism.AspNetCore/Endpoints/EvalEndpoints.cs#L167) | Sevk edilen metin: *"The summary is in-memory (it resets when the process restarts); for an authoritative result, the 'run_scores' table can be queried directly."* |
| [`OnlineEvalSummaryService.cs:13-18`](../src/AgentPrism.Core/Evaluation/OnlineEvalSummaryService.cs#L13) | Pencere bellekte, kiracı başına kuyruk; *"no durable counter store"* kuralına atıf |
| [`IRunScoreStore.cs:37-54`](../src/AgentPrism.Abstractions/Runs/IRunScoreStore.cs#L37) | **Yalnız üç üye**: `UpsertAsync` · `ListAsync(tenantId, runId)` · `DeleteAsync`. Zaman aralığı veya toplulaştırma **yok** |
| [`0017_run_scores.sql:50-52`](../src/AgentPrism.PostgreSql/Migrations/0017_run_scores.sql#L50) | Tek erişim indeksi `(tenant_id, run_id)`; yorum: *"Listing the scores of a run — that is the only access pattern."* Sqlite `0005` ve SqlServer `0005` de aynı |
| [`RunStatisticsQuery.cs`](../src/AgentPrism.Abstractions/Runs/RunStatistics.cs) | Kırılım deseni hazır: `StartedAfter` · `AgentName` · `UserId` · `LabelKey`/`LabelValue` · `MaxAgents` tavanı |

> Kanıtlar 2026-09-07 tarihinde doğrulandı.

---

## 154.1 — Bu bir sorgudur, bir sayaç değildir

Adayın karşı görüşü gerçektir ve bu faz onu **çürütmez**: mevcut tasarım bunu
bilerek böyle yaptı, gerekçesini kodda yazdı ve `RunSampler`'ın saatlik bütçesi
de aynı deseni kullanıyor.

Ayrım net tutulur:

| | Bugünkü `OnlineEvalSummaryService` | Bu fazın eklediği |
|---|---|---|
| Ne | Canlı **gösterge** ve alarm | Kalıcı **sorgu** |
| Nerede | Bellekte, kiracı başına kuyruk | `run_scores` üzerinde `SELECT` |
| Ömür | Süreçle birlikte sıfırlanır | Veri kadar |
| Maliyet | Sıfır I/O | İndeksli okuma |
| Değişiyor mu | ❌ **Hayır — aynen kalır** | ✅ yeni üye |

**"No durable counter store" kuralı korunur.** Bir sayaç yazılmaz; var olan
satırlar okunur. Bu, K-638'in aynı hamlesidir — yargıç checkpoint'i de yeni bir
tablo açmadan mevcut `run_scores` satırlarından okunuyor.

## 154.2 — Kırılım `RunStatistics` desenini izler

Yeni bir sorgu dili icat edilmez. `RunStatisticsQuery`'nin bugünkü şekli
kopyalanır: düz filtre alanları, sayfalama yok, kırılım başına bir tavan.

**Filtreler:** zaman aralığı (`From`/`To`) · `AgentName` · `ScoreName` (Faz 152'den) ·
`Source` (`human`/`api`/`judge`) · `Author`.

**Kırılımlar:** skor adı · yazar · kaynak · agent · zaman kovası.

🚨 **`MessageId` bir kırılım değildir.** Kardinalitesi sınırsızdır ve mesaj
düzeyi skorlar `run` düzeyiyle aynı kovaya girmemelidir. Sorgu bunun yerine bir
`Target` filtresi taşır: `Run` · `Message` · `Any`.

**Zaman kovası** düşük kardinaliteli kapalı bir küme olarak açılır
(`Hour` · `Day` · `Week`) — serbest bir aralık ifadesi değil. Serbest ifade üç
SQL sağlayıcısında üç ayrı tarih fonksiyonu ister ve sessizce sapar.

**Toplulaştırma — Faz 152'nin üç şekli üç farklı cevap ister:**

| `RunScoreKind` | Toplulaştırma |
|---|---|
| `Binary` | sayım + pozitif oranı |
| `Stars` · `Numeric` | sayım + ortalama + en küçük + en büyük |
| `Categorical` | kategori başına sayım (tavanlı) |

🚨 **Farklı `Kind`'lar aynı ortalamaya girmez.** `Stars` (1-5) ile `Numeric`
(0-100) tek ortalamada toplanırsa sonuç anlamsızdır. Kovanın anahtarı
`(name, kind)` çiftidir, yalnız `name` değil.

🚨 **`Value = null` ortalamaya girmez ama sayıma girer.** Faz 152 `null`'a
*"ölçüm yok, sıfır değil"* anlamını verdi; ortalamaya `0` olarak katmak o kararı
sessizce ters çevirir. Sonuç ayrı bir `NoValueCount` taşır.

## 154.3 — Migration: yalnız indeks

Yeni tablo **yok**, yeni sütun **yok**. Üç sağlayıcıda tek bir indeks eklenir:

```sql
CREATE INDEX IF NOT EXISTS run_scores_created_at_idx
    ON {schema}.run_scores (tenant_id, created_at);
```

Gerekçe kodda yazılı: bugünkü tek indeks `(tenant_id, run_id)` ve `0017`'nin
kendi yorumu *"Listing the scores of a run — that is the only access pattern"*
diyor. Zaman aralığı sorgusu bu indeksi kullanamaz; seq scan olur.

Sütun sırası `(tenant_id, created_at)`'tır: her sorgu kiracıya kapalıdır ve
kiracı sütunu **önce** gelmelidir.

> Faz 152 aynı tabloda tekillik indeksini yeniden yazıyor. İki migration
> **ayrıdır** ve sırası bu dokümanın başlığında yazılı: önce 152, sonra 154.

## 154.4 — Sevk edilen metin düzeltilir

Bu faz bir kod boşluğunu kapatmakla kalmaz, **yanlış bir sözü** de siler.
[`EvalEndpoints.cs:167-170`](../src/AgentPrism.AspNetCore/Endpoints/EvalEndpoints.cs#L167)
bugün tüketiciyi `run_scores` tablosuna yönlendiriyor. O cümle kalkar ve yerine
yeni ucun adı gelir.

Aynı düzeltme `OnlineEvalSummaryService`'in XML dokümanında da yapılır: *"an
operator can query its exact result at any time"* cümlesi artık **nasıl**
sorulacağını söyler.

---

## Planlanan Public API

> Taslak imzalardır. Gerçekleşen imzalar kapanışta ayrı bir bölüme yazılır.

```csharp
// AgentPrism.Abstractions
public interface IRunScoreStore
{
    // ... UpsertAsync · ListAsync · DeleteAsync değişmez ...

    /// <summary>Aggregates the scores in a time range.</summary>
    /// <remarks>
    /// A query, not a counter: it reads the rows that already exist. The live
    /// in-memory indicator of <c>OnlineEvalSummaryService</c> is unchanged.
    /// </remarks>
    ValueTask<RunScoreSummary> SummarizeAsync(
        RunScoreQuery query,
        CancellationToken cancellationToken = default);
}

/// <summary>The filter for a score summary query.</summary>
public sealed record RunScoreQuery
{
    /// <summary>The tenant filter. The current tenant is used if left empty.</summary>
    public string? TenantId { get; init; }

    /// <summary>Count only scores created at or after this moment.</summary>
    public DateTimeOffset? From { get; init; }

    /// <summary>Count only scores created before this moment.</summary>
    public DateTimeOffset? To { get; init; }

    /// <summary>Count only this score name.</summary>
    public string? ScoreName { get; init; }

    /// <summary>Count only this agent's runs.</summary>
    public string? AgentName { get; init; }

    /// <summary>Count only this source: human, api, or judge.</summary>
    public string? Source { get; init; }

    /// <summary>Count only this author.</summary>
    public string? Author { get; init; }

    /// <summary>Whether run level scores, message level scores, or both are counted.</summary>
    public RunScoreTarget Target { get; init; } = RunScoreTarget.Any;

    /// <summary>The time bucket of the trend series. Null returns no series.</summary>
    public RunScoreBucket? Bucket { get; init; }

    /// <summary>The maximum number of rows in every breakdown.</summary>
    public int MaxRows { get; init; } = 20;
}

public enum RunScoreTarget { Any = 0, Run = 1, Message = 2 }
public enum RunScoreBucket { Hour = 1, Day = 2, Week = 3 }

/// <summary>A summary of the scores in a time range.</summary>
public sealed record RunScoreSummary
{
    public required IReadOnlyList<RunScoreAggregate> ByName { get; init; }
    public IReadOnlyList<RunScoreAggregate> ByAuthor { get; init; } = [];
    public IReadOnlyList<RunScoreAggregate> BySource { get; init; } = [];
    public IReadOnlyList<RunScoreAggregate> ByAgent { get; init; } = [];

    /// <summary>The trend series. Empty when no bucket was asked for.</summary>
    public IReadOnlyList<RunScoreBucketAggregate> Series { get; init; } = [];
}

/// <summary>One aggregated group. The key of a group is (Name, Kind).</summary>
public sealed record RunScoreAggregate
{
    public required string Key { get; init; }
    public required RunScoreKind Kind { get; init; }
    public required long Count { get; init; }

    /// <summary>The number of rows carrying no value. Never counted into Average.</summary>
    public long NoValueCount { get; init; }

    public double? Average { get; init; }
    public double? Minimum { get; init; }
    public double? Maximum { get; init; }

    /// <summary>Count per category. Set only when Kind is Categorical.</summary>
    public IReadOnlyDictionary<string, long> Categories { get; init; }
        = new Dictionary<string, long>();
}

public sealed record RunScoreBucketAggregate
{
    public required DateTimeOffset BucketStart { get; init; }
    public required IReadOnlyList<RunScoreAggregate> Groups { get; init; }
}
```

### HTTP `endpoint`'leri

| Metot | Yol | Rol | Ne yapar |
|---|---|---|---|
| `GET` | `/api/evaluation/scores/summary` | Reader (`EvalsRead`) | Zaman aralığı ve kırılımla skor özetini döner |

Sorgu parametreleri `RunScoreQuery`'nin alanlarıdır. `400` — `From` `To`'dan
sonra, `MaxRows` sınır dışı, geçersiz `bucket`. Mevcut
`GET /api/evaluation/online` **değişmez**; canlı gösterge olarak kalır ve
açıklama metni yeni ucu işaret eder.

### Arayüz payı

Mevcut skor panosuna kalıcı trend serisi bağlanır (bugün canlı özetten okuyor).
Bugünkü ölçüm (2026-09-07): **151.9 KB** brotli / 250 KB bütçe. Yeni metin
anahtarları `en.ts` **ve** `tr.ts`'e girer (K-228).

---

## Planlanan Dosya Listesi

```
src/AgentPrism.Abstractions/Runs/
├── IRunScoreStore.cs        (değişir — SummarizeAsync)
├── RunScoreQuery.cs         (yeni)
└── RunScoreSummary.cs       (yeni — Summary, Aggregate, BucketAggregate, Target, Bucket)

src/AgentPrism.PostgreSql/Migrations/NNNN_run_scores_created_at_index.sql  (yeni)
src/AgentPrism.Sqlite/Migrations/NNNN_run_scores_created_at_index.sql      (yeni)
src/AgentPrism.SqlServer/Migrations/NNNN_run_scores_created_at_index.sql   (yeni)
  + üç sağlayıcının RunScoreStore uygulamaları                             (değişir)

src/AgentPrism.Core/
├── Runs/InMemoryRunScoreStore.cs        (değişir)
└── Evaluation/OnlineEvalSummaryService.cs  (değişir — yalnız XML dokümanı, 154.4)

src/AgentPrism.AspNetCore/Endpoints/EvalEndpoints.cs   (değişir — yeni uç + metin düzeltmesi)
src/AgentPrism.Testing.Contracts.Xunit/Contracts/RunScoreStoreContract.cs (değişir)
src/AgentPrism.UI/frontend/src/…                        (değişir)

tests/
├── AgentPrism.Core.Tests/Runs/RunScoreSummaryTests.cs                    (yeni)
└── AgentPrism.AspNetCore.FunctionalTests/Evals/RunScoreSummaryEndpointTests.cs (yeni)
```

---

## Hata Modları ve Testler

> Seviyeyi plan seçer. Sınır geçen davranış (DI · HTTP · kiracı · akış · depo ·
> paket) birim testiyle kanıtlanamaz —
> [`.agents/ortak/test-seviyeleri.md`](../.agents/ortak/test-seviyeleri.md).

| Ne bozulabilir | Seviye | Test sınıfı |
|---|---|---|
| Başka kiracının skoru özete girer | Sözleşme | `RunScoreStoreContract` + `TenantIsolationContract` |
| 🚨 `Stars` (1-5) ile `Numeric` (0-100) aynı ortalamada toplanır | Sözleşme | `RunScoreStoreContract` (dört koşumda) |
| 🚨 `Value = null` ortalamaya `0` olarak katılır | Sözleşme | `RunScoreStoreContract` |
| `NoValueCount` sayımdan düşürülür veya iki kez sayılır | Sözleşme | `RunScoreStoreContract` |
| `Categorical` skorlar ortalamaya sokulur | Sözleşme | `RunScoreStoreContract` |
| Kategori kardinalitesi `MaxRows`'u aşınca sessizce kırpılır ve bu görünmez | Birim + Sözleşme | `RunScoreSummaryTests`, `RunScoreStoreContract` |
| Zaman kovası sınırı sağlayıcıya göre kayar (UTC ↔ yerel) | Sözleşme | `RunScoreStoreContract` (üç SQL sağlayıcısı) |
| `From > To` sessizce boş sonuç döner | Fonksiyonel | `RunScoreSummaryEndpointTests` |
| `MessageId` kırılıma girer ⇒ kardinalite patlar | Birim | `RunScoreSummaryTests` |
| `Target = Message` iken `run` düzeyi skorlar sayılır | Sözleşme | `RunScoreStoreContract` |
| İndeks eklenmemişken sorgu seq scan olur | Fonksiyonel | `MigrationTests` (indeksin varlığı doğrulanır) |
| Migration mevcut veriyle çakışır (indeks zaten var) | Fonksiyonel | `MigrationTests` (üç sağlayıcı, ikinci kez koşum) |
| Boş aralıkta özet `null` yerine boş liste döner | Sözleşme | `RunScoreStoreContract` |
| Canlı gösterge (`/api/evaluation/online`) davranışı **değişir** | Fonksiyonel | mevcut `OnlineEvaluationTests` — regresyon koruması |
| İptal: sorgu ortasında `CancellationToken` iptal olur | Sözleşme | `RunScoreStoreContract` |
| Eşzamanlılık: özet okunurken yeni skor yazılır | Sözleşme | `RunScoreStoreContract` |
| `store` hata verir; uç sessiz boş özet döner | Fonksiyonel | `RunScoreSummaryEndpointTests` |
| Yeni ucun metni hâlâ `run_scores` tablosunu işaret ediyor | Birim | mevcut `sevk_edilen_olay_anlatisi()` kapısı |

Beş soru: **iptal** ✅ · **eşzamanlılık** ✅ · **boş/aşırı girdi** ✅ (satır 6,
8, 13) · **başka kiracı** ✅ · **alt sistem hatası** ✅.

---

## Manuel Kabul Case'leri

> Kapanışta `docs/manuel-test/17-EVAL-VE-DENEYLER.md` ve
> `docs/manuel-test/12-GOZLEMLENEBILIRLIK-MALIYET.md` içine eklenecek taslak.

| # | Ön koşul | Adımlar | Beklenen sonuç |
|---|---|---|---|
| 1 | Birkaç `run`'a farklı adlarla skor yazılmış (Faz 152) | `GET /api/evaluation/scores/summary?from=…` | `byName` her `(ad, kind)` çifti için ayrı satır |
| 2 | Aynı | Sunucuyu yeniden başlat, aynı çağrıyı yap | **Aynı** sonuç — sıfırlanmıyor |
| 3 | Aynı | `GET /api/evaluation/online` | Canlı özet **sıfırlanmış** — davranışı değişmedi |
| 4 | `Stars` ve `Numeric` skorlar aynı adla yazılmış | Özet al | İki ayrı satır; ortalamalar karışmıyor |
| 5 | Bir skor `value: null` ile yazılmış | Özet al | `count` onu sayıyor, `average` **saymıyor**, `noValueCount` = 1 |
| 6 | `Categorical` skorlar yazılmış | Özet al | `categories` kategori başına sayım veriyor; `average` `null` |
| 7 | Aynı | `?bucket=day&from=…&to=…` | Gün başına seri; boş günler seride yok |
| 8 | Başka kiracının skorları var | Özet al | Yalnız kendi kiracının satırları |
| 9 | `?from=2026-09-10&to=2026-09-01` | Özet al | `400` |
| 10 | PostgreSQL, SQLite ve SQL Server | Migration'ı koş, sonra ikinci kez koş | İndeks kuruldu; ikinci koşum hata vermiyor |
| 11 | 👤 insan gerekir | Arayüzde skor panosunu aç, sunucuyu yeniden başlat | Trend serisi korunuyor; canlı gösterge sıfırlanıyor |

---

## Açık Sorular

> Planı bloklamayan, faz uygulanırken karara bağlanacak sorular.

| # | Soru | Seçenekler | Öneri |
|---|---|---|---|
| 1 | Kırılım `AgentName`'i nereden okur? `run_scores` agent adı taşımıyor | A: `runs` ile `JOIN` · B: kırılımdan çıkar | **A** — `RunStatistics` zaten `runs` üzerinden kırıyor ve `(tenant_id, run_id)` indeksi `JOIN`'i taşıyor. Ama maliyeti **ölçülmeli**; ölçüm pahalı çıkarsa B'ye düşülür ve gerekçe kapanışa yazılır |
| 2 | Zaman kovası hangi saat diliminde? | A: UTC · B: istemcinin verdiği ofset | **A (UTC)** — üç SQL sağlayıcısında tek davranış. Yerelleştirme istemcinin işidir |
| 3 | Kategori kırpılması nasıl görünür? | A: sessiz · B: `TruncatedCategoryCount` | **B** — sessiz kırpma "bu kategoriler yok" diye okunur |
| 4 | Uç yolu ne olsun? | A: `/api/evaluation/scores/summary` · B: `/api/runs/scores/summary` | **A** — komşusu `/api/evaluation/online` ve aynı `EvalsRead` kapsamındadır |
| 5 | `Series` ve kırılımlar tek çağrıda mı dönsün? | A: tek çağrı · B: ayrı uç | **A** — `RunStatistics` de tek yanıt döndürüyor; `Bucket` `null` iken seri boş kalır ve maliyet oluşmaz |

---

## Bitiş Ölçütleri (DoD)

- [x] `GET /api/evaluation/scores/summary` sunucu yeniden başladıktan **sonra** aynı sonucu döner — `samples/AgentPrism.Api` + SQLite ile gerçek yeniden başlatmayla doğrulandı
- [x] `GET /api/evaluation/online` davranışı **değişmemiştir** (canlı gösterge hâlâ sıfırlanır) — regresyon: mevcut `OnlineEvaluationTests`
- [x] Farklı `RunScoreKind`'lar aynı ortalamaya girmez; kova anahtarı `(name, kind)`'dır — `RunScoreStoreContract` dört store'da
- [x] `Value = null` sayıma girer, ortalamaya girmez ve `noValueCount` ile raporlanır
- [x] `Categorical` skorlar kategori sayımı döner, ortalama dönmez
- [x] `created_at` indeksi üç sağlayıcıda da kuruldu; migration ikinci kez koşulunca hata vermiyor — `MigrationTests`/`MigrationRunnerTests`
- [x] Yeni tablo **açılmadı**; "no durable counter store" kuralı korundu
- [x] `EvalEndpoints.cs`'teki *"the 'run_scores' table can be queried directly"* cümlesi **kalktı**
- [x] Dört doğrulama kapısı sıfır uyarı verir — bkz. "Doğrulama komutları" çıktısı
- [x] `samples/AgentPrism.Api` ile gerçek skor yazımı + yeniden başlatma sonrası özet alındı, çıktı belgeye yazıldı
- [x] `secret` taraması boş döndü
- [x] Manuel kabul case'leri `17-EVAL-VE-DENEYLER.md` (EVAL-128..135) ve `12-GOZLEMLENEBILIRLIK-MALIYET.md` (MT-OBS-060) içine eklendi
- [x] `faz-denetim` koşuldu; 🔴 bulgu **yok** — 🟡 bulgular Denetim Bulguları'nda kapatıldı
- [x] `docs-site/concepts/evaluation.md` güncellendi; `npm run check` (content/build/links/weight) temiz
- [x] `en.ts` ve `tr.ts` eksiksiz; bundle payı ölçüldü — 153.9 KB brotli / 250 KB bütçe
- [x] OpenAPI → NSwag → TypeScript zinciri yeniden üretildi

### Doğrulama komutları

```bash
# Yeniden başlatmadan sağ çıkıyor mu
curl -s "http://localhost:5081/agentprism/api/evaluation/scores/summary?from=2026-09-01T00:00:00Z" | jq '.byName'
# ... sunucuyu yeniden başlat ...
curl -s "http://localhost:5081/agentprism/api/evaluation/scores/summary?from=2026-09-01T00:00:00Z" | jq '.byName'

# Canlı gösterge hâlâ sıfırlanıyor mu (değişmemeli)
curl -s http://localhost:5081/agentprism/api/evaluation/online | jq '.sampleCount'

# İndeks kuruldu mu (PostgreSQL)
psql -c "\di+ *run_scores*"
```

---

## Riskler

| Risk | Önlem |
|------|-------|
| 🚨 Faz 152'den **önce** koşulursa kırılım skor adını içermez ve iki kez elden geçer | Önkoşul dokümanın başlığında ve okuma listesinin 3. maddesinde yazılı |
| Farklı `Kind`'lar tek ortalamada toplanır ⇒ anlamsız sayı | Kova anahtarı `(name, kind)`; sözleşme testi dört koşumda birden |
| `null` değer ortalamaya girer ⇒ Faz 152'nin kararı sessizce ters döner | `NoValueCount` ayrı alan; sözleşme testi |
| `IRunScoreStore`'a üye eklemek üçüncü taraf uygulayıcıyı kırar | `PublicAPI.Shipped.txt` **boştur** (ölçüldü) — `1.0` öncesi yapılır |
| İndekssiz sorgu seq scan olur | Migration DoD'de; indeksin varlığı testle doğrulanır |
| `runs` ile `JOIN` maliyeti büyük veride patlar | Açık Soru 1: maliyet **ölçülür**, pahalıysa `AgentName` kırılımı düşer ve gerekçe kapanışa yazılır |
| "Sayaç deposu yok" kuralı sessizce delinir | 154.1 tablosu sözleşmedir; DoD'de ayrı satır |
| Zaman kovası sağlayıcı başına kayar | UTC sabit; sözleşme testi üç SQL sağlayıcısında koşar |

---

<!-- ============================================================
     AŞAĞISI KAPANIŞTA DOLDURULUR — `faz-tamamlama` skill'i.
     Plan anında boş kalır. Başlıkları SİLME.
     ============================================================ -->

## Plandan Sapmalar

1. **`RunScoreBucketing` (public, plan dışı).** Plan yalnız `RunScoreBucket`'ı
   listeliyordu; kova sınırını hesaplayan `Truncate` mantığı ayrı bir public
   statik sınıfa çıkarıldı. Gerekçe: `AgentPrism.Abstractions`'ta tanımlı
   (`RunScoreBucket`'ın yanı) ama `AgentPrism.Core.InMemoryRunScoreStore`
   ondan **başka derlemeden** çağırıyor — `internal` olamaz. Üç SQL sağlayıcı
   aynı kuralı kendi `date_trunc`/`strftime`/`DATEADD` SQL'inde ayrıca
   uyguluyor (bkz. sınıfın kendi `<remarks>`'ı); paylaşılan sözleşme testi
   dördünü aynı davranışa bağlıyor. Public olması ayrıca üçüncü taraf bir
   `IRunScoreStore` uygulayıcısının Pazartesi-başlangıçlı hafta kuralını
   kendi başına türetmek zorunda kalmamasını sağlıyor.
2. **`IRunScoreStore` DI kaydı `Singleton` → `Factory`.** `ByAgent` kırılımı
   agent adını `run_id` üzerinden çözmek zorunda (skorun kendisi agent adı
   taşımıyor). `InMemoryRunScoreStore`'un kurucusuna `IRunStore`'u doğrudan
   enjekte etmek `IRunStore ⇄ IRunScoreStore` döngüsü açıyordu (ikisi de
   birbirini registration sırasında istiyor). Çözüm: kurucu artık
   `Func<Guid, CancellationToken, ValueTask<string?>>?` alıyor, kayıt bunu
   `provider.GetRequiredService<IRunStore>()`'u yalnız **çağrıldığında**
   çözen tembel bir closure ile besliyor. `ServiceRegistrationSnapshotTests`
   bu şekil değişikliğini yakaladı ve satırı güncellendi.
3. **90 günlük varsayılan pencere (plan dışı, denetimde bulundu).** Plan
   `bucket` `null` iken serinin boş kaldığını söylüyordu ama `bucket` VERİLİP
   `from` verilmediğinde ne olacağını tanımlamıyordu — sınırsız bir seri
   sorgusu tüm tabloyu tarayabilirdi. Kapanış denetimi bunu 🟡 bulgu olarak
   işaretledi (aşağıya bkz.); çözüm: `bucket` verilip `from` verilmediğinde
   **tüm sorgu** (kırılımlar dahil) son 90 güne varsayılan alınıyor —
   `EvalEndpoints.MaxRunScoreSeriesDays`. Açık `from` bu varsayılanı ezer ve
   kendisi sınırsızdır; `docs-site/concepts/evaluation.md` bunu açıkça yazar.
4. **Faz dışı üç kusur bulundu ve düzeltildi** (kullanıcı talimatı: "konuyla
   alakasız bug/defect'lerle karşılaşırsan onları da çöz"):
   - **Enum query-parametresi yanlış case ile 500 dönüyordu.** `?bucket=day`
     (küçük harf) `Nullable<RunScoreBucket>` bağlamada
     `BadHttpRequestException` fırlatıyordu; `JsonBindingProblemMiddleware`
     bunu yalnız `InnerException is JsonException` olduğunda yakalıyordu —
     query/route parametresi bağlama hatası hiç `JsonException` taşımıyor,
     dolayısıyla middleware'den kaçıp tüketicinin genel `500` işleyicisine
     düşüyordu. Sınıf taraması **altı** ucu etkilediğini gösterdi (yeni
     `scores/summary` dahil, ama esasen ÖNCEDEN VAR OLAN `stats/timeseries`,
     `runs`, `quota` uçları). Düzeltme: middleware artık AgentPrism etiketli
     her `BadHttpRequestException`'ı yakalıyor, gövde/parametre ayrımını
     `InnerException is JsonException`'a göre başlık metninde yapıyor.
     Regresyon: `JsonBindingProblemMiddlewareTests.Miscased_enum_query_parameter_returns_400_not_500`.
   - **PostgreSQL `date_trunc` oturum saat dilimine bağımlıydı.**
     `RunScoreBucketing`'in "UTC her zaman" sözleşmesini PostgreSQL'in kendi
     `date_trunc(unit, timestamptz)`'i bozuyordu — sunucunun `TimeZone`
     ayarına göre kova sınırı kayıyordu. Aynı kusur ÖNCEDEN VAR OLAN
     `SelectRunTimeSeries` sorgusunda da vardı. Düzeltme: `(date_trunc(unit,
     col AT TIME ZONE 'UTC') AT TIME ZONE 'UTC')` round-trip deyimi, ikisine
     de uygulandı. Regresyon: `Hour_buckets_group_scores_within_the_same_hour`
     ve `Week_buckets_start_on_Monday_and_group_the_whole_week` — gerçek
     PostgreSQL/SQL Server konteynerlerinde (Testcontainers) koşuyor.
   - **`samples/AgentPrism.Samples.FileRunStore` derlenmiyordu.** Bu örnek
     çözümde değildir (`kapi.py yayin`'in dışında hiçbir kapanış kapısı onu
     derlemez); yerel `dotnet pack` + dirty override ile gerçek tüketici
     paketine karşı derlenince `FileRunStore.cs`'te `sum += score.Value`
     (`Value` `double?`) derleme hatası çıktı — `RunScore.Value`'nun
     nullable olması bu dosyadan ÖNCE gelen bir kural, bu faz onu bozmadı,
     yalnız açığa çıkardı. Düzeltme: `score.Value is { } value` deseniyle
     değersiz skor atlanıyor. Ayrıca aynı dosyada `InMemoryRunScoreStore`'a
     verilen agent-adı çözücü kendi `_gate` kilidinin DIŞINDA `_runs`
     sözlüğünü okuyordu; artık kilit altında okuyor.
   - **Kalan aynı-sınıf vaka faz dışına ertelendi.** `SelectExperimentResults`
     (üç SQL sağlayıcı) ve örneğin deney-sonucu karşılığı hâlâ yalnız
     `message_id IS NULL`/`is null` kontrolü yapıyor — `MessageId = ""` yazan
     bir çağıranda deney ortalaması sessizce yanlış olur. Bu **ayrı bir
     özellik** (deneyler, skor özeti değil) ve bu fazın kapsamı dışında;
     [`ADAYLAR.md` — F-214](ADAYLAR.md) olarak kaydedildi.

## Bu Fazda Verilen Kararlar

Yeni `K-NNN` kaydı **açılmadı**. Yukarıdaki dört sapma da AGENTS.md'nin
eşiğine girmiyor (public API/uyumluluk sözleşmesi, güvenlik/kiracı sınırı,
kalıcı veri/migration veya geri dönüşü pahalı sistem kararı): ikisi (1, 2)
yerel implementation tercihi, ikisi (3, 4) bir kusuru zaten yazılı olan
sözleşmeyle (`RunScore.MessageId`/`Value` dokümanı, `RunScoreBucketing`'in
"UTC her zaman" sözü) hizalayan düzeltmedir — yeni bir karar değil, var olan
sözün uygulanmasıdır.

## Gerçekleşen Public API

Plandakiyle aynı, artı `RunScoreBucketing` (sapma 1). Gerçek imzalar:

```csharp
// AgentPrism.Abstractions
public interface IRunScoreStore
{
    ValueTask<RunScoreSummary> SummarizeAsync(RunScoreQuery query, CancellationToken cancellationToken = default);
}

public sealed record RunScoreQuery { /* plandakiyle birebir aynı, bkz. yukarısı */ }
public enum RunScoreTarget { Any = 0, Run = 1, Message = 2 }
public enum RunScoreBucket { Hour = 1, Day = 2, Week = 3 }
public sealed record RunScoreSummary { /* plandakiyle birebir aynı */ }
public sealed record RunScoreAggregate { /* plandakiyle birebir aynı */ }
public sealed record RunScoreBucketAggregate { /* plandakiyle birebir aynı */ }

/// <summary>Shared pure logic for score trend bucket truncation.</summary>
public static class RunScoreBucketing
{
    public static DateTimeOffset Truncate(DateTimeOffset value, RunScoreBucket bucket);
}
```

`wc -l src/AgentPrism.Abstractions/PublicAPI.Shipped.txt` hâlâ **0 satır**
(K-603 — 1.0 öncesi boş kalır); yeni üyeler `PublicAPI.Unshipped.txt`'e girdi.
`AgentPrism.Abstractions` public yüzey taban çizgisi 388 → 395.

### HTTP ucu (gerçekleşen)

`GET /api/evaluation/scores/summary` plandaki gibi — artı `bucket` verilip
`from` verilmediğinde 90 günlük varsayılan (sapma 3).

## Dosya Listesi (gerçekleşen)

```
src/AgentPrism.Abstractions/Runs/
├── IRunScoreStore.cs                (değişti — SummarizeAsync)
├── RunScoreQuery.cs                 (yeni)
├── RunScoreSummary.cs               (yeni)
└── RunScoreBucketing.cs             (yeni, plan dışı — sapma 1)

src/AgentPrism.PostgreSql/Migrations/0049_run_scores_created_at_index.sql  (yeni)
src/AgentPrism.SqlServer/Migrations/0036_run_scores_created_at_index.sql   (yeni)
src/AgentPrism.Sqlite/Migrations/0036_run_scores_created_at_index.sql      (yeni)
src/AgentPrism.Sql.Shared/Internal/SqlQueriesBase.cs                       (değişti)
src/AgentPrism.Sql.Shared/Stores/SqlRunScoreStore.cs                       (değişti)
src/AgentPrism.PostgreSql/Internal/PostgresQueries.cs                      (değişti — özet + date_trunc UTC düzeltmesi)
src/AgentPrism.Sqlite/Internal/SqliteQueries.cs                            (değişti)
src/AgentPrism.SqlServer/Internal/SqlServerQueries.cs                      (değişti)

src/AgentPrism.Core/Storage/InMemoryRunScoreStore.cs                       (değişti)
src/AgentPrism.Core/AgentPrismServiceCollectionExtensions.Registration.Storage.cs (değişti — sapma 2)
src/AgentPrism.Core/Evaluation/OnlineEvalSummaryService.cs                 (değişti — yalnız XML dokümanı)

src/AgentPrism.AspNetCore/Endpoints/EvalEndpoints.cs                       (değişti — yeni uç + 90 gün varsayılanı)
src/AgentPrism.AspNetCore/Internal/JsonBindingProblemMiddleware.cs         (değişti — faz dışı kusur, sapma 4)

src/AgentPrism.Testing.Contracts.Xunit/Contracts/RunScoreStoreContract.cs  (değişti — ~17 yeni case)

src/AgentPrism.UI/frontend/src/components/charts.tsx                      (değişti — ScoreTrendChart)
src/AgentPrism.UI/frontend/src/lib/chart.ts                                (değişti — primaryScoreIdentity, SCORE_KIND_MAX)
src/AgentPrism.UI/frontend/src/lib/chart.test.ts                          (değişti — primaryScoreIdentity testleri)
src/AgentPrism.UI/frontend/src/screens/dashboard.tsx                       (değişti — PersistentScoreTrendPanel)
src/AgentPrism.UI/frontend/src/lib/server-types.ts                        (değişti)
src/AgentPrism.UI/frontend/src/locales/en/runs.ts, tr/runs.ts             (değişti)
src/AgentPrism.UI/frontend/src/app.test.tsx                               (değişti — emptyRunScoreSummary fixture)

samples/AgentPrism.Samples.FileRunStore/InMemoryRunScoreStore.cs          (değişti — SummarizeAsync)
samples/AgentPrism.Samples.FileRunStore/FileRunStore.cs                   (değişti — SummarizeAsync + iki faz dışı kusur, sapma 4)

tests/AgentPrism.Core.UnitTests/Storage/RunScoreSummaryTests.cs                          (yeni)
tests/AgentPrism.AspNetCore.FunctionalTests/Evals/RunScoreSummaryEndpointTests.cs        (yeni, 13 test)
tests/AgentPrism.AspNetCore.FunctionalTests/JsonBindingProblemMiddlewareTests.cs         (değişti — 500→400 regresyonu)
tests/AgentPrism.PostgreSql.IntegrationTests/MigrationTests.cs                           (değişti)
tests/AgentPrism.SqlServer.IntegrationTests/MigrationRunnerTests.cs                      (değişti)
tests/AgentPrism.SqlServer.IntegrationTests/TenantCoverageTests.cs                       (değişti)
tests/AgentPrism.Sqlite.IntegrationTests/MigrationRunnerTests.cs                         (değişti)
tests/AgentPrism.Core.UnitTests/Configuration/ServiceRegistrationSnapshotTests.cs        (değişti — sapma 2)
tests/AgentPrism.Core.UnitTests/Architecture/public-surface-baseline.txt                 (değişti — 388→395)
tests/AgentPrism.Client.UnitTests/client-description-baseline.txt                        (değişti — 393→394)

docs-site/src/content/docs/concepts/evaluation.md          (değişti — yeni bölüm + 90 gün notu)
docs-site/src/content/docs/http-api.md, index.mdx           (değişti — 165 operasyon)
docs/manuel-test/17-EVAL-VE-DENEYLER.md                     (değişti — EVAL-128..135)
docs/manuel-test/12-GOZLEMLENEBILIRLIK-MALIYET.md           (değişti — MT-OBS-060)
docs/hafiza/http-uc-tuzaklari.md                            (değişti — JsonBindingProblemMiddleware kusur sınıfı)
docs/ADAYLAR.md                                              (değişti — F-214 eklendi)
```

## Denetim Bulguları

`faz-denetim` bağımsız denetçisi çalıştırıldı. 🔴 bulgu **yok**.

| # | Bulgu | Seviye | Sonuç |
|---|---|---|---|
| 1 | Seri için varsayılan pencere/tavan yok — `bucket` verilip `from` verilmezse sorgu sınırsız | 🟡 | **Düzeltildi** — 90 günlük varsayılan (`MaxRunScoreSeriesDays`), sapma 3 |
| 2 | Boş-string `MessageId`/`Author` SQL'de (`IS NULL`) ile bellek içinde (`{Length:>0}`) farklı sınıflandırılıyordu | 🟡 | **Düzeltildi** — üç SQL sağlayıcının `ScoreFilter`/`ByAuthor` sorgusu `''`'i `NULL` ile eşitledi; regresyon: `An_empty_string_MessageId_is_treated_the_same_as_no_message`, `An_empty_string_Author_is_excluded_from_the_author_breakdown_like_no_author` (dört store'da da yeşil) |
| 3 | *(orijinal denetimde numaralanmadı / erken kapandı)* | — | — |
| 4 | Sample'ın agent-adı çözücüsü `_runs`'ı kendi `_gate` kilidi DIŞINDA okuyor | 🟡 | **Düzeltildi** — okuma `lock (_gate)` içine alındı; `AgentPrism.Samples.FileRunStore.Tests` (93 test) yeşil |
| 5 | `RunScoreBucketing` planın "Planlanan Public API" listesinde yok | 🟡 | **Gerekçelendi** — bkz. Plandan Sapmalar #1 |
| 6 | `ScoreTrendChart` bir kovada bir (ad, kind), başka kovada farklı bir (ad, kind) çizebilir — çizgi iki farklı metriği birleştirir | 🟡 | **Düzeltildi** — `primaryScoreIdentity` artık TÜM seri için TEK bir (ad, kind) kimliği seçiyor (`overall` varsa o, yoksa en çok kovada geçen); `lib/chart.ts`'e taşındı ve 5 birim testiyle kanıtlandı |
| 7 | Sekiz yeni dosya `git add` edilmemiş | 🟡 | **Düzeltildi** — commit ile birlikte eklendi |

## Sonraki Faza Devir Notu

**Devraldığı sözleşmeler:**
- `IRunScoreStore.SummarizeAsync(RunScoreQuery, CancellationToken)` dört
  uygulamada da (bellek içi, PostgreSQL, SQLite, SQL Server) aynı davranışı
  verir; `RunScoreStoreContract` bunu ~35 case ile kanıtlıyor.
- `message_id`/`author` sorgu filtrelerinde `""` her zaman `NULL` ile
  eşdeğerdir — yeni bir `run_scores` sorgusu yazan her kod bu kuralı
  **tekrar türetmek zorunda değildir**, `ScoreFilter` local fonksiyonuna
  bakması yeterlidir (üç SQL sağlayıcı, aynı desen).
- `RunScoreBucketing.Truncate` UTC kova sınırının **tek kaynağıdır**; yeni bir
  zaman kovası ihtiyacı (örn. `Month`) buraya ve üç SQL sağlayıcının kendi
  `date_trunc`/`strftime`/`DATEADD` deyimine birlikte eklenmelidir.

**Bilinen tuzaklar:**
- 🚨 PostgreSQL `date_trunc(unit, timestamptz)` oturumun `TimeZone`
  ayarına bağımlıdır — yeni bir zaman-kovalı sorgu yazarken
  `PostgresQueries.cs`'teki `TruncateUtc` yerel fonksiyonunu kopyala, çıplak
  `date_trunc` kullanma.
- 🚨 `samples/AgentPrism.Samples.FileRunStore(.Tests)` hiçbir çözüm dosyasında
  değildir ve kapanış kapısı onu **derlemez**. `AgentPrism.Abstractions`'a
  (veya bağımlı olduğu başka bir pakete) dokunan bir faz, sample'ı gerçekten
  denemek isterse: `dotnet pack src/<Paket> -c Release
  -p:AgentPrismAllowDirtyPack=true -p:MinVerVersionOverride=0.0.0-dirty.<ad>`
  ile yerel feed'e bas, sonra `dotnet build
  -p:AgentPrismSamplePackageVersion=0.0.0-dirty.<ad>` ile sample'ı derle —
  varsayılan `*-*` joker karakteri her zaman en YÜKSEK sürümü seçer, bu yüzden
  dirty sürüm açıkça verilmelidir.
- `F-214` (`docs/ADAYLAR.md`) deneyler özelliğinde aynı boş-string/`NULL`
  sınıfını taşıyor — bu faz onu düzeltmedi, yalnız kaydetti.

**Yarım kalan / açık uçlar:** Yok — DoD'nin tüm satırları kapandı (aşağıya
bkz.).

> Kapanışta doldurulur.
