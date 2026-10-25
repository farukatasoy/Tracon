# Faz 243 — Bütçe Tükenince Tool Çağrısı Başlamaz

> **Durum:** ✅ Tamamlandı (2026-10-25)
> **Kademe:** tam — geri dönüşü pahalı karar ve maliyet sınırı: ağaç bütçesi tüketicinin harcama tavanıdır (K-999 ailesi). Kapı her tool kaynağının zincirine girer; yanlış konum tavanı sessizce delikli bırakır (MEMORY: yapısal iddia, K-320). GA'dan sonra bu davranışı değiştirmek major sürüm ister
> **Plan onayı:** Bakımcı, 2026-10-25 (sohbette alınan karar: koşul `IsExhausted` — token, maliyet ya da süre; ret biçimi: tool koşmaz, model açıklayıcı sonuç alır, sonraki model çağrısı bugünkü kapıda durur)
> **Kaynak:** [ADAYLAR.md](ADAYLAR.md) · **F-530**
> **Genellik:** Ağaç bütçesi (`Tracon:AgentGraph:MaxTotalCost`, `MaxTotalTokens` — varsayılan 200.000, `MaxDuration`) kuran ve tool'u ücret raporlayan (`TraconToolUsage.Report`: ses, görsel, dış API) her kurulum. İş tüketici kodunda kalamaz: tüketici tool'un başlangıcını bütçe ile bağlayamaz; zincir Tracon'undur. İkinci kanıt ilk ilkedir: K-999 tavanı "yeni işi durdurur" diye tanımlar ve yeniden açılma notu bu kalemi adıyla anar ("ücretli tool çağrısını başlamadan reddetme … ayrı karardır")
> **Karşı görüş:** (1) "Tool döngüsü MAF'tadır; Tracon araya giremez." Cevap: döngü MAF'ın `FunctionInvokingChatClient`'ındadır ama her tool çağrısı Tracon'un sarmalayıcı zincirinden geçer (`ToolWrapperChain.Compose`). Kapı oraya girer; MAF değişmez. (2) "Sonraki model çağrısı zaten reddedilir; ikinci tool'un maliyeti küçüktür." Cevap: ikinci tool'un maliyeti sınırsızdır (ses, görsel, dış API) ve yan etkisi geri alınamaz. Sonucunu okuyacak model çağrısı olmayacağı için o tool'u koşturmak yalnız harcamadır
> **Önkoşul:** Yok. Faz 239 (kota sürekliliği) kapandı; kota bu fazda okunmaz (§243.3)
> **Paketler:** `Tracon.Core` (`ToolWrapperChain`, yeni `BudgetGatingAIFunction`, `AgentRunBudget` iç metni, skill script yürütücüsü), `Tracon.Abstractions` (`AgentRunBudget` XML, iç açıklama)
> **Yeni paket:** Yok · **Migration:** Yok
> **Public API:** Büyümüyor — kapı `internal`; değişen yalnız XML metni (`TraconAgentGraphOptions.MaxTotalCost`/`MaxTotalTokens`/`MaxDuration`, `AgentRunBudget`, `TraconToolUsage`)
> **Tüketici yüzeyi:** site `concepts/governance.md` (bütçe), `guides/production.md`, `guides/write-your-own-tool.md` (`TraconToolUsage`), `concepts/workflows.md` (K-999 cümlesinin tool eşi) · sevk edilen: yukarıdaki XML, `CHANGELOG.md` `[Unreleased]` (davranış değişikliği: tükenmiş bütçede tool başlamaz)
> **Manuel test alanı:** `docs/manuel-test/12-GOZLEMLENEBILIRLIK-MALIYET.md`

---

## Bu Faza Başlarken

> `faz-baslangic` skill'ini uygula. Aşağıdaki liste o skill'in 2. adımıdır —
> **tamamını değil, yalnız işaret edilen bölümleri oku.**

1. Bu doküman
2. Kararlar — dosyanın tamamını **okuma**, yalnız bu kalemleri grep'le:
   ```bash
   grep -n "K-999 —\|K-483 —\|K-990 —\|K-893 —\|K-1046 —\|K-737 —\|K-840 —" docs/KARARLAR.md
   ```
   **K-999** (fonksiyon düğümü tükenmiş bütçede başlamaz; bu fazın emsali),
   **K-483** (toplama tek yerde), **K-990** (kota girdisi harcayan run başına),
   **K-893** (yan kanal modelin token'ı run'a girer), **K-1046** (hosted çağrı
   kaydı), **K-737/K-840** (iptal mi arıza mı)
3. Önceki fazdan devir: yok. Faz 210'un (K-999) tam metni gerekirse:
   `git show 2a8149ef:docs/arsiv/fazlar/210-BUTCE-DUGUM-KAPISI.md`
4. Alan hafızası (iki alan):
   [`hafiza/olcum-kota-ve-secenekler.md`](hafiza/olcum-kota-ve-secenekler.md) (bütçe ve toplama tuzakları) ·
   [`hafiza/tool-onay-ve-yetkilendirme.md`](hafiza/tool-onay-ve-yetkilendirme.md) (zincir sırası, ret kaydı)
5. Gerektiğinde: zincirin sıra gerekçesi `ToolWrapperChain.cs`'in kendi yorumundadır
   (`:9-29`); [`MIMARI.md`](MIMARI.md) § *K2 — Tool'lar yalnız kodda tanımlanır*

---

## Amaç

Ağaç bütçesi bugün iki yerde denetlenir: her model çağrısından önce
(`RunBudgetChatClient`) ve her alt agent çağrısından önce (`ChildAgentInvoker`).
Tool çağrısı denetlenmez. Bir model turu iki ücretli tool isterse ve birincisi
tavanı geçirirse, ikincisi yine koşar. Sonraki model çağrısı reddedilir ama
ikinci tool'un parası ve yan etkisi gitmiştir.

- **F-530** — tükenmiş bütçede (token, maliyet ya da süre) hiçbir tool çağrısı
  başlamaz. Tool'un yerine model açıklayıcı bir sonuç alır. Kayıt çağrıyı başarısız
  gösterir. Run, sonraki model çağrısında bugünkü kimlikle kapanır
  (`Failed`, `run_budget_exceeded`).

### Bugün ne çalışmıyor — doğrulanmış kanıt

| Kanıt | Gözlem |
|---|---|
| [`RunBudgetChatClient.cs:94-104`](../src/Tracon.Core/Models/RunBudgetChatClient.cs) | Bütçe yalnız model çağrısından önce okunur (`IsExhausted`) |
| [`ChildAgentInvoker.cs:516`](../src/Tracon.Core/Graph/ChildAgentInvoker.cs) | Alt agent çağrısı da denetlenir |
| [`ToolWrapperChain.cs:59-129`](../src/Tracon.Core/Tools/ToolWrapperChain.cs) | Zincir: ExplainedFailure → Authorizing → Validating → Timeout → ApprovalRequired → Truncating → tool. Hiçbir katman bütçeye bakmaz |
| [`ToolRegistry.cs:130`](../src/Tracon.Core/Tools/ToolRegistry.cs) · [`WorkspaceToolGovernance.cs:112`](../src/Tracon.Core/Workspace/WorkspaceToolGovernance.cs) · [`McpTenantTools.cs:219`](../src/Tracon.Mcp/Internal/McpTenantTools.cs) | Kod tool'ları, çalışma alanı tool'ları ve MCP tool'ları zinciri tek yerden alır |
| [`TraconToolUsage.cs:58`](../src/Tracon.Core/Tools/TraconToolUsage.cs) · [`NonTokenCostAccumulator.cs:347-353`](../src/Tracon.Core/Recording/NonTokenCostAccumulator.cs) | Tool'un raporladığı ücret bütçeye **anında** yazılır (`RecordUsage(0, increase)`). İkinci tool başlarken `IsCostBudgetExhausted` zaten `true` olabilir |
| [`AgentRunBudget.cs:266`](../src/Tracon.Abstractions/Runs/AgentRunBudget.cs), `:385`, `:402` | `IsExhausted` = token ∨ maliyet ∨ süre. Model çağrısı ve fonksiyon düğümü için ayrı ret metinleri var; tool için yok |
| [`ExplainedFailureAIFunction.cs:43-66`](../src/Tracon.Core/Tools/ExplainedFailureAIFunction.cs) | `TraconException`'ın mesajı tool sonucu olur ve çağrı kayıtta başarısız işaretlenir (`ToolExplainedFailures`) |
| [`ModelProviderRegistry.cs:554`](../src/Tracon.Core/Models/ModelProviderRegistry.cs) | `ToolLoop.AllowConcurrentToolCalls` açıksa bir turun tool'ları birlikte başlar |
| `K-999` satırı (`docs/KARARLAR.md:1034`) | Yeniden açılma notu: "ücretli tool çağrısını başlamadan reddetme istenirse ayrı karardır" |

> Kanıtlar 2026-10-25 tarihinde doğrulandı.

---

## 243.1 — Kapı ve zincirdeki yeri

```mermaid
flowchart LR
    A[FunctionInvokingChatClient] --> E[ExplainedFailure]
    E --> B[BudgetGating — yeni]
    B --> Z[Authorizing]
    Z --> V[Validating] --> T[Timeout] --> P[ApprovalRequired] --> R[Truncating] --> F[tool]
    B -. tükenmiş .-> X["TraconRunBudgetExceededException → sonuç metni"]
```

- **Konum:** `ExplainedFailure`'ın hemen içinde, `Authorizing`'den önce.
  Gerekçe: bütçe okuması bellek içidir ve en ucuz denetimdir. Yetki işleyicisi
  (tüketicinin kodu, belki bir ağ çağrısı) tükenmiş bir run için koşmaz.
  `ExplainedFailure` dışta kalır, çünkü reddin mesajını modele o taşır ve çağrıyı
  kayıtta başarısız o işaretler.
- **Koşul:** `TraconRunContext.Current?.Budget is { IsExhausted: true }`. Bu
  koşul `RunBudgetChatClient`'ınkiyle **aynıdır** (bakımcı kararı). Gerekçe: tool'un
  sonucunu ancak bir model çağrısı okur, ve aynı koşul o çağrıyı reddeder.
  Fonksiyon düğümünün kuralı (K-999: yalnız maliyet ve süre) burada geçerli
  değildir, çünkü düğümün çıktısını model değil bir sonraki düğüm okur.
- **Ret:** kapı `TraconRunBudgetExceededException` atar. Mesaj yeni bir iç metinden
  gelir: `AgentRunBudget.DescribeToolStartExhaustion()` → "<aşılan sınır>. The tool
  call did not start." `ExplainedFailure` mesajı modele verir ve çağrıyı başarısız
  kaydeder. MAF'ın döngüsü sonucu modele götürmek ister. `RunBudgetChatClient`
  o çağrıyı reddeder. Run bugünkü kimlikle kapanır.
- **Bütçe yoksa** (`Budget` boş) kapı tek bir `AsyncLocal` okumasıdır; depo turu yoktur.
- **Kapı kaydı tek yerdedir.** `ToolWrapperChain.Compose` kod, çalışma alanı ve
  MCP tool'larına aynı anda ulaşır (`ToolWrapperChain.cs:9-16`).

## 243.2 — Zincirin dışındaki tool kaynakları

**Uygulamanın ilk işi bir ölçümdür.** Bir tool çağrısının geçtiği bütün yollar
listelenir ve her biri için karar yazılır (*Plandan Sapmalar*):

```bash
grep -rn "ToolWrapperChain.Compose\|: AIFunction\b\|: DelegatingAIFunction" src --include='*.cs'
```

| Kaynak | Bugünkü denetim | Bu fazda |
|---|---|---|
| Kod, çalışma alanı, MCP tool'ları | yok | kapı (§243.1) |
| Alt agent (arka plan ve `Tool` modu) | `ChildAgentInvoker:516` | değişmez |
| Skill script'i (`run_skill_script` → `SandboxedSkillScriptRunner.ExecuteAsync:343`) | yok | aynı koşul, aynı ret metni; yürütücünün girişinde. Dış sandbox ücretli olabilir |
| Skill okuma tool'ları, bellek ve todo tool'ları (MAF provider'ları) | yok | **değişmez** — ücret raporlamaz, yan etkisi Tracon'un deposundadır. Ölçüm aksini gösterirse plandan sapma olarak yazılır |
| Hosted tool (sağlayıcının bulutunda) | sonraki model çağrısı | **değişmez** — çağrı model yanıtının içinde olur; Tracon başlangıcını göremez. Site bunu sınır olarak yazar |
| İstemci tool'u (yalnız bildirim) | — | sunucuda koşmaz |

🚨 MEMORY: "Planın YAPISAL iddiasını GREP'le ölç." Kapının her kod, çalışma alanı
ve MCP tool'unu gördüğünü bir fonksiyonel test kanıtlar; yalnız `Compose` okuması
yetmez.

## 243.3 — Sınırlar ve bilinçli dışarıda bırakılanlar

- **Eşzamanlı tool çağrısı.** `AllowConcurrentToolCalls` açıkken bir turun
  tool'ları aynı anda başlar. Kapı, başlamış kardeşi durduramaz. Bu sınır bir
  testle sabitlenir ve site bunu yazar. Tavanı kesin isteyen kurulum eşzamanlı
  çağrıyı kapatır.
- **Tavanı geçiren ilk tool.** Ücret ancak tool bittikten sonra bilinir. Tavanı
  geçiren çağrı koşar; kapı ondan **sonrakini** durdurur. Bu, K-999'un
  "başlamış iş durmaz" kuralının eşidir.
- **Kiracı kotası okunmaz.** Faz 239 kararı: kota yeni bir iş biriminin (fonksiyon
  düğümü, agent adımı) başında okunur, sıcak yolda okunmaz. Tool çağrısı sıcak
  yoldur. Kota yine run kapanırken yazılır (K-990).
- **Muafiyet yok.** K-999'daki `StartsWhenBudgetIsSpent` eşi eklenmez: tool'un
  sonucunu okuyacak model çağrısı olmayacaktır (Açık Soru 1).

---

## Planlanan Public API

> Taslak imzalardır. Gerçekleşen imzalar kapanışta ayrı bir bölüme yazılır.

```csharp
// Tracon.Core — internal
internal sealed class BudgetGatingAIFunction(AIFunction inner) : DelegatingAIFunction(inner);

// Tracon.Abstractions — AgentRunBudget (internal üye)
internal string DescribeToolStartExhaustion();
```

Public yüzey değişmez. XML metinleri yeni davranışı yazar.
**Bugün eklemek bedava / sonradan eklemek kırıcı:** imza değişmez, ama
davranış değişikliği GA'dan sonra Faz 237 kuralına göre "XML'de belgelenen
davranışın değişmesi" olur, yani major. Bugün `CHANGELOG`'da adıyla anılır.

### HTTP `endpoint`'leri

Yeni uç yok. Reddedilen çağrı run olaylarında bugünkü başarısız tool olayıyla
görünür; run'ın kapanış kimliği değişmez (`run_budget_exceeded`).

### Arayüz payı

Yok.

---

## Planlanan Dosya Listesi

```
src/Tracon.Abstractions/Runs/
└── AgentRunBudget.cs                          # DescribeToolStartExhaustion (internal) + XML
src/Tracon.Core/
├── Tools/BudgetGatingAIFunction.cs            # yeni
├── Tools/ToolWrapperChain.cs                  # katman + sıra yorumu
├── Tools/TraconToolUsage.cs                   # XML: tükenmiş bütçede sonraki tool başlamaz
├── Skills/Scripts/SandboxedSkillScriptRunner.cs   # girişte aynı koşul (§243.2)
└── TraconOptions.cs                           # TraconAgentGraphOptions XML
tests/
├── Tracon.AspNetCore.FunctionalTests/ToolBudgetGateTests.cs
└── Tracon.Core.UnitTests/Tools/…              # zincir sırası
```

---

## Hata Modları ve Testler

| Ne bozulabilir | Seviye | Test (taslak ad) |
|---|---|---|
| Aynı turda ikinci ücretli tool, birinci tavanı geçirdikten sonra koşar (bugünkü davranış) | Fonksiyonel (gerçek host, `FakeModelProvider` iki tool çağrısı, `MaxTotalCost`) | `ToolBudgetGateTests` — ikinci tool'un gövdesi çağrılmaz; taban sürümde kırmızı |
| Reddedilen çağrı kayıtta başarılı görünür | Fonksiyonel | aynı sınıf — çağrı kaydı başarısız ve ret metnini taşır |
| Run başka bir kimlikle kapanır | Fonksiyonel | aynı sınıf — `Failed`, `run_budget_exceeded` |
| Token tavanı tool'u durdurmaz | Fonksiyonel | aynı sınıf — `MaxTotalTokens` satırı (varsayılan tavan 200.000 olduğu için bu yol bütçe yapılandırmayan kurulumda da işler) |
| Süre tavanı tool'u durdurmaz | Fonksiyonel (`FakeTimeProvider`) | aynı sınıf — `MaxDuration` satırı |
| MCP ya da çalışma alanı tool'u kapıyı atlar | Fonksiyonel | aynı sınıf — MCP test sunucusu ve çalışma alanı tool'u |
| Skill script'i kapıyı atlar | Fonksiyonel | aynı sınıf — script satırı |
| Bütçesiz run'da kapı bir şey değiştirir | Fonksiyonel (mevcut testler değişmeden yeşil) | tool testleri |
| Kapı yetki işleyicisinden sonra koşar | Birim (zincir sırası) | `ToolWrapperChainTests` |
| Eşzamanlı çağrıda kardeş tool durur sanılır | Fonksiyonel (bilinen sınırın sabiti) | aynı sınıf — `AllowConcurrentToolCalls = true` iki tool da başlar; site cümlesiyle eşleşir |
| Alt agent çağrısı iki kez reddedilir ya da ret metni değişir | Fonksiyonel (mevcut) | alt agent bütçe testleri değişmeden yeşil |
| Ret iptal gibi görünür | Birim | iptal edilen token `OperationCanceledException` kalır (K-737) |

Beş soru: **iptal** — kapı token'ı okumaz; iptal bugünkü yoldan geçer ·
**eşzamanlılık** — yukarıda; bütçe okuması `Interlocked`'tır · **boş/aşırı
girdi** — `MaxTotalCost = 0` "sınır yok" demektir (bugünkü anlam); kapı bunu
değiştirmez · **başka kiracı** — bütçe ağaca aittir, kiracı ayrımı gerekmez ·
**alt sistem hatası** — kapı depoya dokunmaz.

---

## Manuel Kabul Case'leri

| # | Ön koşul | Adımlar | Beklenen sonuç |
|---|---|---|---|
| 1 `🤖` | `samples/Tracon.Api`, ücret raporlayan bir tool (ses tool'u), küçük `Tracon__AgentGraph__MaxTotalCost` | Aynı turda iki seslendirme isteyen istem | Birinci çağrı koşar; ikinci çağrı ret metniyle başarısız; run `Failed`/`run_budget_exceeded`; run detayında iki tool satırı |

---

## Açık Sorular

| # | Soru | Seçenekler | Öneri |
|---|---|---|---|
| 1 | Kodda bir muafiyet bayrağı (`TraconToolRegistration`) olsun mu? | A: yok · B: `StartsWhenBudgetIsSpent` eşi | **A** — sonucu okuyacak model çağrısı yoktur; ihtiyaç ölçülürse ayrı karar |
| 2 | Ret metni modele hangi sınırın aşıldığını söylesin mi? | A: evet (`DescribeExceededLimit` metni) · B: genel cümle | **A** — bugünkü model çağrısı ve düğüm metinleriyle aynı biçim; metin tutar ya da kimlik taşımaz |

---

## Bitiş Ölçütleri (DoD)

- [x] `ToolBudgetGateTests` aynı turdaki ikinci tool'un başlamadığını üç sınır için (maliyet, token, süre) kanıtlar; test taban sürümde kırmızıdır (kayıt: komut + çıktı → *Plandan Sapmalar*; token satırının yan kanal modeli sapma 1)
- [x] Kod, çalışma alanı, MCP ve skill script'i yolları kapıdan geçer (§243.2 ölçümü *Plandan Sapmalar*'da; MCP ve çalışma alanı için kontrol testleriyle)
- [x] Reddedilen çağrı kayıtta başarısız ve ret metnini taşır; run `run_budget_exceeded` ile kapanır (akışlı run'da; akışsız run başarısız bitince hiç araç satırı yazmaz — sapma 3, aday F-689)
- [x] Eşzamanlı çağrı sınırı testle sabitlendi ve sitede yazılı
- [x] `CHANGELOG.md [Unreleased]` davranış değişikliğini adıyla yazar
- [x] Site sayfaları (Tüketici yüzeyi satırı) güncel; `npm run build` + `check-links.mjs` temiz (`npm run check` dördü birden: `kapi.py kapanis` içinde ✅)
- [x] Dört doğrulama kapısı sıfır uyarı verir (`kapi.py kapanis --taban 98702e42`: 27511 test, 39 modül, çıkış 0; ilk koşumda bir zamanlayıcı testi 30 sn'de düştü, izole geçti, tam koşum tekrarında yeşil)
- [x] `samples/Tracon.Api` ile gerçek `run` yapıldı (Manuel 1), çıktı belgeye yazıldı (*Plandan Sapmalar* sonu)
- [x] `secret` taraması boş döndü (`kapi.py tarama` ✅, `kapanis` içinde)
- [x] `🤖` case `docs/manuel-test/12-GOZLEMLENEBILIRLIK-MALIYET.md` içinde (`MT-OBS-085`) ve koşuldu
- [x] `faz-denetim` koşuldu; 🔴 bulgu kalmadı (🔴 yok; 🟡 dördü kapandı, 🟢 üçü aday)

### Doğrulama komutları

```bash
python3 scripts/kapi.py test --proje Tracon.AspNetCore.FunctionalTests --sinif ToolBudgetGateTests
grep -rn "ToolWrapperChain.Compose" src --include='*.cs'      # §243.2 kaynak listesi
```

---

## Riskler

| Risk | Önlem |
|------|-------|
| Kapı yanlış katmana girer ve bir tool kaynağını görmez (K-320 sınıfı) | §243.2 ölçümü ilk iştir; her kaynak için fonksiyonel test |
| `ExplainedFailure` dışına konan kapı ret metnini "Error: Function failed." yapar | Konum §243.1; zincir sırası birim testi |
| Ret, onay bekleyen bir tool'un onay akışını bozar | MAF onay isteğini zinciri **çağırmadan** üretir (`ApprovalRequiredAIFunction` sarmalayıcıdan okunur); kapı yalnız gerçek çağrıda, onay devamının run'ında koşar ve o run'ın bütçesini okur. Uygulama bunu ölçer: tükenmiş bütçede onay isteği hâlâ oluşuyor mu? Oluşuyorsa davranış sitede yazılır. Mevcut onay testleri değişmeden yeşil kalır |
| Tool'un ücreti bütçeye geç yazılırsa kapı tükenmeyi görmez | `NonTokenCostAccumulator.RecordIntoBudget` raporla aynı anda yazar (kanıt tablosu); test bunu sırayla kanıtlar |

---

<!-- ============================================================
     AŞAĞISI ÇALIŞIRKEN DOLAR — doküman kapanışta silinir (`faz-kapat`).
     Kalıcı kısım `faz-tamamlama` Adım 5'te yerine taşınır. Başlıkları SİLME.
     ============================================================ -->

## Plandan Sapmalar

> Çalışırken dolar. Plan ile gerçek arasındaki fark **gizlenmez**. Kapanışta
> kapanış commit mesajına taşınır. §243.2 kaynak listesi ve kararları buraya yazılır.

**Taban sürümde kırmızı kanıt** (DoD 1). Kapı kapalıyken (`ToolWrapperChain` içindeki satır devre dışı):

```bash
python3 scripts/kapi.py test --proje Tracon.AspNetCore.FunctionalTests --sinif "*ToolBudgetGateTests*"
# total: 12, failed: 6 — maliyet / token / süre satırları, kayıt+kimlik satırı, MCP satırı, çalışma alanı satırı
#   "tools.Count(Second) should be 0 but was 1 — the refused tool's body was called"
```

Kapı açıkken aynı komut yeşildir (denetimden sonra 13 test). Betik satırı için runner kapısı devre dışıyken
`SkillScriptExecutorRoutingTests.A_spent_budget_stops_a_file_script_before_the_executor_is_asked` kırmızıdır.

**§243.2 kaynak ölçümü** (`grep -rn "ToolWrapperChain.Compose\|: AIFunction\b\|: DelegatingAIFunction" src`):

| Kaynak | Karar | Kanıt |
|---|---|---|
| Kod tool'ları (`ToolRegistry`) — `GenerateImageTool`, ses tool'ları (`SpeakTool`, `TranscribeTool`, `ListVoicesTool`), `AddScopedTool` | kapı (`Compose`) | `ToolBudgetGateTests` kod tool'u satırları |
| Çalışma alanı tool'ları (`WorkspaceToolGovernance`) | kapı (`Compose`) | `A_workspace_tool_does_not_start_once_the_cost_budget_is_spent` + kontrol |
| MCP tool'ları (`McpTenantTools`; `McpElicitingTool` `Compose` öncesi sarar) | kapı (`Compose`) | `An_mcp_tool_does_not_start_once_the_cost_budget_is_spent` (gerçek MCP sunucusu, `Calls == 0`) + kontrol |
| `run_skill_script` (MAF'ın tool'u) | koşucu girişi (`ExecuteAsync`) | `A_spent_budget_stops_a_file_script_before_the_executor_is_asked` |
| Alt agent (`SubAgentToolFunction`) | değişmez — `ChildAgentInvoker` kapısı | mevcut alt agent bütçe testleri yeşil |
| `RecordedToolPlayback` | değişmez — replay gerçek tool koşturmaz | — |
| **`search_knowledge` (`VectorSearchToolFactory`) — planda yoktu** | **değişmez** — `AgentDefinitionCompiler.Memory.cs:199` onu zincire sokmadan `tools` listesine ekler; ücret raporlamaz | ölçüldü: `tools.Add(...)` doğrudan; aday F-688 |
| **`search_conversation_memory` (`OnDemand` konuşma belleği, MAF `ChatHistoryMemoryProvider`) — planda yoktu; denetim buldu** | **değişmez** — MAF'ın aracıdır, zincire girmez; tüketicinin `VectorStore`'unu ve embedding üreticisini çağırır; ücret raporlamaz | `ConversationMemoryScopeProvider.cs:66-72`; aday F-688'e eklendi; sitede sınır olarak adıyla yazılı |
| Skill okuma, dosya belleği, todo (MAF provider'ları) | değişmez (plana göre) | ücret raporlamaz, yan etkisi Tracon'un deposundadır |
| Hosted tool | değişmez (plana göre) | sağlayıcı bulutunda koşar |

Sapmalar:

1. **Token satırı "ikinci tool" değildir.** `FakeModelProvider` bir tool çağrı turuna kullanım yazamaz; ayrıca token'ı yalnız model harcar. Test, birinci tool'un ağaca `RecordUsage(1000)` yazdığı bir yan kanal modeli (K-893 emsali) kurar: tavan 500, ikinci tool başlamaz. Mekanizma aynıdır, kaynak yapaydır (denetim 🟢2: gerçek yol uçtan uca sınanmıyor).
2. **Betik testi `ToolBudgetGateTests`'te değil `SkillScriptExecutorRoutingTests`'tedir.** Betik düzeneği (yürütücü, yetki kaydı, disk becerisi) o sınıfta `private`'tır; kopyalamak yerine testi düzeneğin yanına koydum. Test `script.RunAsync` çağrısını sınar, model → onay → devam hattını değil (denetim 🟢1).
3. **Kayıt testleri akışlıdır.** Akışsız `RunAsync` run'ı bütçe reddiyle başarısız biterse hiç araç satırı yazmaz (izleyici akışsız yolda mesajları yanıtla birlikte okur). Bu mevcut bir davranıştır, bu fazın değil; kuyruk, devam ve workflow agent adımı akışsız yolu kullanır (denetim 🟡2). Sevk edilen metne "akışlı run'da" koşulu eklendi, boşluk aday F-689. `docs/hafiza/tool-onay-ve-yetkilendirme.md`'ye yazıldı.
4. **Onay satırı beklenenden farklı sonuç verdi** (Riskler, 4. satır). Ölçüm: onay isteği zincir çağrılmadan üretilir ve aynı turdaki onaysız kardeş tool'lar onayla birlikte bekler (`first == 0`). Kapı o yolda hiç koşmaz; onaylanan çağrı onay devamının yeni run'ında, o run'ın bütçesiyle koşar. `A_turn_with_a_tool_that_needs_approval_...` bunu sabitler. Mevcut onay testleri değişmeden yeşil.
5. **Örnek uygulamaya iki ek** (Manuel 1 için gerekli): `narrate` tool'u (yerel `tone` motorunun ücretini bildirir) ve aynı turda iki `narrate` isteyen `echo-narrator-1` modeli + `double-narrator` agent'ı. Örnek uygulamada ücret bildiren bir tool ve tool çağıran bir model yoktu.
6. **Zincirin XML açıklaması "Authorizing dışta" diyordu**; `ToolWrapperChain` sıra yorumu yeni sırayla düzeltildi. `TraconRunBudgetExceededException` XML'i tool eşini anlatır.
7. **"Model ret cümlesini okur" yanlıştı** (denetim 🟡1). Aynı koşul sonraki model çağrısını da reddeder; koşul yalnız büyür (token, maliyet, süre), yani cümleyi hiçbir model okumaz. Plan ve ilk metin bunu yanlış söylüyordu. Cümle olay akışına ve run kaydına gider. Sevk edilen metin düzeltildi.
8. **İptal ile harcanmış bütçe birlikteyken kapı ret verir** (denetim 🟡4). Kapı token'ı okumaz (plan); iptal edilmiş token ile harcanmış bütçe birlikte gelirse ret cümlesi döner, iptal kaybolmaz: bir sonraki model çağrısı onu görür. `A_spent_budget_answers_before_anything_reads_the_token` sırayı sabitler; harcanmamış bütçede iptal `OperationCanceledException` kalır (`A_cancellation_stays_a_cancellation_while_the_budget_is_not_spent`).
9. **Süre reddinin metni** (denetim 🟢3): `DescribeToolStartExhaustion` süre için "model turları arası kesim" cümlesini kullanmaz, yalnız sınırı adlandırır.

**Örnek uygulama koşumu** (Manuel 1; `ASPNETCORE_ENVIRONMENT=Production`, dört boş ortam değişkeni, `Tracon__AgentGraph__MaxTotalCost=0.005`):
`POST /tracon/api/agents/double-narrator/run` (96 karakter) → akışta `narrate-1` = `"Narrated 96 characters."`, `narrate-2` = `The run tree's cost budget is exhausted (0.009600/0.005000). Raise Tracon:AgentGraph:MaxTotalCost to allow more. The tool call did not start.`;
run `Failed`, `run_budget_exceeded`, `QuotaExceeded`, `nonTokenCost 0.0096`; `/tools` iki satır (ilki `succeeded=True` + `usage`, ikincisi `succeeded=False`, `error` ret cümlesi, `usage` yok); `Npgsql` satırı 0.
Karşı kontrol (tavan yok): `Completed`, iki satır da `succeeded=True` (96 ve 103 karakter), `nonTokenCost 0.0199`.

## Bu Fazda Verilen Kararlar

> Çalışırken dolar. Dört kategoriden birine giren karar (AGENTS.md)
> `docs/KARARLAR.md`'ye K-NNN ve kategori etiketiyle girer; girmeyen karar
> kapanışta commit mesajına taşınır (`faz-tamamlama` Adım 8). Plan numara rezerve
> etmez.

- **K-1089** *(public-api, geri-dönüşü-pahalı)*: tükenmiş ağaç bütçesinde tool çağrısı başlamaz (token, maliyet, süre). K-999'un tool yarısı bu kayıtla kapandı.
- Kategori dışı: kapı `ExplainedFailure` içinde, `Authorizing` önünde (plan §243.1). `search_knowledge` ve `search_conversation_memory` kapı dışında kalır (aday F-688). Skill betik kapısı koşucu girişindedir ve `BudgetGatingAIFunction.Refusal()` ile koşulu tek yerden alır. Kapı token'ı okumaz (sapma 8).

## Süreç Ölçümü

> Kapanışta doldurulur. **Tablo olarak** — onay kutusu DEĞİL: `faz-kapat`
> işaretsiz kutuda durur. `faz-kapat` bu tabloyu `docs/FAZ-DEFTERI.md`'ye taşır;
> eşik **167**'den itibaren boş bir değer hücresinde kapanışı durdurur.
> `ölçülmedi` **geçerli bir değerdir** — kapı bir sayı değil, bir **karar** arar.
> Kapı bölümün VARLIĞINI denetler, doğruluğunu denetlemez (K-766).

| Metrik | Değer |
|---|---|
| Plan revizyonu sayısı | 0 (planın metni değişmedi; 9 sapma kaydı) |
| Düzeltme turu sayısı | 1 (denetim sonrası: sevk edilen metin, iptal testi, süre cümlesi) |
| 🔴 bulgu: gerçek / gürültü / araştırılacak | 0 / 0 / 0 |
| Fazın ürettiği regresyon | 0 (mevcut onay, alt agent ve zincir testleri değişmeden yeşil) |
| Faz kapandıktan sonra bulunan kusur | ölçülmedi (kapanış anı) |

## Denetim Bulguları

> `faz-denetim` çıktısı. Her satır: bulgu · seviye (🔴/🟡/🟢) · sonuç
> (düzeltildi / gerekçelendi / F-NN olarak devredildi). 🟢 kapanışta
> `ADAYLAR.md`'ye gider.

Denetçi: `faz-denetcisi` (taze bağlam). **🔴 yok.**

| # | Bulgu | Seviye | Sonuç |
|---|---|---|---|
| 1 | Sevk edilen metin "model ret cümlesini okur" diyordu; aynı koşul sonraki model çağrısını da reddeder, cümleyi hiçbir model okumaz (dokuz yer) | 🟡 | düzeltildi — metin "sonuç cümlesi akışa ve kayda gider" der; sapma 7 |
| 2 | "Kayıt çağrıyı başarısız gösterir" koşulsuz yazılmıştı; akışsız run başarısız bitince hiç araç satırı yazmaz | 🟡 | düzeltildi — metne "akışlı run'da" koşulu, sitede açık cümle; boşluk F-689 olarak devredildi; sapma 3 |
| 3 | `search_conversation_memory` (MAF `OnDemand` bellek aracı) §243.2 tablosunda yoktu ve zincir dışıdır | 🟡 | düzeltildi — tabloya ve sitedeki sınır paragrafına adıyla yazıldı; F-688'e eklendi |
| 4 | `A_cancellation_stays_a_cancellation` bütçeyi harcamıyordu: kapı silinse de geçerdi; harcanmış bütçe + iptal birleşimi sabit değildi | 🟡 | düzeltildi — test harcanmamış bütçeyle yeniden adlandırıldı, harcanmış bütçeli eşi eklendi; sapma 8 |
| 5 | Betik yolu `script.RunAsync` ile sınanır; model → onay → devam hattı ve `script.denied` denetim kaydı yok; token testi yan kanal kullanır | 🟢 | F-690 olarak devredildi |
| 6 | Süre reddinin metni "model turları arası kesim" cümlesini taşıyordu | 🟢 | düzeltildi (küçük) — sapma 9 |
