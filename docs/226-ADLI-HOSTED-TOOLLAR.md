# Faz 226 — Adlı Hosted Tool'lar: Kayıt, Yüzey Doğrulaması, Ücret

> **Durum:** 📋 Planlandı (2026-10-20)
> **Kademe:** tam — maliyet ve kota bütünlüğü (yeni ücret terimi bütçeye girer) · run olay sözleşmesi değişir (tanınan hosted çağrı `tracon.content` yerine tool olayı olur) · geri dönüşü pahalı karar
> **Plan onayı:** farukatasoy, 2026-10-20 (seçim: aynı `ToolNames`; kapsam: üç sağlayıcı)
> **Kaynak:** [ADAYLAR.md](ADAYLAR.md) · **F-603**
> **Genellik:** OpenAI Responses, Anthropic ve Google kullanan her tüketici. Sağlayıcının yerleşik tool'u (web search, code interpreter, file search) bugün yalnız uzantı provider'ıyla ve denetimsiz girer; harness web search'ün ücreti bugün hiçbir yerde sayılmaz (K-892). İş tüketici kodunda kalamaz: ücret, bütçe ve yüzey doğrulaması kontrol düzleminindir.
> **Karşı görüş:** "Code interpreter kiracı verisini sağlayıcının konteynerine çıkarır; tanımda adla açmak bir uyum kararıdır." Cevap: veri, modelin zaten gittiği sağlayıcıya gider (egress politikası sağlayıcıyı izinli saymıştır); yeni taraf yoktur. Tool kodda kaydedilir (K2), tanım yalnız adla seçer, varsayılan kapalıdır (K1); site sayfası sınırı yazar.
> **Önkoşul:** [Faz 225](225-MODEL-ORNEKLEME-VE-TOOL-DONGUSU.md) — sağlayıcı destek bildirimi mekanizmasını kurar; bu faz ona hosted tool türlerini ekler
> **Paketler:** `Tracon.Abstractions`, `.Core`, `.OpenAI`, `.Anthropic`, `.Google`, `.AspNetCore`, `.Client`, `.UI`
> **Yeni paket:** Yok · **Migration:** Yok
> **Public API:** büyüyor — `1.0.0` GA'dan (K-603) önce ucuz
> **Tüketici yüzeyi:** site: yeni `guides/hosted-tools.md`, `concepts/tools.md`, `capabilities.md`, `guides/context-and-memory.md` (harness web search ücreti), `concepts/runs.md` (olay) · sevk edilen: `AddHostedTool` XML `<example>`, `src/Tracon.Core/README.md`
> **Manuel test alanı:** `docs/manuel-test/05-SAGLAYICI-OPENAI.md`, `06-SAGLAYICI-DIGER.md` (sağlayıcı başına tel) ve `12-GOZLEMLENEBILIRLIK-MALIYET.md` (ücret) — `🤖` (gerçek sağlayıcı)

---

## Bu Faza Başlarken

1. Bu doküman
2. Kararlar — yalnız grep:
   ```bash
   grep -n "K-030 —\|K-059 —\|K-208 —\|K-483 —\|K-892 —\|K-1030 —\|K-1034 —\|K-1039 —" docs/KARARLAR.md
   ```
   **K-030** (`store=false`), **K-059** (`secret` yazılmaz), **K-208** (desteklenmeyen ayar sessiz düşmez), **K-483** (toplama ifadesine terim eklemek), **K-892** (harness web search yalnız tanım isteyince), **K-1030** (provider'ın tool'u tool zincirinden geçmez), **K-1034** (tanınmayan içerik `tracon.content`), **K-1039** (run başına seçenek çalışan tool'u reddeder)
3. Ölçüm kaynağı: [`MAF-KAPSAMA.md`](MAF-KAPSAMA.md) §8.0 C-12 satırı (OpenAI tel probu) ve bu dokümandaki Anthropic/Google tel probu
4. Alan hafızası: [`hafiza/olcum-kota-ve-secenekler.md`](hafiza/olcum-kota-ve-secenekler.md) (maliyet terimi, `Total()`), [`hafiza/model-boru-hatti.md`](hafiza/model-boru-hatti.md) (bütçe halkasının yeri), [`hafiza/run-olay-sozlesmesi.md`](hafiza/run-olay-sozlesmesi.md)
5. Faz 225'in kapanış notu (destek bildiriminin gerçekleşen imzası)

---

## Amaç

Tüketici sağlayıcının yerleşik tool'unu kodda adla kaydeder; tanım onu `ToolNames`
ile seçer. Derleyici bağlamanın (ve her yedek halkanın) o türü desteklediğini denetler;
desteklemeyen yüzey derleme hatası verir. Her hosted çağrı run kaydına tool olayı
olarak girer ve ücreti bütçeye, kotaya ve webhook toplamına girer.

- **F-603** — `AddHostedTool`, yüzey doğrulaması, ücret ve kayıt; web search, code interpreter, file search. Image generation ve hosted MCP kapsam dışı.

### Bugün ne çalışmıyor — doğrulanmış kanıt

| Kanıt | Gözlem |
|---|---|
| [`IToolRegistry.cs:19,35`](../src/Tracon.Abstractions/Tools/IToolRegistry.cs) | Kayıt defteri yalnız `AIFunctionDeclaration` döner; hosted tool (`AITool`) kaydedilemez |
| `AgentOptionsGuard.cs` `Tools` satırı | Uzantı `ChatOptions.Tools`'a hosted tool ekleyemez (`400`); provider yolundan girer ve denetlenmez (gerçek host probu 2026-10-20, §0 #32) |
| [`AgentDefinitionCompiler.Agents.cs:290`](../src/Tracon.Core/Compilation/AgentDefinitionCompiler.Agents.cs) | Tek hosted tool harness web search'tür (`DisableWebSearch = !harness.EnableWebSearch`) |
| [`ContentEventPayload.cs:57-64`](../src/Tracon.Core/Recording/ContentEventPayload.cs) | Hosted çağrı `tracon.content` olur (Faz 223); file search orada yok; ücret yok |
| OpenAI tel probu (2026-10-20) | `store=false` code interpreter, web search, file search'ü engellemez; `UsageDetails.AdditionalCounts` boş; code interpreter içeriğinde `RawRepresentation` yok (konteyner kimliği okunamaz) |
| MEAI `OpenAIChatClient` (decompile) | Chat Completions yalnız `HostedWebSearchTool`'u eşler; hosted code interpreter **sessizce düşer** (tel probu: hata yok, model tool'suz cevapladı) |
| Anthropic tel probu (2026-10-20, `claude-haiku-4-5-20251001`) | Web search ve code execution iki turda çalışır (JSON'dan geçen geçmişle); web search `AdditionalCounts["WebSearchRequests"]=1` raporlar; code execution sayı raporlamaz |
| Google tel probu (2026-10-20, `gemini-3.6-flash`) | Google search ve code execution iki turda çalışır; code execution `AdditionalCounts["ToolUsePromptTokenCount"]` raporlar; arama sayısı yok |
| Adaptör decompile | Anthropic `HostedWebSearchTool` → `WebSearchTool20250305`, `HostedCodeInterpreterTool` → `CodeExecutionTool20250825`; Google `HostedWebSearchTool` → `GoogleSearch`, `HostedCodeInterpreterTool` → code execution, `HostedFileSearchTool` → `Retrieval` (kaynaksız; ölçülmedi) |

> Kanıtlar 2026-10-20 tarihinde doğrulandı.

---

## 226.1 — Kayıt ve seçim

```csharp
builder.AddTracon()
    .AddHostedTool("docs_search", HostedToolKind.FileSearch,
        context => new HostedFileSearchTool { Inputs = [new HostedVectorStoreContent(context.Configuration["VectorStores:Docs"]!)] },
        description: "Searches the product documentation.");
```

- Tool kodda kaydedilir (K2); tanım `ToolNames`'te adıyla seçer (👤 kararı). Ad,
  function tool'ları ve hosted tool'lar arasında tekildir; çakışan kayıt açılışta
  hata verir.
- Hosted tool ayrı bir iç kayıt defterinde durur; `IToolRegistry` sözleşmesi değişmez.
  Derleyicinin `ResolveTools`'u iki defteri de okur.
- `GET /api/tools` hosted tool'u listeler; `ToolDescriptor`'a tür alanı eklenir
  (bugün `RunsOnClient` deseni). Konsol tek seçicide rozetle gösterir.
- Fabrika bağlam alır (kiracı, sağlayıcı adı, servisler): BYOK'ta vector store
  kimliği kiracının hesabındadır. Kimlik `secret` değildir; anahtar yazılmaz (K-059).
- Tür başına en çok bir hosted tool (aynı tür iki kez seçilirse doğrulama hatası):
  tel içeriği kayıt adını taşımaz; türden ada eşleme tektir.
- Harness `EnableWebSearch` (K-892) kalır; ücreti bu fazın yolundan sayılır.

## 226.2 — Yüzey doğrulaması

Faz 225'in destek bildirimi hosted türleri taşır. Ölçülen başlangıç tablosu:

| Tür | `openai` | `openai-responses` | `anthropic` | `google` |
|---|---|---|---|---|
| `web_search` | ✗ (K-892: arama dışı modelde `400`) | ✓ | ✓ | ✓ |
| `code_interpreter` | ✗ (sessiz düşer, ölçüldü) | ✓ | ✓ | ✓ |
| `file_search` | ✗ | ✓ | ✗ | ✗ (`Retrieval` kaynaksız; ölçülmedi) |

Derleyici birincil bağlamayı ve her yedek halkayı denetler (`SamplingCapabilityCheck`
deseni). Desteklemeyen halka derleme hatası verir ve türü ve sağlayıcıyı adlandırır.

## 226.3 — Ücret

- Yapılandırma: `Tracon:Pricing:HostedTools:<provider>:<kind>` → `PerCall` (ondalık).
  Fiyat koda yazılmaz (bugünkü `Images`, `Voice` deseni).
- Birim: çağrı içeriği sayısı (`WebSearchToolCallContent`, `CodeInterpreterToolCallContent`,
  file search için ham tipi `FileSearchCallResponseItem` olan taban `ToolCallContent`).
  Code interpreter konteyner başına faturalanır ama konteyner kimliği içerikte yok
  (ölçüldü) → çağrı başına fiyat **tahmindir** (`IsEstimated = true`). Anthropic'in
  `WebSearchRequests` sayısı çapraz denetim için okunur (Açık Soru 2).
- Ücret yeni bir toplama terimi açmaz: tool dışı maliyet yolundan girer
  (`ToolCallUsage` → `RunRecord.NonTokenCost`; bütçe, kota ve `run.completed`
  webhook'u onu zaten sayar). K-483: `Total()` değişmez.
- Bütçe hosted ücreti **sonraki model turundan önce** görmelidir (bütçe tavanı olan
  kiracı). Rapor noktası Açık Soru 1'dedir.

## 226.4 — Kayıt

Kayıtlı türün çağrısı `ToolInvoking`/`ToolInvoked` olur; araç adı kayıt adıdır, yük
çağrı kimliği ve `ToolCallUsage` taşır. Kayıtsız ya da bilinmeyen hosted içerik
bugünkü gibi `tracon.content` kalır (K-1034). File search `ContentEventPayload`'a
eklenir. Atıf (`CitationAnnotation`, web search'te ölçüldü) bu fazda değil, F-595'tedir.

```mermaid
flowchart LR
    M["Model yanıtı<br/>hosted çağrı içeriği"] --> B["bütçe/ücret noktası<br/>(Açık Soru 1)"]
    B -->|"ToolCallUsage"| N["NonTokenCost<br/>bütçe · kota · webhook"]
    M --> R["RunRecordingAgent<br/>ToolInvoking/ToolInvoked"]
```

## 226.5 — Kapsam dışı

- **Image generation:** görsel geçmişe bayt olarak girer (tel probu: bir görselde 1,5 MB);
  Tracon `generate_image` ek kimliği döner. Sunulmaz.
- **Hosted MCP:** F-604 (ayrı güvenlik soruları).
- **Hosted tool için tool zinciri:** yetki, onay, zaman aşımı yoktur; tool sağlayıcıda
  koşar. `RequiredPermission` hosted kayıtta yoktur; site sınırı yazar.

---

## Planlanan Public API

```csharp
// Tracon.Abstractions
public enum HostedToolKind { WebSearch, CodeInterpreter, FileSearch }

public sealed class HostedToolContext
{
    public required string TenantId { get; init; }
    public required string Provider { get; init; }
    public required IServiceProvider Services { get; init; }
}

// Tracon.Core
public static ITraconBuilder AddHostedTool(
    this ITraconBuilder builder,
    string name,
    HostedToolKind kind,
    Func<HostedToolContext, AITool> factory,
    string? description = null);

// Tracon.Core — TraconPricingOptions
public IDictionary<string, IDictionary<string, HostedToolPriceOverride>> HostedTools { get; }
public sealed class HostedToolPriceOverride { public decimal? PerCall { get; set; } }
```

`ToolDescriptor`'a hosted tür alanı (ad faz başında `RunsOnClient` ile birlikte seçilir).
Faz 225'in destek bildirimi hosted türlerle genişler. **Doğrulanmadı — `maf-api-kesfi`
ile ölçülmeli:** `HostedFileSearchTool.Inputs`/`HostedVectorStoreContent` (tel probunda
kullanıldı, imza dökümü alınmadı).

### HTTP `endpoint`'leri

Yeni uç yok. `GET /api/tools` yanıtı büyür (OpenAPI, `Tracon.Client`, `@tracon/client`).

### Arayüz payı

`screens/agent-editor/sections/tools-section.tsx` rozet ve yüzey uyarısı; run
ekranında hosted çağrı satırı. Bundle payı faz başında ölçülür.

---

## Planlanan Dosya Listesi

```
src/Tracon.Abstractions/Tools/HostedToolKind.cs, HostedToolContext.cs, ToolDescriptor.cs
src/Tracon.Core/Tools/HostedToolRegistry.cs            (iç)
src/Tracon.Core/TraconBuilderExtensions.Tools.cs        (AddHostedTool)
src/Tracon.Core/Compilation/AgentDefinitionCompiler.ChatOptions.cs (ResolveTools)
src/Tracon.Core/Compilation/AgentDefinitionValidator.cs (tür başına tek, yüzey)
src/Tracon.Core/Recording/ContentEventPayload.cs, RunRecordingAgent.Persistence.cs
src/Tracon.Core/Recording/<HostedToolUsage>.cs          (Açık Soru 1)
src/Tracon.Core/TraconOptions.cs                        (HostedTools fiyatı)
src/Tracon.OpenAI/ · src/Tracon.Anthropic/ · src/Tracon.Google/  (destek bildirimi)
src/Tracon.AspNetCore/Endpoints/CatalogEndpoints.cs
src/Tracon.UI/frontend/src/screens/agent-editor/sections/tools-section.tsx
```

---

## Hata Modları ve Testler

| Ne bozulabilir | Seviye | Test sınıfı |
|---|---|---|
| Hosted tool modele ulaşmaz / yanlış tür eşlenir | Fonksiyonel (`FakeModelProvider.Requests`) | `HostedToolSelectionTests` |
| Desteklemeyen yüzey sessizce düşürür (birincil ya da yedek) | Fonksiyonel | `HostedToolSurfaceTests` |
| Ad function tool'uyla çakışır | Birim (kayıt) + Fonksiyonel (açılış) | `HostedToolRegistrationTests` |
| Ücret iki kez sayılır ya da hiç sayılmaz (K-483) | Fonksiyonel (bütçe + kota + webhook toplamı) | `HostedToolCostTests` |
| Bütçe tavanı hosted ücretten sonra bir tur daha koşturur | Fonksiyonel | `HostedToolBudgetTests` |
| Başka kiracının vector store kimliği kullanılır | Fonksiyonel (iki kiracı, BYOK) | `HostedToolTenantTests` |
| Kayıtlı çağrı `tracon.content` kalır / kayıtsız içerik kaybolur | Fonksiyonel | `HostedToolRecordingTests` |
| Harness web search ücreti sayılmaz | Fonksiyonel | `HarnessWebSearchCostTests` |

Beş soru: iptal (hosted çağrı sağlayıcıda; iptal yalnız sonraki turu durdurur — testte
belgelenir) · eşzamanlılık (bütçe ayrımı mevcut halkada) · boş/aşırı girdi (fiyat yok →
ücret `0` ve uyarı mı, hata mı: Açık Soru 3) · başka kiracı (fabrika bağlamı) · alt
sistem hatası (fiyat yapılandırması okunamaz → run durmaz, loglanır).

---

## Manuel Kabul Case'leri

| # | Ön koşul | Adımlar | Beklenen sonuç |
|---|---|---|---|
| 1 🤖 | Örnek uygulama, gerçek OpenAI, `openai-responses` | `web_search` ve `code_interpreter` kayıtlı agent'ı koş | Run kaydında iki tool olayı; `NonTokenCost` fiyat × çağrı |
| 2 🤖 | Gerçek Anthropic | Aynı agent `anthropic` bağlamayla | İki tur çalışır; web search ücreti `WebSearchRequests` ile tutar |
| 3 🤖 | Gerçek Google | Aynı agent `google` bağlamayla | İki tur çalışır |
| 4 🤖 | `openai` (Chat Completions) bağlama | `code_interpreter` seçili tanımı kaydet | `400`; tür ve sağlayıcı adlandırılır |

---

## Açık Sorular

| # | Soru | Seçenekler | Öneri |
|---|---|---|---|
| 1 | Ücret nerede raporlanır? | A: döngü içi halka (bütçe halkasının yanında; her model yanıtında içerik sayar) · B: `RunRecordingAgent` (yanıt sonunda) | A: bütçe ücreti sonraki turdan önce görür; B tur içi tavanı kaçırır |
| 2 | Anthropic `WebSearchRequests` ile içerik sayısı ayrışırsa hangisi? | A: sağlayıcı sayısı · B: içerik sayısı | A (sağlayıcının faturası); ayrışma loglanır |
| 3 | Fiyatı yapılandırılmamış hosted tür | A: ücret `0`, run uyarısı · B: derleme hatası | A: model fiyatı yokken bugünkü davranışla aynı |
| 4 | Google `file_search` (`Retrieval`) | ölçülmeli | Faz başında tel probu; çalışmıyorsa `✗` kalır |

---

## Bitiş Ölçütleri (DoD)

- [ ] `AddHostedTool` ile kaydedilen üç tür, `ToolNames` ile seçilince modele ulaşır
- [ ] Destek tablosundaki her `✗` derleme `400` verir; yedek halka dahil
- [ ] Hosted çağrı run kaydında tool olayıdır; ücret `NonTokenCost`'a, bütçeye, kotaya ve webhook'a bir kez girer
- [ ] Harness web search ücreti sayılır
- [ ] Dört doğrulama kapısı sıfır uyarı verir
- [ ] `samples/Tracon.Api` ile gerçek `run` (OpenAI Responses) yapıldı, çıktı belgeye yazıldı
- [ ] `secret` taraması boş döndü
- [ ] `🤖` case'ler koşuldu (üç sağlayıcı)
- [ ] `faz-denetim` koşuldu; 🔴 bulgu kalmadı
- [ ] `docs-site/` güncellendi (`tuketici-dokuman-senkronu`); `npm run check` temiz
- [ ] `en.ts` ve `tr.ts` eksiksiz; bundle payı ölçüldü ve yazıldı

---

## Riskler

| Risk | Önlem |
|------|-------|
| Sağlayıcı fiyat birimi değişir (konteyner, istem başına arama) | Fiyat yapılandırmadadır; birim tahmini `IsEstimated` taşır |
| Kayıt olay tipinin değişmesi tüketici panosunu bozar | Sürüm notu; kayıtsız içerik `tracon.content` kalır |
| Code interpreter çıktısı (dosya) geçmişe büyük içerik koyar | Ölçülmeli: tel probunda çıktı boştu (`include` istenmedi); dosya çıktısı istenirse ek yoluna gider |

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
