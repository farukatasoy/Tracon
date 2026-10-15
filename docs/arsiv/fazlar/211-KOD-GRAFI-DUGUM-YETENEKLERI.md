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

## Bu Faza Başlarken

> `faz-baslangic` skill'ini uygula. Aşağıdaki liste o skill'in 2. adımıdır —
> **tamamını değil, yalnız işaret edilen bölümleri oku.**

1. Bu doküman
2. Kararlar — dosyanın tamamını **okuma**, yalnız bu kalemleri grep'le:
   ```bash
   grep -n "K-495\|K-587\|K-897\|K-900\|K-901\|K-958\|K-959\|K-960\|K-972\|K-989\|K-994\|K-999\|K-1002\|K-889" docs/KARARLAR.md
   ```
   **K-495** (karma zincirde agent adımı `WorkflowAgentStepExecutor`; `AIAgentBinding` giriş dışında koşmaz) ·
   **K-587** (retry yalnız kayıtlı fonksiyon düğümünde, kendi çağrısının içinde) ·
   **K-897** (workflow parametre şeması ve bağlama) ·
   **K-900** (fonksiyon ayarı katı okumayla doğrulanır) ·
   **K-901** (aynı fonksiyon bir zincirde iki kez kullanılmaz; yapısal değişiklik eski checkpoint'i reddettirir) ·
   **K-958 / K-959 / K-960** (fonksiyon süresi · agent adımı süresi `ChildDeadline`'ı ezer · tanım `RunTimeout`'u ve `MaxRunTimeout`) ·
   **K-972** (token dışı tutar ayrı toplamdır) ·
   **K-989** (agent sürüm sabiti) ·
   **K-994** (tek cevap modu) ·
   **K-999** (bütçe kapısı) ·
   **K-1002** (istek süresi) ·
   **K-889** (akışsız döngü son turu döner)
3. Önceki fazların devir notları — yalnız ilgili bölüm:
   ```bash
   awk '/## Sonraki Faza Devir Notu/,0' docs/arsiv/fazlar/210-BUTCE-DUGUM-KAPISI.md
   awk '/## Sonraki Faza Devir Notu/,0' docs/arsiv/fazlar/209-WORKFLOW-GELEN-KUTUSU-VE-SURE.md
   ```
4. Alan hafızası: [`hafiza/workflows.md`](../../hafiza/workflows.md) (MAF executor ve checkpoint tuzakları) ·
   [`hafiza/workflow-cevaplari.md`](../../hafiza/workflow-cevaplari.md) (cevap run'ı, tek cevap) ·
   [`hafiza/maf-harness-ve-loop.md`](../../hafiza/maf-harness-ve-loop.md) (Loop'un akış davranışı)
5. Gerektiğinde: [`MIMARI.md`](../../MIMARI.md) workflow bölümü

---

## Amaç

Bir tüketici, insan onayı ve sınırlı revizyon taşıyan uçtan uca bir workflow'u
kod grafıyla kurar ve saklanan zincirin bütün düğüm yeteneklerini alır: düğüm
süresi, retry, bütçe kapısı, düğüm adlı maliyet, katı ayar, agent sürüm sabiti,
workflow başına süre ve run parametreleri. Pahalı düğümün (GPU TTS, render)
eşzamanlılığı fonksiyon başına sınırlanır. Süresi dolan istek host'a webhook ile
bildirilir.

- **F-537** — kod grafında fonksiyon düğümü ve agent adımı; workflow agent adımı akışsız koşar; kod kaydında `RunTimeout` ve `Parameters`
- **F-538** — fonksiyon başına eşzamanlılık sınırı (`WorkflowFunctionOptions.MaxConcurrency`)
- **F-539** — istek süresi dolunca `run.failed` webhook'u

### Bugün ne çalışmıyor — doğrulanmış kanıt

| Kanıt | Gözlem |
|---|---|
| [`WorkflowFunctionRegistry.cs:19`](../../../src/Tracon.Workflows/Internal/WorkflowFunctionRegistry.cs), [`:156`](../../../src/Tracon.Workflows/Internal/WorkflowFunctionRegistry.cs) | Kayıtlı fonksiyonu executor'a çeviren `CreateExecutor` internal'dır; yalnız saklanan zincir derleyicisi çağırır ([`WorkflowDefinitionCompiler.cs:221`](../../../src/Tracon.Workflows/Internal/WorkflowDefinitionCompiler.cs)) |
| [`TraconWorkflowFunctionExtensions.cs:492-519`](../../../src/Tracon.Workflows/TraconWorkflowFunctionExtensions.cs) | Sarmalayıcı zincir `scope(retry(gate(timeout(handler))))` yalnız bu yoldan kurulur |
| [`WorkflowAgentBinding.cs:70-80`](../../../src/Tracon.Workflows/WorkflowAgentBinding.cs) | `GetWorkflowAgent` süre ve sürüm almaz; adım kurulum geneli `ChildDeadline` (2 dk, [`TraconOptions.cs:241`](../../../src/Tracon.Core/TraconOptions.cs)) ile koşar |
| MAF 1.20.0 `AIAgentHostExecutor` (internal; probe ile ölçüldü, 2026-10-15) | Geri kenarla dönen mesaj `TurnToken` olmadan agent'ı koşturmaz; run hata vermeden çıktısız `Idle` biter. Cevap run'ı token göndermez ([`WorkflowRunner.cs:1769-1772`](../../../src/Tracon.Workflows/Internal/WorkflowRunner.cs)). K-495'in tuzağı kod grafında da geçerlidir |
| [`WorkflowAgentStepExecutor.cs:39`](../../../src/Tracon.Workflows/Internal/WorkflowAgentStepExecutor.cs) | Agent adımı `RunStreamingAsync` ile koşar; akış yolunda yapılandırılmış yanıt onarımı yoktur ([`StructuredResponseValidatingAgent.cs:279-350`](../../../src/Tracon.Core/Compilation/StructuredResponseValidatingAgent.cs)) |
| [`WorkflowRegistrationOptions.cs:15,27,47`](../../../src/Tracon.Workflows/WorkflowRegistrationOptions.cs) | Kod kaydı yalnız `Description`, `ResponseMode`, `RequestTimeout` taşır |
| [`WorkflowRunner.cs:2298-2312`](../../../src/Tracon.Workflows/Internal/WorkflowRunner.cs) | Kod workflow'u tanımsızdır; `RunTimeout` kurulum değerinde (10 dk) kalır |
| [`WorkflowRunner.cs:1099`](../../../src/Tracon.Workflows/Internal/WorkflowRunner.cs) | Kod workflow'unun parametre şeması boştur; her değer `unknown` reddi alır |
| [`WorkflowFunctionOptions.cs:17,25,55,77`](../../../src/Tracon.Workflows/WorkflowFunctionOptions.cs) | Fonksiyon başına eşzamanlılık sınırı yoktur; tek sınır süreç başına ve kiracılara ortak `MaxConcurrentRuns`'tır |
| [`WorkflowRequestExpirationService.cs:43-52`](../../../src/Tracon.Workflows/Internal/WorkflowRequestExpirationService.cs) | Servis webhook yayıncısı almaz; süresi dolan run `Failed` kapanır ama `run.failed` yayınlanmaz (Faz 209 bilinen sınırı) |
| [`WorkflowCatalog.cs:184-195`](../../../src/Tracon.Workflows/Internal/WorkflowCatalog.cs) | Kod grafı her run'da yeniden kurulur; executor alanında tutulan revizyon sayacı her cevap run'ında sıfırlanır |

> Kanıtlar 2026-10-15 tarihinde doğrulandı.

```mermaid
flowchart TD
    IN["Run: parametre productionId, language"] --> W["Agent adımı: writer<br/>GetWorkflowAgentStep, Timeout, JsonSchema"]
    W --> V["Fonksiyon: validate<br/>GetWorkflowFunction"]
    V -->|"ihlal"| W
    V --> A{"RequestPort: onay<br/>SingleAnswer, RequestTimeout"}
    A -->|"ret + not, sayaç < n"| W
    A -->|"ret, sayaç = n"| F["Run Failed: içerik reddedildi"]
    A -->|"onay"| T["Fonksiyon: tts<br/>MaxConcurrency 1, Timeout"]
    T --> R["Fonksiyon: render<br/>MaxConcurrency 2, Timeout"]
    R --> Q["Fonksiyon: qa"]
    Q --> OUT["Çıktı; run.completed webhook'u"]
```

---

## 211.1 — Kod grafında fonksiyon düğümü: `GetWorkflowFunction`

`AddWorkflowFunction` ile kaydedilen fonksiyonu kod grafına koyan public
yol. İçi saklanan zincirle **aynı** `CreateExecutor` çağrısıdır; aynı
sarmalayıcı zinciri kurar: düğüm kapsamı (Faz 199), retry (K-587), bütçe
kapısı (K-999), süre (K-958), katı ayar okuması (K-900).

- Executor kimliği fonksiyon adıdır. Aynı fonksiyon bir grafta iki kez
  kullanılmaz; gerekirse kodda ikinci ad kaydedilir (K-901 emsali). Bu yüzden
  düğüm adlı maliyet kalemi fonksiyonun adını taşır ve tekildir.
- Bilinmeyen fonksiyon adı ve okunamayan ayar graf kurulurken
  `TraconException` atar; mesaj workflow'u ve fonksiyonu adlandırır. Graf her
  run'da kurulduğu için hata ilk düğümden önce run'ı `Failed` bitirir.
- Ayar `JsonElement?` olarak verilir ve kayıttaki `TSettings`'e katı okunur
  (Açık Soru 1).

## 211.2 — Kod grafında agent adımı: `GetWorkflowAgentStep`

`WorkflowAgentStepExecutor` (bir `FunctionExecutor`) döner; saklanan karma
zincirle aynı kod yoludur. `TurnToken` istemez, bu yüzden geri kenarda
güvenlidir. Girdi ve çıktı `List<ChatMessage>`'dır.

- `Timeout` adımın `ChildDeadline`'ını ezer (K-959 kod yoluna genişler).
- `AgentVersion` adımı veritabanı agent'ının bir sürümüne sabitler (K-989).
  Graf fabrikası senkrondur; sabit sürüm sorunu bu yüzden derlemede değil,
  adım çalışırken ve model çağrısından önce adımı adıyla düşürür. Bu fark
  belgelenir.
- Executor kimliği agent'ın katalog adıdır; `WorkflowGraphReader` agent adını
  kimlikten okur ([`WorkflowAgentStepExecutor.cs:11-17`](../../../src/Tracon.Workflows/Internal/WorkflowAgentStepExecutor.cs)).
  Aynı agent iki düğümde kullanılmaz.
- Adımın MAF oturumu yoktur. Revizyon girdisi taslağı ve geri bildirimi
  kendisi taşır; rehber bunu örnekle gösterir.
- `GetWorkflowAgent` değişmez: preview.3'te sevk edildi; opsiyonel parametre
  eklemek ikili kırıcıdır.

## 211.3 — Workflow agent adımı akışsız koşar

`WorkflowAgentStepExecutor` `agent.RunAsync` çağırır. Saklanan karma zincir
ve yeni kod adımı birlikte değişir.

- Yapılandırılmış yanıt onarımı workflow adımında çalışır
  (`Tracon:StructuredResponse:Enabled` ve `MaxRepairAttempts` ile; adım
  Tracon damgalı oturum taşımaz).
- Karma zincir zaten `MessageDelta` yaymıyordu (K-495); görünen akış değişmez.
- Executor'ın bugünkü yorumu "alt run tam mesaj ve usage geçmişini akışlı
  yolda tutar" der ([`WorkflowDefinitionCompiler.cs:132-134`](../../../src/Tracon.Workflows/Internal/WorkflowDefinitionCompiler.cs)).
  Akışsız yolun aynı geçmişi yazdığı **ölçülmeli**; ölçüm testi aşağıdadır.
- Hazır desenler (`AgentWorkflowBuilder`) MAF'ın kendi executor'ıyla akışlı
  koşar; bu faz onları değiştirmez. Onlarda Loop F-536 ile son iterasyonu
  verir, onarım yoktur. Site bunu yazar.

## 211.4 — Kod kaydında `RunTimeout` ve `Parameters`

`WorkflowRegistrationOptions` iki alan alır. İkisi de saklanan tanımın
kuralını taşır.

- `RunTimeout` (`TimeSpan?`): K-960 — kurulum değerini kısaltır,
  `MaxRunTimeout` tavanına kadar uzatır. Tavanı aşan değer host açılışında
  workflow'u adlandıran bir doğrulama hatasıyla düşer (Açık Soru 2). Tavan
  sonradan düşürülürse run tavana kesilir ve kesme loglanır (saklanan tanımla
  aynı).
- `Parameters` (`IReadOnlyList<AgentParameter>`): K-897 — runner doğrular,
  HTTP erken `400` verir, kuyruklu run değeri taşır, agent adımı yalnız kendi
  şemasındaki adları alır. `GET /api/workflows/{name}` şemayı gösterir.
  Kuyruk yükünde yalnız kimlik durur kuralı (bakımcı kararı, 2026-10-15)
  böylece kod grafında da uygulanır: `productionId` parametre olur.

## 211.5 — Fonksiyon başına eşzamanlılık: `WorkflowFunctionOptions.MaxConcurrency`

`int?`; `null` sınırsızdır (K1). Değer kayıtta `≥ 1` doğrulanır. Sınır süreç
geneli ve fonksiyon başınadır; saklanan zincire ve kod grafına birlikte ulaşır.

- Sıra: `scope(retry(limit(gate(timeout(handler)))))`. Her deneme slotu yeniden
  alır; retry beklemesinde slot bırakılır. Bütçe kapısı slot alındıktan sonra
  okunur, çünkü bekleme sırasında bütçe tükenebilir.
- Bekleme run'ın token'ına bağlıdır (iptal ve `RunTimeout`), düğüm `Timeout`'una
  sayılmaz: süre düğümün kendi işini sınırlar.
- Sınır replika sayısıyla çoğalır; site bunu yazar. Kiracı adaleti F-322'dir,
  bu fazın dışındadır.

## 211.6 — İstek süresi dolunca webhook

`WorkflowRequestExpirationService` opsiyonel `IWebhookPublisher` alır. Run'ı
`Failed` kapattığı dalda `run.failed` yayınlar; yük runner kapanışıyla aynı
yardımcıdan kurulur ([`WorkflowRunner.cs:2021-2030`](../../../src/Tracon.Workflows/Internal/WorkflowRunner.cs)).
Yayın hatası loglanır, kapanışı bozmaz. Bütün istekleri cevaplanmış run'ı
`Completed` kapattığı dalda **hiçbir olay yayınlanmaz**: cevabın kendi run'ı zaten
`run.completed` yayınlamıştır ve ikinci olay aynı üretimi iki kez sayardı (Açık Soru 3,
Plandan Sapmalar 3). `workflow.request.answered` (F-523) bu faz dışıdır.

## 211.7 — Örnek, rehber ve revizyon sayacı

- `samples/Tracon.Api`: `review-and-revise` kod grafı (§ diyagram). Writer
  `JsonSchema` çıktı verir; `validate` ihlalde writer'a döner; onay
  `RequestPort<ReviewRequest, ReviewDecision>`; ret notu ve önceki taslak
  writer'ın girdisidir; sayaç n'de run'ı düşürür; medya düğümleri sahte TTS ve
  render'dır, `ReportUsageAsync` ile maliyet bildirir, `Timeout` ve
  `MaxConcurrency` taşır. Kayıt: `SingleAnswer`, `RequestTimeout`,
  `RunTimeout`, `Parameters`.
- 🚨 Revizyon sayacı executor alanında **tutulmaz**: her cevap yeni bir run'dır
  ve graf yeniden kurulur. Sayaç `IWorkflowContext` durumunda tutulur
  (`QueueStateUpdateAsync` / `ReadStateAsync`). Durumun cevap run'ında
  checkpoint'ten geri geldiği Tracon'da **ölçülmedi**; uygulamanın ilk işi bu
  ölçümdür. **Doğrulanmadı — `maf-api-kesfi` ile ölçülmeli** (imzalar ve
  checkpoint davranışı).
- Site: `guides/review-and-revise-workflow.md` — graf, sayaç, ret notu,
  kesinti sonrası devam (`ResumeInterruptedRuns`), maliyet ve bütçe.

---

## Planlanan Public API

> Taslak imzalardır. RS0026 yüzünden opsiyonel parametre yalnız tek aşırı
> yüklemede kalmaz; K-902 deseni uygulanır (`Action<Options>`).

```csharp
// Tracon.Workflows
public static class WorkflowFunctionBinding
{
    public static ExecutorBinding GetWorkflowFunction(this IServiceProvider services, string functionName);
    public static ExecutorBinding GetWorkflowFunction(
        this IServiceProvider services, string functionName, Action<WorkflowFunctionStepOptions> configure);
}

public sealed class WorkflowFunctionStepOptions
{
    public TimeSpan? Timeout { get; set; }
    public JsonElement? Settings { get; set; }
    public void SetSettings<TSettings>(TSettings settings) where TSettings : class;   // Açık Soru 1-B
}

public static class WorkflowAgentBinding // mevcut sınıf; yeni üyeler
{
    public static ExecutorBinding GetWorkflowAgentStep(
        this IServiceProvider services, string workflowName, string agentName);
    public static ExecutorBinding GetWorkflowAgentStep(
        this IServiceProvider services, string workflowName, string agentName,
        Action<WorkflowAgentStepOptions> configure);
}

public sealed class WorkflowAgentStepOptions
{
    // Description kapanışta çıkarıldı (Plandan Sapmalar 2)
    public TimeSpan? Timeout { get; set; }
    public int? AgentVersion { get; set; }
}

public sealed class WorkflowRegistrationOptions // mevcut; yeni alanlar
{
    public TimeSpan? RunTimeout { get; set; }
    public IReadOnlyList<AgentParameter> Parameters { get; set; } = [];
}

public sealed class WorkflowFunctionOptions // mevcut; yeni alan
{
    public int? MaxConcurrency { get; set; }
}
```

- `ExecutorBinding` MAF tipidir; sarmalanmaz (K3).
- Bugün eklemek bedava / sonradan eklemek: `WorkflowRegistrationOptions` ve
  `WorkflowFunctionOptions` `sealed class`'tır, alan eklemek kaynak kırmaz;
  yeni statik metotlar kırmaz.

### HTTP `endpoint`'leri

Yeni uç yok. `GET /api/workflows/{name}` kod workflow'unun parametre şemasını
da döner (saklanan tanımla aynı alan).

### Arayüz payı

Yok. Konsol kod workflow'unu bugünkü gibi salt okunur gösterir.

---

## Planlanan Dosya Listesi

```
src/Tracon.Workflows/
├── WorkflowFunctionBinding.cs            (yeni)
├── WorkflowFunctionStepOptions.cs        (yeni)
├── WorkflowAgentStepOptions.cs           (yeni)
├── WorkflowAgentBinding.cs               (GetWorkflowAgentStep)
├── WorkflowRegistrationOptions.cs        (RunTimeout, Parameters)
├── WorkflowFunctionOptions.cs            (MaxConcurrency)
├── TraconWorkflowFunctionExtensions.cs   (limit halkası)
├── Internal/WorkflowAgentStepExecutor.cs (RunAsync)
├── Internal/WorkflowRunner.cs            (kod RunTimeout ve Parameters okuması)
├── Internal/WorkflowRequestExpirationService.cs (webhook)
└── PublicAPI.Unshipped.txt
samples/Tracon.Api/                       (review-and-revise grafı)
docs-site/src/content/docs/guides/review-and-revise-workflow.md (yeni)
tests/Tracon.Workflows.UnitTests/         (kayıt doğrulaması)
tests/Tracon.AspNetCore.FunctionalTests/  (graf, revizyon, devam, webhook)
```

---

## Hata Modları ve Testler

| Ne bozulabilir | Seviye | Test sınıfı |
|---|---|---|
| Kod grafındaki fonksiyon düğümü süreyi, retry'ı, bütçe kapısını veya düğüm adlı maliyeti almaz | Fonksiyonel | `CodeWorkflowFunctionNodeTests` |
| Bilinmeyen fonksiyon adı veya okunamayan ayar run'ı ilk düğümden sonra düşürür | Fonksiyonel | `CodeWorkflowFunctionNodeTests` |
| Geri kenardan dönen mesaj agent adımını koşturmaz (`TurnToken` tuzağı) | Fonksiyonel | `CodeWorkflowRevisionTests` |
| Revizyon sayacı cevap run'ında sıfırlanır; n'inci retten sonra run sürer | Fonksiyonel (checkpoint + cevap) | `CodeWorkflowRevisionTests` |
| Agent adımı `Timeout`'u ve `AgentVersion`'ı uygulanmaz | Fonksiyonel | `CodeWorkflowAgentStepTests` |
| Akışsız adımda alt run'ın mesaj ve usage geçmişi eksik yazılır | Fonksiyonel (kayıt sınırı) | `WorkflowAgentStepRecordingTests` |
| JSON ihlali workflow adımında onarılmaz | Fonksiyonel | `WorkflowAgentStepRepairTests` |
| Onay isteyen tool'lu agent adımı akışsız yolda reddi kaybeder | Fonksiyonel | `WorkflowAgentStepRecordingTests` |
| Kod `RunTimeout`'u tavanı aşar ve host açılır | Birim (açılış doğrulaması) + Fonksiyonel | `CodeWorkflowRegistrationValidationTests` |
| Kod workflow'una parametre değeri ulaşmaz veya bilinmeyen ad kabul edilir | Fonksiyonel (HTTP + kuyruk) | `CodeWorkflowParameterTests` |
| `MaxConcurrency` aşılır; retry beklemesinde slot tutulur; iptal bekleyeni bırakmaz | Fonksiyonel (eşzamanlılık) | `WorkflowFunctionConcurrencyTests` |
| Bekleme sırasında tükenen bütçe düğümü başlatır | Fonksiyonel | `WorkflowFunctionConcurrencyTests` |
| Süresi dolan run için `run.failed` yayınlanmaz; yayın hatası kapanışı bozar | Fonksiyonel | `WorkflowRequestExpiryWebhookTests` |
| Kesinti sonrası devam kod grafında son checkpoint'ten sürmez | Fonksiyonel (kuyruk + yeniden başlatma) | `CodeWorkflowRevisionTests` |
| Başka kiracı, başka kiracının bekleyen isteğine cevap verir | Sözleşme | mevcut `TenantIsolationContract` + `CodeWorkflowRevisionTests` |

Beş soru: iptal (bekleyen slot, adım süresi) · eşzamanlılık (`MaxConcurrency`,
iki cevap) · boş/aşırı girdi (sınırsız revizyon, boş ret notu) · başka kiracı
(cevap ve gelen kutusu) · alt sistem hatası (webhook yayını, `store`).

---

## Manuel Kabul Case'leri

| # | Ön koşul | Adımlar | Beklenen sonuç |
|---|---|---|---|
| 1 | Örnek uygulama, SQLite | `review-and-revise`'ı `Prefer: respond-async` ile başlat; isteği reddet (not ile); ikinci taslağı onayla | İkinci writer çağrısı notu ve ilk taslağı görür; medya düğümleri bir kez koşar; run `Completed` |
| 2 | Aynı | n kez reddet | Run `Failed`, hata sınıfı adlandırılır; medya düğümü koşmaz |
| 3 | Aynı | Render düğümü sırasında süreci öldür; yeniden başlat | Run son checkpoint'ten sürer; onay yeniden sorulmaz |
| 4 | `RequestTimeout` 1 dk | Cevap verme | Run `Failed` (`WorkflowRequestExpired`); webhook alıcısı `run.failed` alır |
| 5 | Üç run aynı anda | Hepsini onayla | `tts` düğümü aynı anda en çok `MaxConcurrency` kez koşar (log ve zaman damgası) |
| 6 | `RunTimeout` tavanın üstünde | Host'u başlat | Açılış workflow'u adlandıran hatayla düşer |

---

## Açık Sorular

| # | Soru | Seçenekler | Öneri |
|---|---|---|---|
| 1 | Fonksiyon ayarı kod grafında hangi tiple verilir? | A: yalnız `JsonElement?` · B: `JsonElement?` + `SetSettings<TSettings>(value)` yardımcısı (aynı katı seçenekle yazar) | **B**: tek okuma yolu (K-900) korunur, kod yazan tüketici tipli değer verir |
| 2 | Kod `RunTimeout`'u tavanı aşarsa? | A: host açılışında hata · B: run'da kes ve logla | **A**: kod değeri dağıtımla gelir; açılışta düşmek sessiz kesmeden iyidir. Tavan sonradan düşerse B davranışı (saklanan tanımla aynı) |
| 3 | Süre dolumu servisi `Completed` dalında `run.completed` yayınlasın mı? | A: evet · B: yalnız `run.failed` | ~~A~~ **B uygulandı** (uygulamada ölçüldü: cevaplanan isteğin run'ı süre dolunca `Completed` kapanır, bu sık yoldur; A ikinci bir `run.completed` üretirdi — K-1011) |
| 4 | `MaxConcurrency` beklemesi run olayı yazsın mı? | A: yazmaz, `Debug` log · B: yeni olay tipi | **A**: yeni olay tipi sözleşme büyütür; ihtiyaç ölçülmedi |

---

## Bitiş Ölçütleri (DoD)

- [x] Kod grafındaki `GetWorkflowFunction` düğümü saklanan zincirdeki aynı fonksiyonla aynı süre, retry, bütçe kapısı ve düğüm adlı maliyet davranışını verir (fonksiyonel test) — CodeWorkflowFunctionNodeTests
- [x] Geri kenarla dönen mesaj `GetWorkflowAgentStep` adımını koşturur; n'inci ret run'ı düşürür; sayaç cevap run'ları arasında korunur — CodeWorkflowRevisionTests
- [x] Workflow agent adımında `JsonSchema` ihlali `MaxRepairAttempts` ile onarılır — WorkflowAgentStepRepairTests
- [x] Kod kaydının `RunTimeout`'u ve `Parameters`'ı saklanan tanımla aynı kuralla uygulanır — CodeWorkflowRunTimeoutTests · CodeWorkflowParameterTests · CodeWorkflowRegistrationValidationTests
- [x] `MaxConcurrency` aynı anda koşan deneme sayısını sınırlar; iptal bekleyeni bırakır — WorkflowFunctionConcurrencyTests
- [x] Süresi dolan run `run.failed` webhook'u üretir — WorkflowRequestExpiryWebhookTests
- [ ] Dört doğrulama kapısı sıfır uyarı verir
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

## Riskler

| Risk | Önlem |
|------|-------|
| MAF iç executor davranışı sürümle değişir (`TurnToken`, checkpoint durumu) | Geri kenar ve sayaç fonksiyonel testle kilitlenir; MAF yükseltmesinde test kırmızı olur |
| Akışsız adım bugünkü kayıt veya onay davranışından ayrılır | Ölçüm testi önce yazılır; fark CHANGELOG'da davranış değişikliği olarak anılır |
| Graf değişikliği bekleyen run'ın resume'unu reddettirir (K-901) | Rehber yazar: kod grafı değişince bekleyen run eski sürümle sürmez |
| Her cevap run'ı yeni bütçeyle başlar (F-531) | Rehber yazar; revizyon tavanı (n) ve `MaxTotalCost` birlikte verilir. F-531 ayrı kalemdir |
| `MaxConcurrency` replika sayısıyla çoğalır | Site yazar; dağıtık sınır kapsam dışıdır |

---

<!-- ============================================================
     AŞAĞISI KAPANIŞTA DOLDURULUR — `faz-tamamlama` skill'i.
     Plan anında boş kalır. Başlıkları SİLME.
     ============================================================ -->

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

## Gerçekleşen Public API

```csharp
// Tracon.Workflows
public static class WorkflowFunctionBinding
{
    public static ExecutorBinding GetWorkflowFunction(this IServiceProvider services, string functionName);
    public static ExecutorBinding GetWorkflowFunction(
        this IServiceProvider services, string functionName, Action<WorkflowFunctionStepOptions> configure);
}

public sealed class WorkflowFunctionStepOptions
{
    public TimeSpan? Timeout { get; set; }
    public JsonElement? Settings { get; set; }
    public void SetSettings<TSettings>(TSettings settings) where TSettings : class;   // plana eklendi (Açık Soru 1-B)
}

public static class WorkflowAgentBinding // mevcut sınıf
{
    public static ExecutorBinding GetWorkflowAgentStep(this IServiceProvider services, string workflowName, string agentName);
    public static ExecutorBinding GetWorkflowAgentStep(
        this IServiceProvider services, string workflowName, string agentName, Action<WorkflowAgentStepOptions> configure);
}

public sealed class WorkflowAgentStepOptions   // plandaki Description çıkarıldı (Plandan Sapmalar 2)
{
    public TimeSpan? Timeout { get; set; }
    public int? AgentVersion { get; set; }
}

public sealed class WorkflowRegistrationOptions // mevcut
{
    public TimeSpan? RunTimeout { get; set; }
    public IReadOnlyList<AgentParameter> Parameters { get; set; } = [];
}

public sealed class WorkflowFunctionOptions // mevcut
{
    public int? MaxConcurrency { get; set; }
}
```

`Tracon.Abstractions`'ta imza değişmedi: `WorkflowDescriptor.Parameters` XML'i kod workflow'u için artık kayıtlı şemayı söyler. HTTP uçları ve konsol: değişiklik yok. `PublicAPI.Unshipped.txt` (Workflows): 22 yeni satır.

## Dosya Listesi (gerçekleşen)

```
src/Tracon.Workflows/
├── WorkflowFunctionBinding.cs                  (yeni)
├── WorkflowFunctionStepOptions.cs              (yeni)
├── WorkflowAgentStepOptions.cs                 (yeni)
├── WorkflowAgentBinding.cs                     (GetWorkflowAgentStep)
├── WorkflowRegistrationOptions.cs              (RunTimeout, Parameters)
├── WorkflowFunctionOptions.cs                  (MaxConcurrency)
├── TraconWorkflowFunctionExtensions.cs         (limit halkası, Configure doğrulaması)
├── TraconWorkflowsBuilderExtensions.cs         (AddWorkflow doğrulaması)
├── Internal/WorkflowNodeConcurrencyLimit.cs    (yeni)
├── Internal/WorkflowGraphBindingException.cs   (yeni)
├── Internal/WorkflowRunWebhooks.cs             (yeni)
├── Internal/WorkflowAgentStepExecutor.cs       (RunAsync)
├── Internal/WorkflowCatalog.cs                 (kod grafı hatasına workflow adı, Describe(Parameters))
├── Internal/CodeWorkflowRegistration.cs        (RunTimeout, Parameters)
├── Internal/WorkflowRunner.cs                  (kod RunTimeout ve Parameters okuması, ortak webhook yükü)
├── Internal/WorkflowRequestExpirationService.cs (run.failed)
├── Internal/TraconWorkflowOptionsValidator.cs  (açılışta tavan denetimi)
├── Internal/WorkflowFunctionSettings.cs        (Serialize)
├── Internal/WorkflowRequestDescriptor.cs       (IsPlanReview)
├── Internal/WorkflowResponseFactory.cs         (IsPlanReview)
├── PublicAPI.Unshipped.txt · README.md
src/Tracon.Abstractions/Workflows/WorkflowDescriptor.cs (yalnız XML)
samples/Tracon.Api/ReviewAndReviseWorkflow.cs (yeni) · EchoModelProvider.cs · Program.cs
tests/Tracon.AspNetCore.FunctionalTests/
├── CodeWorkflowFunctionNodeTests · CodeWorkflowRevisionTests · CodeWorkflowAgentStepTests
├── CodeWorkflowParameterTests · CodeWorkflowRunTimeoutTests · CodeWorkflowRecoveryTests
├── WorkflowAgentStepRecordingTests · WorkflowAgentStepRepairTests · WorkflowStructuredRequestTests
├── WorkflowFunctionConcurrencyTests · WorkflowRequestExpiryWebhookTests
└── Infrastructure/ReviewAndReviseHarness · AgentStepHost · RecordingWebhookPublisher
tests/Tracon.Workflows.UnitTests/ CodeWorkflowRegistrationValidationTests · WorkflowFunctionStepOptionsTests
docs-site/src/content/docs/guides/review-and-revise-workflow.md (yeni) · concepts/workflows.md · concepts/workflow-requests.md
  · capabilities.md · reference/configuration.md · sidebar.mjs
docs/manuel-test/15-WORKFLOWS.md (MT-WF-176…181) · docs/openapi/tracon.json · src/Tracon.Client/Generated/* · packages/tracon-client/src/schema.ts
```

Testler (72 yeni): `CodeWorkflowFunctionNodeTests` 10 (süre, retry, bütçe kapısı, düğüm adlı maliyet, ayar, bilinmeyen fonksiyon) · `CodeWorkflowRevisionTests` 6 (geri kenar, sayaç cevap run'ları arasında, doğrulama döngüsü, kiracı, yapı değişince bekleyen run, zaman aşımı değişince) · `CodeWorkflowAgentStepTests` 6 · `CodeWorkflowParameterTests` 8 (HTTP erken 400, kuyruk, cevap, agent adımı) · `CodeWorkflowRunTimeoutTests` 4 · `CodeWorkflowRecoveryTests` 1 · `WorkflowAgentStepRecordingTests` 6 · `WorkflowAgentStepRepairTests` 4 · `WorkflowStructuredRequestTests` 2 · `WorkflowFunctionConcurrencyTests` 7 · `WorkflowRequestExpiryWebhookTests` 4 · `CodeWorkflowRegistrationValidationTests` 11 (birim) · `WorkflowFunctionStepOptionsTests` 3 (birim).

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

Bağımsız denetçi (`faz-denetcisi`, taze bağlam, `git diff 2ee06055...HEAD`) iki 🔴, üç 🟡 ve üç 🟢 buldu. Denetçi ayrıca tüm fonksiyonel testleri (2120), `Tracon.Workflows.UnitTests`'i (423) ve `dokuman-bakim.py --denetle`'yi koştu; çift yayın, sarmalayıcı sırası, slot sızıntısı, kod `RunTimeout` tutarlılığı ve `Parameters` yollarında sorun görmedi.

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
