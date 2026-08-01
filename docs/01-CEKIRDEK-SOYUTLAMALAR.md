# Faz 1 — Çekirdek Soyutlamalar ve Runtime

> **Durum:** Tamamlandı (2026-08-02)
> **Önkoşul:** [00-ALTYAPI.md](00-ALTYAPI.md)
> **Sonraki:** [02-POSTGRESQL-KALICILIK.md](02-POSTGRESQL-KALICILIK.md)
> **Paketler:** `AgentPrism.Abstractions`, `AgentPrism.Core`

---

## Amaç

AgentPrism'in sözleşmelerini ve MAF'a bağlanma noktalarını kurmak.

**Bu faz veritabanı olmadan tam çalışır.** Bir geliştirici `AddAgentPrism()` yazar, agent tanımlar, çalıştırır ve çalıştırma kaydını okur — hiçbir altyapı kurmadan. Bu, tasarım kuralı K1'in ("sıfır sürpriz") somut karşılığıdır.

Doğrulandı: örnek API `dotnet run` ile ayağa kalkıyor, agent çalışıyor, çalıştırma olay olay okunabiliyor.

---

## Uygulama Sırasında Alınan Kararlar

Bu kararlar plan yazılırken bilinmiyordu; gerçek MAF API yüzeyi incelenince ortaya çıktı.

### `IAgentSource` — plandaki "MAF `AddAIAgent` kayıtlarını oku" yerine

Plan, katalogun MAF'ın `AddAIAgent` kayıtlarını okumasını öngörüyordu. Ancak `AddAIAgent`, ön sürüm durumundaki `Microsoft.Agents.AI.Hosting` paketindedir ve **K-008 gereği `AgentPrism.Core` o pakete bağımlı olamaz**.

Çözüm: `IAgentSource` soyutlaması. Katalog, önceliğe göre sıralanmış kaynaklardan agent toplar.

| Kaynak | Öncelik | Paket | Faz |
|--------|---------|-------|-----|
| `CodeAgentSource` | 0 | `AgentPrism.Core` | 1 |
| MAF barındırma köprüsü | 10 | `AgentPrism.AspNetCore` | 4 |
| `DefinitionStoreAgentSource` | 100 | `AgentPrism.Core` | 1 |

Sonuç: Core yalnız GA paketlere bağlı kaldı ve mimari genişletilebilir hale geldi. Karar: **K-019**.

### `IAgentDecorator` — çalıştırma kaydı için genişleme noktası

Çalıştırma kaydı doğrudan katalogun içine gömülmedi. `IAgentDecorator` arayüzü tanımlandı; `RunRecordingAgentDecorator` bunu uygular. Tüketici kendi sarmalayıcısını aynı şekilde ekleyebilir.

Bu, tasarım kuralı K4'ün ("her genişleme noktası değiştirilebilir") uygulanmasıdır.

### `AgentPrismId` — kendi UUIDv7 üretecimiz

`Guid.CreateVersion7()` yalnızca .NET 9+ içindedir; paket `net8.0` da hedefliyor. Depolama anahtarlarının tüm hedeflerde aynı üretilmesi gerektiği için RFC 9562 uygulamasını kendimiz yazdık (`AgentPrismId.NewId()`).

Ek kazanç: `AgentPrismId.GetTimestamp(id)` ile kimlikten zaman damgası okunabiliyor.

### AOT uyumluluğu üç yerde ödün istedi

`AgentPrism.Core` AOT uyumlu olarak işaretli (K-006). Üç nokta buna uyarlandı:

| Sorun | Çözüm |
|-------|-------|
| `ValidateDataAnnotations()` → `IL2026` | Elle yazılmış `AgentPrismOptionsValidator` |
| `optionsBuilder.Bind()` → `IL2026` + `IL3050` | `EnableConfigurationBindingGenerator=true` |
| Tool argümanlarını JSON'a çevirme | Elle biçimlendirme (`ad=deger`), yansıma yok |

### `AddToolsFrom<T>()` Faz 3'e ertelendi

Attribute taramalı tool kaydı yansıma gerektirir. Faz 1'de iki aşırı yükleme var:

- `AddTool(AIFunction tool, ...)` — AOT temiz
- `AddTool(Delegate method, ...)` — `[RequiresUnreferencedCode]` + `[RequiresDynamicCode]` ile işaretli

İkinci aşırı yükleme, uyarıyı çağırana **dürüstçe iletir**; bastırmaz.

### MAF harness seçenekleri `MAAI001` ile işaretli

`HarnessAgentOptions` üyeleri **"for evaluation purposes only"** tanısı üretiyor. Bastırma tek bir dosyada (`AgentDefinitionCompiler.CompileHarnessAgent`) yapıldı ve gerekçesi koda yazıldı. MAF bu API'yi değiştirirse yalnız orası güncellenecek. Karar: **K-020**.

---

## Gerçekleşen Public API

### `AgentPrism.Abstractions`

```csharp
// Agent tanımı ve katalog
sealed record AgentDefinition        // Name, Instructions, Model, ToolNames, Harness, Origin, Version, TenantId, Metadata
sealed record AgentDescriptor        // katalog görünümü; SourceName taşır
sealed record ModelBinding           // Provider, Model, Temperature, TopP, MaxOutputTokens, ReasoningEffort
sealed record HarnessSettings        // MAF HarnessAgentOptions'ın güvenli alt kümesi
enum AgentDefinitionOrigin           // Code | Database

interface IAgentSource               // Name, Priority, ListAsync, ResolveAsync
interface IAgentCatalog              // ListAsync, ResolveAsync
interface IAgentDecorator            // Order, Decorate(AIAgent, AgentDescriptor)
interface IAgentDefinitionStore      // Get, List, Save, Delete, ListVersions, Rollback

// Çalıştırma kaydı
interface IRunStore
{
    ValueTask<RunRecord> StartRunAsync(RunStartInfo info, CancellationToken ct = default);
    ValueTask AppendEventAsync(RunEvent runEvent, CancellationToken ct = default);
    ValueTask CompleteRunAsync(RunCompletion completion, CancellationToken ct = default);
    ValueTask<RunRecord?> GetRunAsync(Guid runId, CancellationToken ct = default);
    ValueTask<IReadOnlyList<RunRecord>> QueryRunsAsync(RunQuery query, CancellationToken ct = default);
    IAsyncEnumerable<RunEvent> ReadEventsAsync(Guid runId, long fromSequence = 0, CancellationToken ct = default);
}

sealed record RunRecord · RunEvent · RunUsage · RunError
sealed record RunStartInfo · RunCompletion · RunQuery
enum RunStatus                       // Running | Completed | Failed | Canceled
enum RunEventType                    // RunStarted, MessageDelta, MessageCompleted,
                                     // ToolInvoking, ToolInvoked, ToolFailed, RunCompleted, RunFailed

// Tool'lar ve modeller
interface IToolRegistry              // List(), TryGet(name, out AIFunction)
sealed class AgentPrismToolRegistration
sealed record ToolDescriptor
interface IModelProvider             // Name, Models, CreateChatClient(ModelBinding)
interface IModelProviderRegistry     // List(), CreateChatClient(ModelBinding)
sealed record ModelDescriptor · ModelProviderDescriptor

// Oturum, kiracı, altyapı
interface ISessionStore              // Save, Get, Delete, Query
sealed record SessionRecord · SessionQuery
interface ITenantContext             // TenantId
static class AgentPrismId            // NewId(), NewId(timestamp), GetTimestamp(id)  — UUIDv7
class AgentPrismException
sealed class AgentPrismCompilationException  // AgentName taşır
```

> **Not:** `IRunStore.AppendEventAsync` sıra numarasını **atamaz**; çağıran atar. Bu, sıralamanın tek bir yazıcıdan gelmesini garantiler.
> `IAgentDefinitionStore.DeleteAsync` ve `ISessionStore.DeleteAsync` `ValueTask<bool>` döner.

### `AgentPrism.Core`

```csharp
// Giriş noktası
static class AgentPrismServiceCollectionExtensions
{
    static IAgentPrismBuilder AddAgentPrism(this IHostApplicationBuilder builder);
    static IAgentPrismBuilder AddAgentPrism(this IServiceCollection services, IConfiguration? section = null);
}

interface IAgentPrismBuilder
{
    IServiceCollection Services { get; }
    IAgentPrismBuilder Configure(Action<AgentPrismOptions> configure);
    IAgentPrismBuilder AddTool(AIFunction tool, bool requiresApproval = false);
    IAgentPrismBuilder AddTool(Delegate method, string? name, string? description, bool requiresApproval);  // [RequiresUnreferencedCode]
    IAgentPrismBuilder AddAgent(AgentDefinition definition);
    IAgentPrismBuilder AddAgent(string name, Func<IServiceProvider, AIAgent> factory, string? description = null);
    IAgentPrismBuilder AddModelProvider(IModelProvider provider);
    IAgentPrismBuilder AddModelProvider(Func<IServiceProvider, IModelProvider> factory);
}

sealed class AgentPrismOptions               // DefaultTenantId, RunRecording
sealed class AgentPrismRunRecordingOptions   // Enabled, RecordMessageDeltas, RecordToolPayloads, MaxPayloadLength
sealed class AgentPrismOptionsValidator      // IValidateOptions<AgentPrismOptions>

sealed class AgentDefinitionCompiler         // Compile(AgentDefinition) → AIAgent
sealed class CompiledAgentCache              // GetOrAdd(name, version, factory), Evict(name), Clear()
sealed class CompositeAgentCatalog           // IAgentCatalog
sealed class CodeAgentSource · DefinitionStoreAgentSource
sealed class CodeAgentRegistration           // FromDefinition(...) | FromFactory(...)
sealed class ToolRegistry · ModelProviderRegistry
sealed class RunRecordingAgent : DelegatingAIAgent
sealed class RunRecordingAgentDecorator : IAgentDecorator
readonly record struct RunEventDraft
sealed class RunEventWriter
sealed class InMemoryAgentDefinitionStore · InMemoryRunStore · InMemorySessionStore
sealed class SingleTenantContext : ITenantContext
static class AgentPrismDiagnostics           // ActivitySourceName, MeterName
```

---

## Kullanılan MAF API'si (reflection ile doğrulandı)

Plan yazılırken varsayılan bazı adlar yanlıştı. Gerçek imzalar:

```csharp
// Microsoft.Agents.AI.Abstractions
abstract class AIAgent
{
    protected virtual Task<AgentResponse> RunCoreAsync(
        IEnumerable<ChatMessage> messages, AgentSession? session = null,
        AgentRunOptions? options = null, CancellationToken ct = default);

    protected virtual IAsyncEnumerable<AgentResponseUpdate> RunCoreStreamingAsync(
        IEnumerable<ChatMessage> messages, AgentSession? session = null,
        AgentRunOptions? options = null, CancellationToken ct = default);

    public string Id { get; }  public string? Name { get; }  public string? Description { get; }
}

abstract class DelegatingAIAgent : AIAgent { protected DelegatingAIAgent(AIAgent innerAgent); }
abstract class AgentSession { public AgentSessionStateBag StateBag { get; } }

// DIKKAT: AgentResponse / AgentResponseUpdate — "AgentRunResponse" DEGIL.
sealed class AgentResponse       { IList<ChatMessage> Messages; string Text; UsageDetails? Usage; ... }
sealed class AgentResponseUpdate { IList<AIContent> Contents; string Text; ChatRole? Role; ... }

// Microsoft.Extensions.AI (uzantılar)
static ChatClientAgent AsAIAgent(this IChatClient c, ChatClientAgentOptions o, ILoggerFactory? lf, IServiceProvider? sp);
static HarnessAgent    AsHarnessAgent(this IChatClient c, HarnessAgentOptions o, ILoggerFactory? lf, IServiceProvider? sp);
```

**Tool çağrıları ayrı bir kanca gerektirmiyor.** MAF, tool çağrılarını `FunctionCallContent` ve `FunctionResultContent` olarak mesaj/güncelleme içeriklerine koyuyor; `RunRecordingAgent` bunları okuyor.

---

## Dosya Listesi

```
src/AgentPrism.Abstractions/
├── Agents/       AgentDefinition · AgentDefinitionOrigin · AgentDescriptor · ModelBinding
│                 HarnessSettings · IAgentSource · IAgentCatalog · IAgentDecorator · IAgentDefinitionStore
├── Runs/         IRunStore · RunRecord · RunEvent · RunEventType · RunStatus · RunSupportTypes
├── Tools/        IToolRegistry · ToolDescriptor · AgentPrismToolRegistration
├── Models/       IModelProvider · IModelProviderRegistry · ModelDescriptor
├── Sessions/     ISessionStore (+ SessionRecord, SessionQuery)
├── Tenancy/      ITenantContext
├── AgentPrismException.cs · AgentPrismId.cs

src/AgentPrism.Core/
├── AgentPrismServiceCollectionExtensions.cs · AgentPrismBuilder.cs · IAgentPrismBuilder.cs
├── AgentPrismOptions.cs · AgentPrismOptionsValidator.cs
├── Catalog/      CompositeAgentCatalog · CodeAgentSource · DefinitionStoreAgentSource · CodeAgentRegistration
├── Compilation/  AgentDefinitionCompiler · CompiledAgentCache
├── Tools/        ToolRegistry
├── Models/       ModelProviderRegistry
├── Recording/    RunRecordingAgent · RunRecordingAgentDecorator · RunEventWriter
├── Storage/      InMemoryAgentDefinitionStore · InMemoryRunStore · InMemorySessionStore
├── Tenancy/      SingleTenantContext
└── Diagnostics/  AgentPrismDiagnostics
```

---

## Testler

`tests/AgentPrism.Core.UnitTests` — **42 test, hepsi geçiyor.**

| Sınıf | Kapsam |
|-------|--------|
| `DependencyDirectionTests` | Katman mimarisi, döngüsüzlük, paket README'leri (Faz 0) |
| `AgentDefinitionCompilerTests` | Harness seçimi, model eşlemesi, bilinmeyen tool/sağlayıcı hataları |
| `CompiledAgentCacheTests` | Sürüm artınca yeniden derleme, `Evict` |
| `CompositeAgentCatalogTests` | Kaynak birleştirme, öncelik, dekoratör zinciri |
| `RunRecordingAgentTests` | Olay sırası, tool olayları, akış, hata, **depo hatası çalıştırmayı kesmez** |
| `InMemoryAgentDefinitionStoreTests` | Sürümleme, geri alma, silme |
| `InMemoryRunStoreTests` | Sıra numarası, filtreleme, sıralama, üst sınır |
| `AgentPrismIdTests` | UUIDv7 sürüm/varyant bitleri, zaman sıralılığı, damga geri okuma |

Sahte `IChatClient` kullanılır; hiçbir test ağa çıkmaz.

---

## Bitiş Ölçütleri (DoD)

| Ölçüt | Durum |
|-------|-------|
| `AddAgentPrism()` tek başına, yapılandırmasız çalışır | ✅ |
| Bellek içi store'larla agent tanımlanır → çalıştırılır → kayıt okunur | ✅ |
| Bilinmeyen tool adı anlaşılır hata verir (kayıtlı tool'ları listeler) | ✅ |
| Mimari testi bağımlılık yönünü zorlar | ✅ |
| `dotnet build -c Release` — 0 uyarı | ✅ |
| `dotnet test` — 42/42 | ✅ |
| Örnek API uçtan uca çalışıyor | ✅ |

Örnek API doğrulaması (`samples/AgentPrism.Api`, port 5081):

```
GET  /agents            → support agent'ı, kaynak "code", 2 tool
GET  /tools             → get_order_status, list_recent_orders
POST /agents/support/run → {"text":"Echo: siparisim nerede"}
GET  /runs              → status Completed, usage, eventCount 4, id 019fbf66-… (UUIDv7)
GET  /runs/{id}/events  → #0 RunStarted, #1 MessageDelta, #2 MessageCompleted, #3 RunCompleted
```

---

## Faz 2'ye Devreden Notlar

1. **`ISessionStore` tanımlı ama hiç kullanılmıyor.** Faz 1'de oturum kalıcılığı yok. Faz 2 bunu MAF'ın `AgentSessionStore` yapısına bağlayacak.
2. **`RunRecordingAgent.GetSessionId`** şu an yer tutucu bir değer üretiyor. Faz 2'de gerçek oturum kimliği bağlanmalı.
3. **`CompiledAgentCache.Evict`** çağıran yok. Faz 4'te agent tanımı güncellenince çağrılmalı.
4. **`ToolDescriptor.RequiresApproval`** yalnız bilgi amaçlı; onay akışı Faz 6'da.
5. **`IModelProviderRegistry` boş.** Örnek API kendi `EchoModelProvider` sınıfını kaydediyor; Faz 3'te `UseOpenAI()` gelecek.

---

## Riskler

| Risk | Durum |
|------|-------|
| `DelegatingAIAgent` akış yolunda olay sırasının bozulması | Test ile kapatıldı (`Olay_sira_numaralari_bosluksuz_artar`) |
| `HarnessAgentOptions` API'si MAF'ta değişebilir | `MAAI001` bastırması tek dosyada; değişiklik tek noktayı etkiler |
| Bellek içi store'un üretimde yanlışlıkla kullanılması | Faz 4'te `/api/meta` aktif store tipini bildirecek |
