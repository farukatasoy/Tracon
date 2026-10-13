# Faz 199 — Fonksiyon Düğümünün Token Dışı Maliyeti

> **Durum:** ✅ Tamamlandı (2026-10-14)
> **Plan onayı:** onaylandı (2026-10-13) — § Açık Sorular 1–6 önerilen seçenekle (hepsi A) bakımcı tarafından onaylandı; uygulama başladı
> **Kaynak:** [ADAYLAR.md](../../ADAYLAR.md) · **F-299**
> **Önkoşul:** İşlevsel önkoşul yok. [Faz 195](195-WORKFLOW-DUGUM-DUZENLEME-VE-AYAR.md) kapanmadan başlamaz: aynı sarmalama noktasını (`TraconWorkflowFunctionExtensions.cs:127`, `:309`) paylaşırlar. Faz 198'den bağımsızdır (§199.4)
> **Paketler:** `Tracon.Abstractions`, `.Core`, `.Workflows`, `.Voice`, `.Sql.Shared`, üç SQL sağlayıcısı, `.AspNetCore`, `.Testing.Contracts.Xunit`, `.UI` · üretilen istemciler
> **Yeni paket:** Yok · **Migration:** gerekli, üç sağlayıcı — kalem tablosu, `runs` ve `tool_invocations` sütunları; numara uygulama anında alınır
> **Public API:** büyüyor — `1.0.0` GA'dan (K-603) önce ucuz. Ölçüldü (2026-10-12): `wc -l src/*/PublicAPI.Shipped.txt` → 17 dosya, her biri yalnız `#nullable enable`
> **Tüketici yüzeyi:** site: `concepts/{runs,workflows,governance,tools}.md`, `guides/voice.md:126-131`, `ui.md` + ekran görüntüsü, `capabilities.md` · sevk edilen: `ReportUsageAsync` `<example>`'ı, `ToolCallUsage` XML'i, `src/Tracon.Workflows/README.md`, `CHANGELOG.md`
> **Manuel test alanı:** `docs/manuel-test/12-GOZLEMLENEBILIRLIK-MALIYET.md` (kota case'i `23-SAKLAMA-ARSIV-KOTA.md`)

---

> ### ⚗️ Damıtılmış kayıt
> Bu dosya fazın **planını** değil, fazın bıraktığı **kalıcı bilgiyi**
> taşır. Plan gövdesi, planlanan/gerçekleşen API, dosya listesi, risk ve
> açık soru bölümleri kapanışta düştü — **silinmedi, git geçmişindedir.**
>
> Tam metin — kopyala, çalıştır:
>
> ```bash
> git show c968fa74:docs/arsiv/fazlar/199-FONKSIYON-DUGUMU-MALIYETI.md
> ```
>
> Damıtıldı 2026-10-14 · `scripts/dokuman-bakim.py faz-damit`

---

## Amaç

Fonksiyon düğümü harcamasını bildiremez: TTS karakteri, GPU ve render saniyesi kayda, bütçeye ve kotaya girmez; tool ölçümü konsolda görünmez. Bu faz düğüme raporlama girişi verir, kalemi kalıcı yazar ve tutarı **ayrı bir token dışı toplam** olarak run'a, bütçeye, kotaya ve webhook'a katar. Birim toplanmaz; yalnız aynı para birimi toplanır.

## Bitiş Ölçütleri (DoD)

- [x] Düğüm iki kalem raporlar; `GET /api/runs/{id}/usage` ikisini döner (test 1) — `NodeUsageTests` (6), 2026-10-14 koşumu geçti
- [x] Kökün token dışı toplamı ve ağaç toplamı dört depoda aynıdır (test 8) — `NonTokenUsageContract` dört depoda + `RunStoreContract` 6 yeni case; PostgreSQL, SQL Server, SQLite ve bellek içi koşumları 0 başarısız
- [x] Kota, bütçe ve iki webhook tutarı `RunSpend.Total` ile sayar (test 6) — `NonTokenSpendTests` (9), `NonTokenSpendAgentTreeTests` (3), `WorkflowTreeSpendTests` (7)
- [x] Konsol tool satırında ve düğüm panelinde aynı ölçüm satırını gösterir (test 15) — `run-detail.test.tsx` 8/8 geçti (2026-10-14)
- [x] Dört doğrulama kapısı sıfır uyarı — `kapi.py kapanis --taban d926c508` 2026-10-14 koşumunda çıkış 0; derleme `0 Warning(s)`, `0 Error(s)`. İlk üç koşum kırmızıydı; nedenleri Denetim Bulguları ve Süreç Ölçümü'nde
- [x] `samples/Tracon.Api` ile gerçek `run` (`speak` düğümü kalem raporlar; `curl .../runs/<id>/usage`), çıktı belgeye yazıldı — § Örnek Uygulama Koşumu
- [x] `secret` taraması boş döndü — `kapi.py tarama` temiz (17 işaretli sentetik credential atlandı, gerçek bulgu yok)
- [x] Manuel kabul case'leri `12-GOZLEMLENEBILIRLIK-MALIYET.md` içine eklendi; otomatikleştirilebilenler koşuldu — MT-OBS-070…075 eklendi (index satırı 75); otomatik karşılıkları kapanış koşumunda geçti. 👤 MT-OBS-071'in el koşumu Devir Notu'nda
- [x] `faz-denetim` koşuldu; 🔴 bulgu kalmadı — bağımsız denetçi 2 🔴 ve 6 🟡 üretti; hepsi kapatıldı (Denetim Bulguları). Denetçi fix'lerden sonra yeniden koşulmadı; her 🔴 kapanış kapısıyla ve kendi testiyle doğrulandı
- [x] `docs-site/` güncellendi; `npm run check` temiz — 2026-10-14 kapanış koşumu: 0 kırık bağlantı, SEO temiz, 1084 sayfa 59.000 B gzip altında
- [x] `en.ts` ve `tr.ts` eksiksiz; bundle payı ölçüldü ve yazıldı — 11 anahtar, iki dil; konsol JS 206,8 KB gzip (bütçe 250 KB). Önceki taban 197,7 KB (Faz 194 kaydı); bu fazın payı ayrı ölçülmedi
- [x] `CHANGELOG.md` yeni üyeleri, `IRunStore` kırılmasını ve webhook `cost` anlamını anar

---

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
