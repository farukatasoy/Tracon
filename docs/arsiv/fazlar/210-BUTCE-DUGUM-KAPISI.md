# Faz 210 — Bütçe Tükenince Fonksiyon Düğümü Başlamaz

> **Durum:** ✅ Tamamlandı (2026-10-14)
> **Plan onayı:** Bakımcı, 2026-10-14 — boyut kararı (yalnız maliyet ve süre) ve üç engelleyici soru sohbette cevaplandı: 🔒1 = A (`Failed` + `run_budget_exceeded`), 🔒2 = B (yalnız kodda muafiyet), 🔒3 = A (her denemeden önce). Bloklamayan 4-8 uygulamada karara bağlanır. Uygulama Faz 208 bitince başlar (bakımcı kararı; dosya örtüşmesi yok)
> **Kaynak:** [ADAYLAR.md](../../ADAYLAR.md) · **F-407**
> **Genellik:** Ağaç bütçesine maliyet (`MaxTotalCost`) ya da süre (`MaxDuration`) tavanı koyan ve ücretli fonksiyon düğümü (TTS, render, GPU) çalıştıran her host etkilenir. İş tüketici kodunda kalamaz: tavanı operatör kurulum ayarıyla koyar, düğümü başka biri yazar; denetimi her düğüme bırakmak, unutulan tek düğümde sözleşmeyi bozar. İlk ilke: bütçe model çağrısını durdurur ama ücretli düğümün başlamasına izin verir — kendi sözleşmesiyle çelişir. Faz 199 token dışı tutarı aynı bütçeye saydı (K-972); bugün yalnız fonksiyon düğümlerinden oluşan bir zincir `MaxTotalCost` ve `MaxDuration` ile **hiç** durmaz (§ Kanıt). İki pilot raporu "üretim maliyet tavanı" satırında bu kalemi adlandırır (podcast `:839`, video `:759`); ikisi aynı tüketicinin iki hattıdır, bu yüzden ikinci kanıt ilk ilkedir
> **Karşı görüş:** (1) "Düğüm kendi bütçesini okuyabilir: `TraconRunContext.Current.Budget` public'tir; iş tüketici kodunda kalsın." Cevap: tavan ağaç genelinde bir yönetişim sınırıdır; model çağrısı kapısı da her agent'a bırakılmadı. Kodla kurulan grafın özel executor'ı için okuma yolu belgelenir (§210.5). (2) "Davranış değişikliğidir (K1, Faz 199 bu yüzden ayırdı): tavan koymuş tüketicide ücretsiz son düğüm de reddedilir, run `Failed` olur." Cevap: varsayılan kurulum değişmez — `MaxTotalCost` ve `MaxDuration` varsayılanda kapalıdır, token boyutu bakımcı kararıyla kapının dışındadır (`MaxTotalTokens = 200_000` açık kalır). Değişiklik yalnız tavanı kendisi isteyen host'a ulaşır ve `CHANGELOG`'da adıyla anılır. Ücretsiz son düğüm 🔒2'nin konusudur
> **Önkoşul:** [Faz 208](208-WORKFLOW-KARAR-SOZLESMESI.md) (✅ kapandı, 2026-10-14) — ortak dosyalar (`samples/Tracon.Api/Program.cs`, `CHANGELOG.md`, `concepts/workflows.md`). Bu planın ölçümüne göre kapı fonksiyon sarmalayıcısındadır ve `WorkflowRunner`'a dokunulmaz; dokunmak gerekirse sembolle anılır (Faz 208 onu şu an değiştiriyor). [Faz 199](199-FONKSIYON-DUGUMU-MALIYETI.md) (token dışı tutar bütçeye girer) ve [Faz 205](205-GEC-BITEN-HARCAMA-KOTASI.md) (`TreeAccount`) kapalı. [Faz 209](../../209-WORKFLOW-GELEN-KUTUSU-VE-SURE.md)'dan bağımsızdır; aynı site sayfasına başka bölümde dokunur
> **Paketler:** `Tracon.Abstractions` (yalnız `internal` üye + XML), `Tracon.Core` (yalnız XML), `Tracon.Workflows`
> **Yeni paket:** Yok · **Migration:** Yok
> **Public API:** 🔒2 = A ise büyümüyor; 🔒2 = B ise `WorkflowFunctionOptions` bir özellik alır — `1.0.0` GA'dan (K-603) önce ucuz. Ölçüldü (2026-10-14): `wc -l src/*/PublicAPI.Shipped.txt` → 17 dosya, toplam 17 satır (her biri yalnız `#nullable enable`)
> **Tüketici yüzeyi:** site: `concepts/workflows.md` (`:41-47` red listesi, `:327` bütçe satırı, "Retry a node" `:344`, `:732-739` bütçe paragrafı), `guides/reliability.md:420-436`, `reference/configuration.md:94-97` (`AgentGraph:MaxDuration` satırı yok — eklenir), `guides/production.md:342-343` · sevk edilen: `AgentRunBudget` XML (`AgentRunBudget.cs:26-37`), `TraconRunBudgetExceededException` XML (`:3-26`), `TraconAgentGraphOptions.MaxTotalCost`/`MaxDuration` XML (`TraconOptions.cs:175-208`), 🔒2 = B ise `WorkflowFunctionOptions` XML, `src/Tracon.Workflows/README.md:160-166`, `CHANGELOG.md` `[Unreleased]` (`:1428` cümlesi + davranış değişikliği girdisi)
> **Manuel test alanı:** `docs/manuel-test/12-GOZLEMLENEBILIRLIK-MALIYET.md` (`MT-OBS-*`, MT-OBS-073'ün yanına)

---

> ### ⚗️ Damıtılmış kayıt
> Bu dosya fazın **planını** değil, fazın bıraktığı **kalıcı bilgiyi**
> taşır. Plan gövdesi, planlanan/gerçekleşen API, dosya listesi, risk ve
> açık soru bölümleri kapanışta düştü — **silinmedi, git geçmişindedir.**
>
> Tam metin — kopyala, çalıştır:
>
> ```bash
> git show 6073f89e:docs/arsiv/fazlar/210-BUTCE-DUGUM-KAPISI.md
> ```
>
> Damıtıldı 2026-10-14 · `scripts/dokuman-bakim.py faz-damit`

---

## Amaç

Ağaç bütçesi bugün yalnız model çağrısını ve yeni agent adımını durdurur. Maliyet ya da süre tavanı geçildikten sonra bir fonksiyon düğümü yine başlar ve ücretli işini yapar: TTS, render, GPU. Bu faz düğümün başına bir kapı koyar. Tavan geçildiyse düğüm başlamaz ve run, reddedilen bir agent adımı gibi biter.

## Bitiş Ölçütleri (DoD)

- [x] Maliyet: `speak` → `synthesize`, tavan geçilmiş — run `Failed`, `error.type=run_budget_exceeded`, `error.class=QuotaExceeded`; `synthesize`'ın handler'ı çağrılmadı (`FunctionNodeBudgetGateTests`, taban sürümde kırmızı)
- [x] Süre: aynı sonuç; mesaj `MaxDuration`'ı adlandırır
- [x] Token: yalnız token tükenmişken düğüm başlar, run `Completed`
- [x] Varsayılan kurulum: `NodeUsageTests`, `NonTokenSpendTests`, `WorkflowStepRefusalTests`, `RunDeadlineTests` değiştirilmeden yeşil
- [x] 🔒3'ün cevabı testle sabit; Açık Soru 4'ün cevabı testle sabit
- [x] (🔒2 = B) muaf düğüm koşar; `PublicAPI.Unshipped.txt` +2 satır
- [x] `CHANGELOG.md` `[Unreleased]`: `:1428` cümlesi düzeltildi, davranış değişikliği girdisi yazıldı
- [x] Dört doğrulama kapısı sıfır uyarı verir (`python3 scripts/kapi.py kapanis --taban <faz öncesi commit>`)
- [x] `samples/Tracon.Api` ile gerçek `run` yapıldı (manuel case 1-4), çıktı belgeye yazıldı
- [x] `secret` taraması boş döndü (`kapi.py tarama`)
- [x] Manuel kabul case'leri `docs/manuel-test/12-GOZLEMLENEBILIRLIK-MALIYET.md` içine eklendi; otomatikleştirilebilenler koşuldu
- [x] `faz-denetim` koşuldu; 🔴 bulgu kalmadı
- [x] `docs-site/` güncellendi; `npm run check` temiz

### Doğrulama komutları

```bash
# Kapı ve emsal testler
python3 scripts/kapi.py test --proje Tracon.AspNetCore.FunctionalTests --sinif "*FunctionNodeBudgetGateTests" "*WorkflowStepRefusalTests" "*NonTokenSpendTests" "*RunDeadlineTests"
python3 scripts/kapi.py test --proje Tracon.Workflows.UnitTests --sinif "*WorkflowNodeBudgetGateTests" "*WorkflowNodeRetryTests"

# Örnek uygulama (case 1)
curl -s -X PUT http://localhost:5081/tracon/api/workflows/budget-gate -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"kind":"Sequential","nodes":[{"name":"speak","kind":"Function","settings":{"voice":"alloy"}},{"name":"synthesize","kind":"Function","settings":{"provider":"tone","voice":"high"}}]}'
curl -s http://localhost:5081/tracon/api/runs/<id>/usage -H "Authorization: Bearer $TOKEN"
```

Ayar alanları örnek uygulamanın kendi yorumlarından alındı (`Program.cs:880`, `:1007`); `SpeakSettings.Voice` zorunludur, `SynthesizeSettings`'in iki alanı isteğe bağlıdır.

---

## Plandan Sapmalar

> Plan ile gerçek arasındaki fark **gizlenmez** — sonraki oturumun en değerli bilgisidir.

1. **Kapı iptale yalnız bütçe tükenmişse bakar** (plan §210.1 adım 1 "iptal kontrolü her zaman önce" diyordu). Bağımsız denetim (🟢3) gösterdi ki koşulsuz ön kontrol, tavan koymamış kurulumda da handler'ı iptal edilmiş token ile çağırmayı bırakıyordu; "tavan koymayan host etkilenmez" sözü tam doğru olmazdı. Kapı artık bütçede yer varsa handler'ı eskisi gibi çağırır; tükenmişse ve çağıran durdurmuşsa `Canceled`, durdurmamışsa ret. Sonuç aynı, kapsam daha dar.
2. **Kapı senkron `throw` etmez**: `ValueTask.FromCanceled` / `ValueTask.FromException` döner. Plan gövde sırasını anlatıyordu, yolu söylemiyordu; `ValueTask` dönen temsilcinin senkron atması bu repoda `async` sarmalayıcılarla tutarsızdı.
3. **Manuel case'ler `MT-OBS-078…083`, dosyanın sonunda** (plan "MT-OBS-073'ün yanına" diyordu): dosyada numara sırası var, yeni case'ler arada numara atlatırdı. Her case 073'ün konusuna atıf yapar.
4. **Test seti plandan geniş**: 14 fonksiyonel (plan 9; ek: ayarlı ailede muafiyet, ayarsız ailede iki muafiyet, gerçek kayıtla retry, olay sırası, iptal) · 9 kapı birimi · 8 `AgentRunBudgetTests` · 1 `WorkflowNodeRetryTests`. Plan satır 12'nin fonksiyonel ayağı **ölçüldü ve sınırlıdır**: kapının iptal kontrolü silinince test yeşil kalır (motor iptal edilmiş run'ın sonraki düğümünü başlatmıyor ya da runner iptal edilmiş run'ı attığına bakmadan `Canceled` yazıyor; ikisi ayrıştırılmadı). Kapının kendi sırasını yalnız birim case'i `A_cancelled_call_throws_the_cancellation_before_the_budget_refusal` sabitler.
5. **Ölçüldü (plan "ölçülmeli" demişti)**: reddedilen düğüm için de `ExecutorInvoked` yazılır. Olay sırası `SuperStepStarted → ExecutorInvoked → ExecutorFailed (düğüm adı) → ExecutorFailed (TargetInvocationException) → RunFailed`. Site bunu yazar ("invoked, then failed"); `…the_refused_node_failure_and_no_later_node_runs` sabitler.
6. **Manuel case 6 (token) gerçek bütçeyi tüketmedi**: yerel echo modeli token kullanımı bildirmez. Run `Completed` bitti ve `speak` koştu, ama token bütçesi tükenmedi; tükenmiş token bütçesinin düğümü durdurmadığını fiyatlı sahte sağlayıcılı iki otomatik test kanıtlar (`MT-OBS-083` ➜ CI). Plan bu devri öngörmüştü.
7. **Plan listesinden fazla dokunulan yerler**: `capabilities.md` satırı ve yeniden üretilen `llms-full.txt` (agent haritası boyutu değişmedi, 11 242 B), `guides/production.md` iki satır, `docs/hafiza/olcum-kota-ve-secenekler.md` işaretçisi. `docs/hafiza/workflows.md` bütçesi doluydu: K-989 ve K-987 satırları tek satıra indi, tam metin `HAFIZA-GECMISI.md`'ye taşındı.
8. **Fazdan önce iki kusur kapandı** (kullanıcı isteği): F-502 (`JsonFileRunStore` karışık para birimi) ve F-524 (`Tracon.SqlServer` yamasız `Pkcs`). Bu fazın kodundan bağımsızdır; `kapanis` kapısının F-524 kapanmadan kırmızı olması planın "Bu Faza Başlarken" 0. adımıydı.
9. **Tutuldu**: `WorkflowRunner.cs`'e dokunulmadı; kapı fonksiyon sarmalayıcısındadır. Açık Soru 4'ün kuralı `WorkflowNodeRetry.IsTransient`'ta yereldir ve `FinalFailure`'a girmez.

## Bu Fazda Verilen Kararlar

- **K-999** *(public-api)* — fonksiyon düğümü maliyet veya süre geçilmişse başlamaz; token ve run sayısı durdurmaz; ret reddedilen agent adımıyla aynı kimliği taşır; kapı her denemeden önce koşar ve reddi tekrar denenmez; muafiyet yalnız kodda (`StartsWhenBudgetIsSpent`). Bakımcı kararları: boyut (yalnız maliyet ve süre), 🔒1 = A, 🔒2 = B, 🔒3 = A (2026-10-14, plan onayı).
- Yerel tercihler (K açılmadı): kapının iptal sırası (Sapma 1), `ValueTask` dönüş biçimi (Sapma 2), Açık Soru 5 = A (agent adımı değişmez), 6 = A (katalogda görünmez), 7 = A (log seviyesi `Error`), 8 = A (mesajda yalnız maliyet önce).

## Örnek Uygulama Koşumu

`samples/Tracon.Api`, anahtarsız, yalıtılmış SQLite (`Tracon__PostgreSql__ConnectionString=` boş; koşum boyunca `Npgsql` log satırı **0**), derleme çıktısından, `Tracon:Ui:AuthToken` verilerek. 2026-10-14:

| Case | Ayar · workflow | Sonuç |
|---|---|---|
| 1 (`MT-OBS-078`) | `MaxTotalCost=0.005` · `speak → synthesize` | `Failed`, `run_budget_exceeded`, `QuotaExceeded`; `Could not start function node 'synthesize': The run tree's cost budget is exhausted (0.009600/0.005000). Raise Tracon:AgentGraph:MaxTotalCost to allow more. A node that is already running is not stopped.` `usage` yalnız `speak` (96 `characters`, `0.0096`) |
| 2 (`MT-OBS-079`) | aynı ayar · `word-count → speak` | `Completed`, `nonTokenCost.amount 0.0114` (tavanın üstünde, kesilmedi) |
| 3 (`MT-OBS-080`) | `MaxDuration=00:00:02` · `render(seconds=3) → synthesize` | `render` tamamlandı (`gpu-seconds`), `synthesize` reddedildi: `time budget is exhausted (00:00:03.1346830/00:00:02). Raise Tracon:AgentGraph:MaxDuration …` |
| 4 (`MT-OBS-081`) | varsayılan · `speak → synthesize` | `Completed`, iki kalem, `nonTokenCost 0.0223` |
| 5 (`MT-OBS-082`) | `MaxTotalCost=0.005` · `speak → word-count` · ve `speak → word-count → synthesize` | ilki `Completed` (muaf düğüm koştu), ikincisi `Failed` (`synthesize` reddedildi) |
| 6 (`MT-OBS-083`) | `MaxTotalTokens=1` · `support → speak` | `Completed`, `speak` koştu. Echo modeli kullanım bildirmediği için token bütçesi gerçekte tükenmedi (Sapma 6) |

## Süreç Ölçümü

| Metrik | Değer |
|---|---|
| Plan revizyonu sayısı | 0 (onaydan sonra plan değişmedi; sapmalar yukarıda) |
| Düzeltme turu sayısı | 1 (denetim sonrası: iki test, iptal sırasının daraltılması, altı metin düzeltmesi) |
| 🔴 bulgu: gerçek / gürültü / araştırılacak | 0 / 0 / 0 (🟡 3 hepsi gerçek ve kapandı; 🟢 9: 7'si düzeltildi, 2'si aday oldu) |
| Fazın ürettiği regresyon | 0 (`WorkflowStepRefusalTests`, `NonTokenSpendTests`, `RunDeadlineTests`, `NodeUsageTests`, `NodeUsageStoreFailureTests` değiştirilmeden yeşil; dört kapı sonucu DoD'da) |
| Faz kapandıktan sonra bulunan kusur | 0 |

## Denetim Bulguları

Bağımsız `faz-denetcisi`, `git diff b1d4879f...720e23f7`, 2026-10-14. **🔴 yok.**

| # | Bulgu | Seviye | Sonuç |
|---|---|---|---|
| 1 | Plan satır 12'nin fonksiyonel ayağı (run `Canceled` kalır) yok | 🟡 gerçek | Düzeltildi: `A_run_cancelled_after_its_budget_was_spent_ends_Canceled_not_refused`. Ölçüm: kapı sırasını sabitlemez (Sapma 4); sırayı birim case'i sabitler |
| 2 | `StartsWhenBudgetIsSpent` ayarlı (`TSettings`) ailede test edilmiyor | 🟡 gerçek | Düzeltildi: `A_function_with_settings_can_start_when_the_budget_is_spent` |
| 3 | Altı manuel case olmayan karara (`K-999`) atıf veriyor | 🟡 gerçek | Düzeltildi: kapanışta K-999 açıldı |
| 4 | `WorkflowFunctionNodeScope` XML'i ve `hafiza/workflows.md` eski sarmalayıcı sırasını yazıyor | 🟢 | Düzeltildi |
| 5 | Retry sonrası reddedilen düğümde ilk denemenin hatası kayıttan düşer; retry başarısız denemeyi loglamaz | 🟢 | Aday: F-527 |
| 6 | İptal ön kontrolü tavan koymayan kurulumu da etkiliyordu | 🟢 | Düzeltildi: kapı iptale yalnız bütçe tükenmişse bakar (Sapma 1) |
| 7 | Süre cümlesinin model-çağrısı/çocuk run metni sabitlenmemişti | 🟢 | Düzeltildi: `The_time_messages_of_the_model_call_and_the_child_run_are_unchanged_by_the_node_start_text` |
| 8 | CHANGELOG "(the first entry of this section)" kırılgan | 🟢 | Düzeltildi: girdi adıyla anılıyor |
| 9 | `concepts/workflows.md` workflow bütçe paragrafı fonksiyon düğümünü anmıyor | 🟢 | Düzeltildi |
| 10 | `manuel-test/00-INDEKS.md` durum sütunu; `MT-OBS-082` küçük harfli mesaj | 🟢 | Düzeltildi |
| 11 | `TraconOptions` XML cümlesi bozuk | 🟢 | Düzeltildi |
| 12 | Token testi bütçenin gerçekten tükendiğini iddia etmiyor | 🟢 | Düzeltildi: `TreeUsage.TotalTokens > 1` |

## Sonraki Faza Devir Notu

Sıradaki faz: [Faz 209](../../209-WORKFLOW-GELEN-KUTUSU-VE-SURE.md) (planlı; bu fazla dosya örtüşmesi: aynı site sayfasının başka bölümü, `CHANGELOG`).

**Devralınan sözleşmeler**

- Fonksiyon düğümü sarmalayıcısı `scope(retry(gate(timeout(handler))))`'dur. Kapı bütçeyi `TraconRunContext.Current?.Budget`'tan **okur**, hiçbir `AsyncLocal` yazmaz. Yeni bir sarmalayıcı katmanı ekleyen, sırayı `NodeWrapper` XML'inde ve `hafiza/workflows.md`'de günceller.
- `TraconRunBudgetExceededException` artık üç yerden çıkar: model çağrısı kapısı, agent adımı rezervasyonu ve fonksiyon düğümü kapısı. Üçü de `run_budget_exceeded`/`QuotaExceeded`; `WorkflowNodeRetry` hiçbirini tekrar denemez.
- `AgentRunBudget`'ın cost/time cümleleri `DescribeCostLimit`/`DescribeTimeLimit`'te tektir; model-çağrısı ve çocuk-run metinleri bir testle sabit (`AgentRunBudgetTests`).

**Bilinen tuzaklar**

- 🚨 `TraconTestHost.StartAsync` `configureServices`'i `AddTracon()`'dan **önce** çağırır: `services.ThrowFrom<IRunStore>(…)` orada `Sequence contains no matching element` atar. Hata enjeksiyonu `configureTracon` içinde `builder.Services` üzerinden yazılır (`NodeUsageStoreFailureTests` emsali). Kayıp bir kalem yazımı run satırını `Running` bırakır; kanıt akış sonu ve düğüm sayacıdır, satır durumu değil.
- 🚨 Fonksiyonel test dosyasına `using Microsoft.Agents.AI.Workflows;` ekleme: `NodeUsageHost.Workflow` sabiti ile MAF'ın `Workflow` tipi çakışır (`CS0229`). `IWorkflowContext`'i tam adıyla yaz.
- Reddedilen düğümün `ExecutorFailed` yükünde mesaj JSON'da `\u0027` ile kaçar (`'` → `\u0027`): akış metni üzerine iddia kurarken `Could not start function node` gibi kesiti ara, tırnaklı adı değil.
- Elle koşumda tavan, yerel `tone` motorunun fiyatıyla (`Tracon:Pricing:Voice:tone`, karakter başına `0.0001`) ölçülür: `0.005` tavanı, 50 karakterden uzun bir mesajı okuyan `speak` ile geçilir.

**Yarım kalan işler**

- **Site yayını (`faz-tamamlama` Adım 10) koşulmadı**: `./scripts/site-deploy.sh` dış dünyaya yayın yapar; kullanıcı onayı bekler.
- `MT-OBS-083` yerelde gerçek bir token tüketimi göstermiyor (echo modeli); otomatik testler kanıt.
- Aday olarak açılanlar: F-525 (soğuk restore/advisory taraması), F-526 (sözleşme → örnek depo hatırlatması), F-527 (retry sonrası ret kaydı), F-528 (`ChildDeadline`/`WaitTimeout` satırları), F-529 (kota ile düğüm durdurma), F-530 (tool çağrısı başlamadan ret), F-531 (checkpoint devamında bütçe).
