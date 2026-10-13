# Faz 204 — Kesilen Model Çıktısı ve Anthropic Güncel İstek Ayarları

> **Durum:** 📋 Planlandı (2026-10-13)
> **Plan onayı:** Bakımcı, 2026-10-13 (Açık Sorular'ın önerileri kabul edildi: 1-A, 2-A, 3-A, 4-B, 5-B)
> **Kaynak:** [ADAYLAR.md](ADAYLAR.md) · **F-320**, **F-346**
> **Genellik:** Yapılandırılmış çıktı üreten her agent kesilmeye açıktır ve kesilen JSON bugün her sağlayıcıda yanlış sınıflanır (F-320, ilk ilke). Anthropic ayarları sağlayıcı eşitliğidir: güncel modeller `budget_tokens` ve örneklemeyi `400` ile reddeder (F-346, ekosistem). İkisi tek tüketiciye bağlı değildir
> **Karşı görüş:** F-320 için "doğrulama açıkken kesik JSON zaten reddedilir" — plan değeri sınıflama, onarımı atlama ve uyarı olarak sınırlar. F-346 için "tüketici ayarları boş bırakabilir" — plan yalnız Tracon dışından verilemeyen ayarları (önbellek yerleşimi ve TTL, `effort`, adaptive) ekler
> **Önkoşul:** Yok. F-346, F-320'nin derleme uyarısı yüzeyini kullanır; ikisi aynı faza bu yüzden girer
> **Paketler:** `Tracon.Abstractions`, `.Core`, `.Anthropic`, `.AspNetCore`, `.Testing.Contracts.Xunit` · üretilen istemciler
> **Yeni paket:** Yok · **Migration:** Yok
> **Public API:** büyüyor — `1.0.0` GA'dan (K-603) önce ucuz. Ölçüldü (2026-10-13): `wc -l src/*/PublicAPI.Shipped.txt` → 17 dosya, toplam 17 satır
> **Tüketici yüzeyi:** site: `guides/model-providers.md` (§ Anthropic), `concepts/runs.md` (hata sınıfları), `guides/structured-output.md`, `reference/configuration.md`, `capabilities.md` · sevk edilen: `AnthropicProviderNames` XML'i ve `<example>`, `src/Tracon.Anthropic/README.md`, `CHANGELOG.md`
> **Manuel test alanı:** `docs/manuel-test/06-SAGLAYICI-DIGER.md`, `22-GUARDRAIL-VE-YAPISAL-CIKTI.md`

---

## Bu Faza Başlarken

> `faz-baslangic` skill'ini uygula; yalnız işaret edilen bölümleri oku.

1. Bu doküman
2. Kararlar:
   ```bash
   grep -n "K-889" docs/KARARLAR.md
   grep -n "K-032\|K-208" docs/arsiv/KARARLAR-INDEKS-ARSIV.md
   ```
   **K-889** (doğrulayıcı
   son asistan mesajını doğrular) · **K-032** (yetenek katalogdan gelir,
   kodda tutulmaz) · **K-208** (sağlayıcı ayarı sözleşmede; bilinmeyen
   anahtar hatadır)
3. F-340 kapanışı (ayar doğrulama deseni): `grep -n "F-340" docs/arsiv/ERTELENEN-ADAYLAR.md`
4. Hafıza: [`hafiza/openai-saglayici.md`](hafiza/openai-saglayici.md) ·
   [`hafiza/model-boru-hatti.md`](hafiza/model-boru-hatti.md) ·
   [`hafiza/agent-derleme-ve-onbellek.md`](hafiza/agent-derleme-ve-onbellek.md)
5. Tüketici kanıtı: [podcast raporu](arsiv/incelemeler/2026-10-11-podcast-tuketici-uyum-raporu.md)
   § 2.2 ve § 10 (ölçülen ↔ varsayılan tablosu)

---

## Amaç

Uzun yapılandırılmış çıktı üreten bir agent bugün iki yerde sessiz kalır:
model çıktı sınırına çarpınca kesik yanıt ya "başarılı" döner ya da "geçersiz
JSON" diye reddedilir ve onarım aynı sınıra yeniden çarpar; Anthropic'in
güncel ayarları (`effort`, 1 saatlik önbellek, adaptive thinking) Tracon'dan
verilemez.

- **F-320** — `Length` bitişi ayrı bir sonuç ve hata sınıfıdır; onarım
  koşmaz; doğrulama raporu düşük çıktı sınırını uyarır.
- **F-346** — `anthropic.effort`, önbellek `ttl` ve yerleşimi, adaptive
  thinking ve örnekleme yetenek bayrağı.

**Tüketici bağlamı (ölçüldü, 2026-10-13).** Podcast pilotunun writer
çağrısı Claude Opus 5.5'e `effort: high`, `max_tokens: 128000`, sistem
talimatında `cache_control {ttl: 1h}` ile gider; tek çağrı 48.172 çıktı
token'ına çıktı. Tracon'un Anthropic varsayılanı 4.096'dır. Maliyetin %83'ü
çıktı token'ıdır ve onu `effort` belirler. Plan geneldir; tüketici yalnız bir
ölçüt örneğidir.

### Kapsam dışı

| Kalem | Neden |
|---|---|
| OpenAI strict şema ve etkin şemanın görünürlüğü | F-347 (aday) |
| Anthropic sunucu tarafı yedek (`server-side-fallback`) | Tracon yedeği `FallbackChatClient`'tadır; ikinci mekanizma ayrı karar |
| Kesilen çıktıyı otomatik devam ettirme | Sağlayıcıya özgü; onarım koşmaz kuralıyla çelişir |
| Önbellek yazma token'ı ve fiyatı | F-420 (aday): migration ister; aciliyeti tek ölçümden geliyor (pilotta toplamın ~%1'i) |

### Bugün ne çalışmıyor — doğrulanmış kanıt

| Kanıt | Gözlem |
|---|---|
| `ContentFilterDetectingChatClient.cs:45-81` | Yalnız `ChatFinishReason.ContentFilter` okunur |
| `grep -rn "ChatFinishReason.Length" src/` → boş | Kesilme hiçbir yerde sınıflanmaz |
| `RunErrorClass.cs:27-144` | `OutputTruncated` üyesi yok; son üye `LoopUnsatisfied = 15` |
| `StructuredResponseValidatingAgent.cs:39-65` | Onarım turu bitiş sebebine bakmaz; kesik JSON'u yeniden ister |
| `AnthropicProviderOptions.cs:55` · `AnthropicModelProvider.cs:117` | Varsayılan `DefaultMaxOutputTokens = 4096` |
| `ModelDescriptor.cs:16` · `:25` | `MaxOutputTokens`, `SupportsReasoning`; örnekleme bayrağı yok |
| `AnthropicProviderSettingsChatClient.cs:103-114` | Üst düzey `cache_control: ephemeral` (TTL yok) ve `thinking {type: enabled, budget_tokens}` |
| `AnthropicProviderNames.cs:38` · `:56` | Yalnız `anthropic.promptCaching` ve `anthropic.thinking.budgetTokens` |
| `AgentDefinitionCompiler.ChatOptions.cs:146-147` | `Temperature`/`TopP` doğrudan gider; derleme modele bakmaz |
| Anthropic .NET SDK 12.39.0 XML | `OutputConfig.Effort`, `Ttl.Ttl1h`, `CacheControlEphemeral { Ttl }`, MEAI içerik bloğunda `WithCacheControl(...)` (`Anthropic.xml:19-56`) |

> Kanıtlar 2026-10-13'te `55272673` üzerinde doğrulandı.

---

## 204.1 — Kesilen çıktı (F-320)

- **Algılama:** `ContentFilterDetectingChatClient` bitiş sebebini okuyan tek
  dekoratöre genişler (Açık Soru 1). Akışsız yanıtın ya da akışın son
  güncellemesinin `FinishReason == Length` olması kesilmedir.
- **Sonuç:** yanıt biçimi `Json`/`JsonSchema` ise
  `TraconOutputTruncatedException` → run `Failed`, sınıf
  `RunErrorClass.OutputTruncated`, tip `output_truncated`. Düz metinde run
  düşmez; `tracon.output.truncated` özel olayı yazılır (Açık Soru 2, K1).
- **Onarım koşmaz:** `StructuredResponseValidatingAgent` bu sınıfta onarım
  turu açmaz; olay `RepairSkippedTruncated` gerekçesini taşır.
- 🚨 **Tool turu:** model tool çağrısını yazarken sınıra çarparsa ara yanıt
  da `Length`'tir. **Ölçülmeli (Adım 0):** MEAI `FunctionInvokingChatClient`
  bu ara yanıtı dekoratöre hangi sırayla verir; ara tur ile son yanıt ayrı
  sınanır.
- **Doğrulama raporu:** `ResponseFormat` yapılandırılmışken `MaxOutputTokens`
  boşsa ve sağlayıcının varsayılanı katalogdaki
  `ModelDescriptor.MaxOutputTokens`'tan küçükse `AgentValidationReport`
  `Warning` taşır (`POST /api/agents/validate`). Derleme düşmez.

## 204.2 — Anthropic istek ayarları (F-346)

- `anthropic.effort` (`low`/`medium`/`high` ve SDK'nın taşıdığı üst düzey) →
  `output_config.effort`. Değer listesi SDK enum'undan okunur; 🚨
  **ölçülmeli:** `maf-api-kesfi` ile `Anthropic.Models.Messages` effort tipi
  ve `MessageCreateParams.OutputConfig`'in yapılandırılmış çıktı
  `Format`'ıyla birlikte yazımı (iki yazıcı aynı nesneyi ezer mi).
- `anthropic.promptCaching.ttl` (`5m`/`1h`) → `cache_control.ttl`.
- `anthropic.promptCaching.placement` (`request` varsayılan / `system`):
  `system` iken talimat bloğunun son içeriği `WithCacheControl` ile
  işaretlenir; belgesi değişen istekler talimat önekini paylaşır.
- `anthropic.thinking.mode` (`enabled` + `budgetTokens` / `adaptive`)
  (Açık Soru 3). Güncel modellerde `budget_tokens` `400` alır; katalog
  bayrağı derleme anında söyler.
- Bilinmeyen anahtar ve yanlış değer F-340 deseniyle hatadır
  (`ModelProviderSettings.Validate`).

## 204.3 — Örnekleme yetenek bayrağı

- `ModelDescriptor.SupportsSampling` (`bool?`, `null` = bilinmiyor, K1). Katalog
  `false` diyorsa `Temperature`/`TopP` taşıyan tanım doğrulama raporunda
  `Error`, derlemede `TraconCompilationException` alır. Değer katalogdan
  gelir (K-032); kodda model adı listesi tutulmaz.

---

## Planlanan Public API

> Taslak. **Bugün eklemek bedava / `1.0.0` GA sonrası `sealed record` alanı,
> enum üyesi ve sabit kırıcı.**

```csharp
// Tracon.Abstractions
RunErrorClass.OutputTruncated = 16
sealed class TraconOutputTruncatedException : TraconException          // ad taslak
ModelDescriptor { bool? SupportsSampling; }
RunEventCustomTypes.OutputTruncated = "tracon.output.truncated"
// Tracon.Anthropic
AnthropicProviderNames { EffortSetting; PromptCachingTtlSetting; PromptCachingPlacementSetting; ThinkingModeSetting; }
```

### HTTP `endpoint`'leri

| Metot | Yol | Rol | Değişiklik |
|---|---|---|---|
| `POST` | `/api/agents/validate` | Admin | Kesilme ve örnekleme uyarıları |
| `GET` | `/api/models` | Reader | `supportsSampling` |

### Arayüz payı

Yok. Doğrulama uyarısı konsolun mevcut rapor bileşeninde görünür; ölçülmeli.

---

## Planlanan Dosya Listesi

```
Abstractions Runs/RunErrorClass · Models/ModelDescriptor · TraconException*
Core         Models/ContentFilterDetectingChatClient→FinishReasonDetectingChatClient
             Compilation/{StructuredResponseValidatingAgent,
             AgentDefinitionCompiler.ChatOptions,AgentDefinitionValidator} · Runs/DefaultRunErrorClassifier
Anthropic    AnthropicProviderNames · AnthropicProviderSettingsChatClient · AnthropicChatClientFactory · README.md
AspNetCore   model DTO'su (`supportsSampling`)
Testing.Contracts.Xunit ModelProviderSettingsContract
```

---

## Hata Modları ve Testler

| # | Ne bozulabilir | Seviye | Test sınıfı |
|---|---|---|---|
| 1 | `Length` yapılandırılmış yanıtta `Completed` ya da "invalid JSON" olur | Fonksiyonel (sahte sağlayıcı `Length`) | `OutputTruncationTests` |
| 2 | Onarım kesik yanıtı yeniden ister | Fonksiyonel | `OutputTruncationTests` |
| 3 | Düz metin run'ı düşer (K1 ihlali) ya da olay yazılmaz | Fonksiyonel | `OutputTruncationTests` |
| 4 | Akışlı yolda son güncellemenin sebebi kaçar | Fonksiyonel (akış) | `OutputTruncationStreamingTests` |
| 5 | Tool turundaki ara `Length` yanlış sınıflanır | Fonksiyonel | `OutputTruncationToolTurnTests` |
| 6 | Yeni sınıf `Unknown` olur; yedek zinciri kesilmeyi sağlayıcı hatası sayıp yedeğe geçer | Birim | `DefaultRunErrorClassifierTests`, `FallbackChatClientTests` |
| 7 | Doğrulama uyarısı yanlış koşulda çıkar ya da derlemeyi düşürür | Birim + Fonksiyonel (`/validate`) | `AgentValidationWarningTests` |
| 8 | `effort`, `ttl`, yerleşim, adaptive gövdeye yanlış yazılır; yapılandırılmış çıktıyla `output_config` ezilir | Birim (ham gövde yakalama) | `AnthropicRequestSettingsTests` |
| 9 | Bilinmeyen anahtar ve değer sessiz kalır | Sözleşme | `ModelProviderSettingsContract` |
| 10 | Örnekleme bayrağı derlemede uygulanmaz; `null` bayrak reddeder | Birim | `SamplingCapabilityTests` |
| 11 | **Başka kiracı:** kiracı anahtarlı (BYOK) Anthropic istemcisi ayarları almaz | Fonksiyonel | `AnthropicRequestSettingsTests` |
| 12 | Üretilen istemciler alanı taşımaz | Paket | NSwag/TS kapıları |
| 13 | Gerçek Anthropic'te `effort: high` + `ttl: 1h` + `JsonSchema` | Manuel (anahtar gerekir) | case 1 |

---

## Manuel Kabul Case'leri

| # | Ön koşul | Adımlar | Beklenen sonuç |
|---|---|---|---|
| 1 | Anthropic anahtarı, `JsonSchema`'lı agent, `anthropic.effort=high`, `promptCaching.ttl=1h`, `placement=system` | İki kez koş | İkinci run'da önbellek okuma > 0; istek gövdesinde `output_config.effort` ve `cache_control.ttl` (log) |
| 2 | Aynı agent, `MaxOutputTokens=200` | Koş | `Failed`, `output_truncated`; onarım olayı yok |
| 3 | Düz metin agent, `MaxOutputTokens=50` | Koş | `Completed`; `tracon.output.truncated` olayı |
| 4 | `MaxOutputTokens` boş, `JsonSchema` | `POST /api/agents/validate` | `200`, `Warning` metni varsayılan sınırı adlandırır |
| 5 | Katalogda `supportsSampling=false` | `temperature: 0.2` ile kaydet | `400` doğrulama hatası |

---

## Açık Sorular

| # | Soru | Seçenekler | Öneri |
|---|---|---|---|
| 1 | Algılama nerede | A: içerik filtresi dekoratörü genişler · B: ayrı dekoratör | **A** — bitiş sebebi tek yerde okunur; sıra bugünkü konumdur |
| 2 | Düz metin kesilmesi | A: olay, run sürer · B: run düşer | **A** — K1; metin kısmen yararlıdır |
| 3 | Thinking ayarı | A: `anthropic.thinking.mode` (`enabled`/`adaptive`) · B: `budgetTokens` yokken adaptive varsay | **A** — örtük davranış değişikliği yok |
| 4 | Önbellek yazma fiyatı bu fazda mı | A: bu faz (migration) · B: ayrı aday | **B — karar verildi** (bakımcı, 2026-10-13): F-420; aciliyet tek ölçümdendir |
| 5 | `effort` ile `MaxOutputTokens` ilişkisi | A: bağımsız · B: yüksek effort'ta düşük sınır uyarısı | **B** — ölçülen çıktının %80'i düşünmedir; uyarı 204.1 yüzeyine girer |

---

## Bitiş Ölçütleri (DoD)

- [x] Yapılandırılmış yanıtta `Length` → `Failed` + `OutputTruncated`; onarım koşmaz; düz metinde olay (test 1-6)
- [x] Doğrulama raporu kesilme ve örnekleme uyarılarını taşır (test 7, 10)
- [x] Dört Anthropic ayarı gövdeye doğru yazılır; bilinmeyen anahtar hata (test 8, 9, 11)
- [ ] Dört doğrulama kapısı sıfır uyarı verir — `python3 scripts/kapi.py kapanis --taban <faz öncesi commit>` (kapanışta; commit öncesi `kapi.py ic-dongu` tam koşuma genişledi ve yeşil: derleme 54,9 sn, test 510,4 sn)
- [x] `samples/Tracon.Api` ile gerçek Anthropic `run`'ı (case 1, 2), çıktı belgeye yazıldı
- [x] `secret` taraması boş döndü (`kapi.py tarama`)
- [x] Manuel kabul case'leri iki alan dosyasına eklendi (`06`, `22`; sayılar `00-INDEKS`'te); beşi de örnek uygulamada koşuldu
- [ ] `faz-denetim` koşuldu; 🔴 bulgu kalmadı
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

## Riskler

| Risk | Önlem |
|------|-------|
| Model aileleri ve ayar adları hızlı değişir | Değerler katalogdan ve SDK enum'undan (K-032); kodda model listesi yok |
| `output_config` iki yazıcı (effort, yapılandırılmış çıktı) tarafından ezilir | Adım 0 ölçümü; test 8 ham gövdeyi yakalar |

---

<!-- AŞAĞISI KAPANIŞTA DOLDURULUR — `faz-tamamlama` skill'i.
     Plan anında boş kalır. Başlıkları SİLME. -->

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
| 11 | Plan yok | `ModelBinding.ReasoningEffort` Anthropic adaptöründe `thinking.budget_tokens`'a eşlenir (Low 1024 … ExtraHigh 32768; None → disabled) ve güncel modeller onu `400` ile reddeder; raw `thinking` verilince adaptör `Reasoning`'i yok sayar | Ölçüldü (decompile); site ve README'ye yazıldı, kod değişmedi |
| 12 | Sözleşme "bilinmeyen anahtar ve değer" | `ModelProviderSettingsContract.InvalidSettingValue` (sanal, `null` → atlanır) + `A_supported_setting_with_an_invalid_value_is_rejected`; Anthropic (`effort=extreme`) ve Google (`safety` eşiği) türetir | Sevk edilen sözleşme genişledi → yayın provası zorunlu |
| 13 | Manuel case'ler | Planın beş case'i: MT-PROV-038, MT-PROV-043, MT-GUARD-110..112. `docs/manuel-test` bütçesi aşıldı: `22-…` spec'indeki iki tarihî koşum notu (2026-09-01, 2026-09-02) koşum arşivine **taşındı**; kalan boşluk 37 B | Bütçe kalibrasyonu bakımcı kararıdır (`dokuman-bakim.py`); Faz 201'in case'leri bütçeyi aşar |
| 14 | Örnek uygulama | Katalog: `claude-haiku-4-5-20251001` `MaxOutputTokens: 64000`, `SupportsStructuredOutput: true`; yeni `claude-haiku-5-5`; `sonnet-5`/`opus-5` `MaxOutputTokens: 128000`, `SupportsSampling: false` | Case 1, 4, 5 katalog alanı ister; değerler `claude-api` başvurusundan (önbellek eşiği, örnekleme reddi) |
| 15 | Konsol ekranı yok | İki sözlük anahtarı (`dashboard.errorClass.OutputTruncated`); ekran görüntüleri **yeniden üretilmedi**, yalnız `.ui-source.sha256` damgası yenilendi | Tohum verisinde bu sınıf yok; E2E yeniden üretimi 40+ PNG'yi görünür fark olmadan değiştirip Faz 201'le ikili birleşme çatışması üretirdi. Birleşmeden sonra Faz 201'in yeniden üretimi ikisini kapsar |
| 16 | Plan yok | `QueuedRunParameterTests.A_payload_from_before_this_phase_runs_as_it_always_did` kapanış durumunu bekler (ayrı commit, `kusur-giderme`) | Kapı ikinci koşumda kırmızıydı. Temel commit'te (`643b9eee`) izole üç kez düştü, 3 sn beklemeyle geçti: test model çağrısından hemen sonra durumu bir kez okuyordu, işçi run'ı bir an sonra kapatıyor. Ürün kusuru değil, yarış. Sınıf taraması: aynı dosyanın iki case'i zaten bekliyor; `provider.Requests.Count > 0` bekleyen öbür beş testte hemen durum okuması yok |

## Bu Fazda Verilen Kararlar

Dört karar kapanışta `docs/KARARLAR.md`'ye **K-919…K-922** olarak girer
(taslak; bakımcı onayı bekleyenler işaretli):

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

## Gerçekleşen Public API

```csharp
// Tracon.Abstractions
RunErrorClass.OutputTruncated = 16
public sealed class TraconOutputTruncatedException : TraconException
{
    public const string OutputTruncatedErrorType = "output_truncated";
    public TraconOutputTruncatedException();
    public TraconOutputTruncatedException(string message);
    public TraconOutputTruncatedException(string message, Exception innerException);
    public string? ProviderName { get; init; }
    public int? MaxOutputTokens { get; init; }
    public override string ErrorType { get; }
}
public const string RunEventCustomTypes.OutputTruncated = "tracon.output.truncated";
public bool? ModelDescriptor.SupportsSampling { get; init; }
public sealed record ModelOutputLimit
{
    public required int DefaultMaxOutputTokens { get; init; }
    public bool HighReasoningEffort { get; init; }
}
// Tracon.Anthropic
public const string AnthropicProviderNames.EffortSetting = "anthropic.effort";
public const string AnthropicProviderNames.ThinkingModeSetting = "anthropic.thinking.mode";
public const string AnthropicProviderNames.PromptCachingTtlSetting = "anthropic.promptCaching.ttl";
public const string AnthropicProviderNames.PromptCachingPlacementSetting = "anthropic.promptCaching.placement";
// Tracon.Testing.Contracts.Xunit
protected virtual KeyValuePair<string, JsonElement>? ModelProviderSettingsContract.InvalidSettingValue { get; }
public void ModelProviderSettingsContract.A_supported_setting_with_an_invalid_value_is_rejected();
```

HTTP: `GET /api/models` → `supportsSampling` (`boolean | null`);
`RunErrorClass` şemasına `OutputTruncated`; `POST /api/agents/validate` yeni
kodlar `output_truncation_risk` (Warning, `model.maxOutputTokens`) ve
`sampling_unsupported` (Error, `model.temperature` / `model.topP`) —
`ValidationMessage` tipi değişmedi. Public tip sayısı `Tracon.Abstractions`
409 → 411 (`public-surface-baseline.txt`).

## Dosya Listesi (gerçekleşen)

```
Abstractions  Runs/{RunErrorClass,RunEventCustomTypes} · TraconException · Models/{ModelDescriptor,ModelOutputLimit,IModelProvider}
              Agents/ModelProviderSettings (#5)
Core          Models/FinishReasonDetectingChatClient (ContentFilterDetectingChatClient'in yerine) · Models/{ModelProviderRegistry,ProviderFailureNormalizer}
              Compilation/{StructuredResponseValidatingAgent,AgentDefinitionCompiler.ChatOptions,SamplingCapabilityCheck,
              AgentDefinitionValidator,AgentDefinitionValidator.OutputLimits} · Runs/DefaultRunErrorClassifier · TraconCoreJsonContext
Anthropic     AnthropicProviderNames · AnthropicRequestSettings (yeni) · AnthropicProviderSettingsChatClient · AnthropicChatClientFactory · README.md
Providers.Shared  ProviderRegistrationCore (SupportsSampling bağlama)
Testing.Contracts.Xunit  ModelProviderSettingsContract
UI            locales/{en,tr}/runs.ts
Üretilen      docs/openapi/tracon.json · Tracon.Client/Generated/TraconApiClient.g.cs · packages/tracon-client/src/schema.ts · llms-full.txt
Testler       Core: FinishReasonDetectingChatClientTests (adı değişti, +6), SamplingCapabilityTests (8), AgentValidationWarningTests (7),
              DefaultRunErrorClassifierTests (+1), FallbackChatClientTests (+2), RunErrorClassContractTests
              Fonksiyonel: OutputTruncationTests (5), OutputTruncationStreamingTests (2), OutputTruncationToolTurnTests (4),
              AgentValidationWarningTests (6), AnthropicRequestSettingsEndpointTests (1); altyapı FinishReasonModelProvider, FakeAnthropicServer
              Anthropic: AnthropicRequestSettingsTests (22), AnthropicChatClientFactoryTests (+1), sözleşme türetmesi; Google sözleşme türetmesi
              Paylaşılan: StubHttpServer gövde okur, ProviderRegistrationCoreTests
```

## Tüketici Yüzeyi Envanteri

| Kova | Yüzey | Durum |
|---|---|---|
| `docs-site/` (elle) | `guides/model-providers.md` (§ Provider-specific settings, katalog bayrakları, çalışma sözleşmesi, sorun giderme) · `concepts/runs.md` (§ Truncated output) · `guides/structured-output.md` · `reference/configuration.md` · `capabilities.md` · `packages.md` | Yazıldı; `npm run check` (build + content + links + weight) yeşil |
| `docs-site/` (üretilir) | `api/*` · `http-api/*` · `llms*.txt` | Yeniden üretildi; `build-agent-map.mjs --check` yeşil |
| Sevk edilen metin | `///` (yeni tipler, `AnthropicProviderNames` `<example>`, `IModelProvider` remarks) · `src/Tracon.Anthropic/README.md` · `CHANGELOG.md` `[Unreleased]` (Fixed · Added · Changed) | İç referans yok; `ShippedDocumentationSelfContainmentTests`, `ExampleCompilationTests` yeşil |
| Agent haritası | `capabilities.md` iki satır | Harita değişmedi (satırlar haritanın bölümünde değil); `llms-full.txt` güncellendi |

`--site-denetle --taban 643b9eee`: üç kural, üçü karşılandı. Site **yayınlanmadı**.
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

> Kapanışta doldurulur — `faz-denetim` çıktısı. Bulgu yoksa "🔴 ve 🟡 yok"
> yazılır.

## Sonraki Faza Devir Notu

> Kapanışta doldurulur: sözleşmeler, tuzaklar (🚨), yarım işler, sıradaki faz.
