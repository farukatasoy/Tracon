# Faz 151 — Harness'in Döngü Yeteneği

> **Durum:** ✅ Tamamlandı (2026-09-07)
> **Kaynak:** [ADAYLAR.md](ADAYLAR.md) · **F-192**
> **Önkoşul:** Yok. MAF 1.20.0 yeterlidir; yükseltme beklemez.
> **Paketler:** `AgentPrism.Abstractions`, `AgentPrism.Core`, `AgentPrism.AspNetCore`
> **Yeni paket:** Yok — `LoopAgent` ve beş evaluator `Microsoft.Agents.AI` çekirdeğindedir · **Migration:** Yok
> **Public API:** Büyüyor — `HarnessSettings`'e bir üye, bir yeni `sealed record`, bir builder metodu, bir `RunEventType` değeri. `wc -l src/*/PublicAPI.Shipped.txt` → her dosya **1 satır** (ölçüldü 2026-09-07): hiçbir yüzey sevk edilmemiştir, bugün eklemek **bedavadır**.
> **Tüketici yüzeyi:** site: `docs-site/src/content/docs/concepts/agents.md` (harness bölümü) · sevk edilen: `HarnessSettings` XML dokümanı, `AgentPrism.Core/README.md`
> **Manuel test alanı:** `docs/manuel-test/29-AGENT-DESTEGI.md`

---

## Bu Faza Başlarken

> `faz-baslangic` skill'ini uygula. Aşağıdaki liste o skill'in 2. adımıdır —
> **tamamını değil, yalnız işaret edilen bölümleri oku.**

1. Bu doküman
2. Kararlar — dosyanın tamamını **okuma**, yalnız bu kalemleri grep'le:
   ```bash
   grep -n "K-020\|K-062\|K-007" docs/KARARLAR.md
   ```
   **K-020** (`MAAI001` bastırması tek dosyada toplanır — harness kullanımı
   `AgentDefinitionCompiler.Agents.cs`'te kalır), **K-062** (`FileAccessStore`
   atanmadıkça özellik kapalıdır — bu fazın "varsayılan kapalı" deseninin
   emsali), **K-007** (yeni paket gerekçe ister — bu fazda yeni paket yok).
3. [`arsiv/fazlar/150-ZORUNLU-BINDING-PROFILI.md`](arsiv/fazlar/150-ZORUNLU-BINDING-PROFILI.md) — yalnız devir notu:
   ```bash
   awk '/## Sonraki Faza Devir Notu/,0' docs/arsiv/fazlar/150-ZORUNLU-BINDING-PROFILI.md
   ```
   Bu faz **sekizinci bir genişleme noktası** açıyor (`LoopEvaluator` kaydı).
   Devir notu şunu söylüyor: yeni bir nokta **yalnız**
   `src/AgentPrism.Core/Diagnostics/AgentPrismExtensionPoints.cs` tablosuna
   satır ekler; teşhis raporu ve başlangıç kapısı ikisi de oradan okur.
4. Alan hafızası (bu faz iki alana dokunuyor):
   [`hafiza/maf-api.md`](hafiza/maf-api.md) (MAF tip imzaları ve `MAAI001`) ·
   [`hafiza/cekirdek-calistirma.md`](hafiza/cekirdek-calistirma.md)
   (decorator zinciri ve 🚨 `Activity.Current`/`AsyncLocal` kuralı — `LoopAgent`
   akışlı yolda `RunCoreStreamingAsync`'i sarmalar)
5. Gerektiğinde, tamamı değil ilgili bölümü:
   [`MAF-GENISLEME-NOKTALARI.md`](MAF-GENISLEME-NOKTALARI.md) — döngü satırı
   bugün *"planlanmadı (eval'den AYRI kavram)"* diyor; bu faz onu kapatır.

---

## Amaç

Bugün AgentPrism bir agent'a "bitene kadar çalış" diyemez. Tüketici bunu kendi
dış döngüsüyle yazmak zorundadır ve o döngü `run` kanıtının **dışında** kalır:
kaç iterasyon koştuğu, hangi ölçütün durdurduğu ve kaç token harcandığı
AgentPrism'in kayıtlarında görünmez. MAF bu yeteneği harness'in birinci sınıf,
opt-in bir parçası yapmıştır; AgentPrism onu hiç bağlamamıştır.

- **F-192** — `HarnessSettings`'e bildirimsel bir **bitiş ölçütü** aç, MAF'ın
  `LoopAgent`'ını harness'in en dışına bağla ve her iterasyonu `run` kanıtına yaz.

### Bugün ne çalışmıyor — doğrulanmış kanıt

| Kanıt | Gözlem |
|---|---|
| [`AgentDefinitionCompiler.Agents.cs:236-268`](../src/AgentPrism.Core/Compilation/AgentDefinitionCompiler.Agents.cs#L236) | `HarnessAgentOptions` on üç üye set ediyor; `LoopEvaluators` ve `LoopAgentOptions` **ikisi de yok** |
| `grep -rn "LoopAgent\|LoopEvaluator" src/` | **0 eşleşme** — kaynağın tamamında |
| [`HarnessSettings.cs`](../src/AgentPrism.Abstractions/Agents/HarnessSettings.cs) | On bir üye; hiçbiri bitiş ölçütü değil. `MaximumIterationsPerRequest` bir **tavan**tır |
| [`MAF-GENISLEME-NOKTALARI.md`](MAF-GENISLEME-NOKTALARI.md) | Döngü *"planlanmadı"* diye kayıtlı — **reddedilmemiş, ertelenmiş** |

> Kanıtlar 2026-09-07 tarihinde doğrulandı. Aday metnindeki satır numarası
> (`:172`) kaymıştır; doğru satır **236**'dır.

### MAF imzaları — `maf-api-kesfi` ile doğrulandı (2026-09-07, 1.20.0)

```csharp
// Microsoft.Agents.AI
public sealed class LoopAgent : DelegatingAIAgent
{
    public LoopAgent(AIAgent innerAgent, IEnumerable<LoopEvaluator> evaluators,
                     LoopAgentOptions options, ILoggerFactory loggerFactory);
    protected override Task<AgentResponse> RunCoreAsync(...);
    protected override IAsyncEnumerable<AgentResponseUpdate> RunCoreStreamingAsync(...);
}

public sealed class LoopAgentOptions
{
    public int? MaxIterations { get; set; }
    public bool FreshContextPerIteration { get; set; }
    public bool NonStreamingReturnsLastResponseOnly { get; set; }
    public bool ExcludeOnBehalfOfMessages { get; set; }
    public string OnBehalfOfAuthorName { get; set; }
    public Func<AgentSession, CancellationToken, ValueTask> SessionCreatedCallback { get; set; }
}

public abstract class LoopEvaluator
{
    public virtual ValueTask<LoopEvaluation> EvaluateAsync(LoopContext context, CancellationToken ct);
}

public sealed class LoopEvaluation          // Continue(feedback) · ContinueWithMessages(msgs) · Stop()
{ public string Feedback { get; } public bool ShouldReinvoke { get; } }

// Beş yerleşik evaluator
new TodoCompletionLoopEvaluator(TodoCompletionLoopEvaluatorOptions);            // .Modes, .FeedbackMessageTemplate
new CompletionMarkerLoopEvaluator(string completionMarker, CompletionMarkerLoopEvaluatorOptions);
new AIJudgeLoopEvaluator(IChatClient judgeClient, AIJudgeLoopEvaluatorOptions); // .Criteria, .Instructions
new BackgroundTaskCompletionLoopEvaluator(BackgroundTaskCompletionLoopEvaluatorOptions);
new DelegateLoopEvaluator(Func<LoopContext, CancellationToken, ValueTask<LoopEvaluation>>);

// Microsoft.Agents.AI.Harness — HarnessAgentOptions
public IEnumerable<LoopEvaluator> LoopEvaluators { get; set; }
public LoopAgentOptions LoopAgentOptions { get; set; }
```

---

## 151.1 — Bitiş ölçütü bildirimsel, evaluator kodda kalır

Bu fazın ilk sorusu adayın kendi risk satırındaki sorudur: evaluator seçimi
**bildirimsel bir tip** mi olsun, yoksa MAF tipi doğrudan mı açılsın? Cevap
**ikisi birden**, ama kesin bir sınırla — ve o sınır K2'dir.

`LoopEvaluator` seçmek, agent'a bir **durma koşulu** vermektir. Beş yerleşik
evaluator'dan dördü yalnız **veri** alır (mod adı, işaret metni, ölçüt cümleleri);
beşincisi (`DelegateLoopEvaluator`) bir `Func` alır — yani **kod**. K2 gereği
arayüzden gelen bir tanım kod tanımlayamaz. Bu yüzden yüzey ikiye ayrılır ve
bu ayrım `AddEvalCheck` deseninin **birebir tekrarıdır**
([`AgentPrismEvalCheckRegistration.cs`](../src/AgentPrism.Core/Evaluation/AgentPrismEvalCheckRegistration.cs)):

```mermaid
flowchart TD
    A["AgentDefinition.Harness.Loop<br/>(bildirimsel · JSON · arayüzden gelebilir)"] --> B{LoopEvaluatorRegistry}
    C["IAgentPrismBuilder.AddLoopEvaluator(kind, LoopEvaluator)<br/>(yalnız kodda)"] --> B
    B --> D["IReadOnlyList&lt;LoopEvaluator&gt;"]
    D --> E["HarnessAgentOptions.LoopEvaluators<br/>+ LoopAgentOptions"]
    E --> F["MAF: LoopAgent, harness'in EN DIŞINDA"]
    F --> G["RunRecordingAgent<br/>(iterasyon olayını yazar)"]
```

**Bildirimsel taraf — dört yerleşik `kind`:**

| `kind` | MAF karşılığı | Aldığı veri | K2 durumu |
|---|---|---|---|
| `todoCompletion` | `TodoCompletionLoopEvaluator` | `modes` (string dizisi) | ✅ veri |
| `completionMarker` | `CompletionMarkerLoopEvaluator` | `marker` (string) | ✅ veri |
| `aiJudge` | `AIJudgeLoopEvaluator` | `criteria` (string dizisi), `instructions` | ✅ veri |
| `backgroundTaskCompletion` | `BackgroundTaskCompletionLoopEvaluator` | — | ✅ veri |

**Kod tarafı:** `DelegateLoopEvaluator` **bildirimsel yüzeye hiç girmez.**
Tüketici kendi `LoopEvaluator`'ını `AddLoopEvaluator("kind", evaluator)` ile
kaydeder; tanım yalnız o `kind`'ın **adını** anar. Bilinmeyen bir `kind`
`AgentPrismException` fırlatır — sessizce yok sayılmaz, çünkü sessizce yok
sayılan bir durma koşulu sonsuz döngüye dönüşür.

> 🚨 `aiJudge` bir model çağrısıdır ve **her iterasyonda** koşar. `IChatClient`
> nereden gelecek sorusu Açık Soru 1'dedir.

## 151.2 — `MaximumIterationsPerRequest` ile karıştırılmama sözleşmesi

Bu fazın en büyük yanlış anlaşılma riski adayın risk satırındaki maddedir ve
**tasarımla** çözülür, yalnız dokümanla değil.

| | `HarnessSettings.MaximumIterationsPerRequest` | `LoopSettings.MaxIterations` |
|---|---|---|
| Ne | Bir **tavan** — kaçak döngü güvenliği | Bir **bitiş ölçütü** tavanı |
| Nerede | Harness'in **iç** tool döngüsü | `LoopAgent`'ın **dış** yeniden çağırma döngüsü |
| Aşılınca | Harness durur, yanıt döner | Döngü durur, son yanıt döner |
| Varsayılan | `null` (MAF'ın kendi varsayılanı) | `null` iken **zorunlu bir değer atanır** — 151.3 |

İkisi ayrı üyelerde durur ve **ayrı adlarla** yazılır. `LoopSettings` ayrı bir
`sealed record`'tur; `HarnessSettings`'in düz bir alanı **yapılmaz** — düz alan
iki sayıyı yan yana koyar ve karışıklığı üretir.

## 151.3 — Varsayılan kapalı, ama açıkken sınırsız değil

**K1 — sıfır sürpriz.** `HarnessSettings.Loop` `null`'dur. `null` iken
`LoopEvaluators` ve `LoopAgentOptions` **hiç atanmaz** ve harness bugünkü
davranışını birebir korur. Bu, `FileAccessStore`'un K-062 desenidir.

Döngü açıldığında ise sınırsız bırakılmaz. `LoopAgentOptions.MaxIterations`
`null` ise **AgentPrism bir varsayılan seçer**. Gerekçe: bir bitiş ölçütü
yanlış yazıldığında (örneğin ulaşılamayan bir `completionMarker`) fatura
sınırsız büyür ve tüketici bunu ancak faturada görür. Sayının kendisi Açık
Soru 2'dedir.

Döngü [Faz 114](arsiv/fazlar/114-CALISTIRMA-ICI-BUTCE-TAVANI.md)'ün bütçe
tavanıyla **birlikte** yargılanır: bütçe tavanı iterasyon ortasında dolarsa
`run` durur ve döngü devam etmez. Plan bunu yeni bir mekanizmayla değil, mevcut
tavanın `LoopAgent`'ın **içinde** kalmasıyla sağlar — `LoopAgent` en dıştadır,
bütçe tavanı içerideki `run`'ı keser, kesilme dışarı yayılır.

## 151.4 — İterasyon `run` kanıtına yazılır

Adayın "döngü iterasyonları `run` kanıtına yazılır" cümlesi bir olay tipi ister.
İki yol vardır ve plan birini seçer:

| Yol | Maliyet | Değerlendirme |
|---|---|---|
| **Yeni `RunEventType` değeri** (`LoopIterationCompleted = 30`) | Enum'a **append** — güvenli. Bugün son değer `Custom = 29` (ölçüldü) | ✅ **Seçilen.** Döngü AgentPrism'in yerleşik yeteneğidir, tüketici uzantısı değil |
| `RunEventType.Custom` + `agentprism.loop.iteration` | Yeni enum değeri yok | ❌ `ReservedPrefix` mekanizması *tüketici uzantısı* içindir; yerleşik bir yeteneği oraya koymak iki kavramı karıştırır |

Olay her iterasyon **bittiğinde** yazılır ve şunları taşır: iterasyon numarası,
durduran evaluator'ın `kind`'ı (durduysa), `LoopEvaluation.Feedback`'in
**varlığı**. 🚨 `Feedback` metninin kendisi olaya **yazılmaz** — modele giden
serbest metindir ve `run` olay akışı istemciye açıktır.

> 🚨 `LoopAgent : DelegatingAIAgent` akışlı yolu da sarmalar. Olay yazımı
> `RunCoreStreamingAsync` yolunda **her `MoveNextAsync` öncesi** doğru
> `Activity`/`AsyncLocal` bağlamında olmalıdır — `hafiza/cekirdek-calistirma.md`
> bu sınıfın dört kez tekrarlandığını kaydediyor.

---

## Planlanan Public API

> Taslak imzalardır. Gerçekleşen imzalar kapanışta ayrı bir bölüme yazılır.

```csharp
// AgentPrism.Abstractions
public sealed record HarnessSettings
{
    // ... mevcut on bir üye değişmez ...

    /// <summary>Gets the loop (run-until-done) settings. Null turns the loop off.</summary>
    public LoopSettings? Loop { get; init; }
}

/// <summary>Declarative settings for the harness loop.</summary>
public sealed record LoopSettings
{
    /// <summary>The stop criteria, evaluated in order. At least one is required.</summary>
    public required IReadOnlyList<LoopCriterion> Criteria { get; init; }

    /// <summary>The upper number of loop iterations. Null takes the AgentPrism default.</summary>
    public int? MaxIterations { get; init; }

    /// <summary>Starts every iteration with a fresh context.</summary>
    public bool FreshContextPerIteration { get; init; }
}

/// <summary>One declarative stop criterion.</summary>
public sealed record LoopCriterion
{
    /// <summary>The criterion kind: todoCompletion, completionMarker, aiJudge,
    /// backgroundTaskCompletion, or a kind registered with AddLoopEvaluator.</summary>
    public required string Kind { get; init; }

    /// <summary>The completion marker text. Used by completionMarker only.</summary>
    public string? Marker { get; init; }

    /// <summary>The judge criteria. Used by aiJudge only.</summary>
    public IReadOnlyList<string> Criteria { get; init; } = [];

    /// <summary>Extra judge instructions. Used by aiJudge only.</summary>
    public string? Instructions { get; init; }

    /// <summary>The agent modes the criterion covers. Used by todoCompletion only.</summary>
    public IReadOnlyList<string> Modes { get; init; } = [];
}

public enum RunEventType
{
    // ... Custom = 29 ...
    /// <summary>One harness loop iteration finished.</summary>
    LoopIterationCompleted = 30,
}

// AgentPrism.Core
public sealed record AgentPrismLoopEvaluatorRegistration(
    string Kind,
    Microsoft.Agents.AI.LoopEvaluator Evaluator);

public interface IAgentPrismBuilder
{
    /// <summary>Registers a code-defined loop stop criterion under a kind name.</summary>
    IAgentPrismBuilder AddLoopEvaluator(string kind, Microsoft.Agents.AI.LoopEvaluator evaluator);
}
```

### HTTP `endpoint`'leri

Yeni uç **yok**. `HarnessSettings` agent tanımının parçasıdır; mevcut
`POST`/`PUT /api/agents` gövdesi bir alan kazanır ve OpenAPI/NSwag/TypeScript
zinciri yeniden üretilir.

### Arayüz payı

Bu faz arayüze **dokunmaz** (agent tanımı düzenleyicisi JSON tabanlıdır).
Bugünkü ölçüm (2026-09-07): `src/AgentPrism.UI/wwwroot/assets/` toplam
**151.9 KB** brotli; bütçe 250 KB.

---

## Planlanan Dosya Listesi

```
src/AgentPrism.Abstractions/
├── Agents/
│   ├── HarnessSettings.cs          (değişir — Loop üyesi)
│   ├── LoopSettings.cs             (yeni)
│   └── LoopCriterion.cs            (yeni)
└── Runs/
    └── RunEventType.cs             (değişir — LoopIterationCompleted = 30)

src/AgentPrism.Core/
├── Compilation/
│   ├── AgentDefinitionCompiler.Agents.cs   (değişir — LoopEvaluators + LoopAgentOptions)
│   └── LoopEvaluatorRegistry.cs            (yeni — EvalCheckRegistry deseni)
├── Agents/
│   └── AgentPrismLoopEvaluatorRegistration.cs  (yeni)
├── Diagnostics/
│   └── AgentPrismExtensionPoints.cs        (değişir — sekizinci nokta satırı)
├── AgentPrismBuilder.cs                    (değişir — AddLoopEvaluator)
└── IAgentPrismBuilder.cs                   (değişir)

tests/
├── AgentPrism.Core.Tests/Compilation/LoopEvaluatorRegistryTests.cs   (yeni)
├── AgentPrism.Core.Tests/Compilation/LoopCompilationTests.cs         (yeni)
└── AgentPrism.AspNetCore.FunctionalTests/Agents/HarnessLoopTests.cs  (yeni)
```

---

## Hata Modları ve Testler

> Mutlu yoldan değil, **ne bozulabilir**den türetilir. Seviyeyi plan seçer.
> Sınır geçen davranış (DI · HTTP · kiracı · akış · depo · paket) birim
> testiyle kanıtlanamaz — [`.agents/ortak/test-seviyeleri.md`](../.agents/ortak/test-seviyeleri.md).

| Ne bozulabilir | Seviye | Test sınıfı |
|---|---|---|
| `Loop` `null` iken `LoopEvaluators`/`LoopAgentOptions` yine de atanır ve harness sarmalanır | Birim | `LoopCompilationTests` |
| Bilinmeyen bir `kind` sessizce yok sayılır ⇒ durma koşulsuz sonsuz döngü | Birim | `LoopEvaluatorRegistryTests` |
| `Criteria` boş liste gelir; `LoopAgent` evaluator'sız kurulur | Birim | `LoopEvaluatorRegistryTests` |
| `MaxIterations` `null` iken AgentPrism varsayılanı **uygulanmaz** ⇒ sınırsız fatura | Fonksiyonel | `HarnessLoopTests` |
| İptal (`CancellationToken`) iterasyon **ortasında** gelir; döngü bir tur daha atar | Fonksiyonel | `HarnessLoopTests` |
| Akışlı yolda iterasyon olayı yanlış `Activity` bağlamında yazılır ⇒ `span` ağacı kopar | Fonksiyonel | `HarnessLoopTests` (akışlı `run` + `ActivityListener`) |
| Faz 114 bütçe tavanı dolduğunda döngü **devam eder** | Fonksiyonel | `HarnessLoopTests` |
| `LoopIterationCompleted` olayı başka kiracının akışında görünür | Sözleşme | `TenantIsolationContract` |
| `AddLoopEvaluator` aynı `kind`'ı iki kez kaydeder | Birim | `LoopEvaluatorRegistryTests` |
| `AddLoopEvaluator` yerleşik bir `kind`'ı (`aiJudge`) gölgeler | Birim | `LoopEvaluatorRegistryTests` |
| Sekizinci genişleme noktası `AgentPrismExtensionPoints` tablosuna girmez ⇒ teşhis raporu eksik | Birim | mevcut `ExtensionPointsTests` |
| `aiJudge` model çağrısı hata verir; döngü durmaz veya `run` çöker | Fonksiyonel | `HarnessLoopTests` |
| `Feedback` metni olay akışına sızar | Fonksiyonel | `HarnessLoopTests` |

Beş soru: **iptal** ✅ (satır 5) · **eşzamanlılık** — `LoopEvaluator` singleton
değildir, tanım başına derlenir; paylaşılan durum yoktur · **boş/aşırı girdi**
✅ (satır 3, 4) · **başka kiracı** ✅ (satır 8) · **alt sistem hatası** ✅ (satır 11).

---

## Manuel Kabul Case'leri

> Kapanışta `docs/manuel-test/29-AGENT-DESTEGI.md` içine eklenecek case'lerin
> taslağı.

| # | Ön koşul | Adımlar | Beklenen sonuç |
|---|---|---|---|
| 1 | `samples/AgentPrism.Api` ayakta, harness'li agent | Tanıma `harness.loop` **eklemeden** `run` at | Bugünkü davranış birebir; olay akışında `LoopIterationCompleted` **yok** |
| 2 | Aynı | `harness.loop.criteria = [{kind:"completionMarker", marker:"DONE"}]` ile `run` at | Model `DONE` yazana kadar döner; her iterasyon için bir `LoopIterationCompleted` olayı |
| 3 | Aynı | Ulaşılamayan bir `marker` ver, `maxIterations` verme | AgentPrism varsayılanında durur; `run` tamamlanır, sınırsız çalışmaz |
| 4 | Aynı | `kind:"yok-boyle-bir-sey"` ile tanım kaydet | `400` — kayıt reddedilir, sessizce yok sayılmaz |
| 5 | Aynı | Akışlı (`SSE`) `run` at ve `span` ağacına bak | Her iterasyon ağaçta ayrı görünür; kök `span` kopmaz |
| 6 | Faz 114 bütçe tavanı düşük ayarlı | Döngülü `run` at | Tavan dolduğunda `run` durur; döngü yeni iterasyon **açmaz** |

---

## Açık Sorular

> Planı bloklamayan, faz uygulanırken karara bağlanacak sorular.

| # | Soru | Seçenekler | Öneri |
|---|---|---|---|
| 1 | `aiJudge`'ın `IChatClient`'ı nereden gelir? | A: agent'ın kendi binding'i · B: `ModelRunJudgeOptions.Model` (mevcut judge binding'i) · C: `LoopCriterion`'a kendi binding'i | **B** — judge binding'i zaten yapılandırılmış, doğrulanmış ve ayrı fiyatlandırılıyor. A, döngü maliyetini agent'ın modeline bağlar ve pahalı bir modelde faturayı çarpar |
| 2 | `MaxIterations` varsayılanı kaç? | A: 10 · B: 25 · C: `MaximumIterationsPerRequest` ile aynı | **A (10)** — ölçülmüş bir sayı yoktur; küçük başlamak geri alınabilir, büyük başlamak fatura üretir. Uygulayan oturum bu sayıyı örnek uygulamada **ölçer** ve gerekçesini kapanışa yazar |
| 3 | `NonStreamingReturnsLastResponseOnly` açılsın mı? | A: MAF varsayılanı · B: AgentPrism açıkça `false` (tüm iterasyonlar döner) | **B** — `run` kanıtı bütünlüğü. Ama önce `maf-api-kesfi` ile MAF varsayılanı **ölçülmeli** |
| 4 | `todoCompletion`'ın `Modes`'u AgentPrism'in agent modlarıyla mı eşleşir? | A: doğrudan geçir · B: doğrula | **B** — bilinmeyen mod adı sessiz bir "hiç durmaz" üretir |

---

## Bitiş Ölçütleri (DoD)

- [x] `harness.loop` **verilmeyen** agent tanımının derlenmiş `HarnessAgentOptions`'ında `LoopEvaluators` ve `LoopAgentOptions` **atanmamıştır** — `LoopCompilationTests.A_harness_without_loop_settings_gets_no_loop_at_all` (`GetService<LoopAgent>()` null) ve `HarnessLoopTests.A_harness_without_loop_settings_writes_no_iteration_event` (tek model çağrısı, sıfır olay)
- [x] `completionMarker` ölçütüyle bir `run`, marker gelene kadar döner ve her iterasyon `LoopIterationCompleted` olayı yazar — `HarnessLoopTests` + gerçek koşum 1
- [x] `maxIterations` verilmeyen bir döngü AgentPrism varsayılanında durur — `An_unreachable_criterion_stops_at_the_AgentPrism_ceiling_instead_of_running_on`; gerçek koşum 2 tavan yolunu ayrıca ölçtü
- [x] Bilinmeyen `kind` taşıyan tanım `400` ile reddedilir — `An_unknown_criterion_kind_is_refused_with_400_when_the_definition_is_saved` (HTTP seviyesinde; denetim bulgusu 2)
- [x] `AddLoopEvaluator` ile kaydedilen kod tarafı evaluator bildirimsel `kind` adıyla çözülür — `LoopCompilationTests` + `LoopEvaluatorRegistryTests`
- [x] ~~Sekizinci genişleme noktası `AgentPrismExtensionPoints` tablosundadır~~ — **bu satır ölçülerek düşürüldü (K-709, sapma 3).** O tablo DI'dan çözülen sözleşmeler içindir; emsal `AddEvalCheck` de orada değildir. Yerine kapsam `CapabilityCoverageTests` ile kapandı: `AddLoopEvaluator` yetenek haritasında adıyla görünür
- [x] Dört doğrulama kapısı sıfır uyarı verir
- [x] `samples/AgentPrism.Api` ile gerçek `run` yapıldı, çıktı belgeye yazıldı — üç koşum, ayrı bölümde
- [x] `secret` taraması boş döndü (`kapi.py tarama`)
- [x] Manuel kabul case'leri eklendi — **`02-CEKIRDEK-VE-KATALOG.md`**, `29-AGENT-DESTEGI.md` değil (sapma 7): MT-CORE-123..128
- [x] `faz-denetim` koşuldu; 🔴 bulgu kapatıldı
- [x] `docs-site/` güncellendi (`concepts/agents.md` yeni bölüm, `capabilities.md` yeni satır); `npm run check` dört kapıyı da koştu
- [x] OpenAPI → NSwag → TypeScript zinciri yeniden üretildi; `@agentprism/client` `tsc` temiz, üretilen `AgentPrismApiClient.g.cs` derleniyor

### Doğrulama komutları

```bash
# Döngü kapalıyken bugünkü davranış korunuyor mu
curl -s -X POST http://localhost:5081/agentprism/api/agents/demo/run \
  -H 'content-type: application/json' -d '{"message":"merhaba"}' | jq '.runId'

# Döngü açıkken iterasyon olayları yazılıyor mu
curl -s "http://localhost:5081/agentprism/api/runs/<runId>/events" \
  | jq '[.[] | select(.type=="LoopIterationCompleted")] | length'
```

---

## Riskler

| Risk | Önlem |
|------|-------|
| `MaximumIterationsPerRequest` ile `MaxIterations` karıştırılır | Ayrı `record`'ta ayrı ad; XML dokümanı ikisini yan yana karşılaştırır (151.2 tablosu sevk edilen metne girer) |
| Döngü model faturasını çarpar | `MaxIterations` varsayılanı **zorunlu**; Faz 114 bütçe tavanı `LoopAgent`'ın içinde kalır |
| `aiJudge` her iterasyonda model çağırır — döngü maliyeti iki katına çıkar | Açık Soru 1: ayrı judge binding'i; maliyet `RunKind.Eval` olarak ayrı kaydedilir |
| Akışlı yolda `Activity`/`AsyncLocal` yazımı kaybolur | `hafiza/cekirdek-calistirma.md` kuralı; fonksiyonel test akışlı yolu ayrıca koşar |
| `FreshContextPerIteration` bağlam semantiğini sessizce değiştirir | Varsayılan `false`; XML dokümanı davranış farkını açıkça yazar |
| MAF `MAAI001` yüzeyi değişir | K-020: kullanım tek dosyada (`AgentDefinitionCompiler.Agents.cs`) toplanır |
| Bilinmeyen `kind` yok sayılırsa sonsuz döngü | Kayıt anında `400`; çalışma anında `AgentPrismException` |

---

<!-- ============================================================
     AŞAĞISI KAPANIŞTA DOLDURULUR — `faz-tamamlama` skill'i.
     Plan anında boş kalır. Başlıkları SİLME.
     ============================================================ -->

## Plandan Sapmalar

Yedi sapma. İlk üçü planın **yapısal iddiasını ölçünce** düştü (`faz-uygulama`
Adım 1); kalan dördü uygulama sırasında ortaya çıktı.

### 1. 🚨 `LoopIterationCompleted = 30` DEĞİL, **31**

Plan "bugün son değer `Custom = 29` (ölçüldü)" diyordu. `RunEventType.cs`
okundu: **30 zaten `ChildRunTimedOut`'tur** (Faz 144). `run_events.type` bir
`smallint` sütunudur; var olan bir üyenin sayısal değeri kaydırılamaz. Yeni
değer **31**'dir (K-703).

Bu, `MEMORY.md`'nin *"Plandaki 'yeni enum degeri N' iddiasi kod okunmadan
guvenilmez"* maddesinin (K-492) **ikinci** vakasıdır. Aynı tuzak, aynı dosya.

### 2. 🚨 `LoopAgent` harness'in EN DIŞINDA DEĞİL, **İÇİNDE**

Planın 151.1 diyagramı `LoopAgent`'ı harness'in dışına koyuyordu. Gerçek zincir
bir probe ile ölçüldü (MAF 1.20.0, 2026-09-07):

```
HarnessAgent → LoopAgent → ToolApprovalAgent → OpenTelemetryAgent → ChatClientAgent
```

`AsHarnessAgent` bir `HarnessAgent` döner; `LoopEvaluators` atanınca MAF
`LoopAgent`'ı **harness'in içine** yerleştirir. Sonuç, planın Faz 114 iddiasını
**güçlendirdi**: bütçe tavanı (`RunBudgetChatClient`) `ChatClientAgent`'ın
`IChatClient`'ındadır, yani `LoopAgent`'ın **içindedir** — tavan dolduğunda
döngünün bir sonraki model çağrısı reddedilir ve döngü yeni iterasyon açamaz.
Fonksiyonel test bunu ölçer.

### 3. 🚨 Döngü **sekizinci bir genişleme noktası DEĞİLDİR**

Plan ve DoD, `LoopEvaluator` kaydının `AgentPrismExtensionPoints` tablosuna
satır eklemesini istiyordu. Tablo grep'lendi: o tablo **DI'dan çözülen, tek
örnekli, yerleşik varsayılanı olan yedi sözleşme** içindir —
`AgentPrismDiagnosticsCollector` "hangi uygulama bağlı" diye sorar,
`RequiredBindingValidator` "hâlâ yerleşik varsayılan mı" diye sorar.
`kind` ile anahtarlanmış bir kayıt kümesinin ne yerleşik varsayılanı ne de
`RequireCustomBinding<LoopEvaluator>()` karşılığı vardır.

Emsal aynı repodadır ve birebir aynı şekildedir: `AddEvalCheck` /
`AgentPrismEvalCheckRegistration` de o tabloda **değildir**. Döngü onu izler
(K-709). DoD'un o satırı **karşılanmadı ve karşılanmamalıydı**; yerine kapsam
`CapabilityCoverageTests` (yetenek haritası) ile kapandı.

### 4. `LoopCriterion.Criteria` → **`JudgeCriteria`**, `Instructions` → **`JudgeInstructions`**

Planın taslak imzası `LoopSettings.Criteria` (ölçüt listesi) ile
`LoopCriterion.Criteria` (yargıç ölçüt cümleleri) adlarını bir seviye arayla
yan yana koyuyordu. Public API'de kırıcı değişiklik pahalıdır ve bu ad
karışıklığı okuyanın kafasında bir kez oluşup kalır. İki alan da yalnız
`aiJudge` tarafından okunur; ön ek bunu adın kendisine taşır.

### 5. AgentPrism MAF'a **TEK** evaluator verir, listeyi değil

Plan `LoopEvaluators`'a ölçüt listesini geçirmeyi ima ediyordu. Probe ile üç şey
ölçüldü (MAF 1.20.0):

1. MAF, ilk **`Continue` diyen** evaluator'da **kısa devre** yapar; kalanları o
   iterasyonda hiç çağırmaz.
2. Bir evaluator istisna atarsa istisna dışarı sızar ve **tüm `run`'ı düşürür**.
3. `LoopContext.Feedback` iterasyonlar boyunca **birikir**.

Listeyi doğrudan geçirmek üç şeyi imkânsız kılıyordu: iterasyon başına **tek**
olay (ölçüt başına sarmalayıcı N olay yazardı), **hangi ölçütün** devam
istediğini adlandırmak, ve bir ölçütün istisnasını **kapsamak**.
`RecordingLoopEvaluator` MAF'ın sırasını **birebir** yeniden üretir (kısa devre
dâhil, yani `aiJudge` maliyeti artmaz) ve bu üçünü kazandırır (K-704).

### 6. `todoCompletion.Modes` **doğrulanmaz** (Açık Soru 4'ün B'si uygulanamaz)

Açık Soru 4 "bilinmeyen mod adını doğrula" diyordu. `grep -rn "AgentMode"
src/AgentPrism.Abstractions/Agents/` **sıfır** döndü:
`HarnessAgentOptions.AgentModeProviderOptions` AgentPrism tarafından **hiç
atanmıyor**, yani doğrulanacak bir mod adı kümesi **yok**. Alan geçirilir; XML
dokümanı bunu açıkça yazar ve zorunlu `MaxIterations` tavanı "hiç durmayan
ölçüt" riskini zaten sınırlar.

### 7. Manuel case'ler `29-AGENT-DESTEGI.md`'ye DEĞİL, `02-CEKIRDEK-VE-KATALOG.md`'ye

Plan `29-AGENT-DESTEGI.md`'yi gösteriyordu; o dosyanın alanı tüketici kod
agent'ı desteğidir (analyzer + yetenek haritası). `00-INDEKS.md` §7'ye göre
`src/AgentPrism.Core/Compilation/` ve `src/AgentPrism.Abstractions`
`02-CEKIRDEK-VE-KATALOG.md` (`CORE`) alanındadır. Case'ler oraya yazıldı:
**MT-CORE-123..128**.

### Açık soruların kapanışı

| # | Karar | Gerekçe |
|---|---|---|
| 1 | **B** (öneri) | `aiJudge`, `AddModelRunJudge` binding'ini kullanır. Yapılandırılmamışsa **derleme hatası** — agent'ın kendi modeline sessizce düşmez (K-706) |
| 2 | **A (10)** — ve ölçüldü | Probe: `MaxIterations = null` iken MAF **kendi** varsayılanı olarak 10 iterasyonda duruyor. AgentPrism aynı sayıyı **açıkça** yazar; garanti MAF'ın belgelenmemiş varsayılanına değil AgentPrism'e ait olur (K-705) |
| 3 | **B** — ve ölçüldü | Probe: MAF varsayılanı zaten `false`. AgentPrism yine de **açıkça** `false` atar (Faz 144.4 deseni) |
| 4 | **A**, zorunlu olarak | Bkz. sapma 6 |

## Bu Fazda Verilen Kararlar

K-703 … K-709. Tam metin `docs/KARARLAR.md`'dedir.

## Gerçekleşen Public API

```csharp
// AgentPrism.Abstractions
public sealed record HarnessSettings
{
    public LoopSettings? Loop { get; init; }        // yeni üye
}

public sealed record LoopSettings
{
    public const int DefaultMaxIterations = 10;
    public required IReadOnlyList<LoopCriterion> Criteria { get; init; }
    public int? MaxIterations { get; init; }
    public bool FreshContextPerIteration { get; init; }
}

public sealed record LoopCriterion
{
    public required string Kind { get; init; }
    public string? Marker { get; init; }
    public IReadOnlyList<string> JudgeCriteria { get; init; } = [];
    public string? JudgeInstructions { get; init; }
    public IReadOnlyList<string> Modes { get; init; } = [];
}

public enum RunEventType { /* ... */ LoopIterationCompleted = 31 }

// AgentPrism.Core
public sealed record AgentPrismLoopEvaluatorRegistration(
    string Kind, Microsoft.Agents.AI.LoopEvaluator Evaluator);

public interface IAgentPrismBuilder
{
    IAgentPrismBuilder AddLoopEvaluator(string kind, Microsoft.Agents.AI.LoopEvaluator evaluator);
}

// AgentDefinitionCompiler'ın public ctor'u iki İSTEĞE BAĞLI parametre kazandı:
//   IEnumerable<AgentPrismLoopEvaluatorRegistration>? loopEvaluators = null
//   ModelRunJudgeOptions? judgeOptions = null
// LoopEvaluatorRegistry internal kaldı; ikisi de public tiplerdir.
```

## Dosya Listesi (gerçekleşen)

```
src/AgentPrism.Abstractions/
├── Agents/HarnessSettings.cs               (değişti — Loop)
├── Agents/LoopSettings.cs                  (yeni)
├── Agents/LoopCriterion.cs                 (yeni)
└── Runs/RunEventType.cs                    (değişti — LoopIterationCompleted = 31)

src/AgentPrism.Core/
├── Compilation/LoopEvaluatorRegistry.cs            (yeni — internal)
├── Compilation/RecordingLoopEvaluator.cs           (yeni — internal + payload record)
├── Compilation/AgentPrismLoopEvaluatorRegistration.cs (yeni — public)
├── Compilation/AgentDefinitionCompiler.cs          (değişti — iki ctor parametresi)
├── Compilation/AgentDefinitionCompiler.Agents.cs   (değişti — Loop bağlama)
├── AgentPrismBuilder.cs · IAgentPrismBuilder.cs    (değişti — AddLoopEvaluator)
├── AgentPrismServiceCollectionExtensions.Registration.Core.cs (değişti — DI)
├── AgentPrismCoreJsonContext.cs                    (değişti — payload tipi)
└── buildTransitive/AgentPrism.AgentMap.md          (üretildi)

src/AgentPrism.AspNetCore/Endpoints/RunEndpoints.cs (değişti — SSE adı)
src/AgentPrism.UI/frontend/src/lib/run-event.ts      (değişti)
src/AgentPrism.UI/frontend/src/screens/run-detail.tsx (değişti)
src/AgentPrism.Client/Generated/AgentPrismApiClient.g.cs (üretildi)
packages/agentprism-client/src/schema.ts             (üretildi)
docs/openapi/agentprism.json                         (üretildi)

tests/
├── AgentPrism.Core.UnitTests/Compilation/LoopEvaluatorRegistryTests.cs (yeni — 13)
├── AgentPrism.Core.UnitTests/Compilation/LoopCompilationTests.cs       (yeni — 5)
├── AgentPrism.AspNetCore.FunctionalTests/HarnessLoopTests.cs           (yeni — 8)
├── AgentPrism.Core.UnitTests/Runs/RunEventTypeTests.cs                 (değişti)
├── AgentPrism.Core.UnitTests/Architecture/run-event-payload-baseline.txt (değişti)
└── AgentPrism.Core.UnitTests/Architecture/public-surface-baseline.txt    (değişti)

docs-site/src/content/docs/concepts/agents.md   (elle — "Run until the work is done")
docs-site/src/content/docs/capabilities.md      (elle — Harness loop satırı)
docs/manuel-test/02-CEKIRDEK-VE-KATALOG.md      (MT-CORE-123..128)
```

## Gerçek Koşum (`samples/AgentPrism.Api`, gerçek OpenAI çağrısı, 2026-09-07)

Üç koşum yapıldı. İkisi döngüyü kanıtladı, biri **Faz 151'e ait olmayan** bir
kusuru ortaya çıkardı.

**1 — Ölçüt döngüyü durduruyor** (`loop-poet`, `completionMarker: "ALL DONE"`,
`maxIterations: 5`). Model iki turda bitirdi; `run` `Completed`:

```
LoopIterationCompleted | iteration 1 continued by 'completionMarker'
  {"iteration":1,"continued":true,"continuedBy":"completionMarker","hasFeedback":true,"failedCriterion":null,"ceilingReached":false}
LoopIterationCompleted | iteration 2 stopped
  {"iteration":2,"continued":false,"continuedBy":null,"hasFeedback":false,"failedCriterion":null,"ceilingReached":false}
RunCompleted
```

**2 — Tavan döngüyü durduruyor** (`loop-ceiling`, ulaşılamayan marker,
`maxIterations: 3`). Üç model turu, **iki** olay, sonuncusu tavanı adlandırıyor:

```
LoopIterationCompleted | iteration 1 continued by 'completionMarker'
LoopIterationCompleted | iteration 2 continued by 'completionMarker'; the iteration ceiling ends the loop
  {"iteration":2,...,"ceilingReached":true}
RunCompleted
```

**3 — 🚨 K-053 gerçek bir modelde yeniden üretildi (Faz 151'in kusuru DEĞİL).**
Tool çağıran bir harness agent'ı `HTTP 400 (invalid_request_error)` ile
düşüyor: *"messages with role 'tool' must be a response to a preceeding message
with 'tool_calls'"*. Üç ölçüm bunun bu fazla ilgisiz olduğunu gösteriyor:

| Koşum | Sonuç |
|---|---|
| Döngülü agent, harness tool sağlayıcıları açık | ❌ `ProviderInvocationException` |
| **Birebir aynı agent, `loop` alanı kaldırılmış** | ❌ **aynı hata** |
| `samples/AgentPrism.Api`'nin kendi `researcher`'ı (bu fazda hiç dokunulmadı) | ❌ aynı hata (aynı agent bir önceki çağrıda geçmişti — model davranışına bağlı, deterministik değil) |
| Harness tool sağlayıcıları kapalı + döngü | ✅ 1 ve 2 numaralı koşumlar |

Kusur MAF harness'inin **içindedir** ve `docs/hafiza/maf-api.md`'de K-053 olarak
zaten kayıtlıdır (*"harness akisli yolda tool turlarini dogru tasimaz"*);
`samples/AgentPrism.Api`'nin kendi yorumu da alt-agent seçerken bunu gerekçe
gösteriyor. AgentPrism tarafında düzeltilebilir bir yeri yoktur. Yeni kayıt
açılmadı — var olan kayıt doğru ve bu koşum onu gerçek bir sağlayıcıyla
doğruladı.

## Denetim Bulguları

`faz-denetim` taze bağlamlı bir denetçiyle koşuldu (2026-09-07). Bir 🔴, beş 🟡,
üç 🟢. Denetçi ayrıca yedi sapmanın **hepsini** kodda ölçerek doğruladı.

| # | Seviye | Bulgu | Sonuç |
|---|---|---|---|
| 1 | 🔴 | `MAF-GENISLEME-NOKTALARI.md:353` döngüyü hâlâ *"planlanmadi"* diye kaydediyordu | **Düzeltildi.** Satır `Faz 151 TAMAMLANDI` oldu; `HarnessAgentOptions` üye tablosundan `LoopEvaluators` çıkarıldı ve `LoopAgent`'ın harness'in **içinde** olduğu ölçümü yazıldı |
| 2 | 🟡 | DoD `400` diyordu ama hiçbir test `POST /api/agents`'ı bilinmeyen bir `kind` ile çağırmıyordu | **Düzeltildi.** `HarnessLoopTests.An_unknown_criterion_kind_is_refused_with_400_when_the_definition_is_saved` hem kayıt ucunu (`400`) hem doğrulama ucunu (`200` + `valid:false`) ölçüyor |
| 3 | 🟡 | Tavanla biten döngünün `run` kanıtı hiç okunmuyordu; `continued`'in sevk edilen tanımı kodla çelişiyordu | **Düzeltildi — ve bir kusur buldu.** Ölçüm: tavana ULAŞAN tur değerlendirilmiyor, yani 10 model turu 9 olay üretiyor. Yeni `ceilingReached` alanı bunu kayda açık hâle getirdi; XML, site, manuel case ve testler eşitlendi |
| 4 | 🟡 | Planın istediği akışlı `span` testi sessizce düşürülmüştü | **Düzeltildi.** `A_streaming_loop_keeps_one_unbroken_span_tree` gerçek SSE + `/runs/{id}/trace` üzerinden kök `span`'i ve her `span`'in ebeveynini ölçüyor (`RunTraceEndToEndTests` deseni) |
| 5 | 🟡 | Yetenek satırının bedeli agent haritasından bir cümlenin silinmesiyle ödendi; harita artık bütçenin **tam üstünde** | **Gerekçelendi.** Silinen cümle üçüncü tekrardı: aynı `TryAdd` kuralı sevk edilen haritada 207. ve 224. satırlarda **hâlâ duruyor** (ölçüldü). Bütçe gerçeği devir notuna yazıldı |
| 6 | 🟡 | Tekrarlayan tuzak ve yeni dosyalar alan hafızasına girmemişti | **Düzeltildi.** `maf-api.md` üç yeni ölçüm kazandı (zincirdeki konum · kısa devre ve istisna · tavan turunun değerlendirilmemesi); `kod-haritasi.md` üç yeni dosyayı adlandırıyor; `cekirdek-calistirma.md` K-492'nin **üçüncü** vakasını kaydediyor |
| 7 | 🟢 | Yerleşik `kind` eşleşmesi ordinal, özel kayıt `OrdinalIgnoreCase` — ölü ad alanı | `docs/ADAYLAR.md` **F-211** |
| 8 | 🟢 | `RecordToolPayloads` şartı bazı olay üyelerinin XML'inde var, bazılarında yok | `docs/ADAYLAR.md` **F-212** |
| 9 | 🟢 | Sevk edilen OpenAPI'de `<see cref>` tam imzaya dönüşüyor | **Aday değil, kural ihlali: düzeltildi.** K-517 sözleşme tiplerinde `<c>` yazmayı zorunlu kılıyor; `LoopSettings`, `LoopCriterion` ve `HarnessSettings` içindeki 14 `<see cref>` `<c>`'ye çevrildi ve OpenAPI yeniden üretildi (`grep "int LoopSettings"` → 0) |

Denetçinin iki notu bulguya dönüşmedi ve doğrulandı: gözlemlenebilirlik kuralı
sağlanıyor (`RunEventWriter` `store` hatasını zaten yutar, `run` devam eder) ve
plandaki `TenantIsolationContract` satırı boştaydı — repo'da böyle bir tip yok
ve kiracı damgası (`RunEventWriter`) olay tipinden **bağımsızdır**, yani döngüye
özgü bir kiracı yolu yoktur.

## Sonraki Faza Devir Notu

- **🚨 `AgentPrism.AgentMap.md` şu anda bütçesinin TAM ÜSTÜNDE: 10 240 / 10 240 B.**
  `capabilities.md`'ye yetenek satırı ekleyen bir sonraki faz **önce yer açmak
  zorundadır** — harita o dosyadan üretilir ve üreteç bütçeyi aşınca kırpmaz,
  kırılır. Bu faz yeri bir cümlelik tekrarı silerek açtı; sıradaki fazın aynı
  şansı olmayabilir. Bütçeyi yükseltmek bir karardır ve ölçüm ister.
- **🚨 `docs/KARARLAR.md` de bütçesinin kenarındaydı ve bu faz onu damıtarak
  açtı.** Yedi yeni kalem defteri 390 KB sınırının üstüne çıkardı; on üç eski
  kalemin `Gerekçe` sütunu, tam metni `KARARLAR-GECMISI.md`'de **birebir duran**
  kısmı çıkarılarak kısaltıldı (silinmedi — zaten iki yerde duruyordu). Sonuç
  388 KB. Bir sonraki faz da kalem eklerken aynı işi yapmak zorunda kalacak.
- **MAF'ın döngü davranışı ÖLÇÜLDÜ, belgelenmedi.** Üç davranış MAF'ın
  dokümanında yoktur ve sürümle sessizce değişebilir: ilk `Continue`'da kısa
  devre, `MaxIterations = null` iken 10, ve **tavana ulaşan turun
  değerlendirilmemesi**. Üçü de `docs/hafiza/maf-api.md`'dedir. Bir MAF
  yükseltmesinde bu üçü `maf-api-kesfi`'nin dump-diff'iyle **yeniden ölçülmeli**
  — imza aynı kalıp davranış değişebilir.
- **`LoopIterationCompleted` bir DEĞERLENDİRİLMİŞ iterasyonu işaretler, bir model
  turunu değil.** Tavanla biten döngü turdan bir eksik olay yazar. "Olay sayısı =
  iterasyon sayısı" varsayan her yeni kod (arayüz sayacı, metrik, rapor) tavan
  yolunda bir eksik sayar; `ceilingReached` bu ayrımı taşır.
- **`aiJudge` gerçek bir modelle HİÇ koşulmadı.** Otomatik testler onu yalnız
  derleme düzeyinde kanıtlıyor (`AIJudgeLoopEvaluator` kuruluyor, yargıç
  binding'i çözülüyor). Bir yargıçlı döngünün gerçek maliyeti ve gerçek durma
  davranışı ölçülmedi; ilk gerçek kullanımda ölçülmeli.
- **K-053 hâlâ açık ve artık gerçek bir sağlayıcıyla doğrulandı.** Tool çağıran
  bir harness agent'ı OpenAI Chat Completions'a geçersiz bir mesaj dizisi
  gönderiyor. Döngüyle ilgisi yok ama döngüyü **tool'lu bir agent'ta
  kullanılamaz** yapıyor: döngülü bir örnek yazarken harness tool
  sağlayıcılarını kapat (`disableTodoProvider`, `disableAgentSkillsProvider`,
  `disableAgentModeProvider`) veya tool'suz bir agent seç.
