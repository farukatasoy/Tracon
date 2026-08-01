# Faz 1 — Çekirdek Soyutlamalar ve Runtime

> **Durum:** Planlandı
> **Önkoşul:** [00-ALTYAPI.md](00-ALTYAPI.md)
> **Sonraki:** [02-POSTGRESQL-KALICILIK.md](02-POSTGRESQL-KALICILIK.md)
> **Paketler:** `AgentPrism.Abstractions`, `AgentPrism.Core`

---

## Amaç

AgentPrism'in sözleşmelerini ve MAF'a bağlanma noktalarını kurmak.

**Bu faz veritabanı olmadan tam çalışır.** Bir geliştirici `AddAgentPrism()` yazar, agent tanımlar, çalıştırır ve çalıştırma kaydını okur — hiçbir altyapı kurmadan. Bu, tasarım kuralı K1'in ("sıfır sürpriz") somut karşılığıdır.

---

## Kapsam

- `AgentPrism.Abstractions` — tüm sözleşmeler
- `AgentPrism.Core` — çalışma zamanı, katalog, derleyici, tool defteri, çalıştırma kaydı
- Her store için bellek içi implementasyon
- Mimari testi (bağımlılık yönünü zorlar)

## Kapsam Dışı

- PostgreSQL — Faz 2
- Gerçek bir model sağlayıcısı — Faz 3 (bu fazda test için sahte `IChatClient`)
- HTTP uçları — Faz 4

---

## `AgentPrism.Abstractions`

### Agent tanımı

```csharp
public sealed record AgentDefinition
{
    public required string Name { get; init; }
    public string? DisplayName { get; init; }
    public string? Description { get; init; }
    public string? Instructions { get; init; }
    public required ModelBinding Model { get; init; }
    public IReadOnlyList<string> ToolNames { get; init; } = [];
    public HarnessSettings? Harness { get; init; }
    public AgentDefinitionOrigin Origin { get; init; }
    public int Version { get; init; }
    public IReadOnlyDictionary<string, JsonElement> Metadata { get; init; }
}

public enum AgentDefinitionOrigin { Code, Database }

public sealed record ModelBinding
{
    public required string Provider { get; init; }   // "openai"
    public required string Model { get; init; }      // "gpt-5.4-mini"
    public float? Temperature { get; init; }
    public int? MaxOutputTokens { get; init; }
    public string? ReasoningEffort { get; init; }
}

public sealed record HarnessSettings
{
    public int? MaxContextWindowTokens { get; init; }
    public int? MaxOutputTokens { get; init; }
    public bool DisableTodoProvider { get; init; }
    public bool DisableFileAccess { get; init; }
    public bool DisableFileMemory { get; init; }
    public bool DisableToolAutoApproval { get; init; }
}
```

`ToolNames` **ad** listesidir, kod değil. Tasarım kuralı K2'nin uygulaması: arayüzden gelen tanım yalnızca kodda kayıtlı bir tool'a işaret edebilir.

### Servis sözleşmeleri

```csharp
public interface IAgentCatalog
{
    ValueTask<IReadOnlyList<AgentDescriptor>> ListAsync(CancellationToken ct = default);
    ValueTask<AIAgent?> ResolveAsync(string name, CancellationToken ct = default);
}

public interface IAgentDefinitionStore
{
    ValueTask<AgentDefinition?> GetAsync(string name, CancellationToken ct = default);
    ValueTask<IReadOnlyList<AgentDefinition>> ListAsync(CancellationToken ct = default);
    ValueTask<AgentDefinition> SaveAsync(AgentDefinition definition, CancellationToken ct = default);
    ValueTask DeleteAsync(string name, CancellationToken ct = default);
    ValueTask<IReadOnlyList<AgentDefinition>> ListVersionsAsync(string name, CancellationToken ct = default);
}

public interface IRunStore
{
    ValueTask<RunRecord> StartRunAsync(RunStartInfo info, CancellationToken ct = default);
    ValueTask AppendEventAsync(Guid runId, RunEvent evt, CancellationToken ct = default);
    ValueTask CompleteRunAsync(Guid runId, RunCompletion completion, CancellationToken ct = default);
    ValueTask<RunRecord?> GetAsync(Guid runId, CancellationToken ct = default);
    ValueTask<IReadOnlyList<RunRecord>> QueryAsync(RunQuery query, CancellationToken ct = default);
    IAsyncEnumerable<RunEvent> ReadEventsAsync(Guid runId, long fromSequence, CancellationToken ct = default);
}

public interface IToolRegistry
{
    IReadOnlyList<ToolDescriptor> List();
    bool TryGet(string name, out AIFunction function);
}

public interface IModelProviderRegistry
{
    IReadOnlyList<ModelProviderDescriptor> List();
    IChatClient CreateChatClient(ModelBinding binding);
}
```

### Olay tipleri

```csharp
public enum RunEventType
{
    RunStarted, MessageDelta, MessageCompleted,
    ToolInvoking, ToolInvoked, ToolFailed,
    RunCompleted, RunFailed,
}
```

`RunEvent` append-only'dir ve `Sequence` alanı taşır. Arayüz bu akışı hem canlı (SSE) hem geçmişe dönük (replay) okur — aynı veri, aynı sıralama.

---

## `AgentPrism.Core`

### Yapılandırma zinciri

```csharp
public static IAgentPrismBuilder AddAgentPrism(this IHostApplicationBuilder builder);

public interface IAgentPrismBuilder
{
    IServiceCollection Services { get; }
    IAgentPrismBuilder AddTool(AIFunction function);
    IAgentPrismBuilder AddTool(Delegate method, string? name = null, string? description = null);
    IAgentPrismBuilder AddToolsFrom<T>() where T : class;
    IAgentPrismBuilder Configure(Action<AgentPrismOptions> configure);
}
```

Her store `TryAdd*` ile kaydedilir (tasarım kuralı K4). Faz 2 ve 3 bu zincire `UsePostgreSql()` ve `UseOpenAI()` ekler.

### `AgentDefinitionCompiler`

`AgentDefinition` → `AIAgent` dönüşümü.

```
AgentDefinition
   ├─ ModelBinding    → IModelProviderRegistry.CreateChatClient()
   ├─ ToolNames       → IToolRegistry.TryGet() → AIFunction[]
   └─ HarnessSettings
        ├─ dolu  → chatClient.AsHarnessAgent(new HarnessAgentOptions { .. })
        └─ boş   → chatClient.AsAIAgent(new ChatClientAgentOptions { .. })
```

Derlenmiş agent'lar `(Name, Version)` anahtarıyla önbelleğe alınır. Tanım güncellenince versiyon artar, önbellek doğal olarak geçersizleşir — açık invalidation mantığı gerekmez.

Bilinmeyen bir tool adı **derleme sırasında hata verir**. Sessizce atlanmaz.

### `CompositeAgentCatalog`

İki kaynağı birleştirir:

1. MAF DI'sındaki `AddAIAgent` kayıtları → `Origin = Code`
2. `IAgentDefinitionStore` kayıtları → `Origin = Database`

Ad çakışmasında **kod kazanır** ve bir uyarı loglanır. Gerekçe: kod, derleme zamanında doğrulanmış olandır; veritabanı tanımı çalışma zamanı verisidir.

### `RunRecordingAgent`

`DelegatingAIAgent` türevi. Her çalıştırmayı `IRunStore`'a olay olarak yazar.

**Neden middleware değil?** MAF middleware zinciri agent'a özgüdür ve `HarnessAgent` kendi iç dekoratörlerini ekler. Dış sarmalayıcı, harness dahil **her** agent tipinde aynı şekilde çalışır.

Kayıt yolu asenkron ve hataya dayanıklıdır: `IRunStore` yazma hatası çalıştırmayı **kesmez**, yalnızca loglanır. Gözlemlenebilirlik, işlevselliği bozmamalıdır.

### Bellek içi implementasyonlar

`InMemoryAgentDefinitionStore`, `InMemoryRunStore`, `InMemorySessionStore`.

Bunlar test yardımcısı değil, **birinci sınıf** implementasyonlardır. Sınırlıdırlar (süreç ömrü, tek düğüm) ve bu sınır dokümante edilir.

---

## Dosya Listesi

```
src/AgentPrism.Abstractions/
├── AgentDefinition.cs · AgentDescriptor.cs · AgentDefinitionOrigin.cs
├── ModelBinding.cs · HarnessSettings.cs
├── IAgentCatalog.cs · IAgentDefinitionStore.cs
├── IRunStore.cs · RunRecord.cs · RunEvent.cs · RunEventType.cs · RunQuery.cs
├── IToolRegistry.cs · ToolDescriptor.cs
├── IModelProviderRegistry.cs · ModelProviderDescriptor.cs
├── ISessionStore.cs · SessionRecord.cs
├── ITenantContext.cs
└── AgentPrismException.cs

src/AgentPrism.Core/
├── AgentPrismBuilder.cs · IAgentPrismBuilder.cs
├── AgentPrismServiceCollectionExtensions.cs
├── AgentPrismOptions.cs · AgentPrismOptionsValidator.cs
├── Catalog/CompositeAgentCatalog.cs
├── Compilation/AgentDefinitionCompiler.cs · CompiledAgentCache.cs
├── Tools/ToolRegistry.cs · ToolRegistrationExtensions.cs
├── Recording/RunRecordingAgent.cs · RunEventWriter.cs
├── Storage/InMemoryAgentDefinitionStore.cs · InMemoryRunStore.cs · InMemorySessionStore.cs
└── Diagnostics/AgentPrismDiagnostics.cs
```

---

## Test Stratejisi

`tests/AgentPrism.Core.UnitTests`:

| Test | Neyi doğrular |
|------|---------------|
| `ArchitectureTests` | Bağımlılık yönü — `Abstractions` hiçbir AgentPrism paketine referans vermez |
| `AgentDefinitionCompilerTests` | Model/tool çözümü, harness seçimi, bilinmeyen tool → hata |
| `CompiledAgentCacheTests` | Versiyon artınca önbellek yenilenir |
| `CompositeAgentCatalogTests` | Kod ve DB birleşimi, ad çakışmasında kod kazanır |
| `RunRecordingAgentTests` | Olay sırası ve tipleri; store hatası çalıştırmayı kesmez |
| `InMemoryStoreTests` | Round-trip, eşzamanlı erişim |

Sahte `IChatClient` kullanılır; gerçek ağ çağrısı yoktur.

---

## Bitiş Ölçütleri (DoD)

- [ ] `AddAgentPrism()` tek başına çalışır, hiçbir yapılandırma gerektirmez
- [ ] Bellek içi store'larla agent tanımlanır → çalıştırılır → çalıştırma kaydı okunur
- [ ] Bilinmeyen tool adı derlemede anlaşılır hata verir
- [ ] Mimari testi bağımlılık yönünü zorlar
- [ ] `dotnet build -c Release` 0 uyarı
- [ ] Birim test kapsamı `AgentPrism.Core` için anlamlı yollarda

---

## Riskler

| Risk | Önlem |
|------|-------|
| `DelegatingAIAgent` streaming yolunda olay sırasının bozulması | Sıra numarası tek bir yazıcıdan üretilir; testte doğrulanır |
| `HarnessAgent` API'si MAF'ta değişebilir | Harness kullanımı tek dosyada (`AgentDefinitionCompiler`) toplanır |
| Bellek içi store'un üretimde yanlışlıkla kullanılması | Başlangıçta uyarı loglanır; `/api/meta` hangi store'un aktif olduğunu bildirir |
