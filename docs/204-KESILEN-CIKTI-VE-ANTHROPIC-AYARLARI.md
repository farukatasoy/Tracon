# Faz 204 — Kesilen Model Çıktısı ve Anthropic Güncel İstek Ayarları

> **Durum:** 📋 Planlandı (2026-10-13)
> **Plan onayı:** onaylanmadı — bakımcı 2026-10-13'te F-320'nin öne alınmasını onayladı; aynı gün önbellek yazma fiyatı dilimi yeniden tartıldı ve ayrı adaya (F-420) ayrıldı; engelleyici kararlar § Açık Sorular'da öneriyle bekler, uygulama onaydan önce başlamaz
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

- [ ] Yapılandırılmış yanıtta `Length` → `Failed` + `OutputTruncated`; onarım koşmaz; düz metinde olay (test 1-6)
- [ ] Doğrulama raporu kesilme ve örnekleme uyarılarını taşır (test 7, 10)
- [ ] Dört Anthropic ayarı gövdeye doğru yazılır; bilinmeyen anahtar hata (test 8, 9, 11)
- [ ] Dört doğrulama kapısı sıfır uyarı verir — `python3 scripts/kapi.py kapanis --taban <faz öncesi commit>`
- [ ] `samples/Tracon.Api` ile gerçek Anthropic `run`'ı (case 1, 2), çıktı belgeye yazıldı
- [ ] `secret` taraması boş döndü
- [ ] Manuel kabul case'leri üç alan dosyasına eklendi; otomatikleştirilebilenler koşuldu
- [ ] `faz-denetim` koşuldu; 🔴 bulgu kalmadı
- [ ] `docs-site/` güncellendi; `npm run build` + `check-links.mjs` temiz
- [ ] OpenAPI ve iki istemci tazelendi (yeni enum üyesi, `supportsSampling`)

### Doğrulama komutları

Case 1, 2 ve 4'ten kapanışta gerçek çıktıyla yazılır.

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

> Kapanışta doldurulur. Plan ile gerçek arasındaki fark **gizlenmez**.

## Bu Fazda Verilen Kararlar

> Kapanışta doldurulur. Beklenen: kesilme sınıfı ve olay (`public-api`),
> Anthropic ayar anahtarları ve örnekleme bayrağı (`public-api`).

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

> Kapanışta doldurulur — `faz-denetim` çıktısı. Bulgu yoksa "🔴 ve 🟡 yok"
> yazılır.

## Sonraki Faza Devir Notu

> Kapanışta doldurulur: sözleşmeler, tuzaklar (🚨), yarım işler, sıradaki faz.
