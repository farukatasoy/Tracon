# Faz 151 — Harness'in Döngü Yeteneği

> **Durum:** ✅ Tamamlandı (2026-09-07)
> **Kaynak:** [ADAYLAR.md](../../ADAYLAR.md) · **F-192**
> **Önkoşul:** Yok. MAF 1.20.0 yeterlidir; yükseltme beklemez.
> **Paketler:** `AgentPrism.Abstractions`, `AgentPrism.Core`, `AgentPrism.AspNetCore`
> **Yeni paket:** Yok — `LoopAgent` ve beş evaluator `Microsoft.Agents.AI` çekirdeğindedir · **Migration:** Yok
> **Public API:** Büyüyor — `HarnessSettings`'e bir üye, bir yeni `sealed record`, bir builder metodu, bir `RunEventType` değeri. `wc -l src/*/PublicAPI.Shipped.txt` → her dosya **1 satır** (ölçüldü 2026-09-07): hiçbir yüzey sevk edilmemiştir, bugün eklemek **bedavadır**.
> **Tüketici yüzeyi:** site: `docs-site/src/content/docs/concepts/agents.md` (harness bölümü) · sevk edilen: `HarnessSettings` XML dokümanı, `AgentPrism.Core/README.md`
> **Manuel test alanı:** `docs/manuel-test/29-AGENT-DESTEGI.md`

---

> ### ⚗️ Damıtılmış kayıt
> Bu dosya fazın **planını** değil, fazın bıraktığı **kalıcı bilgiyi**
> taşır. Plan gövdesi, planlanan/gerçekleşen API, dosya listesi, risk ve
> açık soru bölümleri kapanışta düştü — **silinmedi, git geçmişindedir.**
>
> Tam metin — kopyala, çalıştır:
>
> ```bash
> git show 77cf54c4:docs/arsiv/fazlar/151-HARNESSIN-DONGU-YETENEGI.md
> ```
>
> Damıtıldı 2026-09-07 · `scripts/dokuman-bakim.py faz-damit`

---

## Amaç

Bugün AgentPrism bir agent'a "bitene kadar çalış" diyemez. Tüketici bunu kendi dış döngüsüyle yazmak zorundadır ve o döngü `run` kanıtının **dışında** kalır: kaç iterasyon koştuğu, hangi ölçütün durdurduğu ve kaç token harcandığı AgentPrism'in kayıtlarında görünmez.

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
