# Faz 199 — Fonksiyon Düğümünün Token Dışı Maliyeti

> **Durum:** 📋 Planlandı (2026-10-12)
> **Plan onayı:** onaylanmadı — plan 2026-10-12'de bakımcı talebiyle yazıldı; engelleyici kararlar § Açık Sorular'da öneriyle bekler, uygulama onaydan önce başlamaz
> **Kaynak:** [ADAYLAR.md](ADAYLAR.md) · **F-299**
> **Önkoşul:** İşlevsel önkoşul yok. [Faz 195](arsiv/fazlar/195-WORKFLOW-DUGUM-DUZENLEME-VE-AYAR.md) kapanmadan başlamaz: aynı sarmalama noktasını (`TraconWorkflowFunctionExtensions.cs:127`, `:309`) paylaşırlar. Faz 198'den bağımsızdır (§199.4)
> **Paketler:** `Tracon.Abstractions`, `.Core`, `.Workflows`, `.Voice`, `.Sql.Shared`, üç SQL sağlayıcısı, `.AspNetCore`, `.Testing.Contracts.Xunit`, `.UI` · üretilen istemciler
> **Yeni paket:** Yok · **Migration:** gerekli, üç sağlayıcı — kalem tablosu, `runs` ve `tool_invocations` sütunları; numara uygulama anında alınır
> **Public API:** büyüyor — `1.0.0` GA'dan (K-603) önce ucuz. Ölçüldü (2026-10-12): `wc -l src/*/PublicAPI.Shipped.txt` → 17 dosya, her biri yalnız `#nullable enable`
> **Tüketici yüzeyi:** site: `concepts/{runs,workflows,governance,tools}.md`, `guides/voice.md:126-131`, `ui.md` + ekran görüntüsü, `capabilities.md` · sevk edilen: `ReportUsageAsync` `<example>`'ı, `ToolCallUsage` XML'i, `src/Tracon.Workflows/README.md`, `CHANGELOG.md`
> **Manuel test alanı:** `docs/manuel-test/12-GOZLEMLENEBILIRLIK-MALIYET.md` (kota case'i `23-SAKLAMA-ARSIV-KOTA.md`)

---

## Bu Faza Başlarken

> `faz-baslangic` skill'ini uygula. Yalnız işaret edilen bölümleri oku.

1. Bu doküman
2. Kararlar:
   ```bash
   grep -n "K-151\|K-219\|K-394\|K-482\|K-483\|K-806\|K-808\|K-893" docs/KARARLAR.md
   ```
   **K-219** (ses maliyeti token maliyetiyle toplanmaz — bu faz daraltır), **K-483** (toplayan kayıt `Total()` taşır), **K-893** (karışık para biriminde toplam yok), **K-151** (ağaç toplamı ayrı alan), **K-394** (workflow kotası ağaçtan), **K-482** (`null` ≠ `0`), **K-806** (geç harcama aynı satıra), **K-808** ("bilmiyorum" görünür olur)
3. Devir notu — Faz 28 ses kotasını bilerek açık bıraktı:
   `awk '/## Sonraki Faza Devir Notu/,0' docs/arsiv/fazlar/28-SES-TOOLLARI.md`
4. Alan hafızası: `hafiza/olcum-kota-ve-secenekler.md` · `hafiza/workflows.md` · `hafiza/sql-paylasilan-sorgu-uretimi.md` (`runs` okuyucusuna sütun: dört yer) · `hafiza/frontend.md`
5. **Faz 203 devri** (`awk '/## Sonraki Faza Devir Notu/,0' docs/203-COK-KONUSMACILI-DIYALOG-SENTEZI.md`, arşivlendiyse `docs/arsiv/fazlar/` altında): diyalog düğümü `ISpeechDialogueSynthesizer.SynthesizeDialogueAsync` çağırır ve `SpeechDialogueAudio.Audio` (`CharactersBilled`, `UsageSource`, `ModelId`, `ProviderRequestId`) döner; bugün maliyeti hiçbir yere yazılmaz. 🚨 `VoiceOptions.DialogueModelId` boşken sonuç `ModelId = null` taşır: fiyat `ForSynthesis(provider, null, characters)` ile modelsiz aranır ve model anahtarlı fiyat bulunmaz. Düğüm raporu diyalog sonucunu tek ses sonucu gibi taşımalıdır.
6. **Faz 196 devri** (`awk '/## Sonraki Faza Devir Notu/,0' docs/196-UZUN-ADIM-SURE-VE-IPTAL.md`, arşivlendiyse `docs/arsiv/fazlar/` altında): fonksiyon düğümü artık `retry(timeout(handler))` sarmalayıcısında koşar (`Internal/WorkflowNodeTimeout.cs`, `TraconWorkflowFunctionExtensions.NodeWrapper`). 🚨 Süre dolunca run gövdeyi beklemeyi bırakır; token'ı dinlemeyen gövde arka planda bitirir ve geç bitişi yalnız loglanır (`ObserveLate`). Düğüm maliyeti raporu o gövdeden gelirse kapanmış run'a yazılmamalı ya da K-806 gibi geç harcama olarak aynı satıra eklenmeli — karar bu fazındır. Raporlama girişi sarmalayıcının İÇİNDE (deneme başına) mi dışında mı durur, ölçerek seç.

---

## Amaç

Fonksiyon düğümü harcamasını bildiremez: TTS karakteri, GPU ve render saniyesi kayda, bütçeye ve kotaya girmez; tool ölçümü konsolda görünmez. Bu faz düğüme raporlama girişi verir, kalemi kalıcı yazar ve tutarı **ayrı bir token dışı toplam** olarak run'a, bütçeye, kotaya ve webhook'a katar. Birim toplanmaz; yalnız aynı para birimi toplanır.

Tüketici bağlamı: çok kiracılı bir SaaS'ın podcast ve video hatları TTS, render ve GPU adımlarını fonksiyon düğümünde koşar. Kiracı kotası bunları saymalı; bugün host kendi domain'inde tutar.

- **F-299** — düğüm usage raporu, çok kalem, ayrı toplam, kota kuralı, konsol gösterimi.

### Kapsam dışı

| Kalem | Neden |
|---|---|
| Kotada birim limiti (`MaxCharacters`) | Yeni kota sütunu ister; ölçülmüş ihtiyaç yok |
| `/api/stats`, pano, `tracon.run.cost`, okuma görünümleri | Token toplamı kalır; site yazar |
| Bütçe tükenince düğümü başlatmamak | Davranış değişir (K1); ayrı aday (F-407) |
| Çöken run'ın toplamı ve kotası | Token payında da yok; Faz 197 |
| Tools ekranında maliyet (`UsageStrip`) | `ToolUsage` özeti maliyet taşımaz |

### Bugün ne çalışmıyor — doğrulanmış kanıt

Satırlar `99fe0b13` zeminindedir.

| Kanıt | Gözlem |
|---|---|
| `TraconToolUsage.cs:47-64` | `Report` `ToolUsage` biriktiricisi (`:51`) ve `FunctionInvokingChatClient.CurrentContext` çağrı kimliği (`:56`) ister; düğümde ikisi de yok → `false` |
| `WorkflowRunner.cs:559-570` · `RunRecordingAgent.Lifecycle.cs:112-113` | Kök scope ölçüm biriktiricisi kurmaz; agent scope'u kurar |
| `TraconWorkflowContextExtensions.cs:48-58` | `GetRunParameters()` düğümde `TraconRunContext.Current`'ı okur — kök scope görünür |
| `TraconWorkflowFunctionExtensions.cs:127`, `:309` | Executor düğüm adıyla kurulur; işleyici paylaşılır, ad yalnız burada bilinir |
| MAF 1.20.0, `maf-api-kesfi` | `IWorkflowContext` executor kimliği ve servis sağlayıcı taşımaz |
| `WorkflowEventMapper.cs:65-73` | Düğüm yalnız `ExecutorInvoked`/`ExecutorCompleted` üretir |
| `WorkflowRunner.cs:1466-1468`, `:1621`, `:1672-1675` | Kök satırı `cost: null` kapanır; webhook ve kota yalnız `TreeUsage`/`TreeCost` okur |
| `RunRecordingAgent.Notifications.cs:44-56`, `:201-203` | Agent kotası ve webhook'u yalnız token `cost.Total()` yazar |
| `ToolCallUsage.cs:19-45` · `ToolUsageAccumulator.cs:40` · `SqlRunStore.cs:731-738` | Sağlayıcı/model yok; çağrı başına tek ölçüm (son değer kazanır) |
| `run-detail.tsx:545-592` · `tools.tsx:245-276` | `ToolCallRow` `usage` okumaz; `UsageStrip` yalnız sayı ve süre (F-342 XML'i bu turda düzeltir) |
| `CostAddendsCrossCheckTests.cs:27-49` · `SqlQueriesBase.cs:72` | `RunCost`'un her `decimal? *Cost` alanı `CostAddends`'e (token toplamı) girmek zorunda |
| `AgentRunBudget.cs:218` · `QuotaTypes.cs:58-66` | Bütçe token'sız maliyet alır. Kota, fiyat yoksa token'a düşer; token'ı olmayan kalem düşemez |
| `VoicePricing.cs:16-19`, `:62-72` · `SpeechContracts.cs:131-140` | TTS fiyatı `ProviderName` + süreç geneli model ile bulunur (`internal`); public okuyucu yalnız varsayılan sentezleyiciyi fiyatlar |

> Kanıtlar 2026-10-12 tarihinde doğrulandı.

---

## 199.1 — Raporlama girişi

```csharp
await context.ReportUsageAsync(new ToolCallUsage { Unit = ToolUsageUnits.Characters, Quantity = chars,
    Cost = price, Currency = "USD", Provider = "elevenlabs", Model = "eleven_multilingual_v2" });
```

- `ReportUsageAsync` boş argüman dışında **atmaz**, `bool` döner (`TraconToolUsage.Report` kuralı). Run dışında `false`; kodla kurulan graftaki özel executor'da düğüm adı boş kalır.
- **Düğüm kimliği:** sarmalayıcı (`:127`, `:309`) düğüm adını **kendi gövdesinde** bir `AsyncLocal`'a yazar; değer işleyiciye akar. Yeniden deneme içeride kalır; başarısız denemenin kalemi kalır.
- **Kalem eklenir, değiştirilmez** (çok kalem). Tool kuralı (son değer kazanır) değişmez.
- Geçersiz kalem (`Unit` boş, negatif miktar veya tutar) → `false` + uyarı.
- **Yazım hemen** `scope.Writer` ile olur (`RunEventWriter.cs:385-402` deseni), iptal edilmez — harcama gerçektir. `store` hatası mevcut devre dışı yolunu izler, düğüm sürer. Yeni `RunRecordingStages` üyesi log cümlesi alır.
- Run kapandıktan sonra gelen kalem yalnız detaya yazılır (Faz 196 zaman aşımı bu yolu açar).
- `TraconToolUsage.Report` değişmez; düğümde `false` döner, XML yazar. `ToolCallUsage` `Provider`/`Model` alır; `SpeakTool.cs:152`, `TranscribeTool.cs:119`, `GenerateImageTool.cs:114` doldurur.

## 199.2 — Kalıcı kayıt (Açık Soru 4-A)

- **Kalem tablosu** (ad uygulamada kesinleşir): `run_id` (FK `runs`, `ON DELETE CASCADE`), düğüm adı, birim, miktar, maliyet, para birimi, tahmini, sağlayıcı, model, zaman. Kiracı sütunu yok; koruma run'ın kiracısıdır (`tool_invocations` deseni). `tool_invocations` sağlayıcı ve model sütunu alır.
- `runs` dört **snapshot** sütunu alır: tutar, para birimi, tahmini, toplanmayan kalem sayısı; `RunCompletion` ile yazılır. Eski satır `NULL` kalır (K-482).
- 🚨 Okuyucu dört kendi + dört ağaç sütununu **sona ekler**: üç `runColumns` + `RunColumnOrder`.
- **Snapshot run'da, detay tabloda:** detayın silinmesi geçmiş toplamı değiştirmez. Veri sahibi dışa aktarımı ve silmesi tabloyu kapsar (F-371 dersi); saklama run ile siler, yeni `RetentionTargets` sabiti yok.
- `IRunStore` iki üye alır; `RunStoreContract` dört depoda koşar.

## 199.3 — Toplam kuralı (Açık Soru 1, 2, 3, 6)

- 🚨 **Tutar `RunCost`'a girmez** (`CostAddendsCrossCheckTests`). Yeni kayıt `RunNonTokenCost`; `RunRecord.NonTokenCost` (kendi) ve `TreeNonTokenCost` (ağaç, kendini içerir — K-151).
- **Toplama kuralı tek yerde** (`RunCostCombiner.Combine` deseni): aynı para birimi toplanır; boş etiket `Tracon:Pricing:Currency` sayılır. Fiyatsız veya başka para birimli kalem toplanmaz, sayılır; hiçbiri toplanmadıysa tutar `null`. Tahmini kalem toplanır, bayrağı yakar.
- **Tek birleşim:** `RunSpend.Total`. Para kararı veren her yer yalnız bunu çağırır: agent kotası (`Notifications.cs:52`) ve webhook'u (`:202`), workflow kotası (`WorkflowRunner.cs:1673`) ve webhook'u (`:1621`). Bütçe toplanan kalemi rapor anında alır (`RecordUsage(0, cost)`).
- **Tool yolu** (Açık Soru 2-A): `ToolInvocationTracker.cs:150` ölçümü biriktiriciye de ekler.
- Fiyatsız kalem kotada sayılmaz; toplanmayan sayısı ve konsol işareti gösterir (K-808). Webhook `cost` birleşiktir, `nonTokenCost` payı verir.
- Token toplamı kalan yerler: `RunCostCombiner.cs:120-164`, `SqlRunStore.cs:1087-1129`, `InMemoryRunStore.Statistics.cs:347-348`, `run-comparison.tsx:88-100`, PostgreSQL `0001_read_views.sql:50-58`.

## 199.4 — Fiyat kaynağı ve Faz 198

Maliyeti raporlayan hesaplar (Açık Soru 5). TTS fiyatı `Tracon:Pricing:Voice:{provider}:{model}`'dedir ve işi yapan `ISpeechSynthesizer.ProviderName` ile anahtarlanır (doğrulandı). Keyed sentezleyicili düğüm bugün public fiyat okuması bulamaz; host hesaplar. Faz 198 `IVoicePricingReader.ForSynthesis(providerName, modelId, characters)` ekler (§198.5); önce kapanırsa örnek ve site onu kullanır. `Unit` serbest metindir; yeni sabit açılmaz.

## 199.5 — Konsol

- `ToolCallRow` ölçüm satırı gösterir: miktar ve birim · tutar ve para birimi · `tahmini` · tutar yoksa `fiyatsız` · sağlayıcı/model.
- Run detayında düğüm paneli: kalemler `GET /api/runs/{runId}/usage`'dan, **aynı** satır bileşeniyle. Alt satır token dışı toplamı, kökte ağaç toplamını ve toplanmayan sayısını gösterir.
- Veri yoksa panel görünmez. Metin `en.ts` + `tr.ts` (K-228). `ToolCallUsage` XML'i güncellenir.

---

## Planlanan Public API

> Taslak. Kayda alan, statik sınıfa üye: **bugün bedava / GA sonrası kırıcı**. `IRunStore` üyesi bugün de uygulayıcıyı kırar.

```csharp
// Tracon.Abstractions
ToolCallUsage { string? Provider; string? Model; }
RunNonTokenCost { decimal? Amount; string? Currency; bool IsEstimated; int UnsummedItemCount; }
NonTokenUsageRecord { Guid Id; Guid RunId; string? NodeName; ToolCallUsage Usage; DateTimeOffset CreatedAt; }
static RunSpend { decimal? Total(RunCost?, RunNonTokenCost?); decimal? Total(RunTreeCost?, RunNonTokenCost?); } // null: pay yok, para birimi farklı
// RunRecord.NonTokenCost/TreeNonTokenCost · RunCompletion.NonTokenCost · WebhookRunSummary.NonTokenCost
// IRunStore.RecordNonTokenUsageAsync(NonTokenUsageRecord, ct) · ListNonTokenUsageAsync(Guid runId, ct)

// Tracon.Workflows — TraconWorkflowContextExtensions
public static ValueTask<bool> ReportUsageAsync(this IWorkflowContext context, ToolCallUsage usage);
```

### HTTP `endpoint`'leri

| Metot | Yol | Rol | Ne yapar |
|---|---|---|---|
| `GET` | `/api/runs/{runId}/usage` | Reader | Run'ın kalemleri; bilinmeyen veya reddedilen run `404` (K-671, `/tools` deseni) |
| `GET` | `/api/runs/{runId}`, `/api/runs` | Reader | `nonTokenCost`, `treeNonTokenCost` taşır |

### Arayüz payı

Ölçülmeli. Son taban 197,7 KB gzip (Faz 194); Faz 195 kapanışı yeniden ölçer.

---

## Planlanan Dosya Listesi

```
src/
  Tracon.Abstractions/ Tools/ToolCallUsage · Runs/{RunNonTokenCost,NonTokenUsageRecord} (yeni) · Runs/{RunRecord,RunSupportTypes,IRunStore} · Webhooks/WebhookEventPayload
  Tracon.Core/ Recording/{NonTokenCostAccumulator (yeni),TraconRunContext,RunEventWriter,ToolInvocationTracker,RunRecordingAgent.*} · Storage/InMemoryRunStore* · Images/GenerateImageTool
  Tracon.Voice/ Tools/{SpeakTool,TranscribeTool}
  Tracon.Workflows/ TraconWorkflow{Context,Function}Extensions · Internal/{WorkflowFunctionNodeScope (yeni),WorkflowRunner}
  Tracon.Sql.Shared/ SqlQueriesBase · SqlRunStore · RunOrdinals · DataSubjectTargetRegistry
  Tracon.{PostgreSql,SqlServer,Sqlite}/ Migrations/<sonraki>_*.sql · Internal/*Queries
  Tracon.Testing.Contracts.Xunit/ Contracts/{RunStoreContract,ToolInvocationContract}
  Tracon.AspNetCore/ Endpoints/ObservabilityEndpoints
  Tracon.UI/frontend/ screens/run-detail · components/usage-line (yeni) · locales/{en,tr}
samples/Tracon.Api/Program.cs
```

---

## Hata Modları ve Testler

> Sınır geçen davranış birim testiyle kanıtlanamaz (`.agents/ortak/test-seviyeleri.md`).

| # | Ne bozulabilir | Seviye | Test sınıfı |
|---|---|---|---|
| 1 | Kalem yanlış düğüme veya run'a yazılır | Fonksiyonel (gerçek HTTP, iki düğüm) | `NodeUsageTests` |
| 2 | **Eşzamanlılık:** iki run kalem karıştırır; düğüm içi paralel rapor kaybolur | Fonksiyonel + Birim | `NodeUsageConcurrencyTests` |
| 3 | **İptal:** iptal edilen düğümün kalemi veya run'ın kotası düşer | Fonksiyonel | `NodeUsageCancellationTests` |
| 4 | Run dışı veya geçersiz kalem atar | Birim + Fonksiyonel | `NodeUsageTests` |
| 5 | Başarısız yeniden deneme kalemi düşürür | Fonksiyonel | `NodeUsageTests` |
| 6 | 🚨 K-483: bir para kararı yeri `Total()`'u tek başına kullanır | Fonksiyonel — dört yer + bütçe, yer başına test | `NonTokenSpendTests` |
| 7 | Para birimi karışık toplanır; fiyatsız kalem `0` olur | Birim | `NonTokenCostAccumulatorTests` |
| 8 | Ağaç toplamı depolar arasında farklı; kendi + ağaç çift sayılır | Sözleşme (dört depo) | `RunStoreContract` |
| 9 | **Başka kiracı** kalemleri okur | Sözleşme (`TenantIsolationContract`) + Fonksiyonel (`404`) | `RunUsageEndpointTests` |
| 10 | **Alt sistem hatası:** kalem yazımı düşer, düğüm veya run durur | Fonksiyonel (`store` hata verir) | `NodeUsageStoreFailureTests` |
| 11 | **Aşırı girdi:** run başına 1.000 kalem; `decimal` toplamı taşar | Fonksiyonel + Birim | `NodeUsageLimitsTests` |
| 12 | Sağlayıcı/model `tool_invocations`'ta düşer | Sözleşme | `ToolInvocationContract` |
| 13 | Run kapandıktan sonraki kalem toplamı değiştirir | Fonksiyonel | `NodeUsageTests` |
| 14 | Veri sahibi dışa aktarımı/silmesi tabloyu atlar | Fonksiyonel | mevcut veri sahibi testleri |
| 15 | Konsol ölçümü veya `tahmini`/`fiyatsız` işaretini göstermez | Vitest | `run-detail.test.tsx` |
| 16 | Üretilen istemciler yeni alanları taşımaz | Paket | NSwag/TS üretim kapıları |
| 17 | Migration eski satıra `0` yazar | Sözleşme (üç SQL sağlayıcı) | `RunStoreContract` |

---

## Manuel Kabul Case'leri

> Kapanışta eklenir; ID'ler o anda alınır. 👤: insan gerekir.

| # | Ön koşul | Adımlar | Beklenen sonuç |
|---|---|---|---|
| 1 | Örnek uygulama, `speak` düğümlü workflow | Run → `GET /tracon/api/runs/{id}/usage` | Bir `characters` kalemi: tutar, `USD`, sağlayıcı/model |
| 2 | Aynı run | Konsol → run detayı | Düğüm paneli kalemi ve toplamı gösterir — 👤 |
| 3 | Ses veya görsel sağlayıcısı açık | Tool çağıran agent run'ı → run detayı | Tool satırı ölçümü ve `tahmini` işaretini gösterir — 👤 |
| 4 | Kiracı kotası `MaxCost` küçük | Fiyatlı düğümlü iki run | İkinci run `429`; kota tutarı kalemi içerir |
| 5 | Webhook abonesi | Run tamamlanır | `run.completed` `cost` birleşik, `nonTokenCost` pay |
| 6 | Düğüm `EUR`, kurulum `USD` | Run → run detayı | Kalem listelenir; toplanmayan 1; kota artmaz |

---

## Açık Sorular

| # | Soru | Seçenekler | Öneri |
|---|---|---|---|
| 1 | Token dışı tutar run toplamına, bütçeye, kotaya girsin mi? (K-219'u daraltır) | A: evet, ayrı toplam + `RunSpend.Total` · B: hayır, yalnız kayıt ve konsol | **A** — kota TTS ve render'ı saymalı |
| 2 | Tool ölçümü de aynı kurala girsin mi? | A: evet, agent yolu dahil · B: yalnız düğüm | **A** — aynı TTS karakteri yola göre farklı sayılmamalı |
| 3 | Tahmini tutar kotaya girsin mi? | A: evet, bayrakla · B: hayır | **A** — eksik sayım tavanı aşırır (K-483'ün yönü) |
| 4 | Kalıcı biçim | A: kalem tablosu + `runs` snapshot'ı · B: düğüm çağrısı başına `tool_invocations` satırı | **A** — B tek birim taşır, Tools toplamlarını şişirir (K-806) |
| 5 | Maliyeti kim hesaplar? | A: raporlayan · B: Tracon, genel birim fiyat tablosu | **A** — fiyatı host bilir; B Faz 198 ile çakışır |
| 6 | Webhook `cost` | A: birleşik + `nonTokenCost` · B: token payı + yeni alan | **A** — alan "the total amount" der; B tüketiciye elle toplatır |

---

## Bitiş Ölçütleri (DoD)

- [ ] Düğüm iki kalem raporlar; `GET /api/runs/{id}/usage` ikisini döner (test 1)
- [ ] Kökün token dışı toplamı ve ağaç toplamı dört depoda aynıdır (test 8)
- [ ] Kota, bütçe ve iki webhook tutarı `RunSpend.Total` ile sayar (test 6)
- [ ] Konsol tool satırında ve düğüm panelinde aynı ölçüm satırını gösterir (test 15)
- [ ] Dört doğrulama kapısı sıfır uyarı — `python3 scripts/kapi.py kapanis --taban <faz öncesi commit>`
- [ ] `samples/Tracon.Api` ile gerçek `run` (`speak` düğümü kalem raporlar; `curl .../runs/<id>/usage`), çıktı belgeye yazıldı
- [ ] `secret` taraması boş döndü
- [ ] Manuel kabul case'leri `12-GOZLEMLENEBILIRLIK-MALIYET.md` içine eklendi; otomatikleştirilebilenler koşuldu
- [ ] `faz-denetim` koşuldu; 🔴 bulgu kalmadı
- [ ] `docs-site/` güncellendi; `npm run build` + `check-links.mjs` temiz
- [ ] `en.ts` ve `tr.ts` eksiksiz; bundle payı ölçüldü ve yazıldı
- [ ] `CHANGELOG.md` yeni üyeleri, `IRunStore` kırılmasını ve webhook `cost` anlamını anar

---

## Riskler

| Risk | Önlem |
|------|-------|
| 🚨 Elle tekrarlanan toplama ifadesine terim eklemek sessiz kusur sınıfı üretir (K-483, `MEMORY.md`): eksik kalan kota yeri kiracıya tavanı aşırır | Tutar `RunCost`'a girmez; tek birleşim `RunSpend.Total`; yer başına test (test 6); `grep -rn "\.Total()\|Cost ?? 0" src/`; `faz-denetim` bu satırı arar |
| Token toplamı yeri "toplam" sanılır | §199.3 listesi; XML ve site "token toplamı" der |
| Faz 196 aynı sarmalayıcıya zaman aşımı ekler | Sonra gelen faz sırayı ölçer; zaman aşımı düğüm scope'unun içinde |
| Kalem yazım hatası run yazıcısını kapatır | Mevcut tool deseni; snapshot bellekteki kalemle doğru kalır |

---

<!-- AŞAĞISI KAPANIŞTA DOLDURULUR — `faz-tamamlama` skill'i.
     Plan anında boş kalır. Başlıkları SİLME. -->

## Plandan Sapmalar

> Kapanışta doldurulur.

## Bu Fazda Verilen Kararlar

> Kapanışta doldurulur. Beklenen adaylar: ayrı toplamın kotaya girmesi, K-219'un
> daraltılması (`public-api`); kalem tablosu ve snapshot (`kalıcı-veri`).

## Gerçekleşen Public API

> Kapanışta doldurulur.

## Dosya Listesi (gerçekleşen)

> Kapanışta doldurulur.

## Süreç Ölçümü

> Kapanışta doldurulur. **Tablo olarak** — onay kutusu DEĞİL.

| Metrik | Değer |
|---|---|
| Plan revizyonu sayısı | |
| Düzeltme turu sayısı | |
| 🔴 bulgu: gerçek / gürültü / araştırılacak | |
| Fazın ürettiği regresyon | |
| Faz kapandıktan sonra bulunan kusur | |

## Denetim Bulguları

> Kapanışta doldurulur. Bulgu yoksa "🔴 ve 🟡 yok" yazılır.

## Sonraki Faza Devir Notu

> Kapanışta doldurulur.
