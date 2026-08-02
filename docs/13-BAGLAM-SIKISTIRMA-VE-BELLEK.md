# Faz 13 — Bağlam Sıkıştırma ve Bellek Sağlayıcıları

> **Durum:** 📋 Planlandı
> **Kaynak:** [BEYIN-FIRTINASI.md](BEYIN-FIRTINASI.md) · **F-11**
> **Önkoşul:** Yok (Faz 10 ve 12 bu fazı **gerekli** kılar — bağlam onlarla büyür)
> **Paketler:** `AgentPrism.Abstractions`, `.Core`, `.PostgreSql`, `.AspNetCore`, `.UI`
> **Yeni paket:** Yok · **Migration:** Yok

---

## Bu Faza Başlarken

1. [`12-AGENT-CAGRI-GRAFIGI.md`](12-AGENT-CAGRI-GRAFIGI.md) — **önceki faz**; §12.2 (ambient kapsam ve `AsyncLocal` kuralı) ve §12.4 (derleyicideki sağlayıcı listesi)
2. [`02-POSTGRESQL-KALICILIK.md`](02-POSTGRESQL-KALICILIK.md) — `PostgresChatHistoryProvider`, oturum durumu
3. [`KARARLAR.md`](KARARLAR.md) — **K-027** (`json` vs `jsonb`), **K-037** (`ChatHistoryProvider` kaydı), **K-062** (harness alanları kapalı), **K-097** (`AIContextProviders` birinci sınıf yoldur)
4. [`MIMARI.md`](MIMARI.md) — bölüm 4 "Hâlâ kullanılmayan MAF genişleme noktaları"
5. Bu doküman

---

## Faz 12'den Devralınanlar

### Sağlayıcı listesi hazır

`AgentDefinitionCompiler.CompileChatAgent` artık bir **liste** kurar; tek bir
sağlayıcı ataması değildir:

```csharp
var providers = new List<AIContextProvider>(2);

if (definition.SkillNames.Count > 0)
{
    providers.Add(CreateSkillsProvider(definition));
}

if (CreateBackgroundAgentsProvider(definition, callableAgents) is { } backgroundAgents)
{
    providers.Add(backgroundAgents);
}

if (providers.Count > 0)
{
    options.AIContextProviders = providers;
}
```

`CompactionProvider` bu listeye üçüncü öğe olarak eklenir. Harness yolunda
karşılığı `HarnessAgentOptions.DisableCompaction` bayrağıdır (zaten bağlı).

### Önbellek anahtarı iki parmak izi taşıyor

```csharp
CompiledAgentCache.GetOrAdd(
    definition.Name,
    definition.Version,
    CompiledAgentCache.CombineFingerprints(skills.Fingerprint, callable.Fingerprint),
    () => _compiler.Compile(definition, callable));
```

Sıkıştırma ayarı agent tanımının **kendi** alanı olacaksa `Version` zaten artar
ve ek parmak izi gerekmez. Ayar tanım dışında bir yerde yaşayacaksa
(ör. kiracı düzeyinde) `CombineFingerprints` zincirlenmelidir.

### Çalıştırma kapsamı

```csharp
public sealed record AgentRunScope
{
    public required Guid RunId { get; init; }
    public required Guid RootRunId { get; init; }
    public int Depth { get; init; }
    public string? AgentName { get; init; }
    public string? TenantId { get; init; }
    public AgentRunBudget? Budget { get; init; }
    public RunEventWriter? Writer { get; init; }
}

AgentPrismRunContext.Current            // okuma
AgentPrismRunContext.SetCurrent(scope)  // yazma
```

Sıkıştırma olayını çalıştırma akışına yazmak isterseniz **tek yol**
`AgentPrismRunContext.Current?.Writer`'dır. İkinci bir `RunEventWriter` kurmak
sıra numaralarını çakıştırır (K-014).

### 🚨 Bilinen tuzaklar (Faz 12'de ölçüldü)

| Tuzak | Kural |
|-------|-------|
| `async IAsyncEnumerable` gövdesinde yapılan `AsyncLocal` ataması **`yield return` sınırını aşmaz** | Kapsam her `MoveNextAsync`'ten **hemen önce** yeniden yazılmalıdır. Döngü dışında bir kez yazmak yetmez. |
| Ağaçtaki tüm çalıştırmalar aynı W3C trace kimliğini paylaşır | `RunTraceCollector` tamponunun sahibi yalnız kök çalıştırmadır (K-099). Yeni bir span tüketicisi eklerken aynı kural geçerlidir. |
| MAF alt agent'ı `options = null` ile çağırır | Bir `AIContextProvider`'ın açtığı tool'dan tetiklenen çağrılarda gelen ayarlara güvenilemez. |
| `BackgroundAgentsProvider` `MAAI001` işaretlidir | `CompactionProvider` de öyle olabilir — kullanmadan önce derleyin; bastırma tek dosyada toplanır ve `KARARLAR.md`'ye yazılır (K-020, K-097). |

---

## Amaç

MAF'ta hazır olan ve bugün **hiç kullanılmayan** bağlam yönetimi yeteneklerini
açığa çıkarmak. Uzun konuşmalar bugün ya bağlam penceresine sığmayıp hata verir
ya da sessizce pahalılaşır.

Bu faz Faz 10 (skill) ve Faz 12 (alt agent) sonrasına konuldu: her ikisi de
bağlamı büyütür, dolayısıyla sıkıştırma o noktada teorik bir iyileştirme değil,
gerçek bir ihtiyaçtır.

---

## Doğrulanmış MAF API'si

```csharp
namespace Microsoft.Agents.AI.Compaction;

sealed class CompactionProvider : AIContextProvider {
    CompactionProvider(CompactionStrategy strategy, string? stateKey, ILoggerFactory? lf);
    static Task<IEnumerable<ChatMessage>> CompactAsync(CompactionStrategy strategy,
                                                       IEnumerable<ChatMessage> messages,
                                                       ILogger? logger, CancellationToken ct);
}

abstract class CompactionStrategy { protected CompactionStrategy(CompactionTrigger trigger, CompactionTrigger? target); }

sealed class SummarizationCompactionStrategy   : CompactionStrategy {
    SummarizationCompactionStrategy(IChatClient chatClient, CompactionTrigger trigger,
                                    int minimumPreservedGroups, string? summarizationPrompt,
                                    CompactionTrigger? target);
}
sealed class ContextWindowCompactionStrategy   : CompactionStrategy {
    ContextWindowCompactionStrategy(int maxContextWindowTokens, int maxOutputTokens,
                                    double toolEvictionThreshold, double truncationThreshold);
}
sealed class SlidingWindowCompactionStrategy(CompactionTrigger trigger, int minimumPreservedTurns, CompactionTrigger? target);
sealed class TruncationCompactionStrategy(CompactionTrigger trigger, int minimumPreservedGroups, CompactionTrigger? target);
sealed class ToolResultCompactionStrategy(CompactionTrigger trigger, int minimumPreservedGroups, CompactionTrigger? target);
sealed class PipelineCompactionStrategy(IEnumerable<CompactionStrategy> strategies);

static class CompactionTriggers {
    CompactionTrigger TokensExceed(int max);   MessagesExceed(int);   TurnsExceed(int);
    GroupsExceed(int);   HasToolCalls();   TokensBelow(int);   All(...);   Any(...);
}

// Bellek saglayicilari
sealed class ChatHistoryMemoryProvider : MessageAIContextProvider;
sealed class FileMemoryProvider(AgentFileStore fileStore, Func<AgentSession, FileMemoryState>? init,
                                FileMemoryProviderOptions? options) : AIContextProvider;
sealed class TextSearchProvider : MessageAIContextProvider;
sealed class TodoProvider : AIContextProvider;

abstract class AgentFileStore {                       // DOSYA SISTEMI DEGIL, SOYUTLAMA
    Task<string?> ReadAsync(string path, CancellationToken ct);
    Task WriteAsync(string path, string content, CancellationToken ct);
    Task<IReadOnlyList<FileStoreEntry>> ListChildrenAsync(string directory, CancellationToken ct);
    Task<IReadOnlyList<FileSearchResult>> SearchAsync(string directory, string? regexPattern,
                                                      string? globPattern, bool recursive, CancellationToken ct);
    Task<bool> DeleteAsync(string path, CancellationToken ct);
    Task CreateDirectoryAsync(string path, CancellationToken ct);
    Task<bool> FileExistsAsync(string path, CancellationToken ct);
}
sealed class InMemoryAgentFileStore : AgentFileStore;

// Baglanma noktalari
ChatClientAgentOptions.AIContextProviders
HarnessAgentOptions.CompactionStrategy · DisableCompaction · FileMemoryStore · DisableFileMemory · DisableTodoProvider
```

### 🚨 İki bulgu

1. **`AgentFileStore` bir dosya sistemi değildir.** Soyut bir depodur.
   PostgreSQL destekli bir uygulama yazmak, agent'a "dosya" verirken diske
   hiç dokunmamak demektir. Bu, K-062'nin `FileAccessStore` endişesini
   ortadan kaldırır: yollar veritabanı satırlarıdır, sunucudaki dosyalar değil.
2. **Sıkıştırma bir `AIContextProvider`'dır.** Harness zorunlu değil; düz
   `ChatClientAgent` de sıkıştırma alabilir (K-053 nedeniyle tercih edilen yol).

---

## 13.1 — Agent Tanımında Sıkıştırma Ayarı

```csharp
public sealed record CompactionSettings
{
    public CompactionStrategyKind Strategy { get; init; } = CompactionStrategyKind.None;
    public int? TriggerTokens { get; init; }          // TokensExceed
    public int? TriggerMessages { get; init; }
    public int? TriggerTurns { get; init; }
    public int? MinimumPreservedTurns { get; init; }
    public int? MinimumPreservedGroups { get; init; }
    public int? MaxContextWindowTokens { get; init; }
    public int? MaxOutputTokens { get; init; }
    public string? SummarizationPrompt { get; init; }
    public ModelBinding? SummarizationModel { get; init; }   // bos ise agent'in kendi modeli
}

public enum CompactionStrategyKind
{
    None, SlidingWindow, Truncation, ToolResult, Summarization, ContextWindow, Pipeline
}

public sealed record AgentDefinition
{
    // ...mevcut uyeler
    public CompactionSettings? Compaction { get; init; }
    public MemorySettings? Memory { get; init; }
}
```

Geçersiz birleşim **derleme hatasıdır**, sessizce yok sayılmaz — K-034'te
`ReasoningEffort` için verilen kararın aynısı. Örnek: `Summarization` seçilip
hiçbir tetikleyici verilmemesi.

`Pipeline` seçilirse sıra sabittir ve dokümante edilir:
`ToolResult → SlidingWindow → Summarization`. Serbest sıra, arayüzde anlaşılması
zor bir yapılandırma yüzeyi üretir.

---

## 13.2 — Özetleme Modeli

`SummarizationCompactionStrategy` bir `IChatClient` ister. Üç seçenek:

| Seçenek | Sonuç |
|---------|-------|
| Agent'ın kendi modeli | Basit; ama pahalı modelle özet almak maliyeti artırır |
| Yapılandırılmış "yardımcı model" | Ucuz model ile özet; ek ayar |
| Sağlayıcının varsayılanı | Belirsiz; kullanıcı neyin çalıştığını bilmez |

**Karar:** `CompactionSettings.SummarizationModel` boşsa agent'ın kendi modeli
kullanılır; doluysa o `ModelBinding` çözülür. Ek olarak global varsayılan:
`AgentPrismOptions.UtilityModel`. Sıra: agent ayarı → global ayar → agent'ın
kendi modeli.

Özetleme çağrısı **kendi span'ini açar** (`compact_history`) ve
`tool_invocations`'a değil, `run_events`'e yazılır — bu bir tool çağrısı
değildir. Token kullanımı çalıştırmanın toplamına dâhil edilir; aksi hâlde
maliyet raporu (Faz 20) eksik olur.

---

## 13.3 — Bellek Sağlayıcıları

```csharp
public sealed record MemorySettings
{
    public bool EnableChatHistoryMemory { get; init; }
    public bool EnableFileMemory { get; init; }
    public bool EnableTodo { get; init; }
    public bool EnableTextSearch { get; init; }
}
```

- **`ChatHistoryMemoryProvider`** — konuşma içi bellek. Durum oturumda yaşar.
- **`FileMemoryProvider`** — `PostgresAgentFileStore` ile kalıcı ve **kiracıya
  kapalı** bir "dosya" alanı. Yollar `{tenant}/{agent}/{...}` ile öneklenir;
  önek zorlaması depoda yapılır, çağıranın verdiği yola güvenilmez.
- **`TodoProvider`** — harness zaten kullanıyor; düz agent için açılır.
- **`TextSearchProvider`** — bir arama kaynağı ister. Bu fazda **`PostgresAgentFileStore`
  üzerinde** arama ile sınırlıdır; vektör araması kapsam dışıdır ve ayrı bir
  fazın konusudur.

**Kalıcı dosya belleği bu fazın kapsamı dışındadır.** `PostgresAgentFileStore`
yeni bir tablo ister; bu faz migration'sız planlandı. Dolayısıyla
`FileMemoryProvider` desteği `InMemoryAgentFileStore` ile sınırlıdır ve kalıcı
sürüm Faz 14'ün depolama tablosuna bağlanır.

> ⚠️ Bu bir plan kısıtıdır, teknik zorunluluk değil. Uygulama oturumunda kalıcı
> dosya belleği istenirse bu faz bir migration alır ve yol haritasındaki
> numaralar kayar. Karar o oturumda verilir ve gerekçesi yazılır.

---

## 13.4 — Tokenizer Bağımlılığı — ÖNCE ÖLÇÜN

`CompactionMessageIndex` kurucusu bir `Tokenizer` alır. Uygulamaya başlamadan
**önce** şunu ölçün:

```bash
dotnet list src/AgentPrism.Core/AgentPrism.Core.csproj package --include-transitive | grep -i token
```

- Tokenizer `Microsoft.Agents.AI`'ın geçişli bağımlılığı ise ek paket **yoktur**
  ve iş kolaydır.
- Değilse `Microsoft.ML.Tokenizers` doğrudan bağımlılık olur. Bu, K-007'nin
  ("tüketicinin bağımlılık grafiğini kirletme") sınırındadır ve **karar
  gerektirir**: sıkıştırmayı `AgentPrism.Core`'a mı koyacağız, yoksa ayrı bir
  `AgentPrism.Compaction` paketine mi?

Bu belirsizlik bilerek çözülmemiştir; ölçüm yapmadan karar vermek Faz 6'nın S2
sapmasını tekrarlamak olur.

---

## 13.5 — Arayüz

- Agent düzenleyicisinde "Bağlam" sekmesi: strateji seçimi, eşikler, özet modeli
- Oturum detayında: sıkıştırma olduysa "N mesaj özetlendi" satırı ve özet metni
- Playground transcript'inde sıkıştırma olayı görünür (gizli davranış olmamalı)

Bütçe hedefi: **+4 KB gzip'ten az**.

---

## Testler

| Proje | Yeni test |
|-------|-----------|
| `AgentPrism.Core.UnitTests` | Her stratejinin doğru kurulması; geçersiz birleşimde derleme hatası; özet modeli çözümleme sırası; özet token'larının çalıştırma toplamına girmesi; `Pipeline` sırası |
| `AgentPrism.PostgreSql.IntegrationTests` | Sıkıştırılmış geçmişin `conversation_items`'a doğru yazılması; sıkıştırma sonrası oturumun geri yüklenebilmesi |
| `AgentPrism.AspNetCore.FunctionalTests` | Bağlam ayarlarının uçtan uca kaydı ve okunması |
| `AgentPrism.Ui.E2ETests` | Bağlam sekmesi; sıkıştırma göstergesi |

**Gerçek kanıt:** uzun bir konuşma (>50 mesaj) sıkıştırma açıkken çalıştırılır;
mesaj sayısı, token sayısı ve özet metni öncesi/sonrası dokümana yazılır.

---

## Bu Fazda Verilecek Kararlar

1. **Sıkıştırma `AIContextProviders` üzerinden bağlanır** (K-053).
2. **Özet modeli çözümleme sırası:** agent ayarı → global yardımcı model →
   agent'ın kendi modeli.
3. **Özet token'ları çalıştırmanın toplamına dâhildir** — maliyet gerçeği
   yansıtmalıdır.
4. **`Pipeline` sırası sabittir** — serbest sıra kullanılabilir bir arayüz
   üretmez.
5. **Tokenizer bağımlılığı ölçümle karara bağlanır** (bkz. 13.4).

---

## Açık Sorular

1. **Sıkıştırma varsayılan olarak açık mı olsun?** Açık olursa uzun konuşmalar
   kendiliğinden ucuzlar ama geçmiş sessizce değişir. Öneri: **kapalı**;
   `HarnessSettings.DisableCompaction` zaten var ve harness'ta MAF varsayılanı
   korunur.
2. **Özetlenen mesajlar `conversation_items`'ta silinsin mi?** Silinirse denetim
   izi bozulur; kalırsa depo büyür. Öneri: **kalır**, sıkıştırma yalnız modele
   gönderilen bağlamı etkiler.
3. **Kalıcı dosya belleği bu fazda mı gelsin?** (13.3'teki kısıt.) Öneri:
   **hayır**, Faz 14'ün depolama tablosu geldikten sonra.

---

## Bitiş Ölçütleri (DoD)

- [ ] Beş stratejinin her biri bir agent tanımından kurulabiliyor
- [ ] Uzun konuşmada sıkıştırma tetikleniyor; öncesi/sonrası ölçüm dokümanda
- [ ] Özet çağrısının token'ları çalıştırma toplamında görünüyor
- [ ] Sıkıştırma sonrası oturum kaydedilip geri yüklenebiliyor
- [ ] Geçersiz ayar derlemede hata veriyor, sessizce yok sayılmıyor
- [ ] Tokenizer bağımlılığı ölçüldü ve karar yazıldı
- [ ] Dört doğrulama kapısı sıfır uyarı

---

## Riskler

| Risk | Önlem |
|------|-------|
| Yeni paket bağımlılığı (tokenizer) | Önce ölçülür; gerekirse ayrı paket |
| Özetleme maliyeti fark edilmez | Token'lar çalıştırma toplamına girer, span açılır |
| Sıkıştırma bilgiyi kaybettirir | Varsayılan kapalı; `MinimumPreserved*` sınırları; özet metni saklanır |
| Oturum durumu büyür | Sıkıştırma durumu `sessions.state` içinde; `json` sütunu (K-027) ve boyut ölçülür |

---

## Sonraki Faza Devir Notu

- Faz 14'ün `attachments` tablosu kalıcı `AgentFileStore` için doğal ev
  olabilir; oraya bakılmalıdır.
- Faz 18 (eval) sıkıştırmanın kaliteyi düşürüp düşürmediğini ölçebilecek ilk
  araçtır; "sıkıştırma açık/kapalı" bir A/B senaryosudur (Faz 19).
- Faz 20 (maliyet) özet çağrılarını ayrı bir kalem olarak gösterebilir.
