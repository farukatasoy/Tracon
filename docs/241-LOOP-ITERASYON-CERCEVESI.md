# Faz 241 — Native Agent Akışında Loop İterasyon Sınırı

> **Durum:** 📋 Planlandı (2026-10-25)
> **Kademe:** hafif — migration yok · public API'den satır silinmez (.NET public yüzeyi değişmez; SSE'ye yalnız bir olay adı eklenir) · hedef framework değişmez · güvenlik/kiracı sınırı yok · geri dönüşü pahalı karar yok
> **Plan onayı:** Bakımcı, 2026-10-25 (sohbette alınan kararlar: çerçeve adı `loop.iteration-completed`; veri `/events` ile bayt bayt aynı; konsol playground'u iterasyon ayracını gösterir)
> **Kaynak:** [ADAYLAR.md](ADAYLAR.md) · **F-544**
> **Genellik:** Loop'lu agent'ı native akışla koşturan her istemci (konsol playground'u, tüketicinin kendi arayüzü, TS ve .NET istemcisiyle yazılmış okuyucular). İş tüketici kodunda kalamaz: akışta sınır bilgisi yoktur; tüketici ancak ikinci bir bağlantıyla `/events`'i okuyup iki akışı eşleyebilir. İkinci kanıt ilk ilkedir: native akış Tracon'un belgelenmiş sözleşmesidir ve K-1006 bu eksiği yeniden açılma koşulu olarak yazdı
> **Karşı görüş:** "Cevabı isteyen istemci akışsız yolu ya da `AgentAnswerStream` kullanan yüzeyleri kullanır." Cevap: o yüzeyler yalnız son iterasyonu verir; akışın değeri iterasyonları canlı görmektir. Çerçeve ekleyicidir; bilinmeyen çerçeveyi atlayan istemci değişmez (1.x kuralı)
> **Paketler:** `Tracon.Core` (`RecordingLoopEvaluator`, `AgentRunScope` iç kancası), `Tracon.Abstractions` (`TraconRunOptions` iç kancası), `Tracon.AspNetCore` (`AgentRunStream`, OpenAPI açıklaması), `Tracon.UI` (playground)
> **Tüketici yüzeyi:** site `http-api.md` (akış çerçeveleri), `concepts/agents.md` (Loop bölümü), `guides/embedding.md` (kendi widget'ını yazan için çerçeve listesi), `ui.md` (playground) · sevk edilen: `docs/sozlesme/sse-olaylari.txt` (üretilir), OpenAPI açıklaması → `packages/tracon-client` ve `Tracon.Client` (üretilir), `CHANGELOG.md` `[Unreleased]`
> **Manuel test alanı:** `docs/manuel-test/10-ARAYUZ-AGENT-PLAYGROUND.md`

## Amaç

Loop'lu bir agent'ın native akışı (`POST /api/agents/{name}/run`) her iterasyonu
ve her geri bildirimi tek bir `update` dizisi olarak akıtır. Akışta iterasyonun
bittiğini söyleyen bir çerçeve yok. Bu yüzden akışı okuyan istemci ilk taslağı
cevap sanabilir. Sınırı söyleyen olay bugün yalnız `GET /api/runs/{id}/events`
akışında var. K-1006 bu davranışı belgeledi ve yeniden açılma koşulunu şöyle
yazdı: "Native SSE'ye iterasyon sınırı istenirse". Bu faz o koşulu karşılar.

Faz bitince her değerlendirilen iterasyondan sonra akışa bir
`loop.iteration-completed` çerçevesi girer. Çerçevenin verisi, `/events`
akışının aynı olay için yazdığı JSON ile bayt bayt aynıdır (K-991 emsali).
Playground her iterasyonun sonunda bir ayraç gösterir.

**Doğrulanmış kanıt (2026-10-25):**

| Kanıt | Gözlem |
|---|---|
| [`AgentEndpoints.cs:1050-1122`](../src/Tracon.AspNetCore/Endpoints/AgentEndpoints.cs) | Native akışın çerçeveleri: `run`, `update`, `approvals`, `inputs`, `custom` (`RunNoticeMirror`, `done`'dan hemen önce), `error`, `done`. Loop olayı yok |
| [`AgentAnswerStream.cs:21-24`](../src/Tracon.Core/Graph/AgentAnswerStream.cs) | Belgelenmiş davranış: native akış her iterasyonu akıtır; iterasyonları yalnız run olayları ayırır |
| [`RunEndpoints.cs:1356`](../src/Tracon.AspNetCore/Endpoints/RunEndpoints.cs) | `LoopIterationCompleted` yalnız `/events` akışında `loop.iteration-completed` adını alır |
| [`RecordingLoopEvaluator.cs:283`](../src/Tracon.Core/Compilation/RecordingLoopEvaluator.cs), `:325` | Olayı run'ın `RunEventWriter`'ı yazar; kayıt yoksa (`Writer` boş) olay yazılmaz |
| [`AgentTurnService.cs:760-825`](../src/Tracon.AspNetCore/Internal/AgentTurnService.cs) | Emsal: `approvals` ve `inputs` çerçeveleri `TraconRunOptions` kancasıyla (`BeforePendingApprovalIsPublished`, iç `BeforePendingToolInputIsPublished`) run'ın içinden yazılır |
| [`use-playground-run.ts:259-301`](../src/Tracon.UI/frontend/src/screens/playground/use-playground-run.ts) | Playground yalnız `run`, `update`, `approvals`, `inputs`, `error`, `done` okur; iterasyonları tek transcript'e katlar |
| [`SseEventNameSnapshotTests.cs:74`](../tests/Tracon.AspNetCore.FunctionalTests/SseEventNameSnapshotTests.cs) | Native akış adlarını kaynaktaki `WriteEventAsync(…, "<ad>", …)` **literal**'inden tarar; beklenen liste elle yazılıdır |
| **Aday metni düzeltmesi:** [`embed/client.ts:35-40`](../src/Tracon.UI/frontend/src/embed/client.ts), `:64` | Gömülebilir widget **akışsız** yolu kullanır (`Idempotency-Key`). Akışsız Loop yalnız son iterasyonu döner (K-889). Widget bu kusurdan **etkilenmez**. Aday "gömülebilir sohbet taslağı cevap sanar" diyordu; bu iddia düştü. Kalan tüketiciler: playground ve native akışı okuyan her dış istemci (TS ve .NET istemcisi yalnız genel SSE okuyucusu verir) |

## Tasarım

- **İç kanca, public üye değil.** `TraconRunOptions`'a `internal` bir kanca
  eklenir: `AfterLoopIterationRecorded` (`Func<RunEvent, CancellationToken, ValueTask>?`).
  Bu kanca `BeforePendingToolInputIsPublished` deseninin aynısıdır. Kayıt katmanı
  kancayı **yalnız bu run'ın** `AgentRunScope`'una taşır.
- **Yazım yeri.** `RecordingLoopEvaluator.WriteIterationEventAsync`,
  `AppendAsync`'in döndürdüğü `RunEvent`'i kancaya verir. Kanca, ölçütlerin
  `try/catch`'inin **dışında** ve olay yazıldıktan **sonra** çağrılır. Bu yüzden
  kancanın hatası ölçüt hatası (`failedCriterion`) olarak sayılmaz.
- **Sıra.** Evaluator, MAF'ın akışlı döngüsünde iki iterasyon arasında koşar. Uç
  noktanın `await foreach` döngüsü o sırada `MoveNextAsync` içinde bekler. Çerçeve
  bu yüzden önceki iterasyonun son `update`'inden sonra ve sonraki iterasyonun
  ilk `update`'inden önce yazılır. Ara katmanda kanal ya da arka plan pompası yok
  (ölçüldü: `src/Tracon.Core/Recording` ve `AgentTurnService` içinde `Channel` yok).
  `SseWriter`'ın yazım kapısı (`_writeGate`) keep-alive ile yarışı zaten çözer.
  **Uygulamanın ilk ölçümü:** sıra iddiasını bir fonksiyonel test kanıtlar.
- **Veri.** Uç nokta olayı `RunNoticeMirror`'ın serileştiricisiyle yazar.
  Yazım `/events` akışının yazımıyla aynıdır. Bir test iki akışın aynı olay için
  yazdığı JSON'u bayt bayt karşılaştırır.
- **Ad literal'dir.** Uç nokta `WriteEventAsync(sequence++, "loop.iteration-completed", …)`
  çağrısını literal adla yazar. Ad bir sabitten gelirse sözleşme taraması onu
  görmez. `SseEventNameSnapshotTests.cs:74`'teki elle yazılmış liste güncellenir
  ve `sse-olaylari.txt` testle yeniden üretilir.
- **Kapsam dışı yüzeyler.** Kanca yalnız native akışta kurulur. Akışsız yol,
  kuyruklu run (`Prefer: respond-async`), workflow adımı, OpenAI uyumlu uçlar,
  A2A, AG-UI ve ses yolları kancayı kurmaz. `AgentAnswerStream` zaten yalnız son
  iterasyonu verir (K-1006). Alt agent'ın kendi Loop'u ebeveynin akışına çerçeve
  **yazmaz**: kanca çocuk `AgentRunScope`'una geçmez.
- **Kayıt kapalıysa çerçeve yok.** Çerçeve kayıtlı olayın aynasıdır. `Writer`
  yoksa olay da çerçeve de yoktur. K-991'deki `custom` çerçeveleri de bu kurala
  uyar; site bunu yazar.
- **Playground.** `loop.iteration-completed` çerçevesi transcript'e bir ayraç
  ekler. Ayraç iterasyon numarasını ve kararı gösterir: `continuedBy` (devam),
  `ceilingReached` (tavan), `failedCriterion` (ölçüt hatası). Metinler
  `locales/en.ts` ve `tr.ts`'e girer. Geri bildirim metni gösterilmez, çünkü olay
  onu taşımaz.
- **Site cümlesi.** Cevap, `continued: false` taşıyan son
  `loop.iteration-completed` çerçevesinden önceki iterasyondur. Varsayılan tavan
  yolunda (`ceilingReached: true`) cevap, o çerçeveden **sonraki** iterasyondur,
  çünkü tavandaki tur değerlendirilmez (hafıza: `maf-harness-ve-loop.md`).

## Bitiş Ölçütleri (DoD)

- [ ] Loop'lu agent'ın native akışında her değerlendirilen iterasyondan sonra bir `loop.iteration-completed` çerçevesi gelir; Loop'suz agent'ın akışı değişmez
- [ ] Çerçeve verisi `GET /api/runs/{id}/events`'teki aynı olayla bayt bayt aynıdır (test)
- [ ] Alt agent'ın Loop'u ebeveyn akışına çerçeve yazmaz (test)
- [ ] `docs/sozlesme/sse-olaylari.txt` `agent-stream loop.iteration-completed` satırını taşır; `SseEventNameSnapshotTests` yeşil
- [ ] Uç noktanın OpenAPI açıklaması çerçeveyi adlandırır; iki istemci yeniden üretildi
- [ ] Playground ayracı gösterir; `en.ts` + `tr.ts` eksiksiz; bundle payı ölçüldü ve yazıldı (bugün 224,1 KB gzip / 250 KB, 2026-10-23 yapıtı)
- [ ] `samples/Tracon.Api` `reviewed-writer` agent'ı (`Program.cs:741-762`, `MaxIterations = 3`) native akışla koşuldu; çerçeve dizisi belgeye yazıldı
- [ ] Site sayfaları (Tüketici yüzeyi satırı) ve `CHANGELOG.md [Unreleased]` güncel
- [ ] Kapanış kapısı yeşil: `python3 scripts/kapi.py kapanis --taban <faz öncesi commit>`

## Testler

| Davranış | Seviye ([test-seviyeleri](../.agents/ortak/test-seviyeleri.md)) | Test |
|---|---|---|
| Çerçeve iki iterasyonun `update`'leri arasında gelir; sayısı değerlendirilen iterasyon sayısına eşittir (tavan yolunda bir eksik) | Fonksiyonel (gerçek host, `FakeModelProvider`, HTTP SSE) | `LoopIterationFrameTests` |
| Veri `/events` ile bayt bayt aynıdır | Fonksiyonel | `LoopIterationFrameTests` |
| Loop'suz agent'ta çerçeve yok | Fonksiyonel | `LoopIterationFrameTests` |
| Alt agent'ın Loop'u ebeveyn akışına sızmaz | Fonksiyonel | `LoopIterationFrameTests` |
| Kancanın hatası ölçüt hatası sayılmaz (`failedCriterion` boş kalır) | Birim (`RecordingLoopEvaluator`, bugün `LoopEvaluatorRegistryTests` içinde sınanır) | `LoopEvaluatorRegistryTests` |
| Akışsız yol ve kuyruklu run değişmez | Fonksiyonel (mevcut testler değişmeden yeşil) | `HarnessLoopTests`, `LoopAnswerSurfaceTests` |
| Ad sözleşme dosyasına girer | Sözleşme | `SseEventNameSnapshotTests` |
| Playground ayracı çizer ve metinleri iki dilde vardır | Birim (Vitest) + E2E | `playground.test.tsx` · playground E2E |

Beş soru: iptal — istemci giderse uç noktanın bugünkü
`OperationCanceledException` dalı geçerlidir; kanca yeni bir iptal yolu açmaz ·
eşzamanlılık — kanca evaluator'ın akışında koşar; yazım `SseWriter` kapısından
geçer · boş/aşırı girdi — `MaxIterations = 1` olay üretmez, çerçeve de yok
(hafıza notu) · başka kiracı — çerçeve yalnız aynı isteğin run'ından gelir ·
alt sistem hatası — kayıt kapalıysa çerçeve yok; `AppendAsync` hatası bugünkü
gibi davranır (değişmez). `🤖` case: playground'da `reviewed-writer` koşumu ve
ayraçların görsel kontrolü.

## Açık Sorular

| # | Soru | Öneri |
|---|---|---|
| 1 | Kayıt kapalıyken (`Writer` boş) çerçeve yine de yazılsın mı? | Hayır. Çerçeve kayıtlı olayın aynasıdır (K-991). İhtiyaç ölçülürse ayrı karar |

## Plandan Sapmalar

> Çalışırken dolar; kapanışta commit mesajına taşınır.

## Bu Fazda Verilen Kararlar

> Dört kategoriden biri `docs/KARARLAR.md`'ye; gerisi kapanış commit mesajına.
> Beklenen kayıt: native akışın `loop.iteration-completed` çerçevesi *(kategori:
> public-api; alternatif: `loop.iteration` adı ve dar veri — iki akışın adları
> ayrışır)*. K-1006'nın yeniden açılma koşulu bu kayıtla kapanır.

## Süreç Ölçümü

| Metrik | Değer |
|---|---|
| Plan revizyonu sayısı | |
| Düzeltme turu sayısı | |
| 🔴 bulgu: gerçek / gürültü / araştırılacak | |
| Fazın ürettiği regresyon | |
| Faz kapandıktan sonra bulunan kusur | ölçülmedi (kapanış anı) |

## Denetim Bulguları

> `faz-denetim` çıktısı; 🟢 kapanışta `ADAYLAR.md`'ye gider.
