# Faz 114 — Çalıştırma-İçi Bütçe Tavanı

> **Durum:** ✅ Tamamlandı (2026-08-26)
> **Kaynak:** [ADAYLAR.md](../../ADAYLAR.md) · **F-166**
> **Önkoşul:** Faz 12 (agent çağrı grafiği, `AgentRunBudget`) ve Faz 21 (kota) — ikisi de arşivde; yalnız aşağıdaki grep'lerle okunur
> **Paketler:** `AgentPrism.Abstractions` (`Runs/AgentRunBudget.cs`), `AgentPrism.Core` (`Models/`, `Recording/`)
> **Yeni paket:** Yok · **Migration:** Yok — tavan yapılandırmadan gelir, veritabanına yazılmaz
> **Public API:** Büyüyor — mevcut bir tipe alanlar, bir dekoratör, bir exception tipi. Faz 7'den önce ucuz: `wc -l src/*/PublicAPI.Shipped.txt` toplamı **17** satır ve her dosya yalnız başlık taşıyor (K-603)
> **Tüketici yüzeyi:** `docs-site/src/content/docs/concepts/governance.md`, `guides/production.md`, `capabilities.md`
> · sevk edilen: `AgentRunBudget` ve `AgentPrismAgentGraphOptions` XML dokümanları — 🚨 ikisi de bugün **yanlış** şey ilan ediyor
> **Manuel test alanı:** `docs/manuel-test/23-SAKLAMA-ARSIV-KOTA.md`

---

## Bu Faza Başlarken

> `faz-baslangic` skill'ini uygula. Aşağıdaki liste o skill'in 2. adımıdır —
> **tamamını değil, yalnız işaret edilen bölümleri oku.**

1. Bu doküman
2. Kararlar — dosyanın tamamını **okuma**, yalnız bu kalemleri grep'le:
   ```bash
   grep -n "K-627\|K-603" docs/KARARLAR.md
   ```
   **K-627** (`RunErrorClass.BudgetExceeded` kaldırıldı, `9` kalıcı emekli;
   kaydın "Sonraki adım" sütunu bu fazın hata sınıfı kararını **açıkça
   devretmiştir**), **K-603** (`PublicAPI.Shipped.txt` boş).
3. Alan hafızası (bu faz üç alana dokunuyor):
   [`hafiza/cekirdek-calistirma.md`](../../hafiza/cekirdek-calistirma.md) (🚨 `AsyncLocal` tuzağı — bu fazın **ana riski**) ·
   [`hafiza/model-boru-hatti.md`](../../hafiza/model-boru-hatti.md) (`IChatClient` dekoratör halkaları ve sıraları) ·
   [`hafiza/olcum-kota-ve-secenekler.md`](../../hafiza/olcum-kota-ve-secenekler.md) (kota, maliyet, `Bind()`)
4. Gerektiğinde, tamamı değil ilgili bölümü: [`MIMARI.md`](../../MIMARI.md) — çalıştırma yolu

---

## Amaç

AgentPrism kurulumu bugün varsayılan olarak **200 000 token'lık bir ağaç
bütçesi** ilan eder ve bu varsayılanın *bilerek* var olduğunu yazar. Ölçüm o
tavanın **hiç zorlanmadığını** gösterdi: bütçe sayacı yalnız run **bitiminde**
işlenir ve yalnız **yeni bir alt-run başlarken** sorgulanır. Alt-agent'ı
olmayan tek bir run bütçeye hiç bakmaz; uzun bir tool döngüsü tavanı istediği
kadar aşabilir.

Bu faz o beyanı gerçek kılar: bütçe çalışma anında işlenir ve **tool turu
sınırında** uygulanır.

- **F-166** — mevcut `AgentRunBudget`'a maliyet boyutu ekler ve bütçeyi model
  çağrısı halkasının içinde zorlar.

### Bugün ne çalışmıyor — doğrulanmış kanıt

| Kanıt | Gözlem |
|---|---|
| [`AgentPrismOptions.cs:158`](../../../src/AgentPrism.Core/AgentPrismOptions.cs) | `MaxTotalTokens` varsayılanı **200 000** ve XML dokümanı diyor ki: *"The default intentionally exists. An unlimited installation learns about its first invalid definition from the bill."* |
| [`RunRecordingAgent.Completion.cs:102`](../../../src/AgentPrism.Core/Recording/RunRecordingAgent.Completion.cs) | `scope.Budget?.RecordUsage(usage?.TotalTokens ?? 0)` — sayaç **yalnız run bitiminde** artar. Run sürerken bütçe sıfır görünür |
| [`ChildAgentInvoker.cs:230`](../../../src/AgentPrism.Core/Graph/ChildAgentInvoker.cs) | Bütçeye bakılan **tek** yer: yeni bir alt-run başlarken. Tek agent'lı run bu koda hiç girmez |
| [`AgentRunBudget.cs:20-22`](../../../src/AgentPrism.Abstractions/Runs/AgentRunBudget.cs) | Tipin kendi XML dokümanı sınırı ilan ediyor: *"The budget blocks **new** child runs; it does not interrupt a run in progress."* |
| [`QuotaGate.cs:18-20`](../../../src/AgentPrism.AspNetCore/RateLimiting/QuotaGate.cs) | Kota tarafı da aynısını ilan ediyor: *"An ongoing run is **not cut off** when the quota is exceeded. This gate only stops a *new* run."* |
| [`QuotaEnforcer.cs`](../../../src/AgentPrism.Core/Quotas/QuotaEnforcer.cs) | Tipin yalnız **iki** public metodu var: `CheckAsync` (run öncesi) ve `RecordAsync` (run sonrası). Çalışma anı yüzeyi yok |
| `grep -rn "MaxCost\|MaxTokens" src/AgentPrism.Core/Recording src/AgentPrism.Core/Compilation` | **Sıfır isabet** |
| [`HarnessSettings.cs:21`](../../../src/AgentPrism.Abstractions/Agents/HarnessSettings.cs) | `MaximumIterationsPerRequest` **vardır** — `int?`, opt-in, varsayılanı yok |

> Kanıtlar 2026-08-26 tarihinde doğrulandı.

### 🚨 Aday metnindeki iddia ölçümle hem daraldı hem güçlendi

`ADAYLAR.md` şunu yazıyordu: *"hiçbir kod yolu onu çalışırken durdurmaz"* ve
karşı görüşünde *"tek bir run'ın dönem tavanını anlamlı biçimde aştığı ölçülmüş
bir vaka yoktur"* diyordu. Ölçüm ikisini de düzeltti:

| Aday metni | Ölçüm |
|---|---|
| "Kaçak agent döngüsü" gerekçesi | **Daraldı.** Tool döngüsü sınırsız değil: `MaximumIterationsPerRequest` bir iterasyon tavanı verir. Fakat o sayı **maliyeti** ölçmez — bağlamı büyük üç iterasyon, küçük otuzdan pahalıdır. Gerçek boşluk *"döngü sınırsız"* değil, ***"maliyet sayılmıyor"*** |
| "Ölçülmüş bir vaka yok" karşı görüşü | **Güçlendi ve karşı görüş düştü.** Bu bir FinOps konforu değil, bir **beyan hatasıdır**: varsayılan kurulum 200 000 token'lık bir tavan ilan eder ve o tavan tek agent'lı bir run'da hiçbir şey yapmaz. Sınıf olarak K-627 ile aynıdır — beyan edilmiş, üretilmemiş davranış |

Bu fazın gerekçesi budur: yeni bir yetenek eklemek değil, **var olan bir sözün
karşılığını üretmek**.

---

## 114.1 — Bugünkü akış ve deliğin yeri

```mermaid
sequenceDiagram
    participant EP as HTTP ucu
    participant QG as QuotaGate
    participant RA as RunRecordingAgent
    participant CC as IChatClient halkası
    participant CI as ChildAgentInvoker
    participant B as AgentRunBudget

    EP->>QG: CheckAsync (run ÖNCESİ)
    QG-->>EP: izin
    EP->>RA: RunAsync
    RA->>B: budget oluştur (MaxTotalTokens=200000)
    loop tool turu × N — SINIRSIZ HARCAMA
        RA->>CC: model çağrısı
        CC-->>RA: yanıt + usage
        Note over B: sayaç ARTMAZ
    end
    opt yalnız alt-agent varsa
        RA->>CI: alt-agent çağır
        CI->>B: TryReserveRun()
    end
    RA->>B: RecordUsage(total) — ARTIK ÇOK GEÇ
    RA->>QG: RecordAsync (run SONRASI)
```

Döngünün içinde bütçeye bakan hiçbir şey yoktur.

## 114.2 — Kesme noktası: tool turu halkası

**Karar (2026-08-26, kullanıcı):** Tavan mevcut `AgentRunBudget` üzerinde
yaşar; zorlama `UseFunctionInvocation` halkasının **içine** konan bir
`DelegatingChatClient` ile yapılır.

Emsal koddadır: yanıt önbelleği ringi tam olarak bu konumdadır
([`ModelProviderRegistry.cs:434-452`](../../../src/AgentPrism.Core/Models/ModelProviderRegistry.cs)) —
tool döngüsünün **içinde**, telemetrinin **dışında**. Aynı slot kullanılır.

```mermaid
flowchart TD
    A["UseFunctionInvocation<br/>(tool döngüsü)"] --> B["RunBudgetChatClient ← YENİ"]
    B --> C["AgentPrismResponseCachingChatClient<br/>(varsa)"]
    C --> D["UseOpenTelemetry"]
    D --> E["sağlayıcı"]

    B -.->|"çağrı ÖNCESİ: bütçe doldu mu?"| X["AgentPrismRunBudgetExceededException"]
    B -.->|"çağrı SONRASI: usage + cost işle"| Y["AgentRunBudget"]

    style B fill:#dfd,stroke:#0a0
```

**Neden bu konum:** kesme her zaman iki model çağrısının **arasında** olur.
Yarım bir model yanıtı asla kesilmez — aday metnindeki temel riski konum
tasarımı çözer, ayrı bir mekanizma gerekmez.

**Bloklanan tur `chat` span'i üretmez**, çünkü hiçbir model çağrısı yapılmaz.
Bu, önbellek isabetinin bugünkü davranışıyla aynıdır ve tutarlıdır.

## 114.3 — Bütçe run'a nasıl ulaşır

Derlenmiş agent **cache'lenir** (`CompiledAgentCache`), bütçe ise run'a
özeldir. Bu yüzden bütçe boru hattına derleme anında **gömülemez**.

Mekanizma zaten vardır:
[`AgentPrismRunContext.Current?.Budget`](../../../src/AgentPrism.Core/Recording/AgentPrismRunContext.cs)
— `AsyncLocal` ile taşınan run `scope`'u. Dekoratör her çağrıda **kendi
gövdesinde** onu **okur**.

🚨 **Bu repo'nun en pahalı tuzağı burada.** Kural
([`hafiza/cekirdek-calistirma.md`](../../hafiza/cekirdek-calistirma.md)):
`AsyncLocal` **yazımı** çağırana geri akmaz ve async yardımcı metotta
açılmamalıdır. Bu faz yalnız **okur** — yazmaz. Okuma güvenlidir ve
`AgentPrismRunContext`'in XML dokümanı akışın aşağı doğru çalıştığını yazar:
*"The value flows downward … a sub-agent running on a different thread also
sees the same scope."* Dekoratörde `AsyncLocal` **yazan** bir satır çıkarsa
bu bir denetim bulgusudur.

## 114.4 — 🚨 Çifte sayım: fazın en sinsi hatası

Dekoratör her turda `RecordUsage` çağırırsa ve
[`Completion.cs:102`](../../../src/AgentPrism.Core/Recording/RunRecordingAgent.Completion.cs)
run sonunda toplamı **yine** eklerse, her token **iki kez** sayılır. Bütçe
yarı yarıya küçülür ve kimse fark etmez — testler yeşil kalır, çünkü hiçbiri
toplamı iddia etmiyor.

Çözüm yönü ve ölçülmesi gereken sınır **Açık Soru 1**'dedir. Hangi yön
seçilirse seçilsin, **toplamı iddia eden bir test koddan önce yazılır**.

## 114.5 — Maliyet mi token mı

İkisi de. Fakat maliyet fiyatlandırma ister ve fiyat her zaman bilinmez.

Emsal zaten kurulmuştur ([`QuotaTypes.cs:60-66`](../../../src/AgentPrism.Abstractions/Quotas/QuotaTypes.cs)):
*"This limit **cannot be enforced** on a model with undefined pricing: since
the cost is unknown, the quota falls back to tokens."* Aynı kural burada da
uygulanır — **yeni bir felsefe icat edilmez.**

| Durum | Davranış |
|---|---|
| `PricingSource.Catalog` / `Configuration` | Maliyet tavanı zorlanır |
| `PricingSource.Unknown` | Maliyet tavanı **zorlanamaz**; token tavanına düşülür |
| Model bağlı değil (kod agent'ı) | Yalnız token tavanı |

`IRunPricingResolver.Resolve` **senkrondur** ([`IRunPricingResolver.cs:26`](../../../src/AgentPrism.Abstractions/Runs/IRunPricingResolver.cs)),
yani sıcak yolda `await` eklemez. Tavan tanımlı değilse çözümleyici hiç
çağrılmaz.

## 114.6 — Hata sınıfı: `QuotaExceeded` yeniden kullanılır

**Karar (2026-08-26, kullanıcı):** Kesme `RunErrorClass.QuotaExceeded` (4) ile
raporlanır. Yeni üye **açılmaz**; `9` kalıcı olarak emeklidir (K-627).

Gerekçe: kullanıcının gördüğü olgu aynıdır — bir harcama tavanı doldu. Mevcut
istemciler, `RunErrorStatistics` panelleri ve arıza kümeleme kodu değişmeden
çalışır; OpenAPI/TS/NSwag/`dist` zinciri hiç koşmaz.

**Eşleme regex ile yapılmaz.** Bugünkü `QuotaPattern()` mesajda `quota`
kelimesi arar; kesme mesajının o kelimeyi taşımasına güvenmek tam olarak
Faz 113'ün düzelttiği kırılganlıktır. Bunun yerine `StableIdentities`
sözlüğüne ([`DefaultRunErrorClassifier.cs:30-39`](../../../src/AgentPrism.Core/Runs/DefaultRunErrorClassifier.cs))
tipli bir kimlik eklenir — `ToolTimeout` ve `ContentFiltered` ile **aynı**
desen.

## 114.7 — Kapsam dışı

Bunlar bu fazda **yapılmaz** ve gerekirse ayrı aday olur:

| Kapsam dışı | Neden |
|---|---|
| Agent başına tavan (`AgentDefinition` alanı) | Üç SQL migration ve bir arayüz alanı ister. Kurulum düzeyi tavan önce kanıtlanır |
| `QuotaDefinition`'ın dönem tavanının run içinde uygulanması | Farklı bir sözleşme (kiracı × dönem); bu faz **ağaç** bütçesidir |
| Kesme sonrası kısmi yanıtın modele özetletilmesi | Yeni bir model çağrısı demektir — tavanı aşan bir tavan uygulaması olur |
| Arayüzde bütçe göstergesi | Bundle payı ve iki dil dosyası ister; önce sunucu davranışı |

---

## Planlanan Public API

> Taslak imzalardır. Gerçekleşen imzalar kapanışta ayrı bir bölüme yazılır.

```csharp
// AgentPrism.Abstractions — Runs/AgentRunBudget.cs (mevcut tip genişler)
public sealed class AgentRunBudget
{
    // mevcut: MaxTotalTokens, MaxTotalRuns, MaxDepth,
    //         ConsumedTokens, StartedRuns, IsTokenBudgetExhausted,
    //         TryReserveRun(), RecordUsage(long), DescribeExhaustion()

    /// <summary>The maximum amount spendable across the tree. No limit if null.</summary>
    /// <remarks>Cannot be enforced when pricing is undefined; the token limit applies instead.</remarks>
    public decimal? MaxTotalCost { get; init; }

    /// <summary>The amount spent across the tree so far.</summary>
    public decimal ConsumedCost { get; }

    /// <summary>Whether the cost limit has been reached.</summary>
    public bool IsCostBudgetExhausted { get; }

    /// <summary>Whether any limit has been reached.</summary>
    public bool IsExhausted { get; }

    /// <summary>Records tokens and cost spent in one model turn.</summary>
    public void RecordUsage(long tokens, decimal? cost);
}
```

```csharp
// AgentPrism.Abstractions — Runs/
/// <summary>The run tree's budget was exhausted mid-run.</summary>
public sealed class AgentPrismRunBudgetExceededException : AgentPrismException
{
    public const string RunBudgetExceededErrorType = "run_budget_exceeded";
    public override string ErrorType => RunBudgetExceededErrorType;
}
```

```csharp
// AgentPrism.Core — AgentPrismOptions.cs (AgentPrismAgentGraphOptions genişler)
/// <summary>The largest amount a tree may spend. Zero or negative removes the limit.</summary>
public decimal MaxTotalCost { get; set; }   // varsayılan 0 = sınırsız
```

`ModelProviderRegistry` kurucusuna sona iki isteğe bağlı parametre eklenir
(`IRunPricingResolver?`, `TimeProvider?` gerekiyorsa) — tipin bugünkü on iki
isteğe bağlı parametreli deseni korunur.

**`MaxTotalCost` varsayılanı `0`'dır (sınırsız).** `MaxTotalTokens`'ın 200 000
varsayılanı **değişmez** — bu faz onu ilk kez gerçekten uygular; sayıyı da
değiştirmek iki değişkeni aynı anda oynatmak olurdu.

### HTTP `endpoint`'leri

**Yeni uç yok.** Kesilen run mevcut hata yolundan raporlanır:
`runs.error_class = QuotaExceeded`, `runs.error_message` hangi tavanın dolduğunu
yazar. `202 Accepted` ile arka planda koşan run için de aynıdır.

### Arayüz payı

**Yok.** Sunucu yanıtı çevrilmez (K-232); `runs.error_message` olduğu gibi
gösterilir. `locales/en.ts` ve `tr.ts` değişmez. Bugünkü taban çizgisi
ölçüldü: `index-DESnx11L.js.br` = 148 928 B.

---

## Planlanan Dosya Listesi

```
src/AgentPrism.Abstractions/Runs/
├── AgentRunBudget.cs                       (değişir: maliyet boyutu)
└── AgentPrismRunBudgetExceededException.cs (yeni)

src/AgentPrism.Core/Models/
├── RunBudgetChatClient.cs                  (yeni: tool turu halkası)
└── ModelProviderRegistry.cs                (değişir: halkaya ekleme)

src/AgentPrism.Core/Runs/
└── DefaultRunErrorClassifier.cs            (değişir: StableIdentities satırı)

src/AgentPrism.Core/
├── AgentPrismOptions.cs                    (değişir: MaxTotalCost + CreateBudget)
└── AgentPrismServiceCollectionExtensions.Binding.Models.cs   (değişir: Bind)

src/AgentPrism.Core/Recording/
└── RunRecordingAgent.Completion.cs         (değişir: çifte sayım — Açık Soru 1)

tests/AgentPrism.Core.UnitTests/Runs/
├── AgentRunBudgetCostTests.cs              (yeni)
└── RunBudgetAccountingTests.cs             (yeni: toplamı iddia eder)

tests/AgentPrism.Core.FunctionalTests/
└── RunBudgetCutoffTests.cs                 (yeni: gerçek tool döngüsü)
```

---

## Hata Modları ve Testler

> Mutlu yoldan değil, **ne bozulabilir**den türetilir. Seviyeyi plan seçer.
> Sınır geçen davranış (DI · HTTP · kiracı · akış · depo · paket) birim
> testiyle kanıtlanamaz — [`.agents/ortak/test-seviyeleri.md`](../../../.agents/ortak/test-seviyeleri.md).

| Ne bozulabilir | Seviye | Test sınıfı |
|---|---|---|
| Token'lar **iki kez** sayılır (dekoratör + `Completion.cs:102`) | Birim | `RunBudgetAccountingTests` — N turluk run sonunda `ConsumedTokens` toplamı **tam** iddia edilir |
| Kesme bir model yanıtının **ortasında** olur | Fonksiyonel (akış sınırı) | `RunBudgetCutoffTests` — akışlı yolda kesmenin tur sınırında olduğu iddia edilir |
| Alt-agent farklı bir thread'de koşar ve bütçeyi **görmez** | Fonksiyonel | `RunBudgetCutoffTests` — ağaçlı senaryo |
| Eşzamanlı tool çağrıları sayacı kaydırır | Birim | `AgentRunBudgetCostTests` — `Interlocked` deseni `decimal` için **çalışmaz**; 🚨 `decimal` atomik değildir, kilit veya `long` mikro-birim gerekir |
| Fiyat bilinmiyorken maliyet tavanı sessizce sıfır sayılır ve run hemen kesilir | Birim | `AgentRunBudgetCostTests` — `PricingSource.Unknown` → token'a düşer, **kesmez** |
| Kesme `RunErrorClass.Unknown` olarak sınıflanır | Birim | `RunErrorClassContractTests` genişletilir (mevcut sınıf) |
| Kesme exception'ı `UseFunctionInvocation` tarafından yutulur ve modele metin olarak döner | Fonksiyonel | `RunBudgetCutoffTests` — 🚨 **ölçülmeli**: MAF'ın tool döngüsü exception'ı yukarı bırakıyor mu? |
| Tavan tanımsızken sıcak yolda fiyat çözümleme maliyeti doğar | Birim | `AgentRunBudgetCostTests` — çözümleyici **hiç çağrılmamalı** |
| Başka kiracının run'ı aynı bütçe nesnesini paylaşır | Fonksiyonel | `RunBudgetCutoffTests` — bütçe run başına oluşur; `ChildAgentInvoker.cs:216` kiracı kontrolü zaten var |
| Kesilen run `store`'a yazılamaz ve run düşer | Fonksiyonel | mevcut `RunStoreFailure` deseni — gözlemlenebilirlik işlevselliği bozmaz |

Beş sorunun cevabı: **iptal** → `OperationCanceledException` bütçe kontrolünün
**önünde** kalır, iptal bütçe hatasına dönüşmez · **eşzamanlılık** →
`decimal` sayaç için atomiklik ölçülmeli (yukarıda 🚨) · **boş/aşırı girdi** →
`usage == null` dönen sağlayıcı için ayrı case; sayaç artmaz, run kesilmez ·
**başka kiracı** → bütçe run başına oluşturulur, paylaşım yalnız ağaç içidir ·
**alt sistem hatası** → fiyat çözümleyici atarsa token tavanına düşülür ve
hata loglanır; run **durmaz**.

---

## Manuel Kabul Case'leri

> Kapanışta `docs/manuel-test/23-SAKLAMA-ARSIV-KOTA.md` içine eklenecek taslak.

| # | Ön koşul | Adımlar | Beklenen sonuç |
|---|---|---|---|
| 1 | `MaxTotalTokens` düşük (örn. 2000), tool döngüsü uzun bir agent | Agent'ı çalıştır | Run `Failed` biter; `error_class` = `QuotaExceeded`; mesaj hangi tavanın dolduğunu yazar |
| 2 | Aynı kurulum | Kesme anındaki son olayı incele | Son olay bir **tool sonucu** veya **tam bir model yanıtı**; yarım mesaj yok |
| 3 | `MaxTotalCost` tanımlı, fiyatı bilinen model | Tavanı aşacak bir run yap | Maliyet tavanından kesilir |
| 4 | `MaxTotalCost` tanımlı, fiyatı **bilinmeyen** model | Aynı run | Maliyet tavanı **uygulanmaz**; token tavanı geçerlidir; run maliyet yüzünden kesilmez |
| 5 | Hiçbir tavan tanımlı değil (`0`) | Uzun bir run yap | Kesme yok — gerileme yok |
| 6 | Alt-agent çağıran bir ağaç, tavan ortada dolar | Kök agent'ı çalıştır | Ağaçtaki **tüm** dallar aynı tavanı görür; toplam tavanı aşmaz |
| 7 | `202 Accepted` ile arka plan run'ı | Tavanı aşacak run başlat | Run kaydı `Failed`/`QuotaExceeded`; arayüzde hata görünür 👤 insan gerekir |

---

## Açık Sorular

> Planı bloklamayan, faz uygulanırken karara bağlanacak sorular.

| # | Soru | Seçenekler | Öneri |
|---|---|---|---|
| 1 | Çifte sayım nasıl önlenir? | A: `Completion.cs:102` kaldırılır, dekoratör tek kaydeden olur · B: Dekoratör kaydeder, `Completion` yalnız dekoratörün **görmediğini** (sıkıştırma yan kanalı) ekler | **B.** A, `CompactionUsageTrackingChatClient`'ın ürettiği ve `Completion`'ın `MergeUsage` ile eklediği token'ları **kaybeder**. B toplamı korur. 🚨 Sıkıştırma yan kanalının dekoratörden geçip geçmediği **ölçülmeli** |
| 2 | `decimal ConsumedCost` eşzamanlı olarak nasıl artırılır? | A: `lock` · B: maliyeti `long` mikro-birimde tut, `Interlocked.Add` | **B.** Tip bugün tamamen kilitsiz (`Interlocked`) ve XML dokümanı bunu bir tasarım kararı olarak yazıyor; tek bir alan için kilit koymak o kararı bozar |
| 3 | `RecordUsage(long)` mevcut imzası korunsun mu? | A: Aşırı yükleme ekle · B: İsteğe bağlı parametreye çevir | **A.** İsteğe bağlı parametre kaynak uyumlu ama **ikili uyumsuzdur**; aşırı yükleme ikisini de korur ve maliyeti yoktur |
| 4 | Kesme mesajı ne kadar ayrıntı versin? | A: Hangi tavan, ne kadar harcandı, hangi ayar yükseltilir · B: Yalnız "bütçe doldu" | **A.** `DescribeExhaustion()` zaten bu felsefeyi taşıyor: *"unless the exceeded limit is named, the user cannot see which setting to raise"* |
| 5 | MAF'ın tool döngüsü dekoratörün exception'ını yukarı bırakıyor mu? | — | **Ölçülmeli.** Yutuluyorsa kesme modele metin olarak döner ve run **başarılı** biter — yani fazın tamamı sessizce çalışmaz. Kod yazmadan önce `maf-api-kesfi` ile `UseFunctionInvocation` davranışı doğrulanır |

---

## Bitiş Ölçütleri (DoD)

- [x] `MaxTotalTokens` düşük ayarlıyken uzun tool döngülü bir run **kesilir**; `runs.error_class` = `QuotaExceeded` — `RunBudgetAccountingTests` + gerçek `samples/AgentPrism.Api` koşumu (aşağıda)
- [x] Kesme her zaman tool turu sınırındadır — kesilen run'ın son olayı yarım bir model mesajı **değildir** — gerçek koşumda ölçüldü: son olay `RunFailed`'in kendisidir, arada YARIM DEĞİL, hiçbir tool/mesaj olayı yoktur (bkz. Plandan Sapmalar)
- [x] N turluk bir run sonunda `AgentRunBudget.ConsumedTokens` gerçek toplama **eşittir** (çifte sayım yok) — `Two_turn_tool_loop_is_recorded_exactly_once_no_cap`
- [x] `PricingSource.Unknown` modelde maliyet tavanı **uygulanmaz**; token tavanına düşülür — `Unknown_pricing_falls_back_to_the_token_cap_and_never_cuts_on_cost_alone`
- [x] Hiçbir tavan tanımlı değilken (`0`) davranış bugünküyle **aynıdır** — gerileme yok — `No_cap_configured_keeps_todays_unlimited_behaviour`
- [x] Tavan tanımsızken `IRunPricingResolver` sıcak yolda **hiç çağrılmaz** — `Pricing_resolver_is_never_consulted_when_no_cost_cap_is_set`
- [x] Ağaçtaki tüm dallar aynı bütçeyi görür; toplam tavanı aşmaz — `Two_different_agents_sharing_one_tree_budget_are_cut_off_by_each_others_spend`
- [x] `RunBudgetChatClient` içinde `AsyncLocal`'a **yazan** hiçbir satır yok (yalnız okur) — kod okunarak doğrulandı, bağımsız denetimin de teyit ettiği madde
- [x] `RunErrorClass` üye kümesi **değişmedi**; `9` tanımsız kaldı (`RunErrorClassContractTests` yeşil)
- [x] `AgentRunBudget` ve `AgentPrismAgentGraphOptions` XML dokümanları gerçeğe uydu — denetimin 🔴 bulgusu düzeltildi (bkz. Denetim Bulguları)
- [x] Dört doğrulama kapısı sıfır uyarı verir — `python3 scripts/kapi.py kapanis --taban 58cfd03` (tam koşum, SQL Server/Sqlite/PostgreSQL entegrasyon testleri dahil)
- [x] `samples/AgentPrism.Api` ile gerçek `run` yapıldı, çıktı belgeye yazıldı — bkz. aşağıdaki gerçek çıktı
- [x] `secret` taraması boş döndü — `python3 scripts/kapi.py tarama` → `✅ temiz`
- [x] Manuel kabul case'leri `docs/manuel-test/23-SAKLAMA-ARSIV-KOTA.md` içine eklendi; otomatikleştirilebilenler koşuldu — MT-RET-050/051 gerçek OpenAI çağrısıyla koşuldu ve ölçülen sonuca göre düzeltildi; MT-RET-052..055 tam manuel-test koşumuna bırakıldı (bkz. Sonraki Faza Devir Notu)
- [x] `faz-denetim` koşuldu; 🔴 bulgu kalmadı — 1 🔴 bulundu ve düzeltildi (bkz. Denetim Bulguları)
- [x] `docs-site/` güncellendi (`concepts/governance.md` run-içi tavanı anlatır); `npm run build` + `check-links.mjs` temiz — `npm run check` tam koşum yeşil

### Doğrulama komutları (gerçekten koşuldu, 2026-08-26, `samples/AgentPrism.Api`, gerçek OpenAI çağrısı)

```bash
# Tavanı düşür, "support" agent'ını gerçek bir sipariş sorusuyla çalıştır
export AgentPrism__AgentGraph__MaxTotalTokens=150
cd samples/AgentPrism.Api && ASPNETCORE_ENVIRONMENT=Development dotnet run -c Release

curl -s -X POST http://localhost:5080/agentprism/api/agents/support/run \
  -H "Authorization: Bearer manuel-test-token-2026" -H 'Content-Type: application/json' \
  -H "Idempotency-Key: $(uuidgen)" -d '{"message":"Where is my order 42?"}'
```

**Gerçek yanıt (aynen):**
```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.6.3",
  "title": "Agent run failed",
  "status": 502,
  "detail": "The run tree's token budget is exhausted (254/150). Raise AgentPrism:AgentGraph:MaxTotalTokens to allow more. No further model calls can be made in this run tree."
}
```

```bash
RUN_ID=$(curl -s http://localhost:5080/agentprism/api/runs -H "Authorization: Bearer manuel-test-token-2026" | jq -r '.[0].id')
curl -s "http://localhost:5080/agentprism/api/runs/$RUN_ID" -H "Authorization: Bearer manuel-test-token-2026" | jq '{status, errorClass: .error.class, errorType: .error.type}'
```

**Gerçek yanıt (aynen):** `{"status": "Failed", "errorClass": "QuotaExceeded", "errorType": "run_budget_exceeded"}`

**Tavansız kontrol koşumu** (aynı sorgu, `AgentGraph:MaxTotalTokens` unset — varsayılan `200000`): gerçek tool döngüsü (`get_order_status` çağrıldı, sonuç kullanıldı) toplam **551 token** ile `Completed` bitti — varsayılan tavan bu tipik run'ı hiç zorlamaz, gerileme yok.

---

## Riskler

| Risk | Önlem |
|------|-------|
| 🚨 Çifte sayım sessizce bütçeyi yarıya indirir | Toplamı iddia eden test **koddan önce** yazılır; Açık Soru 1 ölçümle kapatılır |
| 🚨 MAF'ın tool döngüsü exception'ı yutar ve faz hiç çalışmaz | Açık Soru 5 `maf-api-kesfi` ile **kod yazmadan önce** ölçülür |
| 🚨 `AsyncLocal` yazımı async yardımcıda açılır ve bütçe akmaz | Dekoratör yalnız **okur**; DoD'de ayrı satır; `hafiza/cekirdek-calistirma.md` bu sınıfın dört vakasını taşıyor |
| `decimal` sayaç eşzamanlı turlarda kayar | Açık Soru 2; `long` mikro-birim önerisi kilitsiz deseni korur |
| Mevcut kurulumlar aniden kesilmeye başlar | `MaxTotalTokens` varsayılanı (200 000) **değiştirilmez**; yalnız ilk kez uygulanır. Bu bir davranış değişikliğidir ve site sayfasında **açıkça** yazılır |
| Sıcak yola fiyat çözümleme maliyeti girer | Tavan tanımsızken çözümleyici hiç çağrılmaz; DoD'de ayrı satır |
| Kesme kısmi çıktıyı kullanıcıya açıklamaz | Mesaj hangi tavanın dolduğunu ve hangi ayarın yükseltileceğini yazar (Açık Soru 4) |

---

<!-- ============================================================
     AŞAĞISI KAPANIŞTA DOLDURULUR — `faz-tamamlama` skill'i.
     Plan anında boş kalır. Başlıkları SİLME.
     ============================================================ -->

## Plandan Sapmalar

- **`ModelProviderRegistry` kurucusuna plan `IRunPricingResolver?` (doğrudan) öneriyordu; gerçekleşen `IServiceProvider?`'dır.** Kod yazmadan önce ölçüldü: `RunPricingResolver`'ın kendisi `IModelProviderRegistry`'ye bağımlı olduğu için doğrudan parametre DI'nin çözemeyeceği bir döngü üretiyordu. `BuildPipeline` fiyat çözümleyiciyi agent derleme anında, DI konteyneri tamamen kurulduktan sonra, geç (lazy) çözer — bkz. K-631.
- **Açık Soru 1 (çifte sayım) planın önerdiği B seçeneğinden ("dekoratör kaydeder, `Completion` yalnız GÖRMEDİĞİNİ ekler") daha güçlü bir sonuçla kapandı.** Ölçüm (sıkıştırma özetleme çağrısının da AYNI `ModelProviderRegistry.BuildPipeline` boru hattından geçtiği) `RunBudgetChatClient`'ın hiçbir şeyi KAÇIRMADIĞINI gösterdi — `Completion.cs`'in eski satırı "eksiği tamamlayan" değil tamamen GEREKSİZ oldu ve kaldırıldı (K-632).
- **Açık Soru 2 (kilitsiz `decimal` sayaç)**: B seçildi — `long` nano-birim (1e9) sabit noktalı sayaç + `Interlocked.Add`. Planın önerdiğiyle birebir aynı yön, tek fark ölçek seçimi (mikro değil nano — token başı fiyatların altı basamakta sıfıra yuvarlanmaması için, bkz. K-632).
- **Açık Soru 3 (`RecordUsage(long)` imzası)**: A seçildi — aşırı yükleme eklendi (`RecordUsage(long, decimal?)`), eski imza `RecordUsage(long tokens) => RecordUsage(tokens, cost: null)` olarak korundu.
- **Açık Soru 4 (kesme mesajı ayrıntısı)**: A seçildi — mesaj hangi tavanın dolduğunu, ne kadar harcandığını VE hangi `AgentPrism:AgentGraph:*` ayarının yükseltileceğini adıyla yazar. Gerçek ölçülen örnek: *"The run tree's token budget is exhausted (254/150). Raise AgentPrism:AgentGraph:MaxTotalTokens to allow more. No further model calls can be made in this run tree."*
- **Açık Soru 5 (MAF'ın tool döngüsü exception'ı yutuyor mu?)**: Kod yazmadan ÖNCE küçük bir probe projesiyle ölçüldü (bu oturumda, `maf-api-kesfi` skill'i yerine doğrudan gerçek `FunctionInvokingChatClient`'a karşı bir istisna senaryosu koşularak) — **yutmuyor**, hem akışsız hem akışlı yolda istisna aynen yukarı bırakılıyor. Tasarım bu ölçümle doğrulandı, değişmedi.
- **`TryReserveRun()`'ın exhaustion kontrolü genişletildi**: plan yalnız yeni maliyet alanlarını eklemeyi öngörüyordu; uygulama sırasında `TryReserveRun`'ın mevcut `IsTokenBudgetExhausted` kontrolü de `IsExhausted`'e (token OR cost) genişletildi — aksi hâlde maliyeti tükenmiş bir ağaç hâlâ yeni alt-run başlatabilirdi, tutarsız bir davranış olurdu. Faz dokümanında planlanmamış küçük bir kapsam genişlemesidir, gerekçesi budur.
- **Bağımsız denetimin bulduğu 🔴** (`AgentRunBudget`'ın sınıf düzeyi XML `<remarks>`'i düzeltilmemişti) düzeltildi — bkz. Denetim Bulguları.
- **Manuel case'lerin ilk taslağı ölçülmeden yazılmıştı**: `samples/AgentPrism.Api`'ye karşı gerçek OpenAI çağrısıyla koşulduğunda iki hata bulundu ve düzeltildi — (1) başarısız bir `POST /run`'ın gövdesi bir `run` kaydı değil `ProblemDetails`'tir (`RUN_ID` `GET /api/runs`'tan okunur), (2) MAF'ın `FunctionInvokingChatClient`'ı bir istisna fırlattığında o ana kadarki kısmi ilerlemeyi (tool çağrısı GERÇEKTEN yapılmış olsa bile) hiç geri döndürmez — kesilen bir run'ın olay akışı `RunStarted → RunFailed`'ten ibarettir, aralarında YARIM DEĞİL, hiçbir tool/mesaj olayı yoktur. MT-RET-051 bu ölçülen gerçeğe göre yeniden yazıldı.

## Bu Fazda Verilen Kararlar

- **K-630** — Çalıştırma-içi bütçe kesmesi yeni bir `RunErrorClass` üyesi açmadan mevcut `QuotaExceeded` (`4`)'e eşlenir; `9` kalıcı emekli kalır.
- **K-631** — `ModelProviderRegistry` kurucusu `IServiceProvider?` alır, fiyat çözümleyiciyi `BuildPipeline` içinde geç çözer (döngüsel DI kırılımı).
- **K-632** — `AgentRunBudget`'ın maliyet sayacı kilitsiz `long` nano-birimdir; `RunRecordingAgent.CompleteAsync`'in run-sonu bütçe kaydı satırı kaldırıldı, muhasebenin %100'ü `RunBudgetChatClient`'a taşındı.

Tam gerekçeler: `docs/KARARLAR.md`.

## Gerçekleşen Public API

```csharp
// AgentPrism.Abstractions — Runs/AgentRunBudget.cs
public sealed class AgentRunBudget
{
    // mevcut üyeler değişmedi (MaxTotalTokens, MaxTotalRuns, MaxDepth,
    // ConsumedTokens, StartedRuns, IsTokenBudgetExhausted, TryReserveRun(),
    // RecordUsage(long), DescribeExhaustion() — imzaları aynı, gövdeleri
    // TryReserveRun/DescribeExhaustion için genişledi)

    public decimal? MaxTotalCost { get; init; }
    public decimal ConsumedCost { get; }
    public bool IsCostBudgetExhausted { get; }
    public bool IsExhausted { get; }                       // token OR cost; run-count HARİÇ

    public void RecordUsage(long tokens, decimal? cost);   // yeni aşırı yükleme
    public string DescribeModelCallExhaustion();            // yeni
}

// AgentPrism.Abstractions — Runs/AgentPrismRunBudgetExceededException.cs (yeni dosya)
public sealed class AgentPrismRunBudgetExceededException : AgentPrismException
{
    public const string RunBudgetExceededErrorType = "run_budget_exceeded";
    public override string ErrorType => RunBudgetExceededErrorType;
    // + üç kurucu (parametresiz, message, message+innerException) — repo şablonu
}

// AgentPrism.Core — AgentPrismOptions.cs (AgentPrismAgentGraphOptions genişler)
public decimal MaxTotalCost { get; set; }   // varsayılan 0 = sınırsız

// AgentPrism.Core — Models/ModelProviderRegistry.cs (kurucu — plandan sapma, bkz. yukarı)
public ModelProviderRegistry(
    /* ... mevcut on iki parametre değişmedi ... */
    IServiceProvider? services = null)   // YENİ — IRunPricingResolver? DEĞİL
```

`src/AgentPrism.Core/Models/RunBudgetChatClient.cs` — planlandığı gibi `internal sealed`, public yüzeye çıkmaz.

## Dosya Listesi (gerçekleşen)

```
src/AgentPrism.Abstractions/Runs/
├── AgentRunBudget.cs                       (değişti: maliyet boyutu)
└── AgentPrismRunBudgetExceededException.cs (yeni)

src/AgentPrism.Abstractions/Runs/RunErrorClass.cs   (değişti: QuotaExceeded XML dokümanı)

src/AgentPrism.Core/Models/
├── RunBudgetChatClient.cs                  (yeni: tool turu halkası)
└── ModelProviderRegistry.cs                (değişti: halkaya ekleme + IServiceProvider)

src/AgentPrism.Core/Runs/DefaultRunErrorClassifier.cs   (değişti: StableIdentities satırı)

src/AgentPrism.Core/
├── AgentPrismOptions.cs                                          (değişti: MaxTotalCost + CreateBudget)
├── AgentPrismServiceCollectionExtensions.Binding.Models.cs       (değişti: Bind)
└── AgentPrismServiceCollectionExtensions.Registration.Core.cs    (değişti: DI kaydı)

src/AgentPrism.Core/Recording/RunRecordingAgent.Completion.cs   (değişti: çifte sayım satırı kaldırıldı)

tests/AgentPrism.Core.UnitTests/
├── Graph/AgentRunBudgetCostTests.cs        (yeni — 8 test)
├── Recording/RunBudgetAccountingTests.cs   (yeni — 11 test, gerçek çok-turlu tool döngüsü)
├── Fakes/FakeRunPricingResolver.cs         (yeni)
└── Runs/DefaultRunErrorClassifierTests.cs  (değişti: +1 InlineData)

docs-site/src/content/docs/
├── guides/reliability.md        (değişti: "Bound multi-agent trees" yanlış iddia düzeltildi)
├── guides/production.md         (değişti: davranış-değişikliği notu + tablo satırı)
├── reference/configuration.md   (değişti: MaxTotalCost satırı)
├── capabilities.md              (değişti: agent graph satırı)
└── concepts/governance.md       (değişti: kota/ağaç bütçesi ayrımı notu)

docs/manuel-test/23-SAKLAMA-ARSIV-KOTA.md   (değişti: §5, MT-RET-050..055, gerçek OpenAI çağrısıyla ölçülüp düzeltildi)
docs/manuel-test/00-INDEKS.md               (değişti: satır 23 güncellendi)
docs/KARARLAR.md                            (değişti: K-630, K-631, K-632)
docs/hafiza/model-boru-hatti.md             (değişti: döngüsel DI tuzağı)
docs/hafiza/olcum-kota-ve-secenekler.md     (değişti: sıkıştırma boru hattı notu + QuotaExceeded düzeltmesi)
```

**Planda olup gerçekleşmeyen**: `tests/AgentPrism.Core.FunctionalTests/RunBudgetCutoffTests.cs` — bu proje repoda **hiç yoktur** (plan bunu ölçmeden varsaymıştı). Karşılığı: (1) `RunBudgetAccountingTests.cs`, `ModelProviderRegistry`+`FakeChatClient` üzerinden REAL boru hattıyla akış-sınırı davranışını kanıtlar (`AgentPrismRunContext`/`AsyncLocal` dahil — `ChildAgentInvokerTests.cs`'nin zaten kullandığı seviye ve desen), (2) `samples/AgentPrism.Api`'ye karşı gerçek OpenAI çağrısı (bkz. DoD doğrulama komutları).

## Testler

- `AgentRunBudgetCostTests` (8) — maliyet boyutunun saf `AgentRunBudget` davranışı: sınır aşımı, `null`/negatif/sıfır maliyetin no-op olması, altı basamağın sıfıra yuvarlanmaması, eşzamanlı `Parallel.ForAsync` toplamı, `TryReserveRun`'ın maliyeti de dinlemesi, `MaxTotalCost=0` → sınırsız.
- `RunBudgetAccountingTests` (11) — gerçek `AgentDefinitionCompiler`/`ModelProviderRegistry`/`FakeChatClient` boru hattı üzerinden: çift sayım yok (tam toplam), üçüncü tur sağlayıcıya hiç ulaşmadan kesilir, akışlı yolda aynı kesme, tavansızken gerileme yok, maliyet tavanı kesmesi, `PricingSource.Unknown` düşüşü, tavansızken çözümleyici hiç çağrılmaz, `usage == null` ne kayıt ne fatal, **iki farklı agent'ın (kök+alt benzeri) aynı bütçeyi paylaşıp birbirinin harcamasıyla kesilmesi**.
- `DefaultRunErrorClassifierTests` — `run_budget_exceeded`'in "quota"/"kota" kelimesi TAŞIMAYAN bir mesajla bile `QuotaExceeded`'e eşlendiğini kanıtlar (regex değil stable-identity eşlemesi).
- `AgentRunBudgetTests` (mevcut, değişmedi) — 2036 testin tamamı yeşil (`dotnet test` tam koşum).

## Denetim Bulguları

Bağımsız denetçi (taze bağlamlı, `git diff` + faz dokümanı) 1 🔴, 2 🟡 buldu.

| # | Seviye | Bulgu | Sonuç |
|---|---|---|---|
| 1 | 🔴 | `AgentRunBudget`'ın sınıf düzeyi XML `<remarks>`'i hâlâ "does not interrupt a run in progress" diyordu — fazın merkezi iddiasıyla doğrudan çelişiyordu | **Düzeltildi.** `<remarks>` `TryReserveRun` (token/count, yalnız yeni child-run engeller) ile token/cost boyutunu (mid-run kesme) ayrı ayrı ve doğru anlatacak şekilde yeniden yazıldı |
| 2 | 🟡 | Ağaç genelinde paylaşılan bütçe için (iki FARKLI agent'ın kendi `RunBudgetChatClient` örneği üzerinden) otomatik test yoktu | **Düzeltildi.** `RunBudgetAccountingTests.Two_different_agents_sharing_one_tree_budget_are_cut_off_by_each_others_spend` eklendi — iki ayrı `ModelProviderRegistry` pipeline'ı, aynı `AgentRunBudget` nesnesi, birinin harcaması diğerini kestiğini kanıtlıyor |
| 3 | 🟡 | `usage == null` (boş/aşırı girdi hata modu) için test yoktu | **Düzeltildi.** `RunBudgetAccountingTests.A_response_with_no_usage_is_neither_recorded_nor_fatal` eklendi |

Denetçinin "doğrulanamayan/kanıtsız" not ettiği DoD satırı (`samples/AgentPrism.Api` ile gerçek run) bu kapanışta gerçekleştirildi — bkz. aşağıdaki DoD tablosu ve doğrulama komutları.

Temiz çıkan başlıklar (denetçinin raporu): 3.2 (test tiyatrosu yok), 3.3 (test seviyesi doğru), 3.5 (imza-gövde kayması yok), 3.6 (plan dışı public API yok, `PublicAPI.Unshipped.txt` tam), 3.7 (repo kuralları), 3.8 (ürün yüzeyi).

## Sonraki Faza Devir Notu

- **`RunBudgetChatClient`'ın ring pozisyonu artık kararlıdır**: `UseFunctionInvocation`'ın içinde, `AgentPrismResponseCachingChatClient`/`UseOpenTelemetry`'nin dışında — `ModelProviderRegistry.BuildPipeline`'a yeni bir halka eklerken bu üç halkanın (budget/cache/telemetry) göreli sırasını bozmadan ekle; `.Use()` çağrılarının kayıt SIRASI (ilk kayıt = en dıştaki katman) bu dosyanın kendi yorumlarında açıklanıyor.
- **🚨 `ModelProviderRegistry`'ye kendisi `IModelProviderRegistry`'ye bağımlı bir servis eklerken döngüsel DI riskini hatırla** — bkz. `docs/hafiza/model-boru-hatti.md`, K-631. `IServiceProvider` + geç çözümleme deseni tekrarlanabilir.
- **Agent başına bütçe** (`AgentDefinition` alanı) ve **`QuotaDefinition`'ın dönem tavanının run içinde uygulanması** bu fazın KAPSAM DIŞI bıraktığı iki komşu iş — `ADAYLAR.md`'ye aday olarak eklenebilir, ayrı ölçüm gerektirir.
- **Manuel case'ler MT-RET-052..055 gerçek bir koşumla doğrulanmadı** (yalnız MT-RET-050/051 gerçek OpenAI çağrısıyla ölçüldü ve düzeltildi) — sonraki tam manuel-test koşumunda (`manuel-test-kosumu` skill'i) bu dört case'in de gerçek sunucuya karşı çalıştığını doğrula.
