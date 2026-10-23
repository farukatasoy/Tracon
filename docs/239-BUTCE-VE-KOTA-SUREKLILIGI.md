# Faz 239 — Devam Zincirinde Bütçe ve Çalışan Ağaçta Kiracı Kotası

> **Durum:** 📋 Planlandı (2026-10-23)
> **Kademe:** tam — kiracı sınırı: kiracı kotası ilk kez çalışan bir ağacın içinde yeni işi durdurur; public sözleşme: yeni ret kimliği
> **Plan onayı:** Bakımcı, 2026-10-23 (engelleyici kararlar sohbette alındı: üç devam yolu da bütçeyi devralır ve sıfırlama yoktur; süre boyutu yalnız aktif yürütme süresini devralır; harcanan bütçe devam başında zincirden okunur — migration yok; kiracı kotası fonksiyon düğümü ve agent adımı başlamadan okunur)
> **Kaynak:** [ADAYLAR.md](ADAYLAR.md) · **F-529**, **F-531**
> **Genellik:** Workflow koşan ve tavan koyan **her** kurulum. F-531: insan onayı döngüsü olan bir workflow, ağaç tavanını her cevapta yeniden alır; tavan bir tavan olmaktan çıkar. F-529: çok kiracılı kurulumda kota bugün yalnız girişte okunur; saatler süren bir workflow kotayı sınırsız geçer. İş tüketici kodunda kalamaz: tüketici devam run'ının bütçesini kuramaz ve düğüm kapısına erişemez. İkinci kanıt ilk ilkedir: K-999 tavanı "yeni işi durdurur" diye tanımlar; devamda ve kotada bu söz bugün tutulmuyor
> **Karşı görüş:** (1) "Operatör resume'u bilinçli bir karardır; tam bütçe almalıdır." Cevap (bakımcı): bütçe mantıksal run başınadır. Daha fazla harcamak isteyen yapılandırmayı yükseltir; reddedilen düğüm açık hatayla yeniden durur. (2) "Düğüm başına kota okuması pahalıdır" (Faz 210 §210.6). Cevap: okuma yalnız kiracının kota tanımı varsa iki depo turudur. Okuma yeni bir işin başında yapılır, sıcak yolda yapılmaz (model çağrısı başına okuma seçilmedi). Maliyet bu fazda ölçülür
> **Önkoşul:** Yok. Faz 237/238 ile dosya çakışması yok. Faz 238 hata kodu kataloğunu kurarsa (§238.3) bu fazın yeni kodu oraya taşınır; sıra fark etmez
> **Paketler:** `Tracon.Abstractions` (`AgentRunBudget` iç seed, yeni ret kimliği), `Tracon.Core` (`QuotaEnforcer`, `TreeAccount`/`TreeQuota`, `ChildAgentInvoker`), `Tracon.Workflows` (`WorkflowRunner`, `WorkflowNodeBudgetGate`)
> **Yeni paket:** Yok · **Migration:** Yok — zincir mevcut sütunlardan okunur (bakımcı kararı)
> **Public API:** büyüyor — yeni ret exception'ı ve hata kodu sabiti; `WorkflowFunctionOptions.StartsWhenBudgetIsSpent` XML'i genişler. `1.0.0` GA'dan (K-603) önce ucuz: `wc -l src/*/PublicAPI.Shipped.txt` = 18 satır
> **Tüketici yüzeyi:** site: `concepts/workflows.md` (bütçe bölümü), `concepts/governance.md` (kota), `guides/production.md`, `guides/reliability.md` (devam), `capabilities.md` satırı, `llms-full.txt` (üretilir) · sevk edilen: `AgentRunBudget` ve `StartsWhenBudgetIsSpent` XML'i, `CHANGELOG.md` `[Unreleased]` (davranış değişikliği: devam run'ı tavanı sıfırdan almaz)
> **Manuel test alanı:** `docs/manuel-test/15-WORKFLOWS.md` · `docs/manuel-test/23-SAKLAMA-ARSIV-KOTA.md`

---

## Bu Faza Başlarken

> `faz-baslangic` skill'ini uygula. Aşağıdaki liste o skill'in 2. adımıdır —
> **tamamını değil, yalnız işaret edilen bölümleri oku.**

1. Bu doküman
2. Kararlar — dosyanın tamamını **okuma**, yalnız bu kalemleri grep'le:
   ```bash
   grep -n "K-483 —\|K-990 —\|K-997 —\|K-999 —\|K-394 —" docs/KARARLAR.md
   ```
   **K-990** (kota girdisi harcayan run başına, kapanırken; tek yazım yolu
   `TreeQuota.RecordAsync`; kimlik `TreeAccount`'tan), **K-999** (fonksiyon
   düğümü maliyet/süre tavanında başlamaz; muafiyet yalnız kodda), **K-997**
   (cevap run'ının satırı ve iddiası), **K-483** (toplama ifadesi tek yerde —
   `RunSpend.Total`), **K-394** (workflow tek run'dır)
3. Önceki fazdan devir: yok. Faz 210'un tam metni gerekirse (bu fazın iki kalemini
   o açtı):
   `git show 88178e3d:docs/210-BUTCE-DUGUM-KAPISI.md | sed -n '162,171p'`
4. Alan hafızası (üç alan):
   [`hafiza/olcum-kota-ve-secenekler.md`](hafiza/olcum-kota-ve-secenekler.md) (K-483 toplama tuzağı) ·
   [`hafiza/workflows.md`](hafiza/workflows.md) ·
   [`hafiza/workflow-cevaplari.md`](hafiza/workflow-cevaplari.md) (cevap run'ı, kaynak bağı)
5. Gerektiğinde: [`MIMARI-GUVENLIK.md`](MIMARI-GUVENLIK.md) kiracı sınırı bölümü

---

## Amaç

K-999 ağaç tavanını "yeni işi durdurur" diye tanımlar. Bu söz iki yerde
tutulmuyor:

- **F-531** — Devam eden run (insan cevabı, operatör resume, kesinti kurtarması)
  **yeni bir run kimliği** ve **sıfır bir bütçe** alır. İnsan onayı döngüsü olan
  bir workflow tavanı her cevapta yeniden alır. Bütçe reddiyle düşen bir run
  resume edilirse reddedilen düğüm başlar.
- **F-529** — Kiracı kotası yalnız HTTP girişinde okunur. Saatler süren bir
  workflow, kota dolduktan sonra da yeni düğüm ve agent adımı başlatır.

İki kalem aynı altyapıyı paylaşır (`AgentRunBudget` + `TreeAccount` +
`TreeQuota` + başlangıç kapıları). Ayrıca birbirine dokunurlar: F-531'in
devraldığı harcama kotaya **zaten** yazılmıştır. F-529'un kapısı onu ikinci kez
saymamalıdır (§239.3).

### Bugün ne çalışmıyor — doğrulanmış kanıt

| Kanıt | Gözlem |
|---|---|
| [`WorkflowRunner.cs:820`](../src/Tracon.Workflows/Internal/WorkflowRunner.cs) | `RunGuardedAsync` her yürütmede `AgentGraph.CreateBudget(_timeProvider)` ile yeni bütçe kurar |
| [`WorkflowRunner.cs:509`](../src/Tracon.Workflows/Internal/WorkflowRunner.cs), `:560` | Resume ve cevap `RunId = request.NewRunId ?? TraconId.NewId()` ile **yeni run** açar; kaynak run'dan yalnız checkpoint okunur |
| [`AgentRunBudget.cs:86`](../src/Tracon.Abstractions/Runs/AgentRunBudget.cs), `:137-144`, `:160-166` | `Deadline` kurucuda `MaxDuration`'dan hesaplanır. `ConsumedTokens`/`ConsumedCost`/`StartedRuns` sıfırdan başlar. Seed girişi yok |
| [`AgentRunBudget.cs:313`](../src/Tracon.Abstractions/Runs/AgentRunBudget.cs) · [`WorkflowNodeBudgetGate.cs:53`](../src/Tracon.Workflows/Internal/WorkflowNodeBudgetGate.cs) | Düğüm kapısı yalnız bellek içi bütçeye bakar; eşzamanlıdır (depo okuması yok) |
| [`ChildAgentInvoker.cs:511`](../src/Tracon.Core/Graph/ChildAgentInvoker.cs) | Agent adımı (ve alt agent çağrısı) yalnız `budget.TryReserveRun()` ile reddedilir; kota okunmaz. Workflow agent adımları bu sarmalayıcıdan geçer (`WorkflowDefinitionCompiler.cs:389`) |
| [`QuotaEnforcer.cs:72`](../src/Tracon.Core/Quotas/QuotaEnforcer.cs) | `CheckAsync` kota tanımlarını, sonra dönem kullanımını okur (iki depo turu; tanım yoksa bir). Çağıranlar yalnız HTTP giriş kapılarıdır (`WorkflowEndpoints.cs:912`, `TriggerEndpoints.cs:345`, `QuotaGate`) |
| [`TreeQuota.cs:21-55`](../src/Tracon.Core/Quotas/TreeQuota.cs) · [`TreeAccount.cs:24`](../src/Tracon.Abstractions/Runs/TreeAccount.cs) | Kota yazımı run kapanırken yapılır (K-990). `TreeAccount` değişmez bir kayıttır; ağacın kotaya ne yazdığını tutmaz. Bu yüzden "sayaç + yazılmamış pay" hesaplanamaz |
| [`WorkflowRunner.cs:2159-2182`](../src/Tracon.Workflows/Internal/WorkflowRunner.cs) `RecordQuotaAsync` | Workflow kökü yalnız kendi payını (`runs: 1`, düğüm tutarı) kapanırken yazar; agent adımları kendi paylarını kendi kapanışlarında yazar |
| Devam bağları (ölçüldü, kod okuması) | Kesinti kurtarması `ContinuedFromRunId` yazar (`WorkflowRecovery.cs:215`, `RunContinuationJobHandler.cs:269`). Cevap run'ı kaynağını `WorkflowResponseLink` olayıyla (`WorkflowRunner.cs:933`) ve `workflow_responses` satırıyla anar. **Operatör resume'u kaynağı hiçbir yere yazmaz** |
| [`WorkflowContinuationChain.cs`](../src/Tracon.Core/Scheduling/WorkflowContinuationChain.cs) | Zinciri yalnız `ContinuedFromRunId` ile ileri yürütür; en çok 128 halka |
| [`RunRecord.cs:128-171`](../src/Tracon.Abstractions/Runs/RunRecord.cs) | Run satırı `TreeUsage`, `TreeCost`, `TreeNonTokenCost`, `ChildRunCount`, `StartedAt`, `CompletedAt` taşır; zincir toplamı bunlardan okunabilir |

> Kanıtlar 2026-10-23 tarihinde doğrulandı.

---

## 239.1 — Devam halkası: her devam kaynağını okunur biçimde anar

Zincirin okunabilmesi için her devam run'ı kaynağını bilmelidir. Bugün üç yol
üç farklı biçimde anar, biri hiç anmaz.

**Uygulamanın ilk işi bir ölçümdür:** `ContinuedFromRunId`'yi okuyan her çağıranı
listele (`grep -rn "ContinuedFromRunId" src/`). Bu çağıranlar
`WorkflowContinuationChain`, `RunReconciliationService`, cevap ucu ve arayüzdür.
Sonra karar kuralı uygulanır:

- **Kural A (öneri):** operatör resume'u `ContinuedFromRunId = request.RunId`
  yazar. Resume anlamca kurtarmayla aynıdır: "bu run'ın checkpoint'inden
  devam et". Cevap run'ı bugünkü bağını (`workflow_responses`) korur.
  **Koşul:** ölçüm, resume'un `ContinuedFromRunId` taşımasının kurtarma
  kararını değiştirmediğini göstermelidir. `WorkflowContinuationChain` "en yeni
  halka uçuştaki iştir" der; elle başlatılan resume da uçuştaki iştir. Bunu bir
  fonksiyonel test kanıtlar.
- **Kural B:** ölçüm A'nın bir çağıranı bozduğunu gösterirse resume, kaynağını
  cevap run'ı gibi bir olayla (`WorkflowResponseLink` deseni) anar.
  `ContinuedFromRunId`'ye dokunulmaz.

Hangi kuralın seçildiği *Plandan Sapmalar*'a yazılır. 🚨 MEMORY: "Bir davranışı
düzeltmek, o davranışa dayanan çağıranı sessizce değiştirir." A seçilirse
kurtarma fonksiyonel testleri **değiştirilmeden** yeşil kalmalıdır.

## 239.2 — Zincirin harcadığını oku ve bütçeyi seed'le (F-531)

Bakımcı kararları: üç yol da devralır, sıfırlama yoktur; süre için yalnız aktif
yürütme süresi sayılır; okuma devam başında zincirden yapılır.

```mermaid
sequenceDiagram
    participant R as WorkflowRunner (devam)
    participant L as BudgetLineage (yeni, internal)
    participant S as IRunStore
    participant B as AgentRunBudget
    R->>L: kaynak run kimliği
    loop kaynak bağı bitene kadar (≤128)
        L->>S: GetRunAsync(kaynak)
        S-->>L: TreeUsage, TreeCost, TreeNonTokenCost, ChildRunCount, StartedAt, CompletedAt
        L->>L: kaynağın kaynağı (ContinuedFromRunId · cevap bağı)
    end
    L-->>R: BudgetCarryOver(tokens, cost, runs, activeDuration)
    R->>B: CreateBudget(timeProvider, carryOver)
```

- **Toplama tek yerdedir** (K-483). Maliyet `RunSpend.Total(TreeCost, TreeNonTokenCost)`
  ile toplanır, ikinci bir elle yazılmış toplam yazılmaz. Para birimi uyuşmazlığı
  bugün `RunSpend`'in kuralıyla çözülür; uygulama bütçenin para birimiyle
  `RunSpend` kuralının aynı olduğunu ölçer.
- **Aktif süre** = Σ (`CompletedAt` − `StartedAt`). Kaynak run kapanmıştır
  (`AwaitingInput`, `Failed` ya da `Completed`). Kapanmamış bir kaynak (yarış) süreyi
  `şimdi − StartedAt` sayar ve bir uyarı loglar.
- **Seed:** `TraconAgentGraphOptions.CreateBudget` için `internal` bir aşırı
  yükleme. Tüketiciler ve harcanan sayaçlar seed değerinden başlar. Son tarih
  `şimdi + (MaxDuration − aktif süre)` olur. Kalan süre ≤ 0 ise bütçe süre
  boyutunda **hemen** tükenmiştir: ilk düğüm ya da model çağrısı K-999 kimliğiyle
  reddedilir.
- **Seed miktarı ayrı tutulur** (`AgentRunBudget` içinde `internal` bir alan).
  §239.3 onu kotadan düşer.
- Okuma hatası **güvenli tarafa** düşer: zincir okunamazsa devam run'ı
  başlamaz ve açık bir hatayla kapanır (`Failed`). Sıfır bir bütçeyle sessizce
  koşmaz. Gerekçe: sessiz sıfır, bu fazın kapattığı deliğin kendisidir
  (Açık Soru 2).
- Zincir 128 halkayı aşarsa `WorkflowContinuationChain.MaxLength` emsali uygulanır:
  okuma durur ve devam reddedilir.

## 239.3 — Çalışan ağaçta kiracı kotası (F-529)

Bakımcı kararı: kota, fonksiyon düğümü ve agent adımı **başlamadan** okunur.
Başlamış iş durmaz (K-999 eşi). Çalışan bir agent'ın model çağrıları kotayla
durmaz; onları bütçe sınırlar.

**Tutarlılık: sayaç + ağacın yazılmamış payı.** Faz 210 bu kalemi şu gerekçeyle
dışarıda bırakmıştı: sayaç, koşan workflow'un kendi düğüm tutarını görmez
(§210.6). Bu fazın çözümü şudur:

- `TreeAccount` değişebilir bir **defter** taşır (`Interlocked`). Defter ağacın
  kotaya yazdığı token ve tutarı tutar. Tek yazım yolu `TreeQuota.RecordAsync`
  başarılı yazımdan sonra defteri artırır (K-990'un tek kapısı korunur).
- Ağacın yazılmamış payı = (`budget.Consumed*` − seed) − `defter`. Seed, F-531'in
  devraldığı harcamadır: önceki run'lar onu **kendi kapanışlarında zaten
  yazdı**. Seed düşülmezse kota iki kez sayılır.
- `QuotaEnforcer` için yeni bir `internal` yol: `CheckTreeStartAsync(account,
  unrecordedTokens, unrecordedCost, kind, ct)`. Tanım ve kullanım okuması
  `CheckAsync`'inkiyle aynıdır (tanım yoksa tek tur). Değerlendirmeye yazılmamış
  pay eklenir.
- **Hangi metrik durdurur:**

  | Başlangıç noktası | `Cost` | `Tokens` | `Runs` |
  |---|---|---|---|
  | Fonksiyon düğümü | ✓ | — (düğüm token harcamaz; K-999'un token kuralı) | — |
  | Agent adımı / alt agent çağrısı | ✓ | ✓ | — (alt run `Runs = 0` yazar, K-990) |

- **Kapı yerleri:** `WorkflowNodeBudgetGate.Wrap` (bütçe kapısından **sonra**:
  bütçe reddi önce gelir, çünkü bellek içidir ve ucuzdur) ve
  `ChildAgentInvoker` (`TryReserveRun`'dan **önce**: reddedilen adım bir run
  hakkı tüketmemelidir). İkisi de bugün eşzamanlıdır; kota okuması onları
  asenkron yapar. Uygulama MEMORY'nin yapısal iddia kuralını uygular: iki yerin
  her agent adımını ve her düğüm denemesini gördüğü grep ile ölçülür.
- **Muafiyet:** `WorkflowFunctionOptions.StartsWhenBudgetIsSpent = true` olan
  düğüm kota kapısından da muaftır. Gerekçe: muafiyet "bu düğüm para harcamaz"
  der; bu cümle bütçe için de kota için de doğrudur. XML ve site bunu yazar.
- **Ret kimliği:** yeni hata kodu `tenant_quota_exceeded` (ad taslaktır; HTTP
  giriş kapısının 429 problem tipiyle aynı kökten seçilir — ölçülmeli),
  `RunErrorClass.QuotaExceeded`. Bütçe reddinden (`run_budget_exceeded`) ayrıdır,
  çünkü çare ayrıdır: bütçe yapılandırmadır, kota dönem sonunu bekler. Mesaj
  metriği, dönemi ve limiti adlandırır (`QuotaGate.cs:79-81` alanları).
- **Tekrar denenmez:** `WorkflowNodeRetry` kota reddini bütçe reddi gibi tekrar
  denemez.
- **Depo hatası:** mevcut `TraconQuotaOptions.AllowOnStoreFailure` uygulanır;
  yeni seçenek yoktur.
- **Kota kapalıysa** (`Enabled = false`) ya da kiracının tanımı yoksa: tanım
  okuması tek turdur ve kapı geçer. Kota hiç kayıtlı değilse (`account.Quota is
  null`) depo turu yoktur.

## 239.4 — Maliyet ölçümü

Faz 210 "düğüm başına okuma pahalıdır" dedi ama bir sayı vermedi. Bu faz ölçer:

- Tanımı olmayan kiracı: düğüm başına **bir** depo turu. Tanımı olan kiracı: iki tur.
- Ölçüm: `Tracon.Capacity.Tests` deseninde, 20 düğümlü ve 5 agent adımlı bir
  workflow'un depo turu sayısı ve süresi, kota açık ve kapalı. Sonuç
  dokümana yazılır.
- **Önbellek bu fazda yazılmaz.** Ölçüm bir sorun gösterirse önbellek
  (tanımlar ağaç başına) ayrı bir aday olur. Gerekçe: önbellek, eşzamanlı
  run'ların harcamasını bayat okur; doğruluk önce gelir (Açık Soru 1).

---

## Planlanan Public API

> Taslak imzalardır. Gerçekleşen imzalar kapanışta ayrı bir bölüme yazılır.

```csharp
// Tracon.Abstractions
/// <summary>A tenant quota period is spent; new work in the run tree does not start.</summary>
public sealed class TraconTenantQuotaExceededException : TraconException
{
    public const string ErrorType = "tenant_quota_exceeded";   // ad taslak
    public QuotaMetric Metric { get; }
    public QuotaPeriod Period { get; }
    public decimal Limit { get; }
}

// AgentRunBudget: yalnız internal üyeler (seed, seed miktarı); public yüzey değişmez.
// WorkflowFunctionOptions.StartsWhenBudgetIsSpent: imza aynı, XML kota kapısını da anar.
```

**Bugün eklemek bedava / sonradan eklemek kırıcı:** yeni exception tipi
GA'dan sonra da ekleyicidir. Ama devam run'ının bütçeyi devralması bir davranış
değişikliğidir. GA'dan sonra bu değişiklik Faz 237 kuralına göre "XML'de
belgelenen davranışın değişmesi" olur, yani major. Bugün `CHANGELOG`'da adıyla
anılır.

### HTTP `endpoint`'leri

Yeni uç yok. Resume ve cevap uçlarının yanıtı değişmez. Kota reddiyle düşen
run `Failed` + yeni hata kodu taşır; run olayları ve SSE `run.failed` bugünkü
çerçeveyle akar.

### Arayüz payı

Yok. Konsol run hatasını hata koduyla gösterir; yeni kod mevcut hata satırında
görünür (ölçülmeli: konsolun bilinmeyen kodu nasıl gösterdiği).

---

## Planlanan Dosya Listesi

```
src/Tracon.Abstractions/Runs/
├── AgentRunBudget.cs                       # internal seed + seed miktarı
├── TreeAccount.cs                          # defter (recorded tokens/cost)
└── TraconTenantQuotaExceededException.cs   # yeni
src/Tracon.Core/
├── TraconOptions.cs                        # CreateBudget(timeProvider, carryOver) internal aşırı yükleme
├── Quotas/QuotaEnforcer.cs                 # CheckTreeStartAsync
├── Quotas/TreeQuota.cs                     # yazımdan sonra defter
├── Graph/ChildAgentInvoker.cs              # kota kapısı, ChildRefusalCause.Quota
└── Scheduling/BudgetLineage.cs             # yeni: zincir toplamı (§239.2)
src/Tracon.Workflows/
├── Internal/WorkflowRunner.cs              # devam: seed; resume: kaynak bağı (§239.1)
├── Internal/WorkflowNodeBudgetGate.cs      # kota kapısı (asenkron)
└── WorkflowFunctionOptions.cs              # XML
tests/
├── Tracon.Workflows.UnitTests/…            # fonksiyonel: devam × bütçe, düğüm × kota
├── Tracon.Core.UnitTests/…                 # BudgetLineage, defter, CheckTreeStartAsync
└── Tracon.Capacity.Tests/…                 # §239.4 ölçümü
```

---

## Hata Modları ve Testler

| Ne bozulabilir | Seviye | Test (taslak ad) |
|---|---|---|
| Cevap run'ı tavanı sıfırdan alır (bugünkü davranış) | Fonksiyonel (runner + depo + cevap yolu) | `ContinuationBudgetTests` — `MaxTotalCost` ilk run'da %80 harcanır, cevap run'ı %30'luk düğümü başlatmaz (taban sürümde kırmızı) |
| Operatör resume'u bütçe reddiyle düşen düğümü başlatır | Fonksiyonel | aynı sınıf: `Failed`/`run_budget_exceeded` → resume → aynı düğüm aynı kimlikle reddedilir |
| Kesinti kurtarması bütçeyi sıfırlar | Fonksiyonel | aynı sınıf, `ResumeInterruptedRuns` yolu |
| İnsan bekleme süresi süre boyutuna sayılır | Fonksiyonel (`FakeTimeProvider`) | 10 dk aktif + 3 gün bekleme, `MaxDuration` 30 dk → devam run'ı koşar, son tarih +20 dk |
| Aktif süre tavanı geçmişse devam run'ı koşar | Fonksiyonel | kalan süre ≤ 0 → ilk düğüm reddedilir |
| Zincir okuma hatası sıfır bütçeyle koşturur | Fonksiyonel (bozulan depo) | devam `Failed`, açık hata; düğüm handler'ı çağrılmaz |
| Resume'un `ContinuedFromRunId` yazması kurtarma kararını değiştirir (§239.1 Kural A) | Fonksiyonel | mevcut kurtarma testleri **değişmeden** yeşil + elle resume edilmiş run'ın kesintisi doğru halkadan kurtarılır |
| Zincir döngüsü (elle yazılmış) sonsuz okur | Birim | `BudgetLineage` 128'de durur |
| Kota dolduktan sonra fonksiyon düğümü başlar | Fonksiyonel | `TreeQuotaGateTests` — kiracı `Cost` kotası dolu, düğüm `Failed`/`tenant_quota_exceeded`, handler çağrılmaz |
| Kota dolduktan sonra agent adımı başlar | Fonksiyonel | aynı sınıf; `TryReserveRun` tüketilmez (`StartedRuns` değişmez) |
| Ağacın kendi düğüm tutarı sayılmaz (Faz 210'un tutarsızlığı) | Fonksiyonel | iki düğüm: birincinin tutarı kotayı doldurur, ikinci reddedilir — sayaçta henüz yazım yokken |
| Devralınan harcama kotada iki kez sayılır | Fonksiyonel | önceki run kotaya yazdı + seed → kapı yalnız yeni harcamayı ekler; sınırın altında koşar |
| Başka kiracının kotası okunur | Sözleşme/Fonksiyonel | iki kiracı; A'nın dolu kotası B'nin düğümünü durdurmaz. Kimlik `TreeAccount.TenantId`'den gelir, ortamdan gelmez (K-990) |
| Kota reddi tekrar denenir | Fonksiyonel | `WorkflowNodeRetry` denemesi 1 kalır |
| Muaf düğüm kota kapısında durur | Fonksiyonel | `StartsWhenBudgetIsSpent = true` → koşar |
| Kota deposu hata verir | Fonksiyonel | `AllowOnStoreFailure` true → koşar + uyarı; false → reddedilir |
| İptal edilen run kota reddi gibi görünür | Fonksiyonel | iptal edilen token `Canceled` kalır (`WorkflowNodeBudgetGate.cs` emsali) |
| Eşzamanlı iki adım aynı yazılmamış payı okur | Birim | defter `Interlocked`; iki iş parçacığında toplam doğru |
| Düğüm başına depo turu beklenenden fazla | Kapasite | §239.4 sayımı: tanımsız kiracıda 1, tanımlıda 2 |

Beş soru: **iptal** — çağıranın iptali reddi yener (yukarıda) ·
**eşzamanlılık** — defter ve paralel adımlar (yukarıda) · **boş/aşırı girdi** —
boş zincir (ilk run: seed sıfır), 128 halka, kalan süre ≤ 0 · **başka kiracı** —
yukarıda; zincir okuması `WorkflowContinuationChain` gibi kiracıyı denetler, başka
kiracının run'ı zincire girmez · **alt sistem hatası** — run deposu (devam
başlamaz), kota deposu (`AllowOnStoreFailure`).

---

## Manuel Kabul Case'leri

| # | Ön koşul | Adımlar | Beklenen sonuç |
|---|---|---|---|
| 1 `🤖` | `samples/Tracon.Api`, insan onaylı bir workflow, `Tracon:AgentGraph:MaxTotalCost` küçük | Run → `AwaitingInput` → cevap | Cevap run'ı devralınan harcamayla başlar; tavanı geçen düğüm `run_budget_exceeded` ile reddedilir. Run listesinde iki run ve hata satırı görünür |
| 2 `🤖` | Kiracıya küçük bir `Cost` kotası, kota açık | Ücretli düğüm zinciri olan workflow | İlk düğümler koşar; kota dolunca sonraki düğüm `tenant_quota_exceeded` ile reddedilir; `quota.threshold` webhook'u bugünkü gibi gelir |
| 3 `🤖` | Case 1'in reddedilen run'ı | `POST …/resume` | Aynı düğüm aynı kimlikle yeniden reddedilir; yapılandırma yükseltilince devam eder |

---

## Açık Sorular

| # | Soru | Seçenekler | Öneri |
|---|---|---|---|
| 1 | Kota okuması önbelleklensin mi? | A: her kapıda okuma · B: ağaç başına tanım önbelleği | **A** — §239.4 ölçümü bir sorun gösterirse B ayrı aday olur |
| 2 | Zincir okunamazsa devam ne yapsın? | A: başlamaz (`Failed`) · B: sıfır bütçeyle koşar + uyarı | **A** — B, kapatılan deliğin kendisidir. `AllowOnStoreFailure` emsali yalnız kota içindir |
| 3 | Ret mesajı kalan zamanı (dönem sonu) yazsın mı? | A: evet, `Retry-After` gibi · B: yalnız metrik/dönem/limit | **B** — dönem sonu kiracının saat dilimine bağlıdır (`ResolveTimeZone`); giriş kapısının alanlarıyla aynı kalır |
| 4 | Tek bir agent run'ında (workflow yok) alt agent çağrısı da kota kapısına girer. Ret tool sonucuna mı döner, run'ı mı düşürür? | A: bütçe reddinin bugünkü yolu (`ChildRefusalCause.Budget` emsali) · B: run düşer | **A** — davranış bütçe reddiyle aynı kalır; ölçülmeli: bugün `ChildRefusal` alt agent çağrısında ne üretiyor |

---

## Bitiş Ölçütleri (DoD)

- [ ] `ContinuationBudgetTests` üç devam yolunu (cevap, resume, kurtarma) ve süre kuralını kanıtlar; testler taban sürümde kırmızıdır (kayıt: komut + çıktı)
- [ ] `TreeQuotaGateTests` düğüm ve agent adımı kapısını, çift sayım yokluğunu, kiracı yalıtımını ve muafiyeti kanıtlar
- [ ] §239.1 kararı (A ya da B) ve ölçümü *Plandan Sapmalar*'da; kurtarma testleri değişmeden yeşil
- [ ] §239.4 ölçümü (depo turu sayısı, süre) dokümanda
- [ ] `CHANGELOG.md [Unreleased]` devam bütçesi davranışını ve yeni ret kodunu adıyla yazar
- [ ] Site sayfaları (Tüketici yüzeyi satırı) güncel; `npm run build` + `check-links.mjs` temiz
- [ ] Dört doğrulama kapısı sıfır uyarı verir
- [ ] `samples/Tracon.Api` ile gerçek `run` yapıldı (Manuel 1–3), çıktı belgeye yazıldı
- [ ] `secret` taraması boş döndü
- [ ] `🤖` case'ler `docs/manuel-test/15-WORKFLOWS.md` ve `23-SAKLAMA-ARSIV-KOTA.md` içinde ve koşuldu
- [ ] `faz-denetim` koşuldu; 🔴 bulgu kalmadı

### Doğrulama komutları

```bash
python3 scripts/kapi.py test --proje Tracon.Workflows.UnitTests --sinif ContinuationBudgetTests
python3 scripts/kapi.py test --proje Tracon.Workflows.UnitTests --sinif TreeQuotaGateTests
grep -rn "ContinuedFromRunId" src/ --include='*.cs'      # §239.1 çağıran listesi
```

---

## Riskler

| Risk | Önlem |
|------|-------|
| Resume'a `ContinuedFromRunId` yazmak kurtarma ya da cevap mantığını sessizce değiştirir | §239.1: önce çağıran listesi; kural B geri dönüş yoludur; mevcut testler değişmeden yeşil kalır |
| Seed düşülmez ve kota iki kez sayılır (K-483 sınıfı) | Yazılmamış pay **tek** bir yardımcıda hesaplanır; çift sayım testi var; `grep -rn "Consumed" src/` ile ikinci hesap aranır |
| Asenkron kapı `ChildAgentInvoker`'ın eşzamanlı yolunu bozar | Yapısal iddia ölçülür (grep); AsyncLocal kuralı: kapı `scope`/`span` açmaz |
| Zincir okuması uzun HITL döngüsünde yavaşlar | En çok 128 okuma, yalnız devam anında. Ölçülür ve yazılır (§239.4'e eklenir) |
| `TreeCost` okuma anında hesaplanıyorsa pahalıdır, kapanışta yazılıyorsa geç harcama eksik kalır | Uygulama ölçer ve yazar: `GetRunAsync` ağaç toplamını nereden alıyor? Eksikse bilinen sınır olarak belgelenir |

---

<!-- ============================================================
     AŞAĞISI ÇALIŞIRKEN DOLAR — doküman kapanışta silinir (`faz-kapat`).
     Kalıcı kısım `faz-tamamlama` Adım 5'te yerine taşınır. Başlıkları SİLME.
     ============================================================ -->

## Plandan Sapmalar

> Çalışırken dolar. Plan ile gerçek arasındaki fark **gizlenmez**. Kapanışta
> kapanış commit mesajına taşınır. §239.1 kararı buraya yazılır.

## Bu Fazda Verilen Kararlar

> Çalışırken dolar. Dört kategoriden birine giren karar (AGENTS.md)
> `docs/KARARLAR.md`'ye K-NNN ve kategori etiketiyle girer; girmeyen karar
> kapanışta commit mesajına taşınır (`faz-tamamlama` Adım 8). Plan numara rezerve
> etmez. Beklenen kayıtlar: devam bütçesi *(public-api; alternatifler: operatör
> resume sıfırdan, sıfırlama bayrağı, sütun ve checkpoint)* · çalışan ağaçta
> kiracı kotası *(güvenlik — kiracı sınırı; alternatifler: yalnız düğüm, her
> model çağrısı)*.

## Süreç Ölçümü

> Kapanışta doldurulur. **Tablo olarak** — onay kutusu DEĞİL: `faz-kapat`
> işaretsiz kutuda durur. `faz-kapat` bu tabloyu `docs/FAZ-DEFTERI.md`'ye taşır;
> eşik **167**'den itibaren boş bir değer hücresinde kapanışı durdurur.
> `ölçülmedi` **geçerli bir değerdir** — kapı bir sayı değil, bir **karar** arar.
> Kapı bölümün VARLIĞINI denetler, doğruluğunu denetlemez (K-766).

| Metrik | Değer |
|---|---|
| Plan revizyonu sayısı | |
| Düzeltme turu sayısı | |
| 🔴 bulgu: gerçek / gürültü / araştırılacak | |
| Fazın ürettiği regresyon | |
| Faz kapandıktan sonra bulunan kusur | ölçülmedi (kapanış anı) |

## Denetim Bulguları

> `faz-denetim` çıktısı. Her satır: bulgu · seviye (🔴/🟡/🟢) · sonuç
> (düzeltildi / gerekçelendi / F-NN olarak devredildi). 🟢 kapanışta
> `ADAYLAR.md`'ye gider.
