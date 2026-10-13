# Faz 199 — Fonksiyon Düğümünün Token Dışı Maliyeti

> **Durum:** ✅ Tamamlandı (2026-10-14)
> **Plan onayı:** onaylandı (2026-10-13) — § Açık Sorular 1–6 önerilen seçenekle (hepsi A) bakımcı tarafından onaylandı; uygulama başladı
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
  Tracon.Workflows/ TraconWorkflow{Context,Function}Extensions · Internal/{WorkflowFunctionNodeScope,WorkflowTreeSpend (yeni),WorkflowRunner}
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

- [x] Düğüm iki kalem raporlar; `GET /api/runs/{id}/usage` ikisini döner (test 1) — `NodeUsageTests` (6), 2026-10-14 koşumu geçti
- [x] Kökün token dışı toplamı ve ağaç toplamı dört depoda aynıdır (test 8) — `NonTokenUsageContract` dört depoda + `RunStoreContract` 6 yeni case; PostgreSQL, SQL Server, SQLite ve bellek içi koşumları 0 başarısız
- [x] Kota, bütçe ve iki webhook tutarı `RunSpend.Total` ile sayar (test 6) — `NonTokenSpendTests` (9), `NonTokenSpendAgentTreeTests` (3), `WorkflowTreeSpendTests` (7)
- [x] Konsol tool satırında ve düğüm panelinde aynı ölçüm satırını gösterir (test 15) — `run-detail.test.tsx` 8/8 geçti (2026-10-14)
- [x] Dört doğrulama kapısı sıfır uyarı — `kapi.py kapanis --taban d926c508` 2026-10-14 koşumunda çıkış 0; derleme `0 Warning(s)`, `0 Error(s)`. İlk üç koşum kırmızıydı; nedenleri Denetim Bulguları ve Süreç Ölçümü'nde
- [x] `samples/Tracon.Api` ile gerçek `run` (`speak` düğümü kalem raporlar; `curl .../runs/<id>/usage`), çıktı belgeye yazıldı — § Örnek Uygulama Koşumu
- [x] `secret` taraması boş döndü — `kapi.py tarama` temiz (17 işaretli sentetik credential atlandı, gerçek bulgu yok)
- [ ] Manuel kabul case'leri `12-GOZLEMLENEBILIRLIK-MALIYET.md` içine eklendi; otomatikleştirilebilenler koşuldu — MT-OBS-070…075 eklendi (index satırı 75); otomatik karşılığı olanlar kapanış koşumunda geçti. 👤 işaretli MT-OBS-071 el koşumunu bekliyor (`manuel-test-kosumu`); kutu o koşumla işaretlenir
- [x] `faz-denetim` koşuldu; 🔴 bulgu kalmadı — bağımsız denetçi 2 🔴 ve 6 🟡 üretti; hepsi kapatıldı (Denetim Bulguları). Denetçi fix'lerden sonra yeniden koşulmadı; her 🔴 kapanış kapısıyla ve kendi testiyle doğrulandı
- [x] `docs-site/` güncellendi; `npm run check` temiz — 2026-10-14 kapanış koşumu: 0 kırık bağlantı, SEO temiz, 1084 sayfa 59.000 B gzip altında
- [x] `en.ts` ve `tr.ts` eksiksiz; bundle payı ölçüldü ve yazıldı — 11 anahtar, iki dil; konsol JS 206,8 KB gzip (bütçe 250 KB). Önceki taban 197,7 KB (Faz 194 kaydı); bu fazın payı ayrı ölçülmedi
- [x] `CHANGELOG.md` yeni üyeleri, `IRunStore` kırılmasını ve webhook `cost` anlamını anar

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

| # | Plan | Gerçekleşen | Gerekçe |
|---|---|---|---|
| 1 | §199.3: tool ölçümü `ToolInvocationTracker.cs:150`'de (çağrı kaydı yazılırken) biriktiriciye eklenir | Ölçüm **rapor anında** girer: `TraconToolUsage.Report` → `NonTokenCostAccumulator.ReportToolMeasurement(çağrıKimliği, ölçüm)`; son rapor kazanır, bütçe artışı o anda duyar. İzleyici (`ToolInvocationTracker`) biriktiriciye dokunmaz; yalnız `DrainUnfinished` artık çağrının bildirdiği ölçümü kayda taşır | Akışsız run tool kayıtlarını **son model turundan sonra** okur; kayıtta girse bütçe sonraki turu hiç kesemezdi (`NonTokenSpendTests.A_tools_amount_stops_the_agents_next_model_call_in_a_non_streaming_run` bunu kanıtlar, mutasyonla doğrulandı). Çağrı kimliğiyle anahtarlamak tool'un "toplamı sonda bildirir" sözleşmesini (son rapor kazanır) korur |
| 2 | §Planlanan Public API: `RunSpend.Total` yorumu `// null: pay yok, para birimi farklı` | Para birimleri ayrışırsa **token toplamı tek başına** döner (null değil) | `null` dönmek token maliyetini kotadan düşürürdü; özel `IRunPricingResolver` dışında bu duruma gelinmez |
| 3 | §199.1: geçersiz kalem tanımı "`Unit` boş, negatif miktar veya tutar" | Sütunların tuttuğu sınırlar da kabulde denetlenir (birim 64, para birimi 16, sağlayıcı 100, model 200, miktar ≤ 9.999.999.999, tutar ≤ 1.000.000.000, toplam ≤ 9.999.999.999) ve run başına 1.000 düğüm kalemi | Taşan değer `numeric(20,10)`/`decimal(20,10)` insert'ini düşürür; düşen insert yazıcıyı kapatır (K-974). SQL Server parametresi `(20,10)` sabit olduğundan snapshot sütunu da `(20,10)` seçildi |
| 4 | §199.4: Faz 198 kapanmadıysa örnek `ForSynthesis`'e bağlanır | Faz 198 kapalıydı (`IVoicePricingReader.ForSynthesis` mevcut). Yerleşik `speak`/`transcribe`/`generate_image` tool'ları fiyatı oradan alır; **örnek uygulama** `UseVoice` olmadığında okuyucu kayıtlı olmadığı için fiyatı `Tracon:Pricing:Voice` bölümünden kendi okur (`samples/Tracon.Api/TonePricing.cs`) | Örnek anahtarsız çalışır; fiyat hesaplayan kod düğümündür (Açık Soru 5-A) |
| 5 | §199.2: `IRunStore` iki üye alır | Varsayılan uygulama **verilmedi**; ~14 test sahtesi güncellendi (`NoOpRunStore` dahil) | Kalemi sessizce atan depo harcamayı gizlerdi; GA öncesi kırmak ucuzdur (K-973) |
| 6 | Planda yok | Yeni sözleşme sınıfı `NonTokenUsageContract` (dört depoda bağlı) + `RunStoreContract`/`ToolInvocationContract`'a case'ler; `TenantCoverageTests` (SQL Server) iki üye aldı | Sevk edilen sözleşme yüzeyi büyüdü (`Tracon.Testing.Contracts.Xunit`, +1 tip) |
| 7 | Planda yok | `ToolInvocationTracker.DrainUnfinished` bildirilmiş ölçümü kapanan kayda taşır | İptal anında "bildirdi ama bitmedi" tool'un harcaması kayıtta kaybolurdu |
| 8 | §Dosya Listesi: `Tracon.Core/Recording/TraconRunContext`, `RunEventWriter`… | `NonTokenUsageReporter` (Core, `internal`) eklendi: doğrula → topla → yaz tek yerde | `ReportUsageAsync` Workflows'ta, mantık Core'da; üç paket aynı gövdeyi paylaşır |
| 9 | §Kanıt tablosu satır numaraları (`99fe0b13`) | Kod K-913 (F-383) ile değişmişti: agent kökü ağacı `ReadTreeConsumptionAsync` ile okuyor | Para birimi ve çocuk payı hesabı `RunSpend` üstüne yeniden kuruldu (`RunRecordingAgent.Notifications.cs`) |
| 10 | Manuel case alanı `12-…` bütçesi | 2026-08-10 inceleme turu notu `arsiv/manuel-test-kosum-2026-09/`'a taşındı (F-441 deseni) | Dizin bütçesi 396 B boştu |
| 11 | `bench/baseline.json` | `QueryRuns` 45.456 → 47.104 B (+1.648 B / 50 satır) güncellendi | `RunRecord`'a iki, okuyucuya sekiz sütun eklendi; sahibi bu faz |
| 12 | Planda yok | `WorkflowTreeSpend` (Workflows, `internal`): workflow'un kota girdisi ve `run.completed` webhook'u, satırın kendi snapshot'ı mağazada **yoksa** düğümlerin bellekteki toplamını ağaç toplamına ekler | Kalem yazımı düşünce yazıcı kapanır, satırın snapshot'ı mağazaya hiç gitmez; workflow ağaç toplamını mağazadan okuduğu için kotası tam düğümlerin harcadığı kadar eksik kalıyordu (agent yolu kendi payını bellekten sayar). Ayrı test: `NodeUsageStoreFailureTests` (önce kırmızı, düzeltmeyle yeşil) + `WorkflowTreeSpendTests` (7) · `AgentRunBudgetCostTests` (+3: taşmada tavan açılmaz). Sağlıklı run'da satır snapshot'ı tuttuğu için çift sayılmaz |
| 13 | §199.2 ve hata modu 14: veri sahibi dışa aktarımı **ve** silmesi tabloyu kapsar | Gerçekleşen yalnız silme: `run_non_token_usage`, `runs`'a `ON DELETE CASCADE` ile bağlı ve üç sağlayıcıda sınanmış. Dışa aktarım hedefleri (`DataSubjectTargetRegistry.ExportTargets`) bu tabloyu içermez | Kalemde mesaj metni yok (düğüm adı, birim, miktar, tutar, para birimi, sağlayıcı, model). `tool_invocations` da dışa aktarılmaz; aynı sınıf. Karar Faz 200 açılışında (devir notu) |

## Bu Fazda Verilen Kararlar

Karar defterine giren (`docs/KARARLAR.md`):

- **K-972** *(public-api)* — Token dışı tutar AYRI toplamdır (`RunNonTokenCost`), `RunCost.Total()`'a girmez; token tutarıyla toplayan tek yer `RunSpend.Total`; aynı para biriminde toplanır, fiyatsız/başka para birimli kalem sayılır; tahmini kalem toplanır ve bayrak yakar. K-219'u daraltır (Açık Soru 1-A, 2-A, 3-A, 5-A).
- **K-973** *(public-api)* — Webhook `cost` birleşik, `nonTokenCost` pay; `IRunStore` iki üye alır; `GET /api/runs/{runId}/usage` run'ın kendi kalemleri (Açık Soru 6-A).
- **K-974** *(kalıcı-veri)* — Kalem tablosu + dört snapshot sütunu + `tool_invocations` sağlayıcı/model; kapanıştan sonra gelen kalem yalnız detaya; kabul sınırları (Açık Soru 4-A).

Faz içinde kalan yerel tercihler (`K-*` açılmadı):

- Faz 196 devri ("raporlama girişi sarmalayıcının içinde mi dışında mı, ölçerek seç"): düğüm adı sarmalayıcının **en dışında** (`scope(retry(timeout(handler)))`) bir `AsyncLocal`'a yazılır; değer zaman aşımından sonra biten gövdeye de akar. Kapanıştan sonra gelen kalem yalnız detaya yazılır ve `Information` olarak loglanır (K-806'nın "aynı satıra ekle"si düğümde yoktur: düğüm kalemi zaten kendi satırıdır).
- Faz 203 devri (diyalog): diyalog düğümü sonucu **tek ses sonucu** gibi raporlanır; `DialogueModelId` boşken fiyat modelsiz aranır ve model anahtarlı fiyat bulunamaz — kalem fiyatsız listelenir.
- Kalem yazma hatası yazıcıyı kapatır (mevcut `tool_invocation` deseni): satırın kendi `nonTokenCost` snapshot'ı depoya yazılmaz. Workflow kökünün kota girdisi ve webhook'u düğümlerin payını bellekten ekler (`WorkflowTreeSpend`, Sapma 12), eksik kalmaz.

## Gerçekleşen Public API

```csharp
// Tracon.Abstractions
ToolCallUsage { string? Provider; string? Model; }                        // yeni iki alan
sealed record RunNonTokenCost { decimal? Amount; string? Currency; bool IsEstimated; int UnsummedItemCount; }
sealed record NonTokenUsageRecord { Guid Id; Guid RunId; string? NodeName; ToolCallUsage Usage;
                                    DateTimeOffset CreatedAt; [JsonIgnore] string? TenantId; }
static class RunSpend { decimal? Total(RunCost?, RunNonTokenCost?); decimal? Total(RunTreeCost?, RunNonTokenCost?); }
RunRecord.NonTokenCost / TreeNonTokenCost · RunCompletion.NonTokenCost · WebhookRunSummary.NonTokenCost (decimal?)
IRunStore.RecordNonTokenUsageAsync(NonTokenUsageRecord, ct) · ListNonTokenUsageAsync(Guid runId, ct)   // GA öncesi kırıcı

// Tracon.Workflows — TraconWorkflowContextExtensions
static ValueTask<bool> ReportUsageAsync(this IWorkflowContext context, ToolCallUsage usage);

// Tracon.Testing.Contracts.Xunit
abstract class NonTokenUsageContract : TenantIsolationContract<IRunStore>   // + RunStoreContract / ToolInvocationContract case'leri
```

Plandan fark: `RunNonTokenCost.IsEstimated`/`UnsummedItemCount` planla aynı; `WebhookRunSummary.NonTokenCost`
`decimal?` (bildirilen pay). İç (`internal`): `NonTokenCostAccumulator`, `NonTokenUsageReporter`,
`WorkflowFunctionNodeScope`, `AgentRunScope.NonTokenCost`, `RunRecordingStages.NonTokenUsage` (`non_token_usage`).

HTTP: `GET /api/runs/{runId}/usage` (Reader, `runs:read`; bilinmeyen/başka kiracı/reddedilen run → aynı `404`);
`GET /api/runs`, `GET /api/runs/{runId}` `nonTokenCost`, `treeNonTokenCost` taşır.

## Dosya Listesi (gerçekleşen)

```
src/
  Tracon.Abstractions/ Tools/ToolCallUsage · Runs/{RunNonTokenCost,NonTokenUsageRecord,RunSpend} (yeni) · Runs/{RunRecord,RunSupportTypes,IRunStore} · Webhooks/WebhookEventPayload
  Tracon.Core/ Recording/{NonTokenCostAccumulator,NonTokenUsageReporter (yeni),TraconRunContext,RunEventWriter,RunRecordingStages,RunRecordingAgent.{,Lifecycle,Completion,Notifications},RunRecordingAgentDecorator,ToolInvocationTracker} · Tools/TraconToolUsage · Storage/InMemoryRunStore.{,Events,Lifecycle,Queries} · Images/GenerateImageTool · Diagnostics/{TraconDiagnostics,TraconMetrics} (yalnız XML)
  Tracon.Voice/ Tools/{SpeakTool,TranscribeTool}
  Tracon.Workflows/ Internal/{WorkflowFunctionNodeScope,WorkflowTreeSpend (yeni),WorkflowRunner} · TraconWorkflow{Context,Function}Extensions
  Tracon.Sql.Shared/ Internal/SqlQueriesBase · Stores/{SqlRunStore,RunOrdinals}
  Tracon.{PostgreSql,SqlServer,Sqlite}/ Migrations/{0058,0046,0045}_non_token_cost.sql (yeni) · Internal/*Queries
  Tracon.AspNetCore/ Endpoints/ObservabilityEndpoints
  Tracon.Testing.Contracts.Xunit/ Contracts/{NonTokenUsageContract (yeni),RunStoreContract,ToolInvocationContract}
  Tracon.UI/frontend/ components/{usage-line,non-token-usage-panel} (yeni) · screens/run-detail · lib/server-types · locales/{en,tr}/runs
  Tracon.Client/Generated · docs/openapi/tracon.json · packages/tracon-client/src/schema.ts (üretildi)
samples/Tracon.Api/{Program.cs,TonePricing.cs (yeni),appsettings.json}
samples/Tracon.Samples.FileRunStore{,.Tests}/ FileRunStore.cs (iki yeni üye; run snapshot'ı ve ağaç toplamı) · JsonFileNonTokenUsageContractTests (yeni)
tests/
  Core.UnitTests/Recording/{NonTokenCostAccumulatorTests (yeni),ToolUsageReportingTests} · Runs/RunSpendTests (yeni) · Contracts/InMemoryStoreContractTests
  Workflows.UnitTests/{TraconWorkflowContextExtensionsTests,WorkflowTreeSpendTests (yeni)}
  AspNetCore.FunctionalTests/{NodeUsageTests,NodeUsageConcurrencyTests,NodeUsageCancellationTests,NodeUsageStoreFailureTests,NodeUsageLimitsTests,NonTokenSpendTests,NonTokenSpendAgentTreeTests (yeni),RunUsageEndpointTests} (yeni) · Infrastructure/NodeUsageHost · RunResourceAuthorizationTests
  {Sqlite,PostgreSql,SqlServer}.IntegrationTests/Contracts/* (NonTokenUsageContract bağı) · DataSubjectStoreTests (cascade) · SqlServer/TenantCoverageTests
  Sql.Shared.UnitTests/Baselines/* (SQL metin tabanı)
docs-site/ (13 sayfa + üretilenler) · CHANGELOG.md · src/{Workflows,Voice,Tracon,AspNetCore}/README.md · Tracon.AgentMap.md
docs/ manuel-test/{12,00-INDEKS} (MT-OBS-070…075) · KARARLAR (K-972…974) · MIMARI · hafiza/*
bench/baseline.json
```

Testler (sınıf → kanıt): `NonTokenCostAccumulatorTests` (34: toplama kuralı, sınırlar, çağrı başına son rapor, eşzamanlılık — kilidi kaldırınca düşer) · `RunSpendTests` (9) · `NodeUsageTests` (6: iki düğüm, para birimi, yeniden deneme, geçersiz kalem, zaman aşımı sonrası, bildirmeyen run) · `NodeUsageConcurrencyTests` (2) · `NodeUsageCancellationTests` (1) · `NodeUsageStoreFailureTests` (2) · `WorkflowTreeSpendTests` (7) · `NodeUsageLimitsTests` (2) · `NonTokenSpendTests` (9: beş para yeri + bütçe, yer başına) · `NonTokenSpendAgentTreeTests` (3: alt agent'ın tool tutarı kökün kotasında ve webhook'unda) · `RunUsageEndpointTests` (2) · sözleşme: `NonTokenUsageContract` (16 × 4 depo + örnek mağaza 16; 9'u ortak izolasyon/iptal tabanından) + `RunStoreContract` 6 yeni case + `ToolInvocationContract` 1 yeni case (2 mevcut case genişledi) · konsol `run-detail.test.tsx` (+5).

## Örnek Uygulama Koşumu

`samples/Tracon.Api`, **anahtarsız** (tüm sağlayıcı anahtarları boş, yalnız echo modeli), geçici SQLite dosyasıyla (`Tracon__Sqlite__ConnectionString`) ve `Tracon:Ui:AuthToken` verilerek, **derleme çıktısı dizininden** (`artifacts/bin/Tracon.Api/release`; çalışma dizini içerik kökü olur ve `appsettings.json` oradan okunur) koşuldu. Üç senaryo, 2026-10-13:

1. **Düğüm kalemleri ve toplam.** `PUT /tracon/api/workflows/narrated-demo` `word-count → speak → render(seconds=1) → synthesize(tone)`; `POST …/run`:
   `GET /api/runs/{id}/usage` → `speak` 76 `characters` `cost 0.0076` `isEstimated true` `tone/tone-1` · `render` 1 `gpu-seconds` `cost null` `local-gpu` · `synthesize` 126 `characters` `cost 0.0126`.
   `GET /api/runs/{id}` → `status Completed`, `nonTokenCost = {amount 0.0202, currency USD, isEstimated true, unsummedItemCount 1}`, `treeNonTokenCost` aynı, `cost`/`treeCost` `null`. `GET /api/quotas/usage` → günlük sayaç `runs 1, tokens 0, cost 0.0202`.
2. **Kota.** `PUT /tracon/api/quotas {"period":"Daily","maxCost":0.03}`; `speak + synthesize` workflow'u dört kez: ilk üçü `200` (sayaç `cost 0.0441`, `tokens 0`), dördüncüsü `429 {"detail":"The daily cost quota for the whole tenant has been exceeded (0.0441/0.03)…","quotaMetric":"Cost"}`. Eski davranışta sayaç `0` kalır ve hiçbir run reddedilmezdi.
3. **Varsayılan dışı yapılandırma — bütçe.** `Tracon__AgentGraph__MaxTotalCost=0.005`; workflow `speak → summarizer(agent)`: run `Failed`, `error.type run_budget_exceeded`, `"Could not call agent 'summarizer': The run tree's cost budget is exhausted (0.005800/0.005000)…"`, `nonTokenCost.amount 0.0058`; `summarizer`'in modeline istek gitmedi.

Başlangıçta bulunan ortam tuzağı: örnek uygulama bakımcının user-secrets'ındaki PostgreSQL bağlantısını sessizce seçer (`Tracon:PostgreSql:ConnectionString` SQL Server'dan sonra, SQLite'tan önce gelir) ve eski adlı geliştirme veritabanına bağlanıp migration'ları denemeye başlar; ilk koşum bu yüzden iptal edildi (veritabanında bu faz hiçbir şey değişmedi: `agentprism.__migrations` son kayıt 41, `non_token_*` sütunu yok). Sonraki koşumlar `Tracon__PostgreSql__ConnectionString=` ile boşaltıldı.

## Süreç Ölçümü

| Metrik | Değer |
|---|---|
| Plan revizyonu sayısı | 0 — onay tek turda (2026-10-13, Açık Sorular 1–6 = A). Plandan sapma 13 satır, hepsi gerekçeli |
| Düzeltme turu sayısı | 2 — uygulama sırasında kota açığı (`c48c4220`; kırmızı test önce yazıldı). Denetim sonrası tur (`2c36dbfe`, `562287f5`, `dd5a8501`) |
| 🔴 bulgu: gerçek / gürültü / araştırılacak | Denetçi: 2 gerçek (yüzey tabanı, örnek mağaza). Kapanış kapısının bir koşumunda 5 farklı test düştü, hepsi gerçek eksik güncelleme (yüzey tabanı denetçinin 🔴 1'iyle aynı, ayrı sayılmadı). Başka bir koşumda 1 gürültü (`JobWorkerLeaseContractTests`, yük altında 30 sn; izolasyonda geçti, sonraki koşumda da geçti). Araştırılacak: 1 (`HarnessDefaultsTests` cwd; tekrarlanmadı, F-483) |
| Fazın ürettiği regresyon | Davranış: 0 — koşum 4'te 38 test koşumunun hiçbiri başarısız değil. Kapı katmanı: `QueryRuns` +1.648 B (kasıtlı, taban güncellendi). Kapanıştan önce yakalanan: 1 derleme kırığı (örnek mağaza; kapanış kapısı görmez, denetçi buldu, yayın provası sonra geçti) |
| Faz kapandıktan sonra bulunan kusur | Henüz ölçülmedi — kapanıştan sonra izlenir |

## Denetim Bulguları

**🔴 (2, ikisi gerçek, ikisi kapatıldı)**

1. **Genel yüzey tabanı bayattı.** `Tracon.Testing.Contracts.Xunit` 50'de kalmıştı, yeni sözleşme sınıfıyla 51 oldu. Taban 51'e yenilendi.
2. **Örnek `JsonFileRunStore` derlenmiyordu.** Kapanış kapısı dışında (`samples/` yalnız yayın provasında derlenir). Örneğe iki üye, run snapshot'ı ve ağaç toplamı eklendi; `NonTokenUsageContract` bağlandı (16 test geçti). Yayın provası (`kapi.py yayin --kuru`) geçti.

**🟡 (6)**

1. Site cümlesi kota ve webhook'un kaçırdığını söylüyordu; düzeltildi (`guides/observability.md`, `llms-full.txt` yeniden üretildi).
2. Bütçe sayacı `long` nano-unit'te sarıyordu; on büyük kalem tavanı açabilirdi. `AgentRunBudget` artık doyuyor (3 test, önce kırmızı).
3. 1.000 kalem sınırı kapanıştan sonra gelen kalemleri saymıyordu; sayılıyor (2 test).
4. Agent kökünün alt run tool tutarı hesabı hiç sınanmıyordu; `NonTokenSpendAgentTreeTests` (3). Hesap bilerek bozulunca 2 test kırıldı, geri alındı.
5. Store-failure testinin son assert'i boştu; run'ın son durumu (`Running`) doğrulanıyor.
6. Plandan sapma eksikti (veri sahibi dışa aktarımı). Sapma 13'e yazıldı. Kod değişmedi, karar devirde.

**🟢 → aday listesi**

- F-482 (kapanıştan sonra gelen tool harcamasının kotaya girmemesi), F-483 (`HarnessDefaultsTests` cwd), F-484 (tool ölçümünün alan sınırları `Report`'ta denetlenmiyor) `ADAYLAR.md`'ye eklendi.
- Devirde kalan 🟢: `ImageEndpoints` operatör yanıtı yeni alanları doldurmuyor; özel `IRunPricingResolver` para birimlerini ayırırsa webhook `Cost` yalnız token olur, `NonTokenCost` dolu gelir (CHANGELOG'da nitelendi, kod değişmedi); konsol panelinin ekran görüntüsü yenilenmedi.

**Denetçi yeniden koşulmadı.** Fix'ler kendi testleri ve kapanış kapısıyla doğrulandı.

## Sonraki Faza Devir Notu

**Faz 200 açılışından önce karar gerekenler**

- **Veri sahibi dışa aktarımı (Sapma 13):** `run_non_token_usage` ve `tool_invocations` dışa aktarılmaz; silinir. Karar ya da ayrı aday. `ADAYLAR.md` 80.000 B sınırında olduğu için satır eklenmedi: önce yer açılmalı.
- **F-407 (bütçe tükenince düğüm başlamasın):** kapsam dışı bırakılmıştı; ön koşulu (maliyet kaydı) bu faz sağladı. Artık planlanabilir.
- **F-482:** kapanıştan sonra gelen tool ölçümü kota ve webhook'ta yok (K-974 sınırı). Karar: belgele ya da farkı kotaya yaz.

**Bu fazdan devralınanlar**

- Workflow kökünün kota ve webhook'u, satırın kendi snapshot'ı depoda yoksa düğüm payını bellekten ekler (`WorkflowTreeSpend`). Agent yolunda bu yok: bir child agent'ın yazıcısı kapanırsa onun token ve token dışı payı kökün ağaç toplamından düşer (kodda `ReadTreeConsumptionAsync` ağacı depodan okuyor). Token için bilinen sınır; token dışı için de aynı. Bu fazda kapatılmadı.
- `IRunStore` iki üye aldı (K-973, kaynak kırılması). Kendi mağazası olan tüketici `NonTokenUsageContract`'ı bağlamalı; örnek mağaza bağlı.
- Konsol panelinin ekran görüntüsü eski; `ui.md` için yeniden çekilmeli.
- Manuel case MT-OBS-071 (👤) el koşumunu bekliyor.
- `HarnessDefaultsTests` (F-483) paralel koşumda bir kez düştü; tekrarlanırsa seri koleksiyona alınır.
