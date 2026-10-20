# Faz 225 — Model Örneklemesi ve Tool Döngüsü Tanımda

> **Durum:** 📋 Planlandı (2026-10-20)
> **Kademe:** tam — public API'den silinen satır (`ModelBinding.AllowConcurrentToolCalls` → `ModelBinding.ToolLoop`) · kalıcı veri (saklanan tanım yükünün biçimi değişir)
> **Plan onayı:** farukatasoy, 2026-10-20 (şekil: iç içe `ToolLoop`)
> **Kaynak:** [ADAYLAR.md](ADAYLAR.md) · **F-598**
> **Genellik:** İlk ilke: model ayarı tanımın işidir (K-1029, K-1039). Bugün `Seed`, `TopK`, `StopSequences`, `ToolMode`, penalty'ler ve `AllowMultipleToolCalls` yalnız kodla (uzantı) ayarlanır ve tanım sürümüne girmez; `Reasoning.Output` hiçbir yolla ayarlanamaz. Her sağlayıcı tüketicisi etkilenir; iş tüketici kodunda kalamaz çünkü tanım sürümü, deney ve replay modeli tanımdan okur.
> **Karşı görüş:** "Alanların çoğu sağlayıcıya özgüdür; sessiz düşme olur." Cevap: §225.3 destek tablosu her alanı her sağlayıcı yüzeyi için ölçülmüş olarak taşır ve desteklenmeyen alan **derleme hatası** verir (K-208 deseni). Sessiz düşme yoktur.
> **Önkoşul:** Yok (K-1038, K-1039 commit `96eb7843`'te)
> **Paketler:** `Tracon.Abstractions`, `.Core`, `.OpenAI`, `.Anthropic`, `.Google`, `.Sql.Shared`, `.AspNetCore`, `.Client`, `.UI`
> **Yeni paket:** Yok · **Migration:** Yok (tanım JSON yükünde durur; eski anahtar okumada çevrilir)
> **Public API:** büyüyor ve bir satır taşınıyor — `PublicAPI.Shipped.txt` dosyaları boş (`wc -l src/*/PublicAPI.Shipped.txt` toplam 17 satır, 2026-10-20), yani `1.0.0` GA'dan (K-603) önce ucuz
> **Tüketici yüzeyi:** site: `guides/model-providers.md`, `concepts/tools.md` (`AllowConcurrentToolCalls`), `capabilities.md`, `guides/any-maf-feature.md` (korunan üye listesi), `reference/changelog.md` · sevk edilen: `ModelBinding` XML doc, `src/Tracon.Core/README.md` model örneği (varsa)
> **Manuel test alanı:** `docs/manuel-test/10-ARAYUZ-AGENT-PLAYGROUND.md` (konsol model bölümü) — `🤖`

---

## Bu Faza Başlarken

1. Bu doküman
2. Kararlar — yalnız grep:
   ```bash
   grep -n "K-034 —\|K-208 —\|K-553 —\|K-1029 —\|K-1031 —\|K-1035 —\|K-1038 —\|K-1039 —" docs/KARARLAR.md
   ```
   **K-034** (geçersiz `ReasoningEffort` derlemede reddedilir), **K-208** (sağlayıcının desteklemediği ayar sessizce yok sayılmaz), **K-553** (`AllowConcurrentToolCalls` Tracon'un FIC'ine bağlanır), **K-1029** (uzantı koruma tablosu), **K-1031** (yüke sonradan eklenen liste `?` taşır), **K-1038/K-1039** (korunan üye kuralı ve run başına kural aynı tabloyu okur)
3. Ölçüm kaynağı: [`MAF-KAPSAMA.md`](MAF-KAPSAMA.md) §0 #29, #30 ve §8.0 C-03 satırı
4. Alan hafızası: [`hafiza/model-boru-hatti.md`](hafiza/model-boru-hatti.md) (halka sırası, fallback), [`hafiza/agent-derleme-ve-onbellek.md`](hafiza/agent-derleme-ve-onbellek.md) (uzantı ve koruma tablosu notu), [`hafiza/openai-saglayici.md`](hafiza/openai-saglayici.md) (`RawRepresentationFactory` ve sağlayıcı ayarları)
5. Konsol: [`hafiza/frontend.md`](hafiza/frontend.md) — yalnız agent düzenleyicisi bölümü

---

## Amaç

Tanım modelin örneklemesini ve tool döngüsünü veriyle seçer. Ayar tanım sürümüne,
deneye ve replay'e girer. Uzantı aynı ayarı artık değiştiremez; tek kaynak tanımdır.
Bir sağlayıcı yüzeyi alanı desteklemiyorsa derleme durur ve alanı ve sağlayıcıyı
adlandırır.

- **F-598** — `ModelBinding`'e örnekleme alanları, `ReasoningOutput` ve iç içe `ToolLoop`; destek tablosu; koruma tablosunda `Free` → `Protected`.

### Bugün ne çalışmıyor — doğrulanmış kanıt

| Kanıt | Gözlem |
|---|---|
| [`AgentDefinitionCompiler.ChatOptions.cs:148-173`](../src/Tracon.Core/Compilation/AgentDefinitionCompiler.ChatOptions.cs) | `BuildChatOptions` yalnız `Instructions`, `ModelId`, `Temperature`, `TopP`, `MaxOutputTokens`, `Reasoning.Effort`, `ResponseFormat`, `Tools` yazar |
| [`AgentOptionsGuard.cs:76-86`](../src/Tracon.Core/Extensions/AgentOptionsGuard.cs) | `AllowMultipleToolCalls`, `FrequencyPenalty`, `PresencePenalty`, `Seed`, `StopSequences`, `ToolMode`, `TopK` `Free`: yalnız uzantı ayarlar (gerçek host probu 2026-10-20: değerler sağlayıcıya ulaştı) |
| `AgentOptionsGuard.cs` `Reasoning` satırı | `Reasoning.Output` korunur ve tanımda alanı yoktur → probe `400`; OpenAI akıl yürütme özeti K0 (§0 #29) |
| [`OpenAIProviderNames.cs:45`](../src/Tracon.OpenAI/OpenAIProviderNames.cs) | OpenAI `SupportedSettings` boş: `ProviderSettings` ham yol değildir |
| [`ModelProviderRegistry.cs:405-418`](../src/Tracon.Core/Models/ModelProviderRegistry.cs) | FIC yalnız `AllowConcurrentInvocation` ve `MaximumIterationsPerRequest` alır; `MaximumConsecutiveErrorsPerRequest`, `TerminateOnUnknownCalls` sabit |
| [`FallbackChatClient.cs:470-479`](../src/Tracon.Core/Models/FallbackChatClient.cs) | Yedek halkanın bağlaması döngü ayarlarını **tek tek elle** kopyalar (`AllowConcurrentToolCalls`, `ToolLoopIterationLimit`): yeni alan unutulursa yedek halka varsayılanla koşar |
| [`ModelBinding.cs:148`](../src/Tracon.Abstractions/Agents/ModelBinding.cs), `:162` | `AllowConcurrentToolCalls` düz public alan; `ToolLoopIterationLimit` internal |
| [`AgentDefinitionPayload.cs:36`](../src/Tracon.Sql.Shared/Internal/AgentDefinitionPayload.cs) | Yük `ModelBinding`'i olduğu gibi yazar: alanın yeri değişirse eski satır okunurken değer sessizce kaybolur |
| `RunChatOptionsRule.cs` (K-1039) | Run başına seçenek korunan üyeyi reddeder; korunan küme bu fazda büyür, run kuralı otomatik izler |

> Kanıtlar 2026-10-20 tarihinde doğrulandı.

---

## 225.1 — Alanlar

`ModelBinding`'e düz örnekleme alanları (bugünkü `Temperature`/`TopP` deseniyle):

| Alan | Tip | `ChatOptions` | Not |
|---|---|---|---|
| `Seed` | `long?` | `Seed` | |
| `TopK` | `int?` | `TopK` | `> 0` |
| `StopSequences` | `IReadOnlyList<string>?` | `StopSequences` | boş liste = ayarsız; K-1031: yükte `?`, okumada `?? []` değil `null` korunur |
| `FrequencyPenalty` | `float?` | `FrequencyPenalty` | |
| `PresencePenalty` | `float?` | `PresencePenalty` | |
| `ToolMode` | `string?` | `ToolMode` | `"auto"` · `"none"` · `"required"` · `"required:<tool>"`; `<tool>` tanımın `ToolNames`'inde olmalı |
| `AllowMultipleToolCalls` | `bool?` | `AllowMultipleToolCalls` | `AllowConcurrentToolCalls`'tan ayrıdır: biri modelin bir turda birden çok çağrı üretmesi, öbürü Tracon'un onları aynı anda koşması |
| `ReasoningOutput` | `string?` | `Reasoning.Output` | `"none"` · `"summary"` · `"full"`; `ReasoningEffort` ile aynı ayrıştırma ve hata deseni (K-034) |

İç içe döngü ayarları — yeni public `sealed record ToolLoopSettings`, `ModelBinding.ToolLoop`:

| Üye | Tip | FIC üyesi | Not |
|---|---|---|---|
| `AllowConcurrentToolCalls` | `bool` | `AllowConcurrentInvocation` | `ModelBinding.AllowConcurrentToolCalls`'tan taşınır (K-553 anlamı aynı) |
| `MaximumConsecutiveErrors` | `int?` | `MaximumConsecutiveErrorsPerRequest` | `>= 0` |
| `TerminateOnUnknownCalls` | `bool?` | `TerminateOnUnknownCalls` | varsayılan MEAI'nin (`false`) |
| `IterationLimit` (internal) | `int?` | `MaximumIterationsPerRequest` | bugünkü `ModelBinding.ToolLoopIterationLimit` buraya taşınır |

`IncludeDetailedErrors` **eklenmez**: Tracon bilerek kapalı tutar (`ExplainedFailureAIFunction.cs:13`).

## 225.2 — Tek kopya: yedek halka

`FallbackChatClient` yedek bağlamayı `ToolLoop = _primaryBinding.ToolLoop` ile kurar.
Elle alan kopyası biter (K-483 sınıfı: elle tekrarlanan ifadeye terim eklemek).
Örnekleme alanları zaten `ChatOptions` ile her halkaya gider (`SamplingCapabilityCheck`
açıklaması); kopya gerekmez.

```mermaid
flowchart LR
    D["AgentDefinition.Model"] -->|"BuildChatOptions"| O["ChatOptions<br/>Seed, TopK, Stop, Penalty, ToolMode, Reasoning.Output"]
    D -->|"ToolLoop"| R["ModelProviderRegistry.BuildPipeline<br/>UseFunctionInvocation"]
    D -->|"ToolLoop (tek nesne)"| F["FallbackChatClient<br/>yedek bağlama"]
    F --> R
```

## 225.3 — Destek tablosu ve derleme hatası

Ölçüldü (MEAI 10.11, Anthropic 12.58, Google.GenAI 1.26.1 adaptörleri, decompile,
2026-10-20): `✓` adaptör alanı tele yazar, `✗` sessizce düşürür.

| Alan | OpenAI Chat (`openai`) | OpenAI Responses (`openai-responses`) | Anthropic | Google |
|---|---|---|---|---|
| `Seed` | ✓ | ✗ | ✗ | ✓ |
| `TopK` | ✗ | ✗ | ✓ | ✓ |
| `StopSequences` | ✓ | ✗ | ✓ | ✓ |
| `FrequencyPenalty` | ✓ | ✗ | ✗ | ✓ |
| `PresencePenalty` | ✓ | ✗ | ✗ | ✓ |
| `ToolMode` | ✓ | ✓ | ✓ | ✓ |
| `AllowMultipleToolCalls` | ✓ | ✓ | ✓ | ✗ |
| `ReasoningOutput` | **ölçülmeli** | ✓ (`reasoning.summary`) | **ölçülmeli** | ✓ (`includeThoughts`) |

Tasarım: sağlayıcı paketi desteklediği alanları bildirir; derleyici ve doğrulayıcı
**tek kural** okur (`SamplingCapabilityCheck` deseni, iki okuyucu). Kural birincil
bağlamayı **ve her yedek halkayı** denetler. Bildirmeyen üçüncü taraf sağlayıcı
için alan reddedilir (sessiz düşme yok). Bildirimin yeri Açık Soru 1'dedir.
`OpenAICompatible` sağlayıcıları Chat Completions yüzeyini paylaşır; destek satırı
`openai` ile aynıdır.

## 225.4 — Koruma tablosu

`AgentOptionsGuard.Chat`'te `AllowMultipleToolCalls`, `FrequencyPenalty`,
`PresencePenalty`, `Seed`, `StopSequences`, `ToolMode`, `TopK` `Free` → `Protect`.
Sonuç: uzantı bu üyeleri artık değiştiremez (bugünkü K1 yolu kapanır, veri yolu
açılır); K-1039 run kuralı aynı tabloyu okuduğu için run başına seçenek de bu
üyeleri reddeder. `guides/any-maf-feature.md` korunan listesi güncellenir; serbest
örnek `ThrowOnChatHistoryProviderConflict` kalır.

## 225.5 — Eski anahtar: yük, HTTP, yapılandırma

| Yol | Davranış |
|---|---|
| Saklanan yük (`agent_definitions` JSON) | Eski `model.allowConcurrentToolCalls` okumada `model.toolLoop.allowConcurrentToolCalls`'a çevrilir; yeni yazım yalnız yeni yeri yazar. Kapı: yük gidiş-dönüş testi (`AgentDefinitionPayloadRoundTripTests` deseni) eski JSON'la |
| HTTP (`PUT /api/agents/{name}`, `POST /api/agents/validate`) | Eski anahtar `400` alır ve yeni yolu adlandırır (K-1027 deseni: sessiz yok sayma yok). Bugün uç tanınmayan üyeyi **yok sayıyor** (yalnız `WorkflowFunctionSettings.cs:194` `Disallow` kullanır) — eski anahtarın açık denetimi gerekir |
| Yapılandırma (`Tracon:UtilityModel`) | Yardımcı model tool koşturmaz; eski anahtar bağlanmaz. Bilinmeyen anahtar raporu (`UtilityModel` "okunamayan sayılar" listesi deseni) varsa ona eklenir — ölçülmeli |

---

## Planlanan Public API

```csharp
// Tracon.Abstractions
public sealed record ModelBinding
{
    public long? Seed { get; init; }
    public int? TopK { get; init; }
    public IReadOnlyList<string>? StopSequences { get; init; }
    public float? FrequencyPenalty { get; init; }
    public float? PresencePenalty { get; init; }
    public string? ToolMode { get; init; }
    public bool? AllowMultipleToolCalls { get; init; }
    public string? ReasoningOutput { get; init; }
    public ToolLoopSettings ToolLoop { get; init; } = new();
    // AllowConcurrentToolCalls kalkar → ToolLoop.AllowConcurrentToolCalls
    // ToolLoopIterationLimit (internal) kalkar → ToolLoop.IterationLimit (internal)
}

public sealed record ToolLoopSettings
{
    public bool AllowConcurrentToolCalls { get; init; }
    public int? MaximumConsecutiveErrors { get; init; }
    public bool? TerminateOnUnknownCalls { get; init; }
    internal int? IterationLimit { get; init; }
}
```

"Bugün eklemek bedava / sonradan eklemek kırıcı": `ModelBinding` `sealed record`'dur;
GA'dan sonra alan taşımak kırıcıdır. Bugün `Shipped` boş.

### HTTP `endpoint`'leri

Yeni uç yok. `AgentDefinition` sözleşmesi büyür (OpenAPI üretimi, `Tracon.Client`
NSwag, `@tracon/client`). Eski anahtar §225.5.

### Arayüz payı

`screens/agent-editor/sections/model-section.tsx`: örnekleme alt bölümü (kapalı
katlanır), `ToolLoop` alt bölümü, `ReasoningOutput` seçici. `en.ts` + `tr.ts`.
Bundle payı faz başında `npm run build` (`scripts/postbuild.mjs`, 250 KB gzip)
çıktısından ölçülür ve yazılır.

---

## Planlanan Dosya Listesi

```
src/Tracon.Abstractions/Agents/ModelBinding.cs            (alanlar, ToolLoop)
src/Tracon.Abstractions/Agents/ToolLoopSettings.cs        (yeni)
src/Tracon.Abstractions/Models/<destek bildirimi>.cs      (Açık Soru 1)
src/Tracon.Core/Compilation/AgentDefinitionCompiler.ChatOptions.cs
src/Tracon.Core/Compilation/AgentDefinitionValidator.cs
src/Tracon.Core/Compilation/<SamplingSupportCheck>.cs     (SamplingCapabilityCheck deseni)
src/Tracon.Core/Extensions/AgentOptionsGuard.cs
src/Tracon.Core/Models/ModelProviderRegistry.cs
src/Tracon.Core/Models/FallbackChatClient.cs
src/Tracon.OpenAI/ · src/Tracon.Anthropic/ · src/Tracon.Google/   (destek bildirimi)
src/Tracon.Sql.Shared/Internal/AgentDefinitionPayload.cs  (eski anahtar okuma)
src/Tracon.AspNetCore/Contracts/AgentContracts.cs · Endpoints (eski anahtar 400)
src/Tracon.Client/ (üretilir) · src/Tracon.UI/frontend/src/screens/agent-editor/sections/model-section.tsx
```

---

## Hata Modları ve Testler

| Ne bozulabilir | Seviye | Test sınıfı |
|---|---|---|
| Alan `ChatOptions`'a yazılmaz (imza değişti, gövde değişmedi) | Fonksiyonel (gerçek host + `FakeModelProvider.Requests`) | `ModelSamplingFieldsTests` |
| Yedek halka döngü ayarını almaz | Birim + Fonksiyonel | `FallbackChatClientTests` (tek nesne), `FallbackToolSideEffectTests` deseni |
| Desteklenmeyen alan sessizce düşer | Fonksiyonel (`openai-responses` + `Seed` → derleme `400`, yedek halka dahil) | `ModelSamplingSupportTests` |
| Uzantı korunan üyeyi değiştirir | Birim (ratchet otomatik: `Every_protected_row_catches_a_change`) + Fonksiyonel | `AgentOptionsGuardTests`, `AgentExtensionRunTests` |
| Run başına seçenek yeni korunan üyeyi geçirir | Birim | `RunChatOptionsRuleTests` (yeni üyeler satırı) |
| Eski yük okunurken `AllowConcurrentToolCalls` kaybolur | Sözleşme/entegrasyon (üç SQL) | `AgentDefinitionPayloadRoundTripTests` (eski JSON) |
| Eski HTTP anahtarı sessizce yok sayılır | Fonksiyonel | `AgentDefinitionLegacyFieldTests` |
| `ToolMode = "required:<tool>"` tanımda olmayan tool'u adlandırır | Birim (doğrulayıcı yolu) | `AgentDefinitionValidatorTests` |
| Yanıt önbelleği yeni alanları ayırmaz | Birim | `TraconResponseCachingChatClientTests` (anahtar `ChatOptions` JSON'unun tamamını özetler — ölçüldü; testle kilitle) |

Beş soru: iptal (değişmez) · eşzamanlılık (`AllowConcurrentToolCalls` testleri taşınır) ·
boş/aşırı girdi (`StopSequences` boş, `TopK <= 0`, bilinmeyen `ReasoningOutput`) ·
başka kiracı (yok; tanım kiracı kapsamlı) · alt sistem hatası (sağlayıcı `400`'ü
bugünkü normalizasyondan geçer).

---

## Manuel Kabul Case'leri

| # | Ön koşul | Adımlar | Beklenen sonuç |
|---|---|---|---|
| 1 🤖 | Örnek uygulama, gerçek OpenAI | `openai-responses` bağlamalı agent'a `reasoningOutput: "summary"` yaz, run koş | Run olaylarında `ReasoningDelta` görünür |
| 2 🤖 | Aynı | `openai-responses` bağlamaya `seed: 7` yaz, kaydet | `400`; hata alanı ve sağlayıcıyı adlandırır |
| 3 🤖 | Konsol (Playwright) | Model bölümünde `TopK` ve `ToolLoop` ayarla, kaydet, yenile | Değerler kalır; sürüm farkı alanları gösterir |

---

## Açık Sorular

| # | Soru | Seçenekler | Öneri |
|---|---|---|---|
| 1 | Destek bildirimi nerede durur? | A: `IModelProvider`'a yeni opsiyonel arayüz (`ISamplingSupport`) · B: `ModelDescriptor` model başına bayraklar | A: destek yüzeye bağlıdır, modele değil (`openai` ↔ `openai-responses` aynı modeli taşır) |
| 2 | `ReasoningOutput` Chat Completions ve Anthropic'te ne yapar? | ölçülmeli | Faz başında decompile + gerçek tel; desteklemiyorsa `✗` |
| 3 | `StopSequences` üst sınırı (OpenAI 4) doğrulayıcıda mı sağlayıcıda mı? | A: destek bildirimi sınır taşır · B: sağlayıcının `400`'ü | A, ölçülürse |

---

## Bitiş Ölçütleri (DoD)

- [ ] Sekiz alan ve `ToolLoop` tanımdan sağlayıcı isteğine ulaşır (fonksiyonel test, her alan bir satır)
- [ ] `FallbackChatClient` `ToolLoop`'u tek nesne olarak kopyalar; elle alan kopyası yok (`grep -n "AllowConcurrentToolCalls =" src/Tracon.Core/Models/FallbackChatClient.cs` boş)
- [ ] Destek tablosundaki her `✗` derleme `400` verir; yedek halka dahil
- [ ] Yedi üye `Protected`; uzantı değiştirirse derleme düşer; run kuralı reddeder
- [ ] Eski yük okunur ve değer korunur (üç SQL); eski HTTP anahtarı `400` alır
- [ ] Dört doğrulama kapısı sıfır uyarı verir
- [ ] `samples/Tracon.Api` ile gerçek `run` yapıldı, çıktı belgeye yazıldı
- [ ] `secret` taraması boş döndü
- [ ] `🤖` case'ler `docs/manuel-test/10-ARAYUZ-AGENT-PLAYGROUND.md` içinde ve koşuldu
- [ ] `faz-denetim` koşuldu; 🔴 bulgu kalmadı
- [ ] `docs-site/` güncellendi (`tuketici-dokuman-senkronu`); `npm run check` temiz
- [ ] `en.ts` ve `tr.ts` eksiksiz; bundle payı ölçüldü ve yazıldı

### Doğrulama komutları

```bash
python3 scripts/kapi.py kapanis --taban <faz öncesi commit>
curl -s -X POST http://localhost:5081/tracon/api/agents/validate -H 'Content-Type: application/json' \
  -d '{"name":"x","instructions":"x","model":{"provider":"openai-responses","model":"gpt-5.4-mini","seed":7}}'
```

---

## Riskler

| Risk | Önlem |
|------|-------|
| Bir halka `ToolLoop`'un yeni üyesini okumaz (imza–gövde) | `faz-uygulama` Adım 4: `BuildPipeline`, `FallbackChatClient`, harness `AgentLoopBinding` gövdeleri elle izlenir |
| Uzantı kullanan tüketici kırılır (üye korunur) | Sürüm notu; hata mesajı tanım alanını adlandırır |
| Destek tablosu sağlayıcı SDK yükseltmesinde bayatlar | Destek bildirimi sağlayıcı paketindedir; SDK yükseltmesinde `maf-api-kesfi` § *Dördüncü adım* yeniden ölçer |

---

<!-- ============================================================
     AŞAĞISI ÇALIŞIRKEN DOLAR — doküman kapanışta silinir (`faz-kapat`).
     Kalıcı kısım `faz-tamamlama` Adım 5'te yerine taşınır. Başlıkları SİLME.
     ============================================================ -->

## Plandan Sapmalar

> Çalışırken dolar. Kapanışta kapanış commit mesajına taşınır.

## Bu Fazda Verilen Kararlar

> Çalışırken dolar. Plan numara rezerve etmez.

## Süreç Ölçümü

| Metrik | Değer |
|---|---|
| Plan revizyonu sayısı | |
| Düzeltme turu sayısı | |
| 🔴 bulgu: gerçek / gürültü / araştırılacak | |
| Fazın ürettiği regresyon | |
| Faz kapandıktan sonra bulunan kusur | ölçülmedi (kapanış anı) |

## Denetim Bulguları

> `faz-denetim` çıktısı.
