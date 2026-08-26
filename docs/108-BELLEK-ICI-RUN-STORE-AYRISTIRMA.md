# Faz 108 — Bellek İçi Run Store Ayrıştırma

> **Durum:** 📋 Planlandı (2026-08-26)
> **Kaynak:** [`kesif/2026-08-23-yapisal-sorun-envanteri.md`](kesif/2026-08-23-yapisal-sorun-envanteri.md) — **kalem 17**. Bu faz bir `F-NN` adayından gelmez
> **Önkoşul:** [Faz 107](arsiv/fazlar/107-RUN-KAYIT-AKISI-AYRISTIRMA.md) — runtime writer sabitlendikten sonra onun varsayılan store'u ayrıştırılır
> **Paketler:** `AgentPrism.Core`
> **Yeni paket:** Yok · **Migration:** Yok
> **Public API:** Büyümüyor. `InMemoryRunStore` internal kalır; `IRunStore` sözleşmesi değişmez
> **Tüketici yüzeyi:** Yok. Store davranışı ve public sözleşme değişmez
> **Manuel test alanı:** [`manuel-test/02-CEKIRDEK-VE-KATALOG.md`](manuel-test/02-CEKIRDEK-VE-KATALOG.md) · [`manuel-test/23-SAKLAMA-ARSIV-KOTA.md`](manuel-test/23-SAKLAMA-ARSIV-KOTA.md)

---

## Bu Faza Başlarken

1. Bu doküman
2. Kararlar — yalnız ilgili satırlar:
   ```bash
   grep -n "K-282\|K-283\|K-421" docs/KARARLAR.md
   ```
3. Aday sınırı: `grep -n "F-148" docs/ADAYLAR.md` — duplicate-sequence performans işi bu refactor'a karıştırılmaz
4. Alan hafızası: [`hafiza/test-altyapisi.md`](hafiza/test-altyapisi.md) ve [`hafiza/cekirdek-calistirma.md`](hafiza/cekirdek-calistirma.md)
5. Mimari: [`MIMARI.md`](MIMARI.md) — yalnız `IRunStore` ve in-memory store haritası

---

## Amaç

`InMemoryRunStore`, run yaşam döngüsünü, event log'unu, query/filtering'i, tree toplamlarını, istatistikleri, experiment sonuçlarını, time series ve tool usage hesaplarını 1.432 satırda taşır. Faz aynı internal sınıfı `partial` dosyalara böler. State ownership ve lock sınırları değişmez.

### Bugün ne çalışmıyor — doğrulanmış kanıt

| Kanıt | Gözlem |
|---|---|
| [`InMemoryRunStore.cs:22`](../src/AgentPrism.Core/Storage/InMemoryRunStore.cs) | Tek internal sınıf **1.432 satırdır** ve 21 davranış metodu taşır. |
| [`InMemoryRunStore.cs:58`](../src/AgentPrism.Core/Storage/InMemoryRunStore.cs) | Run yaşam döngüsü ve event append aynı dosyada başlar. |
| [`InMemoryRunStore.cs:374`](../src/AgentPrism.Core/Storage/InMemoryRunStore.cs) | Query, tenant süzme ve tree toplamları lifecycle state'iyle iç içedir. |
| [`InMemoryRunStore.cs:641`](../src/AgentPrism.Core/Storage/InMemoryRunStore.cs) | Statistics, experiment, time series ve tool analytics dosyanın yarısından fazlasını oluşturur. |

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

- [ ] State alanları tek owner'da kalır; yeni alt-store veya state kopyası yoktur
- [ ] Lifecycle, events, queries, statistics ve analytics ayrı sorumluluk dosyalarındadır
- [ ] `RunStoreContract` ve `TenantIsolationContract` dört sağlayıcı koşumunda yeşildir
- [ ] F-148 davranışı ve aday kaydı değişmeden kalır
- [ ] Public API dosyalarında fark yoktur
- [ ] Dört doğrulama kapısı sıfır uyarı verir
- [ ] `samples/AgentPrism.Api` ile gerçek `run` yapıldı, çıktı belgeye yazıldı
- [ ] `secret` taraması boş döndü
- [ ] Manuel kabul case'leri ilgili ailelere eklendi ve otomatik olanlar koşuldu
- [ ] `faz-denetim` koşuldu; 🔴 bulgu kalmadı

## Riskler

| Risk | Önlem |
|---|---|
| State'i alt nesnelere bölmek atomicity'yi değiştirir | Yalnız `partial` dosya ayrıştırması yapılır |
| Analytics helper'ı tenant süzgecinden önce çalışır | Contract testleri tenant + analytics kombinasyonunu ölçer |
| Refactor F-148 optimizasyonuyla karışır | Performans diff'i açıkça kapsam dışıdır; denetim bunu kontrol eder |

---

## Plandan Sapmalar

> Kapanışta doldurulur.

## Bu Fazda Verilen Kararlar

> Kapanışta doldurulur.

## Gerçekleşen Public API

> Kapanışta doldurulur.

## Dosya Listesi (gerçekleşen)

> Kapanışta doldurulur.

## Denetim Bulguları

> Kapanışta doldurulur.

## Sonraki Faza Devir Notu

> Kapanışta doldurulur.
