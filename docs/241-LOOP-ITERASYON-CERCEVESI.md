# Faz 241 — Native Agent Akışında Loop İterasyon Sınırı

> **Durum:** ✅ Tamamlandı (2026-10-25)
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

- [x] Loop'lu agent'ın native akışında her değerlendirilen iterasyondan sonra bir `loop.iteration-completed` çerçevesi gelir; Loop'suz agent'ın akışı değişmez — `LoopIterationFrameTests`: 3 iterasyon → 3 çerçeve; tavan yolu → 1 çerçeve (2 tur); Loop'suz agent (harness'lı ve harness'sız) yalnız `run` · `update` · `done` akıtır
- [x] Çerçeve verisi `GET /api/runs/{id}/events`'teki aynı olayla bayt bayt aynıdır (test) — `The_frame_data_is_the_event_the_events_stream_serves_byte_for_byte`
- [x] Alt agent'ın Loop'u ebeveyn akışına çerçeve yazmaz (test) — `A_sub_agents_loop_writes_to_its_own_run_and_not_to_the_stream_above_it`: çocuk run'ı 2 `LoopIterationCompleted` yazdı, ebeveyn akışında 0 çerçeve. Test kancanın çocuğa sızmadığını `depth == 0` korumasız kanıtlamaz: çocuk seçenekleri kancayı zaten taşımaz (`ChildAgentInvoker.CreateChildOptions`)
- [x] `docs/sozlesme/sse-olaylari.txt` `agent-stream loop.iteration-completed` satırını taşır; `SseEventNameSnapshotTests` yeşil
- [x] Uç noktanın OpenAPI açıklaması çerçeveyi adlandırır; iki istemci yeniden üretildi — `docs/openapi/tracon.json`, `schema.ts` ve `TraconApiClient.g.cs` yalnız açıklama satırı kadar değişti
- [x] Playground ayracı gösterir; `en.ts` + `tr.ts` eksiksiz; bundle payı ölçüldü ve yazıldı: 224,5 KB gzip / 250 KB (öncesi 224,1; +0,4 KB)
- [x] `samples/Tracon.Api` `reviewed-writer` agent'ı (`Program.cs:741-762`, `MaxIterations = 3`) native akışla koşuldu; çerçeve dizisi belgeye yazıldı — `MT-UIAG-066` Koşum satırı: `run` · 7 `update` · `loop.iteration-completed` (#1, `draft-reviewed`) · `role: "user"` geri bildirimi + 19 `update` · `loop.iteration-completed` (#2, durdu) · `done`
- [x] Site sayfaları (Tüketici yüzeyi satırı) ve `CHANGELOG.md [Unreleased]` güncel
- [x] Kapanış kapısı yeşil: `python3 scripts/kapi.py kapanis --taban 77eb1ea6` — 27.233 test, 39 modül, 0 kırmızı (619 sn). İlk iki koşum kırmızıydı: ekran görüntüsü damgası (UI kaynağı değişti; görüntüler yeniden alındı, dört anasayfa kırpımı açılıp doğrulandı, pin'ler yenilendi) ve altı test (benim riskli `GetByText` locator'ım, bir test yorumundaki Türkçe sözcük, elle koştuğum `vite build`'in sildiği `wwwroot/embed`). Tüketici senkronu: `npm run check` yeşil, `dokuman-bakim.py --site-denetle` üç kural karşılandı

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

- **Kanca `WriteIterationEventAsync`'in içinde değil, `ExtraUsage` toplamından sonra çağrılır.** Plan kancayı olayı yazan metoda koyuyordu. `FailRun` yolunda toplam `failsRun` dalında run'a eklenir; kanca ondan önce atsaydı run harcadığı usage'ı yitirirdi. `WriteIterationEventAsync` olayı döndürür, `EvaluateAsync` sırayla toplamı ekler, kancayı çağırır, ölçüt/tavan istisnasını fırlatır. Sonuç: kanca hatası ölçüt hatası olmaz ve toplamı bozmaz; kanca istisna atarsa o istisna ölçütün ya da tavanın istisnasını ezer.
- **"Kancanın hatası ölçüt hatası sayılmaz" testi birim değil fonksiyoneldir.** Plan `LoopEvaluatorRegistryTests`'te birim test diyordu; o dosya yalnız kaydı sınar, `LoopContext` hiçbir testte doğrudan kurulmaz. Test `LoopIterationFrameTests` içinde: kanca atar, varsayılan `OnCriterionError = Stop` altında run `Failed` olur ve olayın `failedCriterion` alanı boş kalır. Kanca ölçütlerin `try/catch`'i içinde olsaydı `Stop` onu yutardı ve run `Completed` kalırdı.
- **Plana girmeyen eklemeler.** `RunNoticeMirror.Serialize` (iki akışın tek serileştiricisi); `concepts/runs.md` çerçeve tablosu (iki sözleşme tablosunda yeni ad eksik kalırdı); `FailRun` tavanı testi (son çerçeve `error` çerçevesinden hemen önce gelir, `failsRun: true`); `OpenedTurn.RunStreamingAsync` yeni zorunlu parametre alır, AG-UI `null` geçer.
- **Geri bildirim metni akışta VAR.** Plan "geri bildirim metni gösterilmez, çünkü olay onu taşımaz" diyordu. Olay taşımaz, ama iki çerçevenin arasındaki `update` bir `role: "user"` mesajıdır ve Loop'un modele verdiği geri bildirimi taşır (`Continue working on the request…`, `Reviewer findings (round 1): …`). Transcript `update`'in rolünü okumaz; metin ayraçtan sonraki iterasyonun paragrafına katılır. Ayraç yine de iki metni ayırır, yani eski görünümden kötü değildir; geri bildirimi gizleme ya da ayrı gösterme kararı bu fazın dışında kaldı (🟢, `ADAYLAR.md`).
- **Denetim üç 🟡 buldu; ortak kök: çerçeve store'un sunduğu olaydan değil, `AppendAsync`'in bellekte döndürdüğü olaydan üretilir.** (1) Zaman damgası: PostgreSQL `timestamptz` mikrosaniye tutar, olay 100 ns'lik; `/events` ile canlı çerçeve 7. hanede ayrılırdı ("bayt bayt aynı" yalnız kayıpsız store'da doğruydu). `RunEventWriter` artık damgayı tam mikrosaniyeye keser (`AuditChainHasher.TruncateToMicroseconds`, `SqlAuditLog` emsali); kayıtlı `Timestamp` her store'da mikrosaniyedir. PostgreSQL'de koşulmadı: kanıt SQLite'lı bayt eşitliği testi, mikrosaniye birim testi ve emsal. (2) Store olayı reddederse yazıcı kapanır ama olay yine döner; kanca artık `writer.IsDisabled` iken çağrılmaz, yani `/events`'te olmayan çerçeve yoktur (`A_store_that_refuses_the_event…`; kapı kaldırılınca düşer, ölçüldü). (3) `RecordToolPayloads` kapalıyken olayın `payload`'ı `null`'dır; veri değiştirilmedi, üç yüzeye yazıldı, muafiyet kararı F-682.
- **Birim test ilk sürümde yanlış sebeple yeşildi.** `Rig.StartAsync` scope'u kendi gövdesinde `SetCurrent` ile kurunca atama çağırana akmadı; kanca hiç çalışmadı ve "hiçbir şey vermez" iki test geçti. Pozitif kontrol (olay verilen test) dördünü düşürerek gösterdi. Scope artık evaluator'ı bekleyen metodun kendi gövdesinde kurulur.
- **Yerel PostgreSQL'e kısa bağlantı.** Örnek uygulamanın ilk açılışı `faz-tamamlama` Adım 2'nin dört boş ortam değişkeni olmadan koştu; `user-secrets` içindeki bağlantı dizesi (`localhost:55432`, `agentprism` şeması) devreye girdi ve uygulama ~12 sn sonra durdurulana kadar migration kaydı okudu, kuyruk sorgusu çalıştırdı. Run koşulmadı. Sonraki tüm koşumlar boş değişkenlerle ve geçici SQLite dosyasıyla koştu (günlükte `Npgsql` satırı 0). Bekleyen bir migration varsa o veritabanına uygulanmış olabilir.

## Bu Fazda Verilen Kararlar

> Dört kategoriden biri `docs/KARARLAR.md`'ye; gerisi kapanış commit mesajına.

- **K-1086** *(kategori: public-api; alternatif: `loop.iteration` adı ve dar veri; ayrıca çerçeveyi `custom` gibi `done` öncesinde toplu yazmak)* — native akışın `loop.iteration-completed` çerçevesi. K-1006'nın yeniden açılma koşulu bu kayıtla kapandı.
- Kancanın hata davranışı (run'ı düşürür, ölçüt hatası olmaz) ve kancanın yalnız kök scope'a girmesi kategori dışıdır: kapanış commit mesajına gider.

## Süreç Ölçümü

| Metrik | Değer |
|---|---|
| Plan revizyonu sayısı | 0 (plan yeniden yazılmadı; 7 sapma kaydı) |
| Düzeltme turu sayısı | 2 (ekran görüntüsü kapısı · denetimin üç 🟡'si) |
| 🔴 bulgu: gerçek / gürültü / araştırılacak | 0 / 0 / 0 (🟡: 3 gerçek) |
| Fazın ürettiği regresyon | |
| Faz kapandıktan sonra bulunan kusur | ölçülmedi (kapanış anı) |

## Denetim Bulguları

> `faz-denetim` çıktısı (2026-10-25, taze bağlamlı `faz-denetcisi`); 🟢 kapanışta `ADAYLAR.md`'ye gitti.

- 🔴: yok. Kanca kök scope'a hapsolur; konumu ve hata yolları (`FailRun`, ölçüt hatası, iptal) doğru; `sequence++` kapılıdır; testler düşebilir.
- 🟡 ×3 (ortak kök, bkz. Plandan Sapmalar): zaman damgası PostgreSQL'de ayrışır · store yazamayınca `/events`'te olmayan çerçeve · `RecordToolPayloads` kapalıyken `payload: null`. İlk ikisi kodla kapandı (testle), üçüncüsü belgelendi ve F-682'ye gitti.
- 🟢 → `ADAYLAR.md`: F-681 (geri bildirim `update`'i playground'da cevaba yapışır; site cümlesi eklendi) · F-682 (`LoopIterationCompleted` payload muafiyeti) · F-683 (`depth == 0` koruması ve istemci kopması testsiz). Ölçüt hatası çerçevesi testi ve `AgentAnswerStream` yorumu aynı turda kapandı.
