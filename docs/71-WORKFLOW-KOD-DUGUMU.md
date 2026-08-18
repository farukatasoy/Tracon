# Faz 71 — Workflow Kod Düğümü

> **Durum:** 📋 Planlandı (2026-08-18)
> **Kaynak:** [ADAYLAR.md](ADAYLAR.md) · **F-116**
> **Önkoşul:** [Faz 15](15-WORKFLOWS-YURUTME.md) — workflow yürütme ve kalıcılık · [Faz 16](16-WORKFLOWS-ARAYUZ.md) — graf, arayüz, human-in-the-loop
> **Paketler:** `AgentPrism.Abstractions`, `AgentPrism.Workflows`, `AgentPrism.Core`, `AgentPrism.AspNetCore`, `AgentPrism.UI`
> **Yeni paket:** Yok · **Migration:** Yok (düğüm tanımı var olan workflow tanımında yaşar) · **Doğrulanacak:** tanım sütununun şeması değişiyorsa üç set gerekir
> **Public API:** **büyüyor** — `WorkflowNodeKind` enum'una **ekleme**, `WorkflowDefinition`'a alan, bir kayıt yüzeyi. `PublicAPI.Shipped.txt` bugün **boş** — şimdi bedava
> **Site etkisi:** `concepts/workflows.md`, `guides/background-work.md`
> **Manuel test alanı:** [`docs/manuel-test/15-WORKFLOWS.md`](manuel-test/15-WORKFLOWS.md)

---

## Bu Faza Başlarken

> `faz-baslangic` skill'ini uygula. Aşağıdaki liste o skill'in 2. adımıdır —
> **tamamını değil, yalnız işaret edilen bölümleri oku.**

1. Bu doküman
2. Kararlar — dosyanın tamamını **okuma**, yalnız bu kalemleri grep'le:
   ```bash
   grep -n "K-129\|K-040\|K-394\|K-401\|K-403\|K-218" docs/KARARLAR.md
   ```
   **K-129** (🚨 MAF declarative workflow **reddedildi** — bu faz onu yeniden
   açmıyor, aşağıya bak), **K-040** (enum sırası değişmez), **K-394** (workflow'un
   tamamı tek `run` olarak kotaya yazılır), **K-401** (`ToRunError` sarmalayıcıları
   soyar), **K-403** (`RunStreamingAsync` gerçek yineleyici), **K-218** (boş servis sağlayıcı).
3. [`16-WORKFLOWS-ARAYUZ.md`](16-WORKFLOWS-ARAYUZ.md) — yalnız devir notu:
   ```bash
   awk '/## Sonraki Faza Devir Notu/,0' docs/16-WORKFLOWS-ARAYUZ.md
   ```
4. Alan hafızası (bu faz iki alana dokunuyor):
   [`hafiza/workflows.md`](hafiza/workflows.md) (yürütme, executor kimliği, HITL) ·
   [`hafiza/maf-api.md`](hafiza/maf-api.md) (MAF tipleri)

---

## Amaç

AgentPrism Workflows bugün yalnız **agent zinciri** kurabiliyor. Gerçek bir
üretim hattında ise AI çağırmayan adımlar vardır: dosya indirme, biçim
dönüştürme, ses sentezi, veritabanı yazımı. Bunlar grafiğe giremediği için
tüketici workflow'u yalnız hattının AI kısmı için kullanabiliyor; kalanını
kendi kuyruğunda tutuyor. Sonuç iki orkestratör, iki durum kaynağı, iki kurtarma
yolu.

- **F-116** — kayıtlı bir kod fonksiyonunu workflow düğümü olarak bağlamak.

### Bugün ne çalışmıyor — doğrulanmış kanıt

| Kanıt | Gözlem |
|---|---|
| [`WorkflowGraph.cs:94-110`](../src/AgentPrism.Abstractions/Workflows/WorkflowGraph.cs) | `WorkflowNodeKind` dört değer: `Unknown`, `Agent`, `Orchestration`, `RequestPort`, `Output`. Kod düğümü yok |
| [`WorkflowDefinition.cs:40,46`](../src/AgentPrism.Abstractions/Workflows/WorkflowDefinition.cs) | Tanım `AgentNames` ve `ManagerAgentName` taşır — düğüm kümesi **agent adlarıdır** |
| `Microsoft.Agents.AI.Workflows` **1.16.0** | 🚨 `FunctionExecutor<TInput>` ve `FunctionExecutor<TInput,TOutput>` **vardır**. Kurucu: `(string id, Func<TInput, IWorkflowContext, CancellationToken, ValueTask<TOutput>> handlerAsync, ExecutorOptions options, …)` |

> Kanıtlar 2026-08-18 tarihinde doğrulandı. MAF imzası XML dokümanından
> okundu; uygulama öncesi `maf-api-kesfi` ile bir kez daha teyit edilmelidir.

**Kazanç:** MAF ilkeli hazır. Bu faz sıfırdan bir yürütücü yazmaz —
`FunctionExecutor`'ı AgentPrism'in tanım, graf, kayıt ve arayüz katmanlarına
bağlar.

---

## 71.1 — 🚨 K2 sınırı: bu fazın ilk kararı

**K2 der ki: tool'lar yalnızca kodda tanımlanır. Arayüzden kod yazılamaz.**

Kod düğümü bu sınıra **çok yakındır** ve plan çizgiyi baştan çeker:

| İzinli | Yasak |
|---|---|
| Fonksiyon **kodda** kaydedilir; tanım ona **adıyla işaret eder** | Fonksiyonun gövdesi arayüzden veya veritabanından gelir |
| Arayüz kayıtlı fonksiyonlardan **seçtirir** | Arayüz ifade, script veya şablon dili girdirir |
| Tanım düğüm adı, kenar ve yapılandırma değeri taşır | Tanım çalıştırılabilir bir şey taşır |

Bu, `AgentDefinition.ToolNames`'in kayıtlı bir tool'a işaret etmesiyle **birebir
aynı** desendir. Kayıt yeri de aynı olmalıdır: bir düğüm ancak kayıtlı bir
fonksiyona işaret edebilir ve bu kuralı zorlayan **tek bir yer** vardır.

🚨 **K-129 yeniden açılmıyor.** MAF'ın declarative workflow'u ölçülüp
reddedilmişti (+19 paket ve Responses API şartı). Bu faz declarative workflow
**değildir**: yeni bir tanım dili getirmez, var olan AgentPrism tanımına bir
düğüm tipi ekler ve gövdeyi kodda tutar. Fark plan dokümanında yazılıdır ki
sonraki oturum bunu K-129'un ihlali sanmasın.

---

## 71.2 — Tasarım

```mermaid
flowchart LR
    B["AddWorkflowFunction<br/>kod kaydi"] --> C["WorkflowFunctionRegistry"]
    D["WorkflowDefinition<br/>Nodes: ad + tip"] --> V["Dogrulama<br/>kayitli mi"]
    C --> V
    V --> W["WorkflowRunner"]
    W --> F["MAF FunctionExecutor"]
    F --> E["ExecutorInvoked / ExecutorCompleted<br/>olaylari"]
```

**Dört parça:**

1. **Kayıt.** Kod fonksiyonu kurulum anında adıyla kaydedilir. Kayıt
   `IToolRegistry`'nin deseniyle aynıdır: ad çakışması **başlangıç hatasıdır**.
2. **Tanım.** `WorkflowDefinition` düğüm listesini adla taşır; `WorkflowNodeKind`
   sonuna `Function` eklenir (K-040: sıra değişmez, yalnız eklenir).
3. **Doğrulama.** Tanım derlenirken her fonksiyon düğümü kayıtta aranır;
   bulunmayan ad **derleme hatasıdır** — çalışma anı sürprizi değil. Bu,
   agent adlarında zaten uygulanan kuraldır.
4. **Yürütme.** `WorkflowRunner` düğümü `FunctionExecutor` olarak bağlar.
   `ExecutorInvoked` / `ExecutorCompleted` / `ExecutorFailed` olayları
   **zaten** `RunEventType` içinde (14, 15, 16) — akış ve arayüz bedavaya gelir.

### 🚨 K-218 burada da geçerlidir

Fonksiyon gövdesinin bağımlılıkları **kurulum anında** alınır. `IWorkflowContext`
bir servis sağlayıcı vermez ve tool tarafında MAF'ın `EmptyServiceProvider`
geçirdiği ölçülmüştür (K-218). Kayıt bir fabrika alır; fabrika DI'dan çözülür.

### Girdi ve çıktı tipi

`FunctionExecutor<TInput,TOutput>` tiplidir. AgentPrism tanımı ise **veridir**
ve tip taşımaz. Köprü kayıt anında kurulur: fonksiyon kendi tiplerini kayıtta
bildirir, tanım yalnız adı taşır. Tip uyuşmazlığı **tanım doğrulamasında**
yakalanır, çalışma anında değil.

### Kapsam dışı

| Dışarıda | Neden |
|---|---|
| Kod düğümünün maliyet ve token taşıması | AI çağırmaz; `TreeUsage` toplamına `0` katkı verir, `null` değil — bu bir **karardır** ve DoD'ye girer |
| Kod düğümünün kendi timeout'u | [Faz 69](69-TOOL-YETKILENDIRMESI-VE-TIMEOUT.md)'un tool timeout'u ile aynı sözleşme olmalı; ikisi ayrı planlanırsa iki desen doğar. Açık Soru 2 |
| Kod düğümünden alt agent çağırma | Ölçülmemiş ihtiyaç; `IWorkflowContext` üzerinden zaten mümkün olabilir — **doğrulanmadı** |

---

## Planlanan Public API

> Taslak imzalardır. Gerçekleşen imzalar kapanışta ayrı bir bölüme yazılır.
> 🚨 MAF imzaları XML dokümanından okundu; uygulama öncesi `maf-api-kesfi` ile
> teyit edilmelidir.

```csharp
// AgentPrism.Abstractions
public enum WorkflowNodeKind
{
    // ... 0-4 unchanged ...

    /// <summary>A node that runs a function registered in code.</summary>
    Function = 5,
}

public sealed record WorkflowFunctionDescriptor
{
    public required string Name { get; init; }
    public string? Description { get; init; }
    public required Type InputType { get; init; }
    public required Type OutputType { get; init; }
}

// AgentPrism.Workflows — registration
public static class AgentPrismWorkflowFunctionExtensions
{
    public static IAgentPrismBuilder AddWorkflowFunction<TInput, TOutput>(
        this IAgentPrismBuilder builder,
        string name,
        Func<IServiceProvider, Func<TInput, IWorkflowContext, CancellationToken, ValueTask<TOutput>>> factory,
        string? description = null);
}
```

### HTTP `endpoint`'leri

| Metot | Yol | Rol | Ne yapar |
|---|---|---|---|
| `GET` | `/api/workflows/functions` | Reader | Kayıtlı kod düğümlerini listeler (ad, açıklama, tipler) |

Var olan workflow uçları değişmez; graf yanıtı yeni düğüm tipini taşır.

### Arayüz payı

Graf tuvaline yeni bir düğüm şekli ve fonksiyon seçici. Yeni bağımlılık **yok** —
🚨 K-132 mermaid.js'i bundle bütçesi gerekçesiyle reddetti; bu faz o kararı
yeniden açmaz, var olan graf çizimi kullanılır. Bugünkü bundle 146.104 B brotli;
fazın payı kapanışta ölçülür. Yeni metin `en.ts` **ve** `tr.ts` (K-228).

---

## Planlanan Dosya Listesi

```
src/AgentPrism.Abstractions/Workflows/
├── WorkflowGraph.cs                    (değişir — WorkflowNodeKind.Function)
├── WorkflowDefinition.cs               (değişir — düğüm listesi)
└── WorkflowFunctionDescriptor.cs       (YENİ)

src/AgentPrism.Workflows/
├── AgentPrismWorkflowFunctionExtensions.cs (YENİ — kayıt)
├── Internal/WorkflowFunctionRegistry.cs    (YENİ)
└── Internal/WorkflowRunner.cs              (değişir — FunctionExecutor bağlama)

src/AgentPrism.Core/Compilation/          (tanım doğrulaması — kayıtlı mı)
src/AgentPrism.AspNetCore/Endpoints/WorkflowEndpoints.cs (değişir)
src/AgentPrism.UI/                        (graf düğümü + locales)
```

---

## Hata Modları ve Testler

| Ne bozulabilir | Seviye | Test sınıfı |
|---|---|---|
| Tanım kayıtlı olmayan bir fonksiyona işaret eder | Fonksiyonel | `WorkflowFunctionValidationTests` — **derleme hatası**, çalışma anı değil |
| İki fonksiyon aynı adla kaydedilir | Birim | `WorkflowFunctionRegistryTests` — başlangıç hatası |
| Tip uyuşmazlığı çalışma anında patlar | Fonksiyonel | aynı sınıf — doğrulamada yakalanmalı |
| Fonksiyon bağımlılığı `IWorkflowContext`'ten çözülmeye çalışılır (K-218) | Fonksiyonel (gerçek koşum) | `WorkflowFunctionWiringTests` |
| Fonksiyon patlar → workflow sessizce durur | Fonksiyonel | `WorkflowFunctionFailureTests` — `ExecutorFailed` üretilmeli |
| Sarmalayıcı istisna gerçek nedeni gizler (K-401) | Fonksiyonel | mevcut K-401 testi genişletilir |
| İptal fonksiyona ulaşmaz | Fonksiyonel | `WorkflowFunctionCancellationTests` |
| Kontrol noktasından devam fonksiyonu iki kez çalıştırır | Fonksiyonel | `WorkflowFunctionCheckpointTests` — 🚨 **idempotency sözleşmesi belgelenmeli** |
| Kod düğümü `TreeUsage`/maliyet toplamını bozar | Birim | `WorkflowUsageTests` |
| Kota muhasebesi kod düğümünü `run` sayar (K-394) | Fonksiyonel | `WorkflowQuotaTests` |
| Graf yanıtındaki yeni tip eski arayüzde çöker | Sözleşme | `WorkflowGraphContractTests` — bilinmeyen tip yok sayılır |
| Başka kiracının fonksiyon düğümü çalışır | Sözleşme | `TenantIsolationContract` |

**Beş soru:** iptal — token fonksiyona iletilir · eşzamanlılık — aynı fonksiyon
paralel superstep'lerde; **thread-safe olmak zorundadır** ve sözleşmeye yazılır ·
boş/aşırı girdi — `null` girdi ve çok büyük çıktı · başka kiracı — sözleşme
testi · alt sistem hatası — fonksiyon patlarsa `ExecutorFailed`.

🚨 **Kontrol noktasından devam en riskli hata modudur.** Bir kod düğümü yan etki
üretir (dosya yazar, HTTP çağırır). Kontrol noktasından devam onu **yeniden**
çalıştırabilir. Plan bunu çözmez, **sözleşmeye yazar**: kod düğümü idempotent
olmalıdır. Doküman bunu açıkça söyler; sessiz bırakılırsa tüketici veri bozar.

---

## Manuel Kabul Case'leri

| # | Ön koşul | Adımlar | Beklenen sonuç |
|---|---|---|---|
| 1 | Kod düğümü kaydedilmemiş | Var olan bir workflow koş | Bugünkü davranış birebir |
| 2 | Metni büyük harfe çeviren bir kod düğümü | Agent → fonksiyon → agent zinciri koş | Üç düğüm de çalışır; graf üçünü gösterir |
| 3 | Aynı workflow | `GET /api/runs/{id}/events` | `ExecutorInvoked` / `ExecutorCompleted` fonksiyon düğümü için de var |
| 4 | Kayıtlı olmayan ada işaret eden tanım | Tanımı kaydet | **Kaydetme anında** hata; çalıştırma denemesi gerekmez |
| 5 | Patlayan kod düğümü | Workflow koş | `ExecutorFailed`; hata mesajı gerçek nedeni gösterir (K-401) |
| 6 | Uzun süren kod düğümü | Koşuyu iptal et | 👤 Fonksiyon iptali görür ve durur |
| 7 | Kontrol noktalı workflow | Fonksiyondan sonra kontrol noktasından devam et | 👤 Fonksiyonun yan etkisi **tekrarlanır** veya tekrarlanmaz — davranış ölçülür ve belgelenir |
| 8 | Arayüz | Graf ekranını aç | 👤 Fonksiyon düğümü agent düğümünden ayırt edilebilir şekilde çizilir |

---

## Açık Sorular

| # | Soru | Seçenekler | Öneri |
|---|---|---|---|
| 1 | Kod düğümü hangi tipleri taşıyabilir? | A: JSON'a serileştirilebilir her tip · B: yalnız `string` ve `ChatMessage` | **A** — B gerçek bir hattı taşıyamaz. Serileştirilebilirlik kontrol noktası için zaten zorunludur; kural kayıt anında doğrulanır |
| 2 | Timeout bu fazda mı? | A: [Faz 69](69-TOOL-YETKILENDIRMESI-VE-TIMEOUT.md)'un sözleşmesi yeniden kullanılır · B: ayrı bir mekanizma | **A** — 69 önce giderse aynı desen; iki timeout modeli öğretmek maliyetlidir |
| 3 | Kod düğümü maliyet taşır mı? | A: `0` katkı · B: `null` | **A** — `null` "bilinmiyor" demektir ve yanlıştır; AI çağırmayan bir düğümün maliyeti gerçekten sıfırdır |
| 4 | Kayıt `IAgentPrismBuilder` üzerinden mi? | A: evet · B: ayrı bir workflow builder | **A** — tool kaydıyla aynı yüzey; ikinci bir kayıt yolu öğretmez |
| 5 | Var olan `WorkflowDefinition` şeması düğüm listesi taşıyabiliyor mu? | Ölçülmeli | 🚨 Bugün `AgentNames` düz bir liste. Düğüm listesine geçiş **migration gerektirebilir** — uygulama ilk adımda şemayı okumalı |

---

## Bitiş Ölçütleri (DoD)

- [ ] Kod düğümü kaydedilmemişken hiçbir davranış değişmez
- [ ] Agent → fonksiyon → agent zinciri uçtan uca koşar; çıktı belgeye yazıldı
- [ ] Kayıtlı olmayan ada işaret eden tanım **kaydetme anında** reddedilir
- [ ] Fonksiyon düğümü `ExecutorInvoked`/`ExecutorCompleted`/`ExecutorFailed` üretir
- [ ] İptal fonksiyona ulaşır
- [ ] Kontrol noktasından devam davranışı **ölçüldü ve belgelendi**
      (idempotency sözleşmesi dokümana yazıldı)
- [ ] Kod düğümü maliyet toplamına `0` katkı verir
- [ ] Bilinmeyen düğüm tipi eski istemcide yok sayılır
- [ ] Dört doğrulama kapısı sıfır uyarı verir
- [ ] `samples/AgentPrism.Api` ile gerçek workflow koşumu yapıldı, çıktı belgeye yazıldı
- [ ] `secret` taraması boş döndü
- [ ] Manuel kabul case'leri [`docs/manuel-test/15-WORKFLOWS.md`](manuel-test/15-WORKFLOWS.md)
      içine eklendi; otomatikleştirilebilenler koşuldu
- [ ] `faz-denetim` koşuldu; 🔴 bulgu kalmadı
- [ ] `docs-site/` güncellendi; `npm run build` + `check-links.mjs` temiz
- [ ] `en.ts` ve `tr.ts` eksiksiz; bundle payı ölçüldü ve yazıldı

### Doğrulama komutları

```bash
# Kayıtlı kod düğümleri
curl -s http://localhost:5081/agentprism/api/workflows/functions | jq

# Graf yeni tipi taşıyor mu
curl -s http://localhost:5081/agentprism/api/workflows/mixed/graph | jq '.nodes[].kind'
```

---

## Riskler

| Risk | Önlem |
|------|-------|
| 🚨 K2 sessizce delinir — gövde tanımdan gelmeye başlar | Sınır §71.1'de yazılı; `faz-denetim` bunu ilk kontrol eder |
| K-129 (declarative workflow) yeniden açılmış sanılır | Fark plan dokümanında yazılı; kapanışta karar defterine de yazılır |
| MAF imzası değişmiş olur | `maf-api-kesfi` uygulama öncesi koşulur; plan imzayı 1.16.0'dan okudu ve tarihini yazdı |
| Kontrol noktasından devam yan etkiyi tekrarlar | Sözleşme "idempotent olmalı" der; Manuel Case 7 davranışı ölçer |
| `WorkflowDefinition` şeması düğüm listesini taşıyamaz → beklenmedik migration | Açık Soru 5; uygulama ilk adımda şemayı ölçer |
| K-218 tuzağı tekrar eder | Kayıt bir fabrika alır; `WorkflowFunctionWiringTests` gerçek koşumla kanıtlar |

---

<!-- ============================================================
     AŞAĞISI KAPANIŞTA DOLDURULUR — `faz-tamamlama` skill'i.
     Plan anında boş kalır. Başlıkları SİLME.
     ============================================================ -->

## Plandan Sapmalar

> Kapanışta doldurulur.

## Bu Fazda Verilen Kararlar

> Kapanışta doldurulur. K-NNN numaraları burada alınır; plan numara rezerve etmez.

## Gerçekleşen Public API

> Kapanışta doldurulur.

## Dosya Listesi (gerçekleşen)

> Kapanışta doldurulur.

## Denetim Bulguları

> Kapanışta doldurulur.

## Sonraki Faza Devir Notu

> Kapanışta doldurulur.
