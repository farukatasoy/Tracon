# Faz 13 — Bağlam Sıkıştırma ve Bellek Sağlayıcıları

> **Durum:** ✅ Tamamlandı (2026-08-02)
> **Kaynak:** [BEYIN-FIRTINASI.md](../BEYIN-FIRTINASI.md) · **F-11**
> **Önkoşul:** Yok (Faz 10 ve 12 bu fazı **gerekli** kılar — bağlam onlarla büyür)
> **Paketler:** `AgentPrism.Abstractions`, `.Core`, `.PostgreSql`, `.AspNetCore`, `.UI`
> **Yeni paket:** Yok (ölçüldü — bkz. §13.4) · **Migration:** Yok

---

## Bu Faza Başlarken (Faz 14 için)

1. [`12-AGENT-CAGRI-GRAFIGI.md`](12-AGENT-CAGRI-GRAFIGI.md) — §12.2 (ambient kapsam) ve §12.4
2. [`02-POSTGRESQL-KALICILIK.md`](02-POSTGRESQL-KALICILIK.md) — `PostgresChatHistoryProvider`, oturum durumu
3. [`KARARLAR.md`](../../KARARLAR.md) — **K-027**, **K-037**, **K-062**, **K-097**, ve bu fazda eklenen **K-104…K-110**
4. [`MIMARI.md`](../../MIMARI.md) — bölüm 4 "Faz 13'te kullanılanlar"
5. Bu doküman

---

## Faz 12'den Devralınanlar

### Sağlayıcı listesi hazır

`AgentDefinitionCompiler.CompileChatAgent` bir sağlayıcı **listesi** kurar.
`CompactionProvider` ve bellek sağlayıcıları (`FileMemoryProvider`,
`TodoProvider`, `TextSearchProvider`) bu listeye eklendi — aşağıya bakın.

### Çalıştırma kapsamı

```csharp
AgentPrismRunContext.Current            // okuma
AgentPrismRunContext.SetCurrent(scope)  // yazma
```

`AgentRunScope` bu fazda yeni bir alan kazandı: `internal CompactionUsageAccumulator? ExtraUsage`
(bkz. §13.2). Sıkıştırma olayını akışa yazmanın **tek yolu** hâlâ
`AgentPrismRunContext.Current?.Writer`'dır.

### 🚨 Bilinen tuzaklar (Faz 12'de ölçüldü, bu fazda da geçerli kaldı)

| Tuzak | Kural |
|-------|-------|
| `async IAsyncEnumerable` gövdesinde yapılan `AsyncLocal` ataması **`yield return` sınırını aşmaz** | Kapsam her `MoveNextAsync`'ten **hemen önce** yeniden yazılmalıdır. |
| MAF alt agent'ı `options = null` ile çağırır | Bir `AIContextProvider`'ın açtığı tool'dan tetiklenen çağrılarda gelen ayarlara güvenilemez. |

### 🚨 Bu fazda keşfedilen yeni tuzaklar

| Tuzak | Kural |
|-------|-------|
| `CompactionStrategy.CompactCoreAsync` **korumalıdır** ve C#'ta bir kardeş türetilmiş tipin örneği üzerinden çağrılamaz | Bir strateji sarmalayıcısı (`ObservedCompactionStrategy`) iç stratejiyi **`CompactAsync`** (public, sanal olmayan) ile çağırır. `CompactAsync` iç stratejinin **kendi** tetikleyicisini tekrar kontrol eder — bu yüzden iç strateji de dış ile **aynı** tetikleyiciyle kurulur (`ContextWindow`/`Pipeline` hariç, onlar kendi iç tetikleyicilerini kendileri taşır). |
| `AgentDefinitionPayload` (`AgentPrism.PostgreSql`) `AgentDefinition`'ı **ayrı bir DTO** ile serileştirir, kaynak üretecinin `AgentDefinition`'dan otomatik türettiği şema **değil** | Yeni bir `AgentDefinition` alanı eklendiğinde `AgentPrismCoreJsonContext` yeterli değildir — `Internal/AgentDefinitionPayload.cs`'e de elle eklenmelidir. Ölçüldü: `Compaction`/`Memory` alanları eklenmeden önce PostgreSQL round-trip testi sessizce `null` döndürüyordu (build/test kırmıyordu, yalnızca round-trip testi yakaladı). **Aynı dosyada Faz 12'den kalma bağımsız bir hata daha bulundu ve düzeltildi:** `CallableAgentNames` de bu payload'da hiç yoktu — PostgreSQL'e yazılan bir agent'ın çağırabileceği alt agent listesi sessizce kayboluyordu. |
| `CompactionMessageIndex` gerçek bir `Microsoft.ML.Tokenizers.Tokenizer` ister ama `CompactionProvider`'ın kendisi **tokenizer parametresi almaz** | Ölçüldü: `CompactionProvider(strategy, stateKey, loggerFactory)` ile kurulan bir agent gerçek bir `RunAsync` çağrısında hatasız çalıştı — MAF tokenizer'ı içeride kendisi çözüyor, `Microsoft.ML.Tokenizers.Data.*` gibi ek bir veri paketi **gerekmedi**. Bu, §13.4'ün "yeni paket yok" kararını doğrular. |
| Enum HTTP'ye yansıyorsa `JsonStringEnumConverter` **eklenmeden** unutmak sessiz bir sayı sızıntısı üretir | `CompactionStrategyKind` ilk yazıldığında bu öznitelik unutuldu; bir fonksiyonel test (`.GetString()` beklerken sayı geldi) yakaladı. K-040 deseni her yeni HTTP'ye yansıyan enum için tekrarlanmalı. |

---

## Amaç

MAF'ta hazır olan ve daha önce hiç kullanılmayan bağlam yönetimi yetenekleri
açığa çıkarıldı: beş sıkıştırma stratejisi (+ sabit sıralı bir pipeline) ve üç
bellek sağlayıcısı (dosya belleği, todo, metin araması), hem düz `ChatClientAgent`
hem `HarnessAgent` yolunda.

---

## Doğrulanmış MAF API'si (reflection, MAF 1.16.0 — uygulama sırasında ikinci kez doğrulandı)

```csharp
namespace Microsoft.Agents.AI.Compaction;

sealed class CompactionProvider : AIContextProvider {
    CompactionProvider(CompactionStrategy compactionStrategy, string? stateKey, ILoggerFactory? loggerFactory);
}

abstract class CompactionStrategy {
    protected CompactionStrategy(CompactionTrigger trigger, CompactionTrigger? target);
    // PUBLIC ve SANAL DEĞİL — bir sarmalayıcının iç stratejiyi çağırdığı yer burasıdır.
    public ValueTask<bool> CompactAsync(CompactionMessageIndex index, ILogger logger, CancellationToken ct);
    protected virtual ValueTask<bool> CompactCoreAsync(CompactionMessageIndex index, ILogger logger, CancellationToken ct);
}

sealed class SlidingWindowCompactionStrategy(CompactionTrigger trigger, int minimumPreservedTurns, CompactionTrigger? target) : CompactionStrategy;
sealed class TruncationCompactionStrategy(CompactionTrigger trigger, int minimumPreservedGroups, CompactionTrigger? target) : CompactionStrategy;
sealed class ToolResultCompactionStrategy(CompactionTrigger trigger, int minimumPreservedGroups, CompactionTrigger? target) : CompactionStrategy;
sealed class SummarizationCompactionStrategy(IChatClient chatClient, CompactionTrigger trigger, int minimumPreservedGroups, string? summarizationPrompt, CompactionTrigger? target) : CompactionStrategy;
sealed class ContextWindowCompactionStrategy(int maxContextWindowTokens, int maxOutputTokens, double toolEvictionThreshold, double truncationThreshold) : CompactionStrategy; // tetikleyici YOK — kendi içinde kurar
sealed class PipelineCompactionStrategy(IEnumerable<CompactionStrategy> strategies) : CompactionStrategy;              // tetikleyici YOK — her alt strateji kendi tetikleyicisini taşır

sealed class CompactionMessageIndex(IList<CompactionMessageGroup> groups, Tokenizer tokenizer) {
    int TotalMessageCount / IncludedMessageCount / TotalTokenCount / IncludedTokenCount { get; }
}
delegate bool CompactionTrigger(CompactionMessageIndex index);
static class CompactionTriggers { TokensExceed/MessagesExceed/TurnsExceed/GroupsExceed/HasToolCalls/TokensBelow/All/Any }

// Bellek — DOĞRULANDI, dokümanın ilk taslağından SAPMA içerir (bkz. §13.3)
sealed class FileMemoryProvider(AgentFileStore fileStore, Func<AgentSession,FileMemoryState>? stateInitializer, FileMemoryProviderOptions? options) : AIContextProvider;
sealed class TodoProvider(TodoProviderOptions? options) : AIContextProvider;
sealed class TextSearchProvider(Func<string,CancellationToken,Task<IEnumerable<TextSearchResult>>> searchAsync, TextSearchProviderOptions? options, ILoggerFactory? lf) : MessageAIContextProvider;
abstract class AgentFileStore { ReadAsync/WriteAsync/ListChildrenAsync/SearchAsync/DeleteAsync/CreateDirectoryAsync/FileExistsAsync }
sealed class InMemoryAgentFileStore : AgentFileStore;   // bu fazda kullanılan somut depo
sealed class FileSystemAgentFileStore : AgentFileStore; // MAF'ın kendi disk uygulaması — kullanılmadı (K-062 ruhu)

// 🚨 SAPMA: dokümanın ilk taslağı ChatHistoryMemoryProvider'ı basit "oturum içi
// bellek" sanıyordu. Gerçek imza:
sealed class ChatHistoryMemoryProvider(VectorStore vectorStore, string collectionName, int vectorDimensions,
    Func<AgentSession,State>? stateInitializer, ChatHistoryMemoryProviderOptions? options, ILoggerFactory? loggerFactory)
    : MessageAIContextProvider;
// Vektör tabanlı anlamsal aramadır; depoda somut bir VectorStore implementasyonu
// yok. KARAR (kullanıcı onayladı, K-105): bu fazda YOK — TextSearchProvider'ın
// vektör aramasının zaten kapsam dışı bırakılmasıyla tutarlı.

// Harness bağlanma noktaları (hepsi doğrulandı ve kullanıldı)
HarnessAgentOptions {
    CompactionStrategy CompactionStrategy; bool DisableCompaction;
    AgentFileStore FileMemoryStore; bool DisableFileMemory; bool DisableTodoProvider;
    IEnumerable<AIContextProvider> AIContextProviders; // TextSearchProvider bu yoldan eklendi, dedike alanı yok
}
```

---

## 13.1 — Agent Tanımında Sıkıştırma Ayarı (gerçekleşen)

```csharp
public sealed record CompactionSettings
{
    public CompactionStrategyKind Strategy { get; init; } = CompactionStrategyKind.None;
    public int? TriggerTokens { get; init; }
    public int? TriggerMessages { get; init; }
    public int? TriggerTurns { get; init; }
    public int? MinimumPreservedTurns { get; init; }
    public int? MinimumPreservedGroups { get; init; }
    public int? MaxContextWindowTokens { get; init; }
    public int? MaxOutputTokens { get; init; }
    public string? SummarizationPrompt { get; init; }
    public ModelBinding? SummarizationModel { get; init; }
}

public enum CompactionStrategyKind
{
    None, SlidingWindow, Truncation, ToolResult, Summarization, ContextWindow, Pipeline
}

public sealed record MemorySettings
{
    public bool EnableFileMemory { get; init; }
    public bool EnableTodo { get; init; }
    public bool EnableTextSearch { get; init; }
    // EnableChatHistoryMemory YOK — bkz. yukarıdaki SAPMA notu.
}

public sealed record AgentDefinition
{
    // ...mevcut üyeler
    public CompactionSettings? Compaction { get; init; }
    public MemorySettings? Memory { get; init; }
}
```

Planla **birebir aynı** — tek fark `MemorySettings`'ten `EnableChatHistoryMemory`
alanının çıkarılmış olması (kapsam dışı bırakma kararı).

Geçersiz birleşim **derleme hatasıdır** (`AgentDefinitionCompiler.BuildCompactionStrategy`):
- Tetikleyicisiz `SlidingWindow`/`Truncation`/`ToolResult`/`Summarization`/`Pipeline` → `AgentPrismCompilationException`
- `ContextWindow` + `MaxContextWindowTokens` yok → `AgentPrismCompilationException` (bu strateji tetikleyici gerektirmez, ama bu alan zorunludur)
- Harness'ta `Compaction.Strategy != None` + `Harness.DisableCompaction == true` → çakışma hatası (aynısı `EnableFileMemory`/`DisableFileMemory` ve `EnableTodo`/`DisableTodoProvider` için)

`Pipeline` sırası sabit ve kodda kurulu: `ToolResultCompactionStrategy → SlidingWindowCompactionStrategy → SummarizationCompactionStrategy`.

---

## 13.2 — Özetleme Modeli (gerçekleşen)

Karar planla birebir aynı uygulandı: `CompactionSettings.SummarizationModel`
→ `AgentPrismOptions.UtilityModel` → agent'ın kendi modeli
(`AgentDefinitionCompiler.ResolveSummarizationChatClient`).

Özetleme çağrısı `Core/Compilation/CompactionUsageTrackingChatClient.cs` ile
sarılır: kendi `compact_history` span'ini açar
(`AgentPrismDiagnostics.CompactHistoryActivityName`) ve token kullanımını
`AgentPrismRunContext.Current?.ExtraUsage` üzerinden toplar. `RunRecordingAgent.CompleteAsync`
bu toplamı çalıştırmanın nihai `RunUsage`'ına **birleştirir** (`MergeUsage`) —
hem `runs.usage` hem `AgentRunBudget` özetleme maliyetini görür. Gerçek bir
çalıştırmayla doğrulandı (bkz. Testler).

Özetleme olayı **ayrıca** `run_events`'e `RunEventType.HistoryCompacted` olarak
yazılır — bu, `Core/Compilation/ObservedCompactionStrategy.cs`'in işi ve
**her** strateji türü için (yalnız Summarization değil) çalışır.

---

## 13.3 — Bellek Sağlayıcıları (gerçekleşen, planla sapma var)

```csharp
public sealed record MemorySettings
{
    public bool EnableFileMemory { get; init; }
    public bool EnableTodo { get; init; }
    public bool EnableTextSearch { get; init; }
}
```

- **`FileMemoryProvider`** — planla aynı: `InMemoryAgentFileStore` ile kurulu
  (`AgentPrismServiceCollectionExtensions`, `TryAddSingleton<AgentFileStore>`).
  Kalıcı sürüm hâlâ ileri bir faza bırakıldı.
- **`TodoProvider`** — planla aynı.
- **`TextSearchProvider`** — planla aynı, kayıtlı `AgentFileStore` üzerinde
  regex aramasıyla çalışır (`AgentDefinitionCompiler.SearchFileStoreAsync`).
  Kalıcı bir `AgentFileStore` kayıt edilirse (ör. gelecekte `PostgresAgentFileStore`)
  kod değişmeden kalıcı aramaya döner.
- **`ChatHistoryMemoryProvider` — 🚨 planla SAPMA, kapsam dışı bırakıldı.**
  Dokümanın ilk taslağı bunu basit "oturum içi bellek" sanıyordu. Reflection
  ile doğrulandı: gerçek kurucusu `VectorStore` + embedding boyutu istiyor —
  vektör tabanlı anlamsal arama. Depoda somut bir `VectorStore` implementasyonu
  yok. **Karar (kullanıcı onayladı, K-105):** bu fazın kapsamı dışında;
  `TextSearchProvider`'ın vektör aramasının zaten kapsam dışı bırakılmasıyla
  tutarlı. Vektör deposu kararı verildiğinde ayrı bir faz olarak planlanmalı.

---

## 13.4 — Tokenizer Bağımlılığı — ÖLÇÜLDÜ

```bash
dotnet list src/AgentPrism.Core/AgentPrism.Core.csproj package --include-transitive | grep -i token
# > Microsoft.ML.Tokenizers   2.0.0   (Microsoft.Agents.AI.Abstractions'ın geçişli bağımlılığı)
```

**Sonuç: yeni paket gerekmedi.** Sıkıştırma `AgentPrism.Core` içinde kaldı,
ayrı bir `AgentPrism.Compaction` paketi açılmadı.

**Ek doğrulama (gerçek çalıştırma ile):** `CompactionProvider` kurucusu bir
`Tokenizer` parametresi almıyor; MAF içeride kendi tokenizer'ını çözüyor.
Gerçek bir `RunAsync` çağrısı `Microsoft.ML.Tokenizers.Data.*` gibi bir ek veri
paketi olmadan **hatasız** çalıştı (bkz. Testler → "Gerçek kanıt"). Bu, ilk
ölçümü ikinci kez ve daha güçlü biçimde doğruladı — karar K-104.

---

## 13.5 — Arayüz (gerçekleşen)

- Agent düzenleyicisinde yeni **"Context"** paneli (`agent-editor.tsx`):
  strateji seçimi, koşullu tetikleyici/korunan-sayı alanları, `ContextWindow`
  için max pencere/çıktı token'ları, `Summarization`/`Pipeline` için özet
  istemi ve model provider/model metin girdileri, ayrı bir "Memory" bölümünde
  üç bellek checkbox'ı.
- Oturum/çalıştırma detayında (`run-detail.tsx`): `HistoryCompacted` olayı
  `history.compacted` etiketi ve mor (`--ap-violet`) renkle rozet listesinde
  görünür.
- Playground transkriptinde (`lib/transcript.ts` + `components/transcript.tsx`):
  yeni `'compaction'` transkript türü, "N mesaj özetlendi" satırı + `title`
  ipucunda önce/sonra sayıları.
- **Bütçe:** taban (Faz 12 sonrası) 97,3 KB gzip → Faz 13 sonrası 98,2 KB gzip.
  **Fark: +0,9 KB**, hedefin (+4 KB) çok altında.

---

## Testler (gerçekleşen)

| Proje | Dosya | Kapsam |
|-------|-------|--------|
| `AgentPrism.Core.UnitTests` | `Compilation/AgentDefinitionCompilerTests.cs` | Her `CompactionStrategyKind` sorunsuz kuruluyor; tetikleyicisiz strateji ve `ContextWindow` eksik alan derleme hatası veriyor; Harness çakışma denetimleri; `SummarizationModel` çözümleme sırası (agent → yardımcı model → agent'ın kendi modeli) üç ayrı testte; `Pipeline` sabit sırası `ObservedCompactionStrategy.Inner` (test-only `internal` erişim) üzerinden doğrulanıyor; bellek sağlayıcıları (dosya deposu yoksa hata, varsa üç sağlayıcı kuruluyor) |
| `AgentPrism.Core.UnitTests` | `Recording/CompactionUsageAccumulatorTests.cs` | Toplama aritmetiği, null-safe `Add`, boşken `null` |
| `AgentPrism.Core.UnitTests` | `Recording/RunRecordingAgentTests.cs` (genişletildi) | **Gerçek bir MAF `CompactionProvider` zinciriyle** (sahte `IChatClient`, gerçek `SlidingWindowCompactionStrategy`): uzun bir konuşma `RunEventType.HistoryCompacted` üretiyor; özetleme senaryosunda token kullanımı `runs.usage`'a doğru ekleniyor |
| `AgentPrism.PostgreSql.IntegrationTests` | `Contracts/AgentDefinitionStoreContract.cs` (genişletildi) | `Compaction`+`Memory`+`CallableAgentNames` dolu bir tanımın PostgreSQL round-trip'i — bu, `AgentDefinitionPayload`'daki eksikliği yakaladı (yukarıdaki tuzak notu) |
| `AgentPrism.AspNetCore.FunctionalTests` | `AgentCrudTests.cs` (genişletildi) | Compaction/Memory ayarlarının HTTP üzerinden round-trip'i — bu, `CompactionStrategyKind`'in `JsonStringEnumConverter` eksikliğini yakaladı |
| `AgentPrism.UI` (vitest + tsc) | mevcut dosyalar | Yeni `'compaction'` transkript türü mevcut testleri kırmadı; `tsc --noEmit` temiz |
| `AgentPrism.Ui.E2ETests` | `UiTests.cs` (genişletildi) | Gerçek bir tarayıcıda: strateji seçilene kadar koşullu alanlar gizli; `SlidingWindow` seçilip tetikleyici/memory dolduruluncaya kadar "Request preview" panelinin canlı JSON'unda doğru yansıdığı doğrulanıyor |

**Gerçek kanıt (örnek uygulama, gerçek API maliyeti yok):** `samples/AgentPrism.Api`
`EchoModelProvider` ile (OpenAI/OpenRouter anahtarları o oturum için boşaltılarak)
çalıştırıldı. `SlidingWindow` (TriggerMessages=6, MinimumPreservedTurns=1) ile
kurulu bir agent'a aynı oturumda 5 tur mesaj gönderildi. 4. turda sıkıştırma
gerçekten tetiklendi:

```
event: HistoryCompacted
text: "2 mesaj ozetlendi"
payload: beforeMessages=7, afterMessages=5, beforeTokens=31, afterTokens=22
```

---

## Bu Fazda Verilen Kararlar (gerçekleşen — K-104'ten devam)

1. **Sıkıştırma `AIContextProviders`/`HarnessAgentOptions.CompactionStrategy` üzerinden bağlanır** (K-053 ile tutarlı).
2. **Tokenizer bağımlılığı zaten geçişli — yeni paket yok** (K-104).
3. **`ChatHistoryMemoryProvider` bu fazın kapsamı dışında** — VectorStore gerekçesiyle (K-105, kullanıcı kararı).
4. **Sıkıştırma varsayılan kapalıdır** (K-106, kullanıcı kararı).
5. **Özetlenen mesajlar `conversation_items`'ta saklanır, silinmez** (K-107, kullanıcı kararı).
6. **Özet modeli çözümleme sırası:** agent ayarı → `AgentPrismOptions.UtilityModel` → agent'ın kendi modeli (K-108).
7. **Özet token'ları çalıştırmanın toplamına dâhildir** (K-108, `CompactionUsageAccumulator`/`MergeUsage` ile).
8. **`Pipeline` sırası sabittir:** ToolResult → SlidingWindow → Summarization (K-109).
9. **`TextSearchProvider` kayıtlı `AgentFileStore`'a bağımlıdır**, bu faz `InMemoryAgentFileStore` (K-110).

Tam gerekçeler `docs/KARARLAR.md`'de.

---

## Bitiş Ölçütleri (DoD)

- [x] Beş stratejinin (+ Pipeline) her biri bir agent tanımından kurulabiliyor
- [x] Uzun konuşmada sıkıştırma tetikleniyor; öncesi/sonrası ölçüm bu dokümanda (yukarıda)
- [x] Özet çağrısının token'ları çalıştırma toplamında görünüyor (`RunRecordingAgentTests.Ozetleme_token_kullanimi_calistirma_toplamina_eklenir`)
- [x] Sıkıştırma sonrası oturum kaydedilip geri yüklenebiliyor (PostgreSQL round-trip testi)
- [x] Geçersiz ayar derlemede hata veriyor, sessizce yok sayılmıyor
- [x] Tokenizer bağımlılığı ölçüldü ve karar yazıldı (K-104)
- [x] Dört doğrulama kapısı sıfır uyarı

---

## Riskler (gerçekleşen sonuç)

| Risk | Sonuç |
|------|-------|
| Yeni paket bağımlılığı (tokenizer) | **Gerçekleşmedi** — geçişli bağımlılık yeterli, gerçek çalıştırmayla da doğrulandı |
| Özetleme maliyeti fark edilmez | Çözüldü — `CompactionUsageAccumulator` + `compact_history` span |
| Sıkıştırma bilgiyi kaybettirir | Varsayılan kapalı; `MinimumPreserved*` sınırları; özet metni `run_events`'te saklanır |
| Oturum durumu büyür | `agent_definitions.definition` sütunu ölçülmedi ama alan sayısı küçük (10 civarı ek alan); risk düşük görüldü |

---

## Sonraki Faza Devir Notu

- Faz 14'ün `attachments` tablosu kalıcı `AgentFileStore` için doğal ev olabilir.
- `ChatHistoryMemoryProvider` (vektör tabanlı bellek) hâlâ hiç kullanılmıyor —
  bir `VectorStore` implementasyonu ve embedding sağlayıcısı kararı gerektirir;
  ayrı bir faz olarak planlanmalı (F-11'in tamamlanmamış kısmı).
- Faz 18 (eval) sıkıştırmanın kaliteyi düşürüp düşürmediğini ölçebilecek ilk araçtır.
- Faz 20 (maliyet) özet çağrılarını ayrı bir kalem olarak gösterebilir — `RunUsage`
  zaten toplamı taşıyor ama özet payı ayrıştırılmış değil.
- **Bağımsız bulunan hata:** `AgentDefinitionPayload`'da `CallableAgentNames`
  eksikti (Faz 12'den kalma); bu fazda bulunup düzeltildi. Yeni bir
  `AgentDefinition` alanı eklerken **hem** `AgentPrismCoreJsonContext`'in
  kapsadığı tipi **hem** `AgentPrism.PostgreSql/Internal/AgentDefinitionPayload.cs`'i
  güncellemeyi unutmayın — ikisi ayrı şemalardır.
