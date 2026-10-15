# Faz 204 — Kesilen Model Çıktısı ve Anthropic Güncel İstek Ayarları

> **Durum:** ✅ Tamamlandı (2026-10-13)
> **Plan onayı:** Bakımcı, 2026-10-13 (Açık Sorular'ın önerileri kabul edildi: 1-A, 2-A, 3-A, 4-B, 5-B)
> **Kaynak:** [ADAYLAR.md](../../ADAYLAR.md) · **F-320**, **F-346**
> **Genellik:** Yapılandırılmış çıktı üreten her agent kesilmeye açıktır ve kesilen JSON bugün her sağlayıcıda yanlış sınıflanır (F-320, ilk ilke). Anthropic ayarları sağlayıcı eşitliğidir: güncel modeller `budget_tokens` ve örneklemeyi `400` ile reddeder (F-346, ekosistem). İkisi tek tüketiciye bağlı değildir
> **Karşı görüş:** F-320 için "doğrulama açıkken kesik JSON zaten reddedilir" — plan değeri sınıflama, onarımı atlama ve uyarı olarak sınırlar. F-346 için "tüketici ayarları boş bırakabilir" — plan yalnız Tracon dışından verilemeyen ayarları (önbellek yerleşimi ve TTL, `effort`, adaptive) ekler
> **Önkoşul:** Yok. F-346, F-320'nin derleme uyarısı yüzeyini kullanır; ikisi aynı faza bu yüzden girer
> **Paketler:** `Tracon.Abstractions`, `.Core`, `.Anthropic`, `.AspNetCore`, `.Testing.Contracts.Xunit` · üretilen istemciler
> **Yeni paket:** Yok · **Migration:** Yok
> **Public API:** büyüyor — `1.0.0` GA'dan (K-603) önce ucuz. Ölçüldü (2026-10-13): `wc -l src/*/PublicAPI.Shipped.txt` → 17 dosya, toplam 17 satır
> **Tüketici yüzeyi:** site: `guides/model-providers.md` (§ Anthropic), `concepts/runs.md` (hata sınıfları), `guides/structured-output.md`, `reference/configuration.md`, `capabilities.md` · sevk edilen: `AnthropicProviderNames` XML'i ve `<example>`, `src/Tracon.Anthropic/README.md`, `CHANGELOG.md`
> **Manuel test alanı:** `docs/manuel-test/06-SAGLAYICI-DIGER.md`, `22-GUARDRAIL-VE-YAPISAL-CIKTI.md`

---

> ### ⚗️ Damıtılmış kayıt
> Bu dosya fazın **planını** değil, fazın bıraktığı **kalıcı bilgiyi**
> taşır. Plan gövdesi, planlanan/gerçekleşen API, dosya listesi, risk ve
> açık soru bölümleri kapanışta düştü — **silinmedi, git geçmişindedir.**
>
> Tam metin — kopyala, çalıştır:
>
> ```bash
> git show d9431027:docs/arsiv/fazlar/204-KESILEN-CIKTI-VE-ANTHROPIC-AYARLARI.md
> ```
>
> Damıtıldı 2026-10-13 · `scripts/dokuman-bakim.py faz-damit`

---

## Amaç

Uzun yapılandırılmış çıktı üreten bir agent bugün iki yerde sessiz kalır: model çıktı sınırına çarpınca kesik yanıt ya "başarılı" döner ya da "geçersiz JSON" diye reddedilir ve onarım aynı sınıra yeniden çarpar; Anthropic'in güncel ayarları (`effort`, 1 saatlik önbellek, adaptive thinking) Tracon'dan verilemez.

## Bitiş Ölçütleri (DoD)

- [x] Yapılandırılmış yanıtta `Length` → `Failed` + `OutputTruncated`; onarım koşmaz; düz metinde olay (test 1-6)
- [x] Doğrulama raporu kesilme ve örnekleme uyarılarını taşır (test 7, 10)
- [x] Dört Anthropic ayarı gövdeye doğru yazılır; bilinmeyen anahtar hata (test 8, 9, 11)
- [x] Dört doğrulama kapısı sıfır uyarı verir — `python3 scripts/kapi.py kapanis --taban 126e6c61` birleştirmeden sonra `main`'de (`85f60d91`, Faz 201 + Faz 204 birlikte) çıkış 0: build 86,7 sn · test 441,0 sn (38 koşum, 20 241 geçti, 19 atlandı; E2E UI'lı derlemeyle yeşil, SQL Server yeşil) · pack · format · site `npm run check`. Aynı ağaçta `yayin --kuru --surum 1.0.0-preview.4` çıkış 0: kırıcı liste 2 tip, 2 paket (hepsi `Unreleased`'da) · 20 paket · npm dry-run · altı paketlenmiş sample (`CustomModelProvider` 38 geçti, 1 atlandı — MT-TEST-079) · AOT ve net8.0 smoke. Eski `a7b50750` provası aynı kimlikte farklı içerik taşıdığı için `artifacts/package/yayin/1.0.0-preview.4-onceki-a7b50750`'e taşındı (silinmedi). Uygulama turunda `kapi.py ic-dongu` tam koşuma genişledi ve yeşildi (derleme 54,9 sn, test 510,4 sn). Denetim düzeltmelerinden sonra (derleme 59,3 sn, test 483,8 sn) çıkış 1: 91 E2E "UI assets are not embedded" ile düştü (F-356: genişleyen koşum UI'sız derler) ve 16 SQL Server testi yük altında zaman aşımına uğradı, 16'sı da izole geçti. Ayrıca: UI'lı `dotnet build Tracon.slnx -c Release` sonra E2E 91/91 yeşil; SQL Server derlemesi tek başına 912/913, kalan deadlock testi izole 5/5 geçti → **F-430** (faz SQL'e dokunmadı). `yayin --kuru --surum 1.0.0-preview.4` `2bde5d54` üzerinde çıkış 0: 20 paket, kırıcı liste 2 tip (hepsi `Unreleased`'da), npm dry-run, AOT ve net8.0 smoke. İlk deneme uygulama turunun prova çıktısına (`artifacts/package/yayin/1.0.0-preview.4`, aynı kimlik farklı içerik) çarptı; o dizin silinip yeniden koşuldu
- [x] `samples/Tracon.Api` ile gerçek Anthropic `run`'ı (case 1, 2), çıktı belgeye yazıldı
- [x] `secret` taraması boş döndü (`kapi.py tarama`)
- [x] Manuel kabul case'leri iki alan dosyasına eklendi (`06`, `22`; sayılar `00-INDEKS`'te); beşi de örnek uygulamada koşuldu
- [x] `faz-denetim` koşuldu (bağımsız denetçi); 🔴 yok, üç 🟡 ve iki 🟢 kapandı, bir 🟢 adaya (§ Denetim Bulguları)
- [x] `docs-site/` güncellendi; `npm run check` (build + content + links + weight) temiz
- [x] OpenAPI ve iki istemci tazelendi (yeni enum üyesi, `supportsSampling`)

### Doğrulama komutları

`samples/Tracon.Api`, gerçek Anthropic anahtarıyla (user-secrets), Development,
bellek içi depo (`--Tracon:PostgreSql:ConnectionString=` ve
`--Tracon:Sqlite:ConnectionString=` boş; paralel fazın veritabanından yalıtım),
`127.0.0.1:5281`, 2026-10-13. Çıktılar gerçektir; komutlar
`docs/manuel-test/06-SAGLAYICI-DIGER.md` MT-PROV-038/043 ve
`22-GUARDRAIL-VE-YAPISAL-CIKTI.md` MT-GUARD-110..112'dedir.

| Case | Çıktı |
|---|---|
| 1 · `claude-haiku-5-5`, `JsonSchema`, `effort=high`, `thinking.mode=adaptive`, `promptCaching` + `ttl=1h` + `placement=system`, iki farklı mesaj | İki run `Completed`, yanıt `{"title": "A New Library Opened"}` / `{"title": "A team won the cup."}` (gerçek API `effort` ve `adaptive`'i kabul etti). 1. run `usage.cachedInputTokens: 0`, `additionalCounts.CacheCreationInputTokens: 1537`; 2. run `cachedInputTokens: 1537` — mesaj farklıyken talimat öneki önbellekten okundu (`system` yerleşimi). Gövdenin kendisi (`output_config.effort` + `format`, `system[-1].cache_control.ttl = "1h"`) `AnthropicRequestSettingsEndpointTests`'te yerel sahte sunucuda yakalanır |
| 2 · `claude-haiku-4-5-20251001`, `JsonSchema`, `maxOutputTokens: 200`, `StructuredResponse.Enabled` + `MaxRepairAttempts: 2` | `HTTP 502`, `detail: "The 'anthropic' model stopped at the output token limit of 200 before the structured response was complete. …"`; run `status: Failed`, `error.type: output_truncated`, `error.class: OutputTruncated`, `usage: {inputTokens: 166, outputTokens: 200}`; olaylar `RunStarted`, `StructuredResponseRejected` (`repairSkippedTruncated: true`), `RunFailed` — `StructuredResponseRepairAttempted` yok |
| 3 · aynı model, düz metin, `maxOutputTokens: 50` | `HTTP 200`, yanıt `# How a Library Works … At its core, a library operates on the principle` (yarım); run `Completed`, `error: null`, `usage.outputTokens: 50`; olaylarda `customType: tracon.output.truncated` |
| 4 · `MaxOutputTokens` boş `JsonSchema` → `POST /api/agents/validate` | `HTTP 200`, `valid: true`, tek mesaj `{"severity":"Warning","code":"output_truncation_risk","message":"MaxOutputTokens is not set, so the 'anthropic' provider sends its default output limit of 4096 tokens; the catalog states that model 'claude-haiku-4-5-20251001' can produce up to 64000. The agent asks for a structured response, and one cut off at the limit fails the run (output_truncated). Set ModelBinding.MaxOutputTokens.","path":"model.maxOutputTokens"}`; `maxOutputTokens: 64000` ile `messages: []` |
| 5 · katalogda `claude-haiku-5-5` `supportsSampling: false`, `temperature: 0.2` | `GET /api/models` → `claude-haiku-5-5 supportsSampling: false`, `claude-haiku-4-5-20251001 supportsSampling: null`; `/validate` → `valid: false`, `sampling_unsupported` at `model.temperature` ("… reject sampling parameters: anthropic/claude-haiku-5-5. Remove ModelBinding.Temperature …"); `POST /api/agents` → `HTTP 400` `Definition invalid` |
| Yanlış ayar değerleri (`/validate`) | `anthropic.effort: extreme` → `invalid_setting` "'anthropic.effort' has an unrecognized value: 'extreme'. Valid values: low, medium, high, xhigh, max." · `promptCaching.ttl` tek başına → "… has no effect unless 'anthropic.promptCaching' is true …" · `adaptive` + `budgetTokens` → "'anthropic.thinking.mode' is 'adaptive', which takes no budget …" |

---

## Plandan Sapmalar

Numara `#N` ile verilir; sonraki fazlar bu numarayla atıf yapabilir.

| # | Plan | Gerçek | Neden |
|---|---|---|---|
| 1 | Uyarı "sağlayıcının varsayılanı" ile karşılaştırır; varsayılanın Core'a nasıl geleceği yazılmamış | Yeni public `ModelOutputLimit` kaydı: sağlayıcının **bağlamaya göre kurduğu** ham istemci `GetService(typeof(ModelOutputLimit))` ile varsayılan sınırı ve yüksek effort'u söyler; doğrulayıcı tam boru hattından okur. Anthropic istemcisi artık ayar olmasa da `AnthropicProviderSettingsChatClient` ile sarılır (ayarsız yolda kopya yok) | Core sağlayıcı anahtarını (`anthropic.effort`) bilmez (yerleşik emsal yok). Değerlendirilen iki seçenek: `IModelProvider` için isteğe bağlı arayüz + `ModelProviderDescriptor` alanı (bağlamaya göre değil; doğrulayıcıya kurucu/registry tesisatı, Faz 201 ile çakışan dosyalar) ve Core'un ayar adı okuması. MEAI'nin `ChatClientMetadata` deyimi en dar yüzeydi. **Bakımcı onayı bekler** (K-922 taslağı) |
| 2 | Açık Soru 5-B "yüksek effort'ta düşük sınır uyarısı" | Eşik sabiti yok: uyarı yalnız `MaxOutputTokens` boşken, varsayılan katalogdaki `MaxOutputTokens`'tan küçükse ve (yapılandırılmış biçim **ya da** sağlayıcının `HighReasoningEffort`'u: `high`/`xhigh`/`max`) varsa çıkar. Katalog sınırı bilinmiyorsa uyarı yok | Sabit eşik model listesi gibi bayatlar (K-032); açık sınırlı bağlamada uyarı gürültü olurdu |
| 3 | `SupportsSampling` "katalog `false` diyorsa" | `Fallbacks` bağlantılarının modeli de denetlenir | `FallbackChatClient` bağlantıya aynı `ChatOptions`'ı (sıcaklık dahil) gönderir; bağlantı aynı `400`'ü alır |
| 4 | Plan kullanımı söylemiyor | **Kesilen ve filtrelenen yanıtın token'ı run'a katlanır** (akışsız ve akışlı). Ölçüldü: kaydedici istisnayla biten run'a usage yazmaz — akışlı yolda geçen usage güncellemelerini de düşürür. Yan kanal çağrısı akışsızda kendi modeline katlanır, akışlıda katlanmaz (`SideChannelUsageTrackingChatClient` `finally`'de sayar) | Kesilen çağrı tam bir çıktı sınırı harcar; tavanı olan kiracı onu ücretsiz tekrarlayabilirdi (K-483 sınıfı). İçerik filtresi aynı yolun önceden var olan kusuruydu; aynı satırla kapandı (`OutputTruncationTests.A_filtered_empty_response_still_records_the_tokens_it_spent`) |
| 5 | Plan yok | `ModelProviderSettings.TypeMismatch` artık `ProviderSettingsValidationException` (ayrı commit, `kusur-giderme`) | Ölçüldü: yanlış JSON tipli ayar registry'den geçince "The model provider request failed." oluyordu; yeni string ayarlar da aynı yolu kullanır. Sınıf taraması: dört sağlayıcının `CreateChatClient` gövdesinde başka maskelenen ayar istisnası yok (kurulum anı `ApiKey` hataları kapsam dışı) |
| 6 | Dekoratör genişler (1-A) | Ad `FinishReasonDetectingChatClient`; akışta **son** bildirilen bitiş sebebi okunur (eskiden ilk) | Ölçüldü (MEAI 10.9.0 `FunctionInvokingChatClient` decompile): akışlı döngü ara turun güncellemelerini kendi sebebiyle verir, sonra tool sonuçlarını, sonra sonraki turu; akışsız döngü son turun yanıt nesnesini döner. İçerik filtresi için sonuç değişmez: araya tool turu girmişse `sawContent` zaten doğrudur |
| 7 | Düz metin kesilmesi olay yazar | Olay run olay akışına (`GET /api/runs/{id}/events`) yazılır; **canlı SSE akışına gitmez** | Ölçüldü: `update` çerçeveleri `finishReason` taşımaz; canlı iletim kota bildirimi gibi ayrı bir SSE sözleşmesi kararıdır → **F-427** |
| 8 | 204.2 yerleşim | `placement=system` talimat boşsa üst düzey kırılma noktasına düşer; `ttl`/`placement` `promptCaching=true` olmadan hatadır; talimat bloğu çağıranın sistem mesajlarından **sonra** eklenir (bugünkü sıra) | Önbellek istendi; sessizce kapatmak yerine çalışan yerleşim. Ölçüldü: adaptör `Instructions`'ı cache kontrolsüz bir `TextBlockParam` yapar, yalnız içerik `AdditionalProperties`'ten okur |
| 9 | "ölçülmeli: `OutputConfig.Effort` ve `Format` birbirini ezer mi" | Ezmez. Ölçüldü (Anthropic 12.39.0 decompile + ham gövde testi): adaptör `OutputConfig?.Format == null` ise mevcut `OutputConfig`'i `with { Format }` ile kopyalar | `AnthropicRequestSettingsTests.Effort_and_a_structured_response_share_output_config_without_overwriting_each_other` ve uçtan uca `AnthropicRequestSettingsEndpointTests` |
| 10 | Effort tipi "ölçülmeli" | `Anthropic.Models.Messages.Effort` (`Low`, `Medium`, `High`, `Xhigh`, `Max`) → tel değeri SDK'nın kendi `ApiEnum<string, Effort>.Raw()` eşlemesiyle (`low` … `max`); TTL için `Ttl` (`5m`, `1h`) | Kodda değer listesi yok |
| 11 | Plan yok | `ModelBinding.ReasoningEffort` Anthropic adaptöründe `thinking.budget_tokens`'a eşlenir (Low 1024 … ExtraHigh 32768; None → disabled) ve güncel modeller onu `400` ile reddeder; raw `thinking` verilince adaptör `Reasoning`'i yok sayar | Ölçüldü (decompile); site ve README'ye yazıldı, kod değişmedi. Denetim 🟢2: ham gövde testi eşlemeyi (`Low` 1024, `High` 16384, `ExtraHigh` 32768) ve `thinking` ayarının önceliğini SDK'nın gönderdiği gövdeden kanıtlar |
| 12 | Sözleşme "bilinmeyen anahtar ve değer" | `ModelProviderSettingsContract.InvalidSettingValue` (sanal, `null` → atlanır) + `A_supported_setting_with_an_invalid_value_is_rejected`; Anthropic (`effort=extreme`) ve Google (`safety` eşiği) türetir | Sevk edilen sözleşme genişledi → yayın provası zorunlu |
| 13 | Manuel case'ler | Planın beş case'i: MT-PROV-038, MT-PROV-043, MT-GUARD-110..112. `docs/manuel-test` bütçesi aşıldı: `22-…` spec'indeki iki tarihî koşum notu (2026-09-01, 2026-09-02) koşum arşivine **taşındı**. Denetim 🟡1 düzeltmeleri bütçeyi 431 B aştı: `01-…` spec'indeki dokuz 2026-09-24 sonucu (MT-PKG-132, 135, 137, 144, 148, 150–153) `arsiv/manuel-test-kosum-2026-09/01-…`'e taşındı, yerinde bağlantı kaldı; kalan boşluk 2384 B | Bütçe kalibrasyonu bakımcı kararıdır (`dokuman-bakim.py`); Faz 201'in case'leri bütçeyi aşar |
| 14 | Örnek uygulama | Katalog: `claude-haiku-4-5-20251001` `MaxOutputTokens: 64000`, `SupportsStructuredOutput: true`; yeni `claude-haiku-5-5`; `sonnet-5`/`opus-5` `MaxOutputTokens: 128000`, `SupportsSampling: false` | Case 1, 4, 5 katalog alanı ister; değerler `claude-api` başvurusundan (önbellek eşiği, örnekleme reddi) |
| 15 | Konsol ekranı yok | İki sözlük anahtarı (`dashboard.errorClass.OutputTruncated`); ekran görüntüleri **yeniden üretilmedi**, yalnız `.ui-source.sha256` damgası yenilendi | Tohum verisinde bu sınıf yok; E2E yeniden üretimi 40+ PNG'yi görünür fark olmadan değiştirip Faz 201'le ikili birleşme çatışması üretirdi. Birleşmeden sonra Faz 201'in yeniden üretimi ikisini kapsar |
| 16 | Plan yok | `QueuedRunParameterTests.A_payload_from_before_this_phase_runs_as_it_always_did` kapanış durumunu bekler (ayrı commit, `kusur-giderme`) | Kapı ikinci koşumda kırmızıydı. Temel commit'te (`126e6c61`) izole üç kez düştü, 3 sn beklemeyle geçti: test model çağrısından hemen sonra durumu bir kez okuyordu, işçi run'ı bir an sonra kapatıyor. Ürün kusuru değil, yarış. Sınıf taraması: aynı dosyanın iki case'i zaten bekliyor; `provider.Requests.Count > 0` bekleyen öbür beş testte hemen durum okuması yok |
| 17 | Plan yok | Online değerlendirme, çıktı sınırında kesilen yargıç yanıtını **yeniden denemez**: `OnlineEvalJobHandler` istisna grafiğinde `TraconOutputTruncatedException` görürse `retryable: false` yazar; madde ilk denemede `judge_failed` ile `Failed` olur (kalıcı `4xx` gibi, F-338) | Denetim 🟡2. Sonraki deneme aynı sınırı gönderir, aynı token'ı öder ve yine kesilir. Grafik okunur: başka paketin değerlendiricisi istisnayı sarabilir. MEAI 10.9.0 kalite değerlendiricileri (`ChatResponseFormat.Json`) `GetResponseAsync`'i sarmaz (decompile). Sınıf taraması aşağıda |

**#17 sınıf taraması** — yapılandırılmış yanıt isteyen modeli çağıran ve tekrar kararı veren her yol:

| Yol | Tekrar kararı | Sonuç |
|---|---|---|
| `OnlineEvalJobHandler` (`ModelRunJudge` `JsonSchema`, `EvaluatorRunJudge` MEAI `Json`) | Kendi sınıflar (`IsRetryable`) | **Düzeltildi** (#17) |
| `StructuredResponseValidatingAgent` onarımı | Kendi döngüsü | Faz kodu: kesilmede onarım yok |
| `WorkflowNodeRetry` | Kapalı küme (`ProviderError`, `ProviderUnavailable`, `RateLimited`, `Timeout`) | `OutputTruncated` kümede yok → tekrar yok |
| `FallbackChatClient`, `CircuitBreakingChatClient` | Durum ve sınıf | Algılayıcı yedek zincirinin **dışında**; kesilme ikisine istisna olarak hiç ulaşmaz (`FallbackChatClientTests` +2) |
| MAF `AIJudgeLoopEvaluator` (loop `aiJudge`) | `OnCriterionError` politikası | Tekrar yok; ölçüt hatası olur |
| `AgentBatchJobHandler`, `WorkflowJobHandler`, `EvalJobHandler` case run'ı | Madde/case başına yakalar | Tekrar yok; madde `Failed` |
| `EvalJobHandler` değerlendiricisi · `AgentRunJobHandler` · `RunContinuationJobHandler` · `ApprovalResumeJobHandler` | Sınıflamaz; işçi `NonRetryableJobException` dışındaki her hatayı `MaxAttempts`'e kadar tekrarlar | Kalıcı `4xx` için de bugün böyle. Değerlendirme işi varsayılan 3 deneme; öbür üçü 1. Tek karar ister → **F-429** |
| Sıkıştırma özeti | — | Düz metin; kesilme olay olur, istisna değil |

## Bu Fazda Verilen Kararlar

Dört karar `docs/KARARLAR.md`'ye **K-919…K-922** olarak yazıldı (2026-10-13;
bakımcı onayı bekleyenler işaretli). Özet:

- **K-919** *(public-api)* — Çıktı sınırında kesilme ayrı bir sonuçtur:
  `RunErrorClass.OutputTruncated = 16`, `TraconOutputTruncatedException`
  (`output_truncated`), `RunEventCustomTypes.OutputTruncated`
  (`tracon.output.truncated`). Karar son turdan verilir; yapılandırılmış yanıt
  run'ı düşürür ve onarım koşmaz (ret olayında `repairSkippedTruncated`), düz
  metin `Completed` + olay (Açık Soru 2-A). Reddedilen yanıtın token'ı run'a
  katlanır (#4).
- **K-920** *(public-api)* — Anthropic ayarları: `anthropic.effort`,
  `anthropic.thinking.mode` (`enabled`/`adaptive`; tek başına bütçe `enabled`),
  `anthropic.promptCaching.ttl`, `anthropic.promptCaching.placement`
  (`request`/`system`). Değerler SDK enum'larından; `ttl`/`placement`
  `promptCaching` olmadan, `adaptive` + bütçe ve `enabled` bütçesiz hatadır.
- **K-921** *(public-api)* — `ModelDescriptor.SupportsSampling` (`bool?`,
  `null` = bilinmiyor). `false` iken `Temperature`/`TopP` derlemeyi durdurur ve
  doğrulama `sampling_unsupported` verir; **yedek bağlantılar da denetlenir**
  (#3, bakımcı onayı bekler).
- **K-922** *(public-api)* — Sağlayıcının varsayılan çıktı sınırı
  `ModelOutputLimit` kaydıyla `IChatClient.GetService` üzerinden bildirilir;
  doğrulama `output_truncation_risk` uyarısı verir (#1, #2; **bakımcı onayı
  bekler**: seam biçimi).

Yerel tercihler: doğrulayıcının yeni denetimleri kendi `partial`
dosyasında, `ValidateAsync`'te tek çağrıyla (Faz 201 ile çakışmayı daraltır) ·
uyarı için istemci ikinci kez kurulur (istek yok) · payload kaydı
`OutputTruncatedEventPayload` yalnız sağlayıcı ve sınırı taşır.

## Tüketici Yüzeyi Envanteri

| Kova | Yüzey | Durum |
|---|---|---|
| `docs-site/` (elle) | `guides/model-providers.md` (§ Provider-specific settings, katalog bayrakları, çalışma sözleşmesi, sorun giderme; denetim: `system` yerleşiminin düşüşü) · `concepts/runs.md` (§ Truncated output; denetim: yargıç tekrarlanmaz) · `guides/structured-output.md` · `reference/configuration.md` · `capabilities.md` · `packages.md` | Yazıldı; `npm run check` (build + content + links + weight) yeşil |
| `docs-site/` (üretilir) | `api/*` · `http-api/*` · `llms*.txt` | Yeniden üretildi; `build-agent-map.mjs --check` yeşil |
| Sevk edilen metin | `///` (yeni tipler, `AnthropicProviderNames` `<example>`, `IModelProvider` remarks) · `src/Tracon.Anthropic/README.md` (denetim: `system` yerleşiminin talimatsız düşüşü) · `src/Tracon.Testing.Contracts.Xunit/README.md` (`InvalidSettingValue`) · `CHANGELOG.md` `[Unreleased]` (Fixed · Added · Changed; denetim: online değerlendirme tekrarı) | İç referans yok; `ShippedDocumentationSelfContainmentTests`, `ExampleCompilationTests` yeşil |
| Agent haritası | `capabilities.md` iki satır | Harita değişmedi (satırlar haritanın bölümünde değil); `llms-full.txt` güncellendi |

`--site-denetle --taban 126e6c61`: üç kural, üçü karşılandı. Site **yayınlanmadı** (`scripts/site-deploy.sh` dışa açık bir iştir; bakımcı koşar) — Faz 201 ve Faz 204'ün sayfaları birlikte yayınlanır.

## Süreç Ölçümü

> Kapanışta doldurulur. **Tablo olarak** — onay kutusu DEĞİL.

| Metrik | Değer |
|---|---|
| Plan revizyonu sayısı | 0 — plan onaylandı (1-A, 2-A, 3-A, 4-B, 5-B); 17 sapma uygulama içinde ölçülüp **Plandan Sapmalar**'a yazıldı |
| Düzeltme turu sayısı | 6 — (1) derleme ve analyzer tanıları (`IDE0055`, `MA0006`, `CS8604`, `CS0104`, `CS8852`, `CS0136`); (2) `secret` taraması test anahtarı biçimini yakaladı; (3) sevk doküman kapıları (`ExampleCompilationTests` parça örnek, `///`'da 🚨 ve K-/F- referansı); (4) `docs/manuel-test` bütçesi (iki kez); (5) `QueuedRunParameterTests` yarışı (#16); (6) denetimin üç 🟡'ı, üç 🟢'ı ve hüküm 6 |
| 🔴 bulgu: gerçek / gürültü / araştırılacak | 0 / 0 / 0 (🟡 3 gerçek, üçü kapandı · 🟢 3 gerçek, ikisi kapandı, biri adaya: F-428) |
| Fazın ürettiği regresyon | 0 — `QueuedRunParameterTests` yarışı temel commit'te (`126e6c61`) de düşüyordu (#16) |
| Faz kapandıktan sonra bulunan kusur | ölçülmedi (faz kapanmadı) |

## Denetim Bulguları

Bağımsız denetçi (`faz-denetim`, 2026-10-13): 🔴 yok. Triyaj kanıtla yapıldı;
düzeltmeler kodla ve testle doğrulandı.

| # | Seviye | Bulgu | Triyaj | Sonuç |
|---|---|---|---|---|
| 🟡1 | Orta | Sözleşmenin yeni senaryosundan sonra üç manuel case ve sözleşme README'si bayat: MT-PKG-106 iki atlama sayıyordu, MT-TEST-079 `skipped: 0` bekliyordu, MT-PROV-031 eski ret metnini yazıyordu; README `InvalidSettingValue`'yu anlatmıyordu | gerçek | **Düzeltildi** — MT-PKG-106 üç atlama (Azure/OpenAI), MT-TEST-079 `skipped: 1` (boolean `contoso.shout`), MT-PROV-031 İngilizce mesaj ve altı anahtar; README paragrafı |
| 🟡2 | Orta | Online değerlendirmede kesilen yargıç yanıtı `retryable` sayılıyor; iş aynı isteği `MaxAttempts`'e kadar tekrarlıyor | gerçek | **Düzeltildi** (#17) + sınıf taraması; işçi düzeyi açık **F-429**. Testler: `OnlineEvalJobHandlerTests` (+2), `OnlineEvalRetryTests` (+1; düzeltme geri alınınca kırmızı, ölçüldü). `CHANGELOG` Changed, `concepts/runs.md` |
| 🟡3 | Orta | `StructuredResponseValidatingAgent`'ın iki kesilme dalının uçtan uca testi yok (akış, onarım turu) | gerçek | **Düzeltildi** — akış: tek ret, attempt 1/1, `repairSkippedTruncated` JSON `null` (ölçüldü); onarım turu: attempt 2/3, `repairSkippedTruncated: true`, usage 100/220 |
| 🟢1 | Düşük | `OutputTruncationStreamingTests` yorumu "akışta katlama yok" diyor; algılayıcı akışta da katlar | gerçek | **Düzeltildi** |
| 🟢2 | Düşük | `ReasoningEffort` eşlemesi ve `thinking` önceliği yalnız decompile'dan | gerçek | **Düzeltildi** — ham gövde testi (+4 case); 1024 / 16384 / 32768 gövdeden ölçüldü |
| 🟢3 | Düşük | Örnekleme denetimi yan kanal bağlamalarını kapsamıyor | gerçek | **Devredildi** — **F-428** (`ModelRunJudge` `Temperature`/`TopP` gönderir; özetleyici göndermez) |
| Hüküm 6 | — | `placement=system`'in talimatsız düşüşü sitede ve README'de yazılı değil | gerçek | **Düzeltildi** — `guides/model-providers.md` tablo ve madde, `src/Tracon.Anthropic/README.md` tablo |

Denetim sonrası kapılar (2026-10-13): § Doğrulama komutları ve DoD satırı.

## Sonraki Faza Devir Notu

**Sözleşmeler.**
- `RunErrorClass.OutputTruncated = 16`; sıradaki boş değer **17**. Yeni sınıf
  `DefaultRunErrorClassifier.StableIdentities`'e, `RunErrorClassContractTests`'e
  ve iki UI sözlüğüne (`dashboard.errorClass.*`) birlikte girer.
- Kesilme yalnız yapılandırılmış istekte istisnadır; düz metin `Completed` +
  `tracon.output.truncated` olayı. Karar son bildirilen bitiş sebebinden verilir.
- Sağlayıcı varsayılan çıktı sınırını, bağlamaya göre kurduğu ham istemcinin
  `GetService(typeof(ModelOutputLimit))` yanıtıyla bildirir. Bugün yalnız
  Anthropic bildirir; öbür sağlayıcılarda `output_truncation_risk` uyarısı çıkmaz.
- `ModelProviderSettingsContract.InvalidSettingValue`: kapalı değer kümeli ayarı
  olan sağlayıcı türetir; `null` → senaryo atlanır.

**🚨 Tuzaklar** (alan dosyaları: `model-boru-hatti.md`, `openai-saglayici.md`,
`cekirdek-calistirma.md`).
- MEAI 10.9.0 `FunctionInvokingChatClient` akışta ara turun güncellemelerini kendi
  bitiş sebebiyle verir; **son** bildirilen sebep okunur.
- `RunRecordingAgent` istisnayla biten run'a usage yazmaz (iki yol); reddedilen
  yanıtın token'ı `TraconRunContext.Current.ExtraUsage`'a katlanır.
- Anthropic adaptörü: mevcut `OutputConfig`'i `with { Format }` ile kopyalar;
  `Instructions` cache kontrolsüz blok olur; yazma token'ı `InputTokenCount`'a
  girer ve `AdditionalCounts["CacheCreationInputTokens"]`'e yazılır.
- İş işçisi `NonRetryableJobException` dışındaki her hatayı tekrarlar (F-429).

**Devredilen ve açık işler.**
- **F-426** Anthropic `model_context_window_exceeded`/`pause_turn` → `Stop`.
  Düzeltme `FinishReasonDetectingChatClient`'a değil Anthropic paketine girer;
  `RawRepresentation` okunur.
- **F-427** Canlı SSE düz metin kesilmesini söylemez; olay yalnız run olaylarında.
- **F-428** Yargıç bağlaması örnekleme denetiminden geçmez.
- **F-429** İşçi düzeyinde "tekrar aynı cevabı alır" kararı (kalıcı `4xx` + kesilme).
- **F-430** SQL Server upsert'i eşzamanlı yazımda deadlock'u yeniden denemez (kapı koşumunda görüldü; fazdan bağımsız).
- **F-420** Ölçüm bu fazda yapıldı (yukarıdaki tuzak): yazma token'ı sayılır,
  düz girdi fiyatıyla fiyatlanır → kalem fiyat eksiğidir, planlamada kalır.
- **F-347** OpenAI strict şema: kesik strict yanıt da `OutputTruncated` olur.
  OpenAI istemcisi `ModelOutputLimit` bildirmez; uyarı isteniyorsa F-347 ekler.
- **K-921 #3 ve K-922 #1** bakımcı onayı bekler (yedek bağlantı denetimi; seam biçimi).
- Ekran görüntüleri yeniden üretilmedi (#15): Faz 201 ile birleşmeden sonra tek
  yeniden üretim ikisini kapsar.

**Aynı dosyalara dokunan planlı fazlar.**
- **Faz 201**: `AgentDefinitionValidator` artık `partial`; bu fazın denetimleri
  `AgentDefinitionValidator.OutputLimits.cs`'te, `ValidateAsync`'te
  `CheckModelAsync`'ten sonra tek çağrı. Birleşmede yalnız o satır çakışır.
- **Faz 196**: `DefaultRunErrorClassifier.cs:41-51` atıfları geçerli; yeni sınıf 17'den.
- **Faz 203**: `ModelProviderSettings.TypeMismatch` artık
  `ProviderSettingsValidationException` (#5). 🚨 Faz 203 "ret metni değeri
  yankılamaz" der; Anthropic (`effort=extreme`) ve Google (`safety` eşiği)
  kapalı küme değerini yankılar. Kural ya üçüne birden uygulanır ya da kapalı
  küme yankısı gerekçelendirilir.

**Sıradaki faz:** bakımcı seçer ([YOL-HARITASI](../../YOL-HARITASI.md)).
