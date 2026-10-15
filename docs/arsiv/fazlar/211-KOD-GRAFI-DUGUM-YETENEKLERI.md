# Faz 211 — Kod Grafında Düğüm Yetenekleri ve Revizyon Döngüsü

> **Durum:** ✅ Tamamlandı (2026-10-15)
> **Plan onayı:** kapsam kararları bakımcıdan, 2026-10-15 (tam paket · yeni `GetWorkflowAgentStep` · agent adımı akışsız · SDK sabiti ayrı iş). Yazılı planın onayı: uygulama talimatı "Faz 211 geliştir" ve "commit atabilirsin" ile verildi (2026-10-15); dört Açık Soru bakımcıya ayrıca sorulmadı, planın önerileri uygulandı (1-B · 2-A · 4-A) ve 3-A önerisi ölçümle çürütüldüğü için B uygulandı (Plandan Sapmalar 3).
> **Kaynak:** **F-537**, **F-538**, **F-539**
> **Genellik:** İnsan onayı ve revizyon geri kenarı taşıyan her workflow kod grafı ister, çünkü saklanan tanım yalnız doğrusal agent/fonksiyon zinciridir ([`WorkflowNodeReference.cs:19-23`](../../../src/Tracon.Abstractions/Workflows/WorkflowNodeReference.cs)). Faz 196 (düğüm süresi), 199 (düğüm adlı maliyet), 200 (agent sürüm sabiti) ve 210 (bütçe kapısı) yetenekleri yalnız saklanan zincire ulaştı; kod grafı yazan her tüketici bunları kaybeder. Bütçe kapısı tüketici kodunda kurulamaz, çünkü `AgentRunBudget.IsNodeStartExhausted` internal'dır. Kanıt: iki tüketici hattı (video yol A, podcast insan onayı genişlemesi) ve örnek uygulamanın kendi `BuildSummarizeAndApprove` grafı ([`samples/Tracon.Api/Program.cs:1612-1670`](../../../samples/Tracon.Api/Program.cs)) aynı boşluğa düşer.
> **Karşı görüş:** (1) "Kod grafı yazan tüketici kendi `Executor`'ını yazar." Cevap: süre, retry, katı ayar okuması ve düğüm adlı maliyet kopyalanabilir, ama bütçe kapısı kopyalanamaz. `AIAgentBinding` geri kenarda `TurnToken` ister; unutulursa run hata vermeden çıktısız biter (ölçüldü, § Bugün ne çalışmıyor). (2) "Onay düğümü saklanan tanıma girsin; operatör düzenlesin." Cevap: bu faz dışı. K-131 ve K-901 ile çatışır, konsol editörü ve kalıcı veri değişir; F-411 onayı `Loop` ölçütüne taşıma yönündedir.
> **Önkoşul:** Faz 196, 199, 200, 208, 209, 210 (kapandı) · `kusur-giderme` F-536 (Loop'lu agent akışta yalnız son iterasyonu verir; 2026-10-15)
> **Paketler:** `Tracon.Workflows`, `Tracon.Core`, `Tracon.AspNetCore`
> **Yeni paket:** Yok · **Migration:** Yok
> **Public API:** Büyüyor — `1.0.0` GA'dan (K-603) önce ucuz. `PublicAPI.Shipped.txt` dosyaları boştur (`wc -l src/*/PublicAPI.Shipped.txt` uygulama başında ölçülür)
> **Tüketici yüzeyi:** site: yeni `guides/review-and-revise-workflow.md`; `concepts/workflows.md` (kod workflow'u bölümü), `capabilities.md` (workflow satırı), `reference/configuration.md` (`AgentGraph:ChildDeadline` satırı, F-528) · sevk edilen: `GetWorkflowFunction` ve `GetWorkflowAgentStep` XML `<example>`, `src/Tracon.Workflows/README.md`, `CHANGELOG.md`
> **Manuel test alanı:** [`docs/manuel-test/15-WORKFLOWS.md`](../../manuel-test/15-WORKFLOWS.md)

---

> ### ⚗️ Damıtılmış kayıt
> Bu dosya fazın **planını** değil, fazın bıraktığı **kalıcı bilgiyi**
> taşır. Plan gövdesi, planlanan/gerçekleşen API, dosya listesi, risk ve
> açık soru bölümleri kapanışta düştü — **silinmedi, git geçmişindedir.**
>
> Tam metin — kopyala, çalıştır:
>
> ```bash
> git show 02c3f845:docs/arsiv/fazlar/211-KOD-GRAFI-DUGUM-YETENEKLERI.md
> ```
>
> Damıtıldı 2026-10-15 · `scripts/dokuman-bakim.py faz-damit`

---

## Amaç

Bir tüketici, insan onayı ve sınırlı revizyon taşıyan uçtan uca bir workflow'u kod grafıyla kurar ve saklanan zincirin bütün düğüm yeteneklerini alır: düğüm süresi, retry, bütçe kapısı, düğüm adlı maliyet, katı ayar, agent sürüm sabiti, workflow başına süre ve run parametreleri.

## Bitiş Ölçütleri (DoD)

- [x] Kod grafındaki `GetWorkflowFunction` düğümü saklanan zincirdeki aynı fonksiyonla aynı süre, retry, bütçe kapısı ve düğüm adlı maliyet davranışını verir (fonksiyonel test) — CodeWorkflowFunctionNodeTests
- [x] Geri kenarla dönen mesaj `GetWorkflowAgentStep` adımını koşturur; n'inci ret run'ı düşürür; sayaç cevap run'ları arasında korunur — CodeWorkflowRevisionTests
- [x] Workflow agent adımında `JsonSchema` ihlali `MaxRepairAttempts` ile onarılır — WorkflowAgentStepRepairTests
- [x] Kod kaydının `RunTimeout`'u ve `Parameters`'ı saklanan tanımla aynı kuralla uygulanır — CodeWorkflowRunTimeoutTests · CodeWorkflowParameterTests · CodeWorkflowRegistrationValidationTests
- [x] `MaxConcurrency` aynı anda koşan deneme sayısını sınırlar; iptal bekleyeni bırakır — WorkflowFunctionConcurrencyTests
- [x] Süresi dolan run `run.failed` webhook'u üretir — WorkflowRequestExpiryWebhookTests
- [x] Dört doğrulama kapısı sıfır uyarı verir — `kapi.py kapanis --taban 3bea3861` (üçüncü koşumda `dotnet format` yalnız using sırasında kırmızıydı, düzeltildi; son koşum aşağıda)
- [x] `samples/Tracon.Api` ile gerçek `run` yapıldı (ret → revizyon → onay; kesinti sonrası devam), çıktı belgeye yazıldı — § Örnek Uygulama Koşumu (MT-WF-176…181)
- [x] `secret` taraması boş döndü — `kapi.py tarama` (kapanış kapısının ilk komutu) her koşumda temiz
- [x] Manuel kabul case'leri `docs/manuel-test/15-WORKFLOWS.md` içine eklendi; otomatikleştirilebilenler koşuldu — MT-WF-176…181, altısı da gerçek süreçte koşuldu
- [x] `faz-denetim` koşuldu; 🔴 bulgu kalmadı — § Denetim Bulguları (iki 🔴 de kapandı)
- [x] `docs-site/` güncellendi (yeni rehber, `concepts/workflows.md`, `capabilities.md`, `reference/configuration.md`); `npm run build` + `check-links.mjs` temiz — `npm run check` kapıda geçti; yayın (`site-deploy.sh`) kullanıcı onayı bekler

### Doğrulama komutları

```bash
# Kod grafı revizyon döngüsü (örnek uygulama)
curl -s -X POST http://localhost:5081/tracon/api/workflows/review-and-revise/run \
  -H 'Prefer: respond-async' -H 'Content-Type: application/json' \
  -d '{"message":"start","parameters":{"productionId":"p-1","language":"tr"}}'
curl -s http://localhost:5081/tracon/api/workflows/requests?state=pending
```

---

## Plandan Sapmalar

1. **`GET /api/workflows/{name}` kod workflow'unun şemasını dönmez.** Plan (§ HTTP `endpoint`'leri) uç şemayı da döner demişti. O uç `WorkflowDefinition` döner ve kod workflow'u için `404` ("No editable definition") verir; şema ve `requestTimeout`, `ResponseMode` `GET /api/workflows` listesindeki betimleyicidedir. Uç değiştirilmedi (sözleşme büyürdü); boşluk F-541 olarak açıldı. Site ve CHANGELOG "liste" der.
2. **`WorkflowAgentStepOptions.Description` çıkarıldı.** Plan taslağında vardı. Adım `FunctionExecutor` olarak bağlanır; açıklama hiçbir modele ulaşmaz (yalnız `GroupChat`/`Magentic` katılımcı listesi okur) ve yalan bir ayar olurdu. Sonradan eklemek bedavadır (sealed sınıf).
3. **Açık Soru 3: A değil B uygulandı** (süre dolumu servisi yalnız `Failed` kapanışında `run.failed` yayınlar, `Completed`'da yayınlamaz). Planın önerisi A idi; uygulamada `WorkflowRequestExpirationHttpTests.An_answered_request_closes_its_run_as_completed...` şunu gösterdi: cevaplanan isteğin kaynak run'ı süre dolana kadar `AwaitingInput` kalır ve o anda `Completed` kapanır — bu SIK yoldur. A, her süreli insan onayı için bir yıla kadar sonra ikinci bir `run.completed` yayınlardı. Bakımcıya sorulmadan değiştirildi; geri almak tek satırdır (`PublishClosedAsync`'teki `status != Failed` koşulu). K-1011.
4. **Plan dışı kusur: plan-review tespiti.** `WorkflowResponseFactory.Create` ve `WorkflowRequestDescriptor` Magentic plan-review'u `TryGetDataAs<MagenticPlanReviewRequest>` ile (verinin ne olarak deserialize edilebildiğiyle) tanıyordu. Checkpoint'ten gelen JSON veri, tüm üyeleri isteğe bağlı bu tipe herhangi bir kayıttan hatasız okunur; kayıt tipli (`{"text":..., "violation":null}`) istek cevap run'ında yanlış cevap tipine (`MagenticPlanReviewResponse`) çevrilirdi. Kod grafında kayıt tipli istek en doğal tasarım olduğu için faz kapsamına alındı: tespit port'un bildirdiği istek tipine bağlandı (`WorkflowRequestDescriptor.IsPlanReview`); `WorkflowStructuredRequestTests` taban sürümde kırmızı, sonra yeşil. Örnek uygulama ve rehber yine de `string` istek tipi kullanır (console kayıt tipini yalnız tip adıyla gösterir).
5. **Plan "sayaç durumunun cevap run'ında checkpoint'ten geri geldiği ölçülmedi" demişti: ölçüldü, geri gelir** (`CodeWorkflowRevisionTests.The_revision_counter_survives_the_run_that_answers_and_the_run_fails_after_the_limit`). İmza ölçümü: `ReadStateAsync<T>(key, scopeName, ct)`, `QueueStateUpdateAsync<T>(key, value, scopeName, ct)` (MAF 1.20.0 XML dokümanı); güncelleme yazan executor için hemen, diğerleri için sonraki super-step'ten itibaren görünür.
6. **Kod grafında katılımcı kapsaması yok** (K-1010). Plan "agent adımı yalnız kendi şemasındaki adları alır" diyordu — bu sağlandı (`CodeWorkflowParameterTests.An_agent_step_receives_only_the_values...`); run başındaki kapsama denetimi (`RequireParticipantCoverageAsync`) saklanan tanım içindir, kod grafında eksik zorunlu değer adım koşarken adıyla düşer. F-540.
7. **Akışsız adım ölçümü** (plan "ölçülmeli" demişti): aynı agent'la taban ve yeni sürüm karşılaştırıldı — alt run satırı eskiden `IsStreaming = True`, olaylar `run.started`/`message.delta`/`run.completed`; yeni `IsStreaming = False`, `message.completed` bir olay fazla; token, maliyet ve girdi aynı. Fark CHANGELOG'da davranış değişikliği olarak anılır (K-1008).
8. **Üretilen yüzeyler yenilendi.** `WorkflowDescriptor.Parameters` XML'i ("kod workflow'u için her zaman boş") yanlış kaldığı için düzeltildi; `docs/openapi/tracon.json`, `TraconApiClient.g.cs` (nswag yeniden koşuldu, fark tek satır) ve `packages/tracon-client/src/schema.ts` yenilendi. Ayrıca `reference/configuration.md`'ye `AgentGraph:ChildDeadline` ve `WaitTimeout` satırları eklendi (F-528 kapandı).
9. **Beklenmeyen bütçe işi.** `docs/KARARLAR.md` bütçesi (586 000 B) beş yeni kararla aşıldı; `karar-damit` 3,8 KB taşıdı, kalan baytlar yeni satırların gerekçeleri `KARARLAR-GECMISI.md`'ye taşınarak karşılandı (bütçe artırılmadı). `docs/hafiza/workflows.md` aşıldı; yeni notlar `workflow-cevaplari.md` "Kod grafı" bölümüne, bir eski uzun not `HAFIZA-GECMISI.md`'ye taşındı. Agent haritası (11 264 B tavan) için `capabilities.md`'de yeni satır açılmadı, mevcut `Workflow functions` satırı genişletildi.
10. **Yeni internal tipler** (planda yoktu): `WorkflowNodeConcurrencyLimit`, `WorkflowGraphBindingException` (katalog workflow adını ekler, böylece `GetWorkflowFunction` imzası plandaki gibi `workflowName` almaz), `WorkflowRunWebhooks` (runner ve süre servisi için ortak `run.*` yükü). `TraconWorkflowOptionsValidator` artık `IEnumerable<CodeWorkflowRegistration>` alan (varsayılanlı) bir kurucuya sahiptir.

## Bu Fazda Verilen Kararlar

Dört kategoriye giren kararlar `docs/KARARLAR.md`'dedir:

- **K-1007** — kod grafı yardımcıları `GetWorkflowFunction` / `GetWorkflowAgentStep` (kullanıcı kararı: tam paket, yeni `GetWorkflowAgentStep`).
- **K-1008** — workflow agent adımı akışsız (kullanıcı kararı).
- **K-1009** — `WorkflowFunctionOptions.MaxConcurrency` (süreç başına, fonksiyon başına; sarmalayıcı sırası; bekleme `Timeout`'a sayılmaz).
- **K-1010** — kod kaydında `RunTimeout` (açılışta tavan doğrulaması) ve `Parameters`; kod grafında katılımcı kapsaması yok.
- **K-1011** — süre dolumu servisi yalnız `Failed` kapanışında `run.failed` yayınlar.

Yerel tercihler (K açılmadı): Açık Soru 1-B (`SetSettings<T>` yardımcısı, aynı katı seçenekle yazar), 2-A (açılışta düşer), 4-A (bekleme `Debug` loglanır, yeni olay tipi yok); SDK sabiti ayrı iş kalır; sample'ın revizyon sınırı sabittir (`MaxRevisions = 2`), parametre değildir.

## Örnek Uygulama Koşumu

`samples/Tracon.Api`, SQLite (`Tracon__PostgreSql__ConnectionString=""`), `http://localhost:5081`; gerçek model anahtarı yapılandırılı olduğu için `episode-writer` gerçek `gpt-5.4-mini` ile koştu (yerel `echo-writer-1` modeli anahtarsız ortam içindir). Tüm altı manuel case (`MT-WF-176`…`181`) gerçek süreçte koşuldu:

| Case | Gerçek çıktı |
|---|---|
| 176 (ret → revizyon → onay) | İlk taslak `[Jenerik] Merhaba! Bugün küçük bir soruyla başlıyoruz…`; `approved:false, note:"Daha kısa olsun ve bir soruyla bitsin."` → yeni run, ikinci taslak `Merhaba! Bir fikir nasıl harekete dönüşür? … Peki, bugün sizin ilk adımınız ne?` (daha kısa, soruyla biter); onay → son run `Completed`, olaylar `episode-review`, `approved`, `episode-tts`, `episode-render`, `episode-qa` (her biri bir kez), `WorkflowOutput` metni `Episode p-1 (tr) passed QA: …` (parametreler cevap run'larında korundu). `GET /runs/{id}/usage`: `episode-tts` 151 karakter · 0,0151 USD (tahmini), `episode-render` 2 gpu-seconds (fiyatsız); `nonTokenCost` 0,0151 USD |
| 177 (sınır) | Üç ret → `Failed`, `error.type` `content_rejected`, `The script was sent back 2 times and is still not accepted; the limit is 2.`; `usage` boş |
| 178 (SIGKILL → devam) | Render sürerken `kill -9`; yeniden başlatınca öldürülen run `Failed` `interrupted` ("Run … continues"), devam run'ı `Completed`, `continuedFromRunId` öldürülen run; devam `usage`: yalnız `episode-render` (tts tekrar koşmadı); bekleyen istek 0 (onay yeniden sorulmadı) |
| 179 (süre dolumu webhook) | Yerel HTTP alıcısı sırasıyla `workflow.request.pending` ve `run.failed` aldı (`kind: Workflow`, `status: Failed`, `agentName: review-and-revise`, `durationMs: 64963`); `RequestTimeout` 1 dk |
| 180 (`MaxConcurrency`) | Üç run aynı anda onaylandı: `episode-tts` çağrıları 0,31 sn ve 0,61 sn'de bitti (biri bekledi), ikisi aynı anda koşmadı; `episode-render` (sınır 2) iki çağrı 3,0 sn aynı pencerede koştu. (Worker iş eşzamanlılığı 2 olduğundan üçüncü run daha geç başladı; sınırın kendisi tts'in 0,3 sn'lik serileşmesiyle görünür) |
| 181 (tavan) | `Tracon__Demo__ReviewAndRevise__RunTimeout=02:00:00`: host `OptionsValidationException: Workflow 'review-and-revise' sets WorkflowRegistrationOptions.RunTimeout to 02:00:00, above the longest run timeout a workflow may set (00:10:00). Lower it, or set 'Tracon:Workflows:MaxRunTimeout' above it …` ile açılmadı |

İlk gerçek koşum bir kusur gösterdi: yazar agent'ın talimatı uzunluk sınırını söylemiyordu ve gerçek model 400 karakteri aşan Türkçe taslak yazdı; doğrulama iki kez geri gönderip `content_rejected` ile run'ı düşürdü (döngü doğru çalıştı, örnek yanlıştı). Talimata sınır eklendi.

## Süreç Ölçümü

| Metrik | Değer |
|---|---|
| Plan revizyonu sayısı | 0 (plan yazılı onaya gelmeden uygulamaya geçildi; sapmalar yukarıda 10 madde) |
| Düzeltme turu sayısı | 3 (gerçek model ilk koşumda uzunluk sınırı kusuru → talimat; `KARARLAR.md` bütçesi → damıtma + sıkıştırma; agent haritası bütçesi → satır birleştirme) |
| 🔴 bulgu: gerçek / gürültü / araştırılacak | bkz. Denetim Bulguları |
| Fazın ürettiği regresyon | 0 (tam `ic-dongu` koşumunda tek kırmızı `OpenApiSnapshotTests`, beklenen: XML doküman metni değişti, snapshot yenilendi) |
| Faz kapandıktan sonra bulunan kusur | ölçülmedi (kapanış anında henüz yok) |

## Denetim Bulguları

Bağımsız denetçi (`faz-denetcisi`, taze bağlam, `git diff 3bea3861...HEAD`) iki 🔴, üç 🟡 ve üç 🟢 buldu. Denetçi ayrıca tüm fonksiyonel testleri (2120), `Tracon.Workflows.UnitTests`'i (423) ve `dokuman-bakim.py --denetle`'yi koştu; çift yayın, sarmalayıcı sırası, slot sızıntısı, kod `RunTimeout` tutarlılığı ve `Parameters` yollarında sorun görmedi.

| # | Seviye | Triyaj | Bulgu | Sonuç |
|---|---|---|---|---|
| 1 | 🔴 | gerçek | Sevk edilen `///` metnine iç referans (`F-537`, `K-889`, `phase 211`, `🚨`) girdi; `ShippedDocumentationSelfContainmentTests` taban 0 iken 5 dosya | Düzeltildi: cümleler tüketici sesiyle yeniden yazıldı (5 dosya, 7 satır); test yeşil, taban dokunulmadı |
| 2 | 🔴 | gerçek | Yeni testlerde 5 `Task.Delay` sınıf etiketsiz (`TestDelayClassificationTests`) | Düzeltildi: negatif kanıt olanlara `// delay: negative`, takılı/iş taklidine `// delay: simulated` |
| 3 | 🟡 | gerçek | `PublicSurfaceBaselineTests`: `Tracon.Workflows` 8 → 11 genel tip | Plana uygun büyüme (`WorkflowFunctionBinding`, `WorkflowFunctionStepOptions`, `WorkflowAgentStepOptions`); taban `TRACON_PUBLIC_SURFACE_REFRESH=1` ile yenilendi |
| 4 | 🟡 | gerçek | `MaxConcurrency`'nin saklanan zincire ulaştığı ve zincirle kod grafının aynı slotu paylaştığı hiçbir testte kanıtlı değildi | Düzeltildi: `WorkflowFunctionConcurrencyTests.A_stored_chain_and_a_code_graph_share_the_functions_slots` |
| 5 | 🟡 | gerçek | Süre dolumu webhook testi yükün token/maliyet alanlarını doğrulamıyordu | Düzeltildi: `WorkflowRequestExpiryWebhookTests.The_payload_carries_what_the_run_spent_before_it_waited` (`Cost`, `NonTokenCost`, `Currency`) |
| 6 | 🟢 | gerçek, devredildi | Zaman aşımında limit sarmalayıcısı slotu bırakır; token'ı yok sayan handler arka planda sürer, `RetryPolicy` ile yeni deneme `MaxConcurrency=1`'i aşabilir | Planlı sıra ve belgeli sınır ("pass the token on"); F-542 |
| 7 | 🟢 | gerçek | `A_pin_to_a_version_the_agent_does_not_have_fails_the_step_by_name…` yalnız `"7"` arıyordu | Düzeltildi: `Version 7` ve `'writer'` aranır |
| 8 | 🟢 | gerçek, devredildi | Kod `RunTimeout`'unun çalışma anı kesme dalı (tavan sonradan düşünce) kod kaydı için sınanmıyor | Dal saklanan tanımla aynı kodu paylaşır, fark log metni; F-543 |

Denetçinin plan sapması listesi (plan-review tespiti · `Description` yok · `Completed`'da yayın yok · `SetSettings`) "Plandan Sapmalar"la örtüşür; ikisi — Açık Soru 3 metni ve "Planlanan Public API" bloğu — denetim sonrası koda göre düzeltildi (yukarıda).

## Sonraki Faza Devir Notu

Sıradaki faz seçilmedi; aday sırası [`ADAYLAR.md`](../../ADAYLAR.md)'dedir. Bu faz şunları bıraktı:

- **Devralınan sözleşmeler:** yukarıdaki *Gerçekleşen Public API*. Fonksiyon sarmalayıcı sırası `scope(retry(limit(gate(timeout(handler)))))`'dır (`NodeWrapper` XML'i ve `hafiza/workflows.md`). Yeni bir halka eklenirse üç yerde de güncellenir. Kod grafı hatası katalogda `WorkflowGraphBindingException` ile workflow adını alır.
- 🚨 **Bilinen tuzaklar** (ayrıntı [`hafiza/workflow-cevaplari.md`](../../hafiza/workflow-cevaplari.md) "Kod grafı"): geri kenara dönen mesaj `AIAgentBinding`'i koşturmaz (agent adımı `GetWorkflowAgentStep`); revizyon sayacı `IWorkflowContext` durumunda tutulur (executor alanı sıfırlanır); `ExternalRequest.TryGetDataAs<T>` checkpoint'ten gelen JSON'u istenen tipe okumayı dener (plan-review tespiti port tipindendir); kod grafı yapısı değişirse bekleyen run sürmez, zaman aşımı/ayar değişirse sürer; süre dolumu servisi yalnız `Failed` kapanışta webhook yayınlar.
- **Açık uçlar (ADAYLAR):** F-540 (kod grafında katılımcı kapsaması run başında), F-541 (kod workflow'u için tekil betimleyici ucu), F-542 (zaman aşımı sonrası slot ve token'ı yok sayan handler), F-543 (kod `RunTimeout` çalışma anı kesmesinin testi). F-528 bu fazda kapandı.
- **Sınırlar (kullanıcıya söylenmiştir):** `MaxConcurrency` süreç başınadır, replika sayısıyla çoğalır; her cevap run'ı yeni bütçeyle başlar (F-531), bu yüzden revizyon sayacı ve `MaxTotalCost` birlikte verilir; kod grafı `Parameters`'ı için katılımcı kapsaması adım koşarken denetlenir.
- **El ile kalan:** yok; altı manuel case örnek uygulamada koşuldu. Site yayını (`faz-tamamlama` Adım 10) **koşulmadı**: `./scripts/site-deploy.sh` dış dünyaya yayın yapar, kullanıcı onayı bekler.
- **Geliştirici ortamı notu:** örnek uygulamada gerçek model anahtarı kullanıcı sırlarındadır; `Tracon__PostgreSql__ConnectionString=""` ve `Tracon__Sqlite__ConnectionString` verilmezse ortak PostgreSQL'e bağlanır. Webhook alıcısı için `Tracon__Webhooks__AllowInsecureHttp=true` ve `AllowPrivateNetworkTargets=true` gerekir.
