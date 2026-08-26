# Faz 107 — Run Kayıt Akışı Ayrıştırma

> **Durum:** ✅ Tamamlandı (2026-08-26)
> **Kaynak:** [`kesif/2026-08-23-yapisal-sorun-envanteri.md`](kesif/2026-08-23-yapisal-sorun-envanteri.md) — **kalem 17**. Bu faz bir `F-NN` adayından gelmez
> **Önkoşul:** [Faz 106](arsiv/fazlar/106-AGENT-DERLEYICI-AYRISTIRMA.md) — teknik zorunluluk yoktur; yapısal tur compiler'dan runtime wrapper'a ilerler
> **Paketler:** `AgentPrism.Core`
> **Yeni paket:** Yok · **Migration:** Yok
> **Public API:** Büyümüyor. `RunRecordingAgent` constructor ve davranışı değişmez; `PublicAPI.Shipped.txt` girdisi bugün **0**
> **Tüketici yüzeyi:** Yok. Public imza, XML metni ve observable contract değişmez
> **Manuel test alanı:** [`manuel-test/11-ARAYUZ-RUN-SESSION-SSE.md`](manuel-test/11-ARAYUZ-RUN-SESSION-SSE.md) · [`manuel-test/12-GOZLEMLENEBILIRLIK-MALIYET.md`](manuel-test/12-GOZLEMLENEBILIRLIK-MALIYET.md)

---

## Bu Faza Başlarken

1. Bu doküman
2. Kararlar — yalnız ilgili satırlar:
   ```bash
   grep -n "K-154\|K-157\|K-243\|K-421" docs/KARARLAR.md
   ```
3. Alan hafızası: [`hafiza/cekirdek-calistirma.md`](hafiza/cekirdek-calistirma.md), [`hafiza/olcum-kota-ve-secenekler.md`](hafiza/olcum-kota-ve-secenekler.md) ve [`hafiza/test-altyapisi.md`](hafiza/test-altyapisi.md)
4. Mimari: [`MIMARI.md`](MIMARI.md) — yalnız run sequence ve `RunRecordingAgent` notları

---

## Amaç

`RunRecordingAgent`, iki yürütme gövdesini, ambient scope/span yaşamını, store yazımını, event sink yayınını, maliyeti, kotayı, online eval sampling'i ve hata dönüşümünü 1.331 satırda toplar. Faz bu sorumlulukları `partial` dosyalara ayırır. Span ve `AsyncLocal` yazım yerlerini değiştirmez.

### Bugün ne çalışmıyor — doğrulanmış kanıt

| Kanıt | Gözlem |
|---|---|
| [`RunRecordingAgent.cs:31`](../src/AgentPrism.Core/Recording/RunRecordingAgent.cs) | Public wrapper **1.331 satırdır** ve 22 davranış metodu taşır. |
| [`RunRecordingAgent.cs:188`](../src/AgentPrism.Core/Recording/RunRecordingAgent.cs) | Akışsız giriş gövdesi scope, span, cancellation ve completion sırasını aynı yerde kurar. |
| [`RunRecordingAgent.cs:297`](../src/AgentPrism.Core/Recording/RunRecordingAgent.cs) | Streaming gövde her `MoveNextAsync` öncesi ambient scope'u yeniden yazar; bu yapısal zorunluluktur. |
| [`RunRecordingAgent.cs:945`](../src/AgentPrism.Core/Recording/RunRecordingAgent.cs) | Completion tek gövdede store, usage, cost, metrics, quota, webhook ve eval işlerini birleştirir. |

> Kanıtlar 2026-08-26 tarihinde doğrulandı.

## 107.1 — Entry-point gövdeleri yerinde kalır

`RunRecordingAgent` `public sealed partial class` olur. Constructor, `RunCoreAsync` ve `RunCoreStreamingAsync` ana dosyada kalır.

🚨 `Activity` ve `AgentPrismRunContext` başlangıcı async helper'a çıkarılmaz. Streaming döngüsündeki ambient yazım her `MoveNextAsync` öncesinde kalır. `AmbientWriteSiteTests` taban çizgisi yalnız dosya taşıması nedeniyle güncellenir; yazım yeri sayısı artmaz.

> **Faz 106'dan devir:** aynı `partial` ayrıştırma deseni orada denendi ve
> işe yaradı — önce hiç `using`/pragma eklemeden metotları hedef dosyaya taşı,
> sonra `dotnet build` çalıştır. `TreatWarningsAsErrors=true` eksik `using`'i
> `CS0246`, gereksiz pragma'yı `IDE0079` olarak geri verir; ikisi de tahmin
> etmekten daha hızlı ve kesin. `RunRecordingAgent.cs` bugün hiç `MAAI001`
> taşımıyor (kontrol edildi), o yüzden bu fazda pragma sınırı sorunu
> çıkması beklenmez — ama teknik yine de geçerlidir.

## 107.2 — Yaşam döngüsü ve kalıcılık

Private gövdeler şu dosyalara ayrılır:

- run hazırlama, scope üretimi ve cancellation kaydı;
- başlangıç, input, attachment ve tool event yazımı;
- completion, usage/cost birleştirme ve error mapping;
- sink, quota, webhook, metrics ve online evaluation bildirimleri.

Gözlemlenebilirlik store veya sink hatasında run'ı bozamaz. Kök/child ayrımı ve `scope.Depth == 0` koşulları aynı yerde kalır.

## 107.3 — İki yolun simetri kapısı

Akışlı ve akışsız yollar için ortak outcome matrisi eklenir: completed, failed, canceled ve awaiting approval. Her hücre status, event sonu, cancellation cleanup ve span kapanışını doğrular. Bu matris, helper taşımasının yalnız bir yolu değiştirmesini yakalar.

## Planlanan Public API

Yeni public üye yoktur. Constructor parametreleri ve sırası değişmez.

### HTTP `endpoint`'leri

Yok.

### Arayüz payı

Yok.

## Planlanan Dosya Listesi

```text
src/AgentPrism.Core/Recording/
├── RunRecordingAgent.cs
├── RunRecordingAgent.Lifecycle.cs
├── RunRecordingAgent.Persistence.cs
├── RunRecordingAgent.Completion.cs
└── RunRecordingAgent.Notifications.cs
tests/AgentPrism.Core.UnitTests/Recording/
└── RunRecordingAgentOutcomeMatrixTests.cs
```

## Hata Modları ve Testler

| Ne bozulabilir | Seviye | Test sınıfı |
|---|---|---|
| Streaming scope yalnız ilk turda yazılır ve iç çağrıda kaybolur | Fonksiyonel | streaming ambient/span regresyon testleri + `AmbientWriteSiteTests` |
| Akışlı ve akışsız yol farklı status/event üretir | Birim / fonksiyonel | `RunRecordingAgentOutcomeMatrixTests` |
| İptal linked CTS yerine gelen token'a bağlanır | Birim | dış iptal ve caller iptali senaryoları |
| Store veya sink hatası gerçek run'ı keser | Fonksiyonel | store/sink failure testleri |
| Child run kota veya webhook'u ikinci kez sayar | Birim | root/child completion testleri |
| Başka kiracının context'i run kaydına sızar | Fonksiyonel | tenant context run testleri |
| Boş usage veya bilinmeyen content tipi completion'ı kırar | Birim | null/unknown content senaryoları |

## Manuel Kabul Case'leri

| # | Ön koşul | Adımlar | Beklenen sonuç |
|---|---|---|---|
| 1 | Sample + sahte provider | Akışlı ve akışsız aynı prompt'u çalıştır | İki run `Completed`; event sonu ve usage tutarlıdır |
| 2 | Devam eden run | Cancel endpoint'ini çağır | Run `Canceled`; cancellation registry temizlenir |
| 3 | OTel listener | Gerçek run yap | İç model/tool span'leri `agentprism.run` span'inin çocuğudur |

## Açık Sorular

Yok. Yeni lifecycle abstraction kapsam dışıdır.

## Bitiş Ölçütleri (DoD)

- [x] Entry-point gövdeleri span/scope yazımını kendi gövdelerinde tutar
- [x] `AmbientWriteSiteTests` yazım yeri sayısının artmadığını kanıtlar
- [x] Outcome matrisi streaming ve non-streaming dört sonucu kapsar
- [x] Store/sink failure testleri run davranışının bozulmadığını gösterir
- [x] `git diff -- 'src/*/PublicAPI.*.txt'` boş döner
- [x] Dört doğrulama kapısı sıfır uyarı verir
- [x] `samples/AgentPrism.Api` ile gerçek `run` yapıldı, çıktı belgeye yazıldı
- [x] `secret` taraması boş döndü
- [x] Manuel kabul case'leri ilgili ailelere eklendi ve otomatik olanlar koşuldu
- [x] `faz-denetim` koşuldu; 🔴 bulgu kalmadı

## Riskler

| Risk | Önlem |
|---|---|
| Görünüşte mekanik taşıma ambient akışı bozar | Entry-point gövdeleri taşınmaz; APG0501 ve ambient write kapısı koşar |
| Completion sırası değişir | Outcome matrisi ve store/sink failure testleri sıra etkisini ölçer |
| Constructor collaborator nesnesine sarılıp public davranış değişir | Constructor imzası aynen kalır; yeni public collaborator eklenmez |

---

## Plandan Sapmalar

Plana büyük ölçüde birebir uyuldu. Dosya adları (`RunRecordingAgent.Lifecycle.cs`,
`.Persistence.cs`, `.Completion.cs`, `.Notifications.cs`) planla aynen eşleşti.
Plan dört dosyanın sorumluluğunu düz yazıyla anlatıyordu, hangi metodun hangi
dosyaya gideceğini vermiyordu; uygulama sırasında şu eşleme yapıldı:

- `Lifecycle.cs`: `PrepareRun`, `CreateScope`, `ResolveAttribution` ve
  `RunStart`/`RunScope` `record` tipleri. `ResolveAttribution` plandaki dört
  bulletten hiçbirine açıkça girmiyordu; `PrepareRun`'ın kendi gövdesinden
  çağrıldığı ve aynı "run hazırlama" sorumluluğunu taşıdığı için buraya kondu.
  İki `record` tipi de burada kaldı — `PrepareRun`/`CreateScope`'un ürettiği
  veri sözleşmesi oldukları için üretildikleri dosyada durmaları doğal.
- `Persistence.cs`: `WriteRunStartAsync`, `WriteDocumentAttachedEventsAsync`,
  `SaveInputAsync`, `WriteContentsAsync`, `WriteToolCallAsync`,
  `WriteToolResultAsync`, `FormatArguments`, `GetSessionId`, `ExtractQuery`,
  `IsDocumentChannelMessage`.
- `Completion.cs`: `CompleteAsync`, `ApprovalError`,
  `InvokeBeforePendingApprovalAsync`, `MergeUsage`, `AddOrNull`, `ToRunUsage`,
  `ToRunError`.
- `Notifications.cs`: `RecordQuotaAsync`, `SampleForOnlineEvalAsync`,
  `PublishRunEventAsync`.

`RunRecordingAgent.cs` constructor, `RunCoreAsync`, `RunCoreStreamingAsync`
dışında hiçbir gövde taşımadı — 1331 satırdan 464 satıra indi.

Taşıma sırasında hiçbir `using`/pragma tahminle eklenmedi; Faz 106'nın devrettiği
teknik izlendi — önce hiç `using` eklemeden taşı, sonra `dotnet build` çalıştır.
`CS0246` iki dosyada eksik `using Microsoft.Agents.AI;`/`Microsoft.Extensions.Logging;`
gösterdi, ilk denemede düzeltildi; `IDE0079`/`MAAI001` hiç çıkmadı (dosya bugün
hiç pragma taşımıyordu, plan bunu doğru öngörmüştü).

107.3'ün istediği outcome matrisi `RunRecordingAgentOutcomeMatrixTests.cs`
olarak eklendi; dört sonucun (Completed, Failed, Canceled, AwaitingApproval)
ikisi (Canceled, AwaitingApproval) `RunEventWriter.CompleteAsync`'in **var
olan** davranışı gereği kapanış olayı olarak `RunEventType.RunFailed` yazıyor
— bu Faz 107'nin ürettiği bir şey değil, dokunulmayan `RunEventWriter.cs`'in
önceden beri sahip olduğu bir quirk (default `switch` kolu). Matris bu
gerçek davranışı PİNLEDİ, "doğru" olanı değil — düzeltme bu fazın kapsamı
dışında.

## Bu Fazda Verilen Kararlar

Yok. Faz saf bir kod taşıma işiydi; public API/compatibility contract,
güvenlik/kiracı sınırı veya kalıcı veri kararı gerektiren bir seçim yapılmadı.

## Gerçekleşen Public API

Değişiklik yok. `git diff -- 'src/*/PublicAPI.*.txt'` boş döndü; constructor
imzası ve davranışı birebir korundu.

## Dosya Listesi (gerçekleşen)

```text
src/AgentPrism.Core/Recording/
├── RunRecordingAgent.cs                (1331 -> 464 satır)
├── RunRecordingAgent.Lifecycle.cs      (yeni)
├── RunRecordingAgent.Persistence.cs    (yeni)
├── RunRecordingAgent.Completion.cs     (yeni)
└── RunRecordingAgent.Notifications.cs  (yeni)
tests/AgentPrism.Core.UnitTests/
├── Recording/RunRecordingAgentOutcomeMatrixTests.cs  (yeni)
└── Architecture/ambient-write-baseline.txt           (`PrepareRun` satırı dosya taşıması nedeniyle güncellendi)
```

## Denetim Bulguları

Taze bağlamlı bir `general-purpose` agent, `faz-denetim` skill'ini uygulayarak
koştu (2026-08-26). Yöntem: silinen bloğun tam metni (`git diff`) her yeni
partial dosyanın içeriğiyle satır satır karşılaştırıldı — hepsi karakter
düzeyinde birebir taşınmış bulundu; `dotnet build src/AgentPrism.Core` üç
hedefte de bağımsız olarak yeniden koşuldu.

**🔴 ve 🟡 yok.**

**🟢 (aday listesine, F-164 olarak eklendi):** Örnek uygulamada
(`samples/AgentPrism.Api`) `RunTraceCollector` span örneklemesi hiç
tutmuyor — Faz 107'nin dokunmadığı bir alan (DI kaydı/config bağlama),
kod diff'iyle ilgisiz.

## Sonraki Faza Devir Notu

- Aynı taşıma deseni Faz 108'de (`InMemoryRunStore` ayrıştırması) tekrar
  geçerli: önce hiç `using` eklemeden taşı, `dotnet build` derleyiciye
  eksik/gereksiz `using`'i söyletsin.
- `AmbientWriteSiteTests`'in `<path>:<method>` anahtarı dosya taşımasına
  duyarlıdır — bir metot yeni bir `partial` dosyaya taşınırsa baseline'da
  yalnız o satırın **yolu** değişir, sayısı değişmez;
  `AGENTPRISM_AMBIENT_WRITE_REFRESH=1` ile yenile ve `REPLACE ME` yer
  tutucusunu eski satırdaki gerekçeyle değiştir — otomatik yenileme
  gerekçeyi KORUMAZ, yalnız yeni yolu placeholder'la yazar.
- Örnek uygulamada (`samples/AgentPrism.Api`) span örneklemesi
  (`RunTraceCollector`/`ITraceStore`) ~45 ayrı denemede hiç tutmadı —
  `SuccessSampleRatio=1` ortam değişkeni de etkisizdi. Kök neden
  ölçülmedi; Faz 107'nin dokunmadığı bir alan (DI kaydı/config bağlama).
  Otomatik testler (`ObservabilityTests`) aynı mekanizmayı izole biçimde
  doğru çalıştırıyor, yani bu örnek-uygulamaya özgü bir sorun — bir
  sonraki oturum `RunTraceCollector`'ın örnek uygulamada gerçekten
  `IsCollecting=true` olup olmadığını ölçebilir. Ayrıntı:
  `docs/manuel-test/12-GOZLEMLENEBILIRLIK-MALIYET.md` MT-OBS-050.
