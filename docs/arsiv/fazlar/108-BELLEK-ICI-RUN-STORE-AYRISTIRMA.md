# Faz 108 — Bellek İçi Run Store Ayrıştırma

> **Durum:** ✅ Tamamlandı (2026-08-26)
> **Kaynak:** [`kesif/2026-08-23-yapisal-sorun-envanteri.md`](../../kesif/2026-08-23-yapisal-sorun-envanteri.md) — **kalem 17**. Bu faz bir `F-NN` adayından gelmez
> **Önkoşul:** [Faz 107](107-RUN-KAYIT-AKISI-AYRISTIRMA.md) — runtime writer sabitlendikten sonra onun varsayılan store'u ayrıştırılır
> **Paketler:** `AgentPrism.Core`
> **Yeni paket:** Yok · **Migration:** Yok
> **Public API:** Büyümüyor. `InMemoryRunStore` internal kalır; `IRunStore` sözleşmesi değişmez
> **Tüketici yüzeyi:** Yok. Store davranışı ve public sözleşme değişmez
> **Manuel test alanı:** [`manuel-test/02-CEKIRDEK-VE-KATALOG.md`](../../manuel-test/02-CEKIRDEK-VE-KATALOG.md) · [`manuel-test/23-SAKLAMA-ARSIV-KOTA.md`](../../manuel-test/23-SAKLAMA-ARSIV-KOTA.md)

---

## Bu Faza Başlarken

1. Bu doküman
2. Kararlar — yalnız ilgili satırlar:
   ```bash
   grep -n "K-282\|K-283\|K-421" docs/KARARLAR.md
   ```
3. Aday sınırı: `grep -n "F-148" docs/ADAYLAR.md` — duplicate-sequence performans işi bu refactor'a karıştırılmaz
4. Alan hafızası: [`hafiza/test-altyapisi.md`](../../hafiza/test-altyapisi.md) ve [`hafiza/cekirdek-calistirma.md`](../../hafiza/cekirdek-calistirma.md)
5. Mimari: [`MIMARI.md`](../../MIMARI.md) — yalnız `IRunStore` ve in-memory store haritası

---

## Amaç

`InMemoryRunStore`, run yaşam döngüsünü, event log'unu, query/filtering'i, tree toplamlarını, istatistikleri, experiment sonuçlarını, time series ve tool usage hesaplarını 1.432 satırda taşır. Faz aynı internal sınıfı `partial` dosyalara böler. State ownership ve lock sınırları değişmez.

### Bugün ne çalışmıyor — doğrulanmış kanıt

| Kanıt | Gözlem |
|---|---|
| [`InMemoryRunStore.cs:22`](../../../src/AgentPrism.Core/Storage/InMemoryRunStore.cs) | Tek internal sınıf **1.432 satırdır** ve 21 davranış metodu taşır. |
| [`InMemoryRunStore.cs:58`](../../../src/AgentPrism.Core/Storage/InMemoryRunStore.cs) | Run yaşam döngüsü ve event append aynı dosyada başlar. |
| [`InMemoryRunStore.cs:374`](../../../src/AgentPrism.Core/Storage/InMemoryRunStore.cs) | Query, tenant süzme ve tree toplamları lifecycle state'iyle iç içedir. |
| [`InMemoryRunStore.cs:641`](../../../src/AgentPrism.Core/Storage/InMemoryRunStore.cs) | Statistics, experiment, time series ve tool analytics dosyanın yarısından fazlasını oluşturur. |

> Kanıtlar 2026-08-26 tarihinde doğrulandı.

## 108.1 — Tek state owner korunur

`InMemoryRunStore` `internal sealed partial class` olur. Dictionary, queue, score store ve tenant context alanları tek state dosyasında kalır. Yeni alt-store, yeni cache veya state kopyası oluşturulmaz.

Bu sınır önemlidir. Ayrı service'ler state'i paylaşmak için yeni concurrency sözleşmesi gerektirir. Kullanıcı yalnız dosya monolitini gidermek istedi; davranış mimarisini değiştirmek kapsam dışıdır.

> **Faz 107'den devir:** aynı `partial` taşıma deseni orada da işe yaradı —
> önce hiç `using`/pragma eklemeden metotları hedef dosyaya taşı, sonra
> `dotnet build` çalıştır; `TreatWarningsAsErrors=true` eksik `using`'i
> `CS0246`, gereksiz pragma'yı `IDE0079` olarak geri verir. Faz 107'de bu
> teknikle iki eksik `using` (`Microsoft.Agents.AI`, `Microsoft.Extensions.Logging`)
> ilk denemede yakalandı, tahmin gerekmedi. `InMemoryRunStore.cs` bugün hiç
> `MAAI001` taşımıyor (varsayım, kontrol edilmedi — Faz 107'de `RunRecordingAgent.cs`
> için kontrol edilmişti), bu fazda erken doğrulanmalı.
> Ayrıca: private nested tipler (bu fazda tally/aggregate record'lar) en
> doğal biçimde onları **üreten** metodun taşındığı dosyada kalır — Faz 107
> `RunStart`/`RunScope` record'larını `PrepareRun`/`CreateScope` ile birlikte
> `Lifecycle.cs`'e taşıdı, ayrı bir "types" dosyası açmadı.

## 108.2 — Sorumluluk dosyaları

Metotlar şu eksenlere ayrılır:

- run lifecycle, heartbeat, orphan claim ve trim;
- event ve tool invocation log'ları;
- query, tenant ownership ve tree toplamları;
- statistics, breakdown ve error clustering;
- experiment, time series ve tool usage analytics.

Nested tally tipleri kullanan hesapla aynı dosyada yaşar. Paylaşılan küçük matematik helper'ları `Analytics` dosyasında kalır.

## 108.3 — Contract-first doğrulama

Önce mevcut `RunStoreContract` ve tenant isolation koşumları alınır. Refactor sonrası in-memory ve üç SQL sağlayıcısı aynı contract setini geçer. Böylece in-memory davranışının sağlayıcılardan sessizce sapması yakalanır.

F-148 kapsam dışıdır. `AppendEventAsync` duplicate-sequence taraması bu fazda optimize edilmez. Mekanik refactor ile performans davranışı aynı diff'e girmez.

## Planlanan Public API

Yok. Tip internal kalır; interface veya record değişmez.

### HTTP `endpoint`'leri

Yok.

### Arayüz payı

Yok.

## Planlanan Dosya Listesi

```text
src/AgentPrism.Core/Storage/
├── InMemoryRunStore.cs
├── InMemoryRunStore.Lifecycle.cs
├── InMemoryRunStore.Events.cs
├── InMemoryRunStore.Queries.cs
├── InMemoryRunStore.Statistics.cs
└── InMemoryRunStore.Analytics.cs
tests/AgentPrism.Core.UnitTests/Storage/
└── InMemoryRunStoreStructureTests.cs
```

## Hata Modları ve Testler

| Ne bozulabilir | Seviye | Test sınıfı |
|---|---|---|
| Run start/complete taşınırken event veya tool log sıfırlanır | Sözleşme | `RunStoreContract` lifecycle/event senaryoları |
| Tenant ownership helper'ı bir query yolunda çağrılmaz | Sözleşme | `TenantIsolationContract` in-memory + üç SQL koşumu |
| Eşzamanlı append aynı `Sequence`'ı kabul eder | Birim / sözleşme | event concurrency testleri |
| Trim run'ı siler ama event/tool/heartbeat kalır | Birim | `InMemoryRunStoreStructureTests` trim senaryosu |
| Boş store statistics veya time series hesabı çöker | Birim | boş/aşırı tarih aralığı testleri |
| Score store hata verir veya iptal olur | Birim | statistics cancellation/failure testleri |
| Experiment/tree toplamları başka kiracının run'ını sayar | Sözleşme | tenant analytics senaryoları |

## Manuel Kabul Case'leri

| # | Ön koşul | Adımlar | Beklenen sonuç |
|---|---|---|---|
| 1 | Varsayılan in-memory sample | Bir root ve child run üret, Runs/Dashboard ekranlarını aç | Run ağacı ve toplamlar doğru görünür |
| 2 | `MaxRuns` küçük değer | Sınırı aşan run üret | En eski run ve ona bağlı event/tool kayıtları birlikte düşer |
| 3 | İki tenant context'i | Aynı store örneğinde tenant'ı değiştirip query yap | Her tenant yalnız kendi run'larını görür |

## Açık Sorular

Yok. F-148 performans değişikliği ayrı aday olarak kalır.

## Bitiş Ölçütleri (DoD)

- [x] State alanları tek owner'da kalır; yeni alt-store veya state kopyası yoktur — beş `Dictionary`/`Queue` alanı `InMemoryRunStore.cs`'te tek başına kaldı
- [x] Lifecycle, events, queries, statistics ve analytics ayrı sorumluluk dosyalarındadır
- [x] `RunStoreContract` ve `TenantIsolationContract` dört sağlayıcı koşumunda yeşildir — bellek içi 1970/1970, PostgreSQL 88/88, SQL Server 88/88, SQLite 88/88
- [x] F-148 davranışı ve aday kaydı değişmeden kalır — `AppendEventAsync`'in doğrusal `Sequence` taraması karakter düzeyinde taşındı, dokunulmadı
- [x] Public API dosyalarında fark yoktur — `PublicAPI.Shipped/Unshipped.txt` diff'i boş (tip zaten `internal`)
- [x] Dört doğrulama kapısı sıfır uyarı verir — `scripts/kapi.py kapanis --taban 9dc53b9` yeşil
- [x] `samples/AgentPrism.Embedded` ile gerçek `run` yapıldı, çıktı belgeye yazıldı — bkz. MT-CORE-106
- [x] `secret` taraması boş döndü
- [x] Manuel kabul case'leri ilgili ailelere eklendi ve otomatik olanlar koşuldu — MT-CORE-106 (`02-CEKIRDEK-VE-KATALOG.md`), MT-RET-044 (`23-SAKLAMA-ARSIV-KOTA.md`)
- [x] `faz-denetim` koşuldu; 🔴 bulgu kalmadı — iki 🟡 bulgu, ikisi de bu kapanışta kapatıldı

## Riskler

| Risk | Önlem |
|---|---|
| State'i alt nesnelere bölmek atomicity'yi değiştirir | Yalnız `partial` dosya ayrıştırması yapılır |
| Analytics helper'ı tenant süzgecinden önce çalışır | Contract testleri tenant + analytics kombinasyonunu ölçer |
| Refactor F-148 optimizasyonuyla karışır | Performans diff'i açıkça kapsam dışıdır; denetim bunu kontrol eder |

---

## Plandan Sapmalar

Plana birebir uyuldu; sorumluluk eksenleri planın 108.2 bulletlarıyla aynen
eşleşti. İki küçük yerleşim kararı, plan düz yazıyla anlattığı için burada
netleştirilir:

- `EnsureExpectedTenant` planın hiçbir bulletine açıkça girmiyordu (üç dosyada
  — `Lifecycle`, `Events` — çağrılıyor). Ana state dosyasında (`InMemoryRunStore.cs`)
  bırakıldı: `_runs`'a doğrudan erişen, tek bir sorumluluk eksenine ait
  olmayan paylaşılan bir helper.
- `IsOwnedByCurrentTenant`/`TryGetOwnedRun` de üç dosyada kullanılıyor
  (`GetRunAsync`, `ListToolInvocationsAsync`, `ReadEventsAsync`). "Query,
  tenant ownership ve tree toplamları" ekseni bunları açıkça adlandırdığı için
  `Queries.cs`'e kondu; `Events.cs` onları partial class üyesi olarak çağırır.

DoD'nin "gerçek `run`" satırı `samples/AgentPrism.Api` yerine
`samples/AgentPrism.Embedded` ile koşuldu: `AgentPrism.Api`'nin manuel test
kurulumu PostgreSQL ister (`manuel-test/02-CEKIRDEK-VE-KATALOG.md` "Koşmadan
önce"), oysa bu fazın konusu tam olarak **bellek içi** store'dur —
`AgentPrism.Embedded` bağlantı dizesi istemeden `InMemoryRunStore`'u
doğrudan ayağa kaldırır (`persistenceProvider: InMemory`).

Ayrıca plandaki dosya listesinde olmayan bir dosya eklendi:
`tests/AgentPrism.Core.UnitTests/Storage/InMemoryRunStoreStructureTests.cs`
(planın kendi "Planlanan Dosya Listesi"nde zaten adı geçiyordu, içeriği
belirtilmemişti). Denetim bu dosyanın varlığını doğrudan istemedi; hata
modları tablosunun "Trim run'ı siler ama event/tool/heartbeat kalır" ve
"Score store hata verir veya iptal olur" satırlarının karşılıksız olduğu
görülünce eklendi.

## Bu Fazda Verilen Kararlar

Yok. Faz saf bir kod taşıma işiydi; public API/compatibility contract,
güvenlik/kiracı sınırı veya kalıcı veri kararı gerektiren bir seçim yapılmadı.

## Gerçekleşen Public API

Yok. `InMemoryRunStore` `internal sealed partial class` oldu (`partial`
eklendi, `internal` ve `sealed` korundu); `IRunStore` sözleşmesi değişmedi.
`PublicAPI.Shipped.txt`/`PublicAPI.Unshipped.txt` diff'i boştur.

## Dosya Listesi (gerçekleşen)

Plandakiyle birebir aynı, artı bir test dosyası:

```text
src/AgentPrism.Core/Storage/
├── InMemoryRunStore.cs               (state, ctor, MaxRuns, EnsureExpectedTenant — 76 satır, eskiden 1432)
├── InMemoryRunStore.Lifecycle.cs     (StartRunAsync, CompleteRunAsync, UpdateRunCostAsync, TouchHeartbeatAsync, ClaimOrphanedRunsAsync, TrimIfNeeded)
├── InMemoryRunStore.Events.cs        (AppendEventAsync, RecordToolInvocationAsync, ListToolInvocationsAsync, ReadEventsAsync)
├── InMemoryRunStore.Queries.cs       (GetRunAsync, QueryRunsAsync, WithTreeTotals, IsOwnedByCurrentTenant, TryGetOwnedRun, MatchesLabel, Accumulate)
├── InMemoryRunStore.Statistics.cs    (GetStatisticsAsync, RunCostTotal, AgentTally, BreakdownTally, ModelTally, ErrorClusterTally)
└── InMemoryRunStore.Analytics.cs     (GetExperimentResultsAsync, GetTimeSeriesAsync, GetToolUsageAsync, VariantTally, BucketTally, ToolTally)
tests/AgentPrism.Core.UnitTests/Storage/
└── InMemoryRunStoreStructureTests.cs (trim'in event/tool log'unu da sildiğini; score store hata/iptalinin GetStatisticsAsync'i yutmadığını doğrular — 3 test)
```

Doğrulama yöntemi: her taşınan blok, orijinal `git show 9dc53b9:...` çıktısıyla
satır satır `diff`'lendi (Faz 107'nin denetim yönteminin aynısı) — tek fark
eklenen `{`/`<inheritdoc />`/boş satır ayırıcılarıydı, hiçbir metot gövdesi
veya `record` tipi kaybolmadı ya da değişmedi.

## Denetim Bulguları

Taze bağlamlı bir `general-purpose` agent `faz-denetim` skill'ini uyguladı
(2026-08-26). Yöntem: `git diff 9dc53b9` satır satır okundu, orijinal dosyayla
karşılaştırıldı; bağımsız olarak `dotnet build`, `dotnet test
AgentPrism.Core.UnitTests` (1970/1970) ve tam `scripts/kapi.py kapanis
--taban 9dc53b9 --site-atla` (tarama, dokuman-bakim, unittest, agent-map,
denetim-paketi, build, tam test paketi, pack, `dotnet format` — hepsi ✅)
koştu.

**🔴 yok.**

**🟡 (ikisi de bu kapanışta kapatıldı):**

1. DoD satırı "`samples/AgentPrism.Api` ile gerçek `run` yapıldı" karşılıksızdı
   — denetim, uygulayan oturumun sample koşumunu henüz belgelemediği anda
   koştuğu için bunu yakaladı. **Kapatıldı:** `samples/AgentPrism.Embedded`
   ile gerçek koşum yapıldı (bkz. Plandan Sapmalar — neden `Api` değil
   `Embedded`), MT-CORE-106 olarak belgelendi.
2. DoD satırı "Manuel kabul case'leri ilgili ailelere eklendi" karşılıksızdı.
   **Kapatıldı:** MT-CORE-106 (`02-CEKIRDEK-VE-KATALOG.md`) ve MT-RET-044
   (`23-SAKLAMA-ARSIV-KOTA.md`) eklendi.

**🟢 yok.**

**Temiz çıkan başlıklar:** 3.1 (kalan tüm DoD satırları), 3.2, 3.3, 3.4, 3.5
(imza değişikliği yok), 3.6 (yeni public API yok), 3.7, 3.8.

## Sonraki Faza Devir Notu

- Aynı satır-satır-diff denetim yöntemi (Faz 107, 108) üçüncü kez işe yaradı;
  büyük bir `partial` taşımasını doğrulamanın ucuz ve güvenilir yolu budur:
  `git show <taban>:<yol>` çıktısını `sed`'le blok blok kes, yeni dosyanın
  gövdesini (class bildirimi ile kapanış `}` arası) çıkar, `diff` al — fark
  yalnız eklenen `{`/`<inheritdoc />`/boş satırlarsa taşıma birebirdir.
- `InMemoryRunStore` artık `partial`; yeni bir sorumluluk ekseni (örn. F-148
  performans optimizasyonu) eklenirse mevcut beş dosyadan hangisine ait
  olduğuna bakılmalı, altıncı bir dosya açmadan önce.
- `samples/AgentPrism.Embedded`, bağlantı dizesi istemeyen bellek-içi
  senaryolar için `samples/AgentPrism.Api`'den daha uygun bir manuel test
  yüzeyidir — `AgentPrism.Api`'nin kendi manuel test dosyası PostgreSQL
  şart koşar.
