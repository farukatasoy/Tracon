# AgentPrism — Mimari

> Bu doküman AgentPrism'in kalıcı mimari resmidir. Faz dokümanları (`00`–`07`) uygulama sırasını anlatır; bu doküman **ne** inşa ettiğimizi anlatır.
>
> **Bu dosya her fazın sonunda güncellenir.** Gerçekleşen tasarım ile bu doküman arasında fark varsa doküman yanlıştır — koda göre düzeltilir.

## Güncel Durum (2026-08-02)

| Paket | Durum | Faz |
|-------|-------|-----|
| `AgentPrism.Abstractions` | ✅ Tamamlandı | 1 · 3 (`[AgentPrismTool]`) · 4 (çalıştırma özeti) |
| `AgentPrism.Core` | ✅ Tamamlandı | 1 · 2 (oturum yönetimi) · 3 (tool tarama, reasoning) · 4 (sohbet geçmişi kaydı) |
| `AgentPrism.PostgreSql` | ✅ Tamamlandı | 2 · 4 (özet sorgusu) |
| `AgentPrism.OpenAI` | ✅ Tamamlandı | 3 |
| `AgentPrism.AspNetCore` | ✅ Tamamlandı | 4 |
| `AgentPrism.UI` | ⬜ İskelet | 5 |
| `AgentPrism` (meta) | ✅ Paketleniyor | 0 |

Testler: **302 test geçiyor** — 110 birim testi (62 Core + 48 OpenAI) + 88 fonksiyonel test
(TestHost, gerçek HTTP) + 104 entegrasyon testi (Testcontainers, gerçek PostgreSQL).
Build, test, pack ve format kapıları sıfır uyarı.

Faz 4 sonunda dış yüzey açık: stok OpenAI SDK'sı `base_url` değiştirerek AgentPrism'e
bağlanıyor, agent'ı `model` alanından seçiyor, tool döngüsü sunucuda tamamlanıyor,
konuşma hem `previous_response_id` hem `conversations.create()` ile zincirleniyor ve
her çalıştırma `run_events` tablosuna yazılıp SSE ile geri oynatılabiliyor.

---

## 1. Neden AgentPrism?

Microsoft Agent Framework (MAF) 1.16.0 ile GA oldu. Güçlü bir agent runtime sunar. Ancak resmî geliştirici arayüzü **DevUI** hâlâ preview ve dokümanı açıkça şunu söyler:

> "DevUI is a **sample app** to help you visualize and debug your agents and workflows during development. It is **not** intended for production use."

DevUI'nin kaynak kodundan doğrulanan sınırları:

| Sınır | Kanıt |
|-------|-------|
| Kalıcılık yok | `Hosting.OpenAI/ServiceCollectionExtensions.cs` yalnız `InMemoryConversationStorage`, `InMemoryAgentConversationIndex`, `InMemoryResponsesService` kaydeder |
| Erişim kilitli | `DevUIAuthFilter` loopback dışı istekleri 403 döner; token tek sabit değer |
| Agent yönetimi yok | `/v1/entities` ve `/v1/entities/{id}/info` salt okunur |
| .NET dokümanı yok | Learn sayfası C# pivotunda "Coming Soon" |
| PostgreSQL yok | Kalıcılık paketleri yalnız `CosmosNoSql` ve `Valkey` |

**AgentPrism bu boşluğu doldurur.** DevUI'nin yerine geçmez — DevUI'nin bıraktığı yerden devam eder.

| | DevUI | AgentPrism |
|---|-------|------------|
| Amaç | Geliştirme sırasında görselleştirme | Üretimde çalışan kontrol düzlemi |
| Kalıcılık | Bellek içi | PostgreSQL (`agentprism` şeması) |
| Erişim | Loopback + sabit token | Loopback + token + authorization policy |
| Agent tanımı | Salt okunur | Kod + veritabanı, versiyonlu, geri alınabilir |
| Çok kiracılılık | Yok | `tenant_id` ile her sorguda |
| Denetim izi | Yok | `audit_log` |

---

## 2. Katman Mimarisi

```mermaid
flowchart TD
    T["Tüketici uygulama · ASP.NET Core<br/>builder.AddAgentPrism().UsePostgreSql(..).UseOpenAI(..)<br/>app.MapAgentPrism('/agentprism')"]

    UI["<b>AgentPrism.UI</b><br/>gömülü React SPA + middleware"]

    HTTP["<b>AgentPrism.AspNetCore</b><br/>MapAgentPrism · /api/* yönetim API'si<br/>/v1/responses · /v1/chat/completions · /v1/conversations<br/>loopback · bearer · policy · SSE"]

    PG["<b>AgentPrism.PostgreSql</b><br/>kalıcılık"]
    OA["<b>AgentPrism.OpenAI</b><br/>openai · openai-responses"]

    CORE["<b>AgentPrism.Core</b><br/>IAgentCatalog ◄ IAgentSource[] · kod · MAF · veritabanı<br/>IAgentDecorator[] · çalıştırma kaydı<br/>AgentDefinitionCompiler · CompiledAgentCache<br/>AgentSessionManager · AgentSessionIdentity<br/>ToolRegistry · ToolMethodScanner · ModelProviderRegistry<br/>InMemory*Store"]

    ABS["<b>AgentPrism.Abstractions</b><br/>sözleşmeler"]

    MAF["<b>Microsoft Agent Framework</b><br/>AIAgent · AgentSession · ChatClientAgent · HarnessAgent<br/>ChatHistoryProvider · AgentSessionStore · Workflows"]

    T --> UI --> HTTP
    HTTP --> PG
    HTTP --> OA
    PG --> CORE
    OA --> CORE
    HTTP --> CORE
    CORE --> ABS --> MAF
```

**Bağımlılık yönü tek yönlüdür ve döngü içermez:**

```mermaid
flowchart RL
    PostgreSql --> Core
    OpenAI --> Core
    AspNetCore --> Core
    UI --> AspNetCore
    Core --> Abstractions
    Meta["AgentPrism · meta"] --> UI
    Meta --> PostgreSql
    Meta --> OpenAI

    classDef aot fill:#1f6f4a,stroke:#0d3b27,color:#ffffff
    classDef notaot fill:#7a4a1f,stroke:#3d250f,color:#ffffff
    class Abstractions,Core,PostgreSql,OpenAI aot
    class AspNetCore,UI,Meta notaot
```

> Yeşil paketler AOT uyumludur, turuncular değildir (karar K-006).

Bu grafiği bozan bir referans eklemek yasaktır. `AgentPrism.Core.UnitTests` içindeki
mimari testi bunu Faz 1'den itibaren zorlar.

---

## 3. Dört Değişmez Tasarım Kuralı

### K1 — Sıfır sürpriz
`AddAgentPrism()` tek başına çalışır. PostgreSQL yapılandırılmazsa tüm depolama bellek içine düşer. Veritabanı **zorunlu değildir**. Bir geliştirici paketi kurar, tek satır yazar ve çalışan bir arayüz görür.

### K2 — Tool'lar yalnız kodda tanımlanır
Arayüzden agent oluşturulabilir, ancak tool **kodu** yazılamaz. Arayüz sadece kodda kayıtlı tool'lardan seçim yaptırır.

> **Gerekçe:** Arayüzden çalıştırılabilir kod tanımlanabilseydi, AgentPrism arayüzüne erişen herkes sunucuda kod çalıştırabilirdi. Bu sınır bilinçlidir ve gevşetilmeyecektir.

### K3 — MAF nesneleri sızdırılır, sarmalanmaz
`AIAgent`, `AgentSession`, `ChatMessage`, `AIFunction` doğrudan kullanılır. AgentPrism bunların üzerine kendi paralel tip hiyerarşisini koymaz.

> **Gerekçe:** Sarmalama, MAF'ın her yeni sürümünde bakım borcu üretir ve tüketiciyi MAF ekosisteminden koparır. AgentPrism bir *kontrol düzlemi*dir, bir *soyutlama katmanı* değil.

### K4 — Her genişleme noktası değiştirilebilir
Tüm servisler `TryAdd*` ile kaydedilir. Tüketici kendi implementasyonunu daha önce kaydederse onunki kazanır. Aynı kural MAF'ın kendi `Hosting.OpenAI` paketinde de geçerlidir — bu yüzden `IConversationStorage` gibi arayüzleri değiştirebiliyoruz.

---

## 4. Kullandığımız MAF Genişleme Noktaları

Aşağıdaki imzalar **reflection ile doğrulanmıştır** (`Microsoft.Agents.AI` 1.16.0). Bir sonraki fazda yeni bir MAF tipi kullanacaksanız önce imzayı doğrulayın — `.agents/skills/maf-api-kesfi/SKILL.md`.

### Faz 1'de kullanılanlar

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
}

abstract class DelegatingAIAgent : AIAgent { protected DelegatingAIAgent(AIAgent innerAgent); }

// DİKKAT: AgentResponse / AgentResponseUpdate — "AgentRunResponse" DEĞİL.
sealed class AgentResponse       { IList<ChatMessage> Messages; string Text; UsageDetails? Usage; }
sealed class AgentResponseUpdate { IList<AIContent> Contents; string Text; ChatRole? Role; }

// Microsoft.Extensions.AI uzantıları
static ChatClientAgent AsAIAgent(this IChatClient c, ChatClientAgentOptions o, ILoggerFactory? lf, IServiceProvider? sp);
static HarnessAgent    AsHarnessAgent(this IChatClient c, HarnessAgentOptions o, ILoggerFactory? lf, IServiceProvider? sp);
```

**Tool çağrıları ayrı kanca gerektirmez.** MAF onları `FunctionCallContent` / `FunctionResultContent` olarak içeriklere koyar.

**`HarnessAgentOptions` üyeleri `MAAI001` ("evaluation purposes only") tanısı üretir.** Bastırma tek dosyada toplanmıştır: `AgentDefinitionCompiler.CompileHarnessAgent`.

### Faz 2'de kullanılanlar

Aşağıdaki imzalar `AgentPrism.PostgreSql` içinde **gerçekten uygulandı** ve testlidir.

```csharp
// Microsoft.Agents.AI/ChatHistoryProvider — özel kalıcılık için taban sınıf
public abstract class ChatHistoryProvider
{
    // DİKKAT: parametresiz protected ctor YOKTUR. Üç filtreyi de vermek gerekir.
    protected ChatHistoryProvider(
        Func<IEnumerable<ChatMessage>, IEnumerable<ChatMessage>>? provideOutputMessageFilter,
        Func<IEnumerable<ChatMessage>, IEnumerable<ChatMessage>>? storeInputRequestMessageFilter,
        Func<IEnumerable<ChatMessage>, IEnumerable<ChatMessage>>? storeInputResponseMessageFilter);

    public virtual IReadOnlyList<string> StateKeys { get; }
    protected virtual ValueTask<IEnumerable<ChatMessage>> ProvideChatHistoryAsync(InvokingContext ctx, CancellationToken ct = default);
    protected virtual ValueTask StoreChatHistoryAsync(InvokedContext ctx, CancellationToken ct = default);
}

// İç içe bağlam tipleri — ChatHistoryProvider.InvokingContext / .InvokedContext
sealed class InvokingContext { AIAgent Agent; AgentSession? Session; IEnumerable<ChatMessage> RequestMessages; }
sealed class InvokedContext  { AIAgent Agent; AgentSession? Session; IEnumerable<ChatMessage> RequestMessages;
                               IEnumerable<ChatMessage>? ResponseMessages; Exception? InvokeException; }

// Microsoft.Agents.AI/ProviderSessionState<TState> — session içinde tipli durum
ProviderSessionState(Func<AgentSession, TState> stateInitializer, string stateKey, JsonSerializerOptions? opts);
TState GetOrInitializeState(AgentSession session);
void   SaveState(AgentSession session, TState state);

// Microsoft.Agents.AI.Abstractions/AgentSessionStateBag — oturumla birlikte kalıcılaşır
JsonElement Serialize();
void        SetValue<T>(string key, T value, JsonSerializerOptions? opts);
bool        TryGetValue<T>(string key, out T value, JsonSerializerOptions? opts);

// AIAgent — oturum yaşam döngüsü
ValueTask<AgentSession> CreateSessionAsync(CancellationToken ct = default);
ValueTask<JsonElement>  SerializeSessionAsync(AgentSession session, JsonSerializerOptions? opts, CancellationToken ct = default);
ValueTask<AgentSession> DeserializeSessionAsync(JsonElement state, JsonSerializerOptions? opts, CancellationToken ct = default);

// ChatClientAgentOptions ve HarnessAgentOptions — İKİSİNDE DE var:
ChatHistoryProvider? ChatHistoryProvider { get; set; }
```

**ÖNEMLİ:** `ChatHistoryProvider` örneği **tüm oturumlarda paylaşılır**. Oturuma özgü hiçbir durum alan olarak tutulamaz; `ProviderSessionState` ile `AgentSession` içinde saklanır. `PostgresChatHistoryProvider` yalnız `NpgsqlDataSource` referansını tutar.

### Faz 3'te kullanılanlar

Reflection ile doğrulandı: `OpenAI` 2.12.0, `Microsoft.Extensions.AI.OpenAI` 10.8.3, `Microsoft.Agents.AI.OpenAI` 1.16.0.

```csharp
// OpenAI 2.12.0 — DİKKAT: tip adı ResponsesClient, "OpenAIResponseClient" DEĞİL
sealed class OpenAIClient
{
    OpenAIClient(ApiKeyCredential credential, OpenAIClientOptions options);
    ChatClient      GetChatClient(string model);
    ResponsesClient GetResponsesClient();               // [OPENAI001]
    Uri Endpoint { get; }                               // [OPENAI001]
}

sealed class OpenAIClientOptions : ClientPipelineOptions
{
    Uri?    Endpoint       { get; set; }
    string? OrganizationId { get; set; }                // "Organization" DEĞİL
    string? ProjectId      { get; set; }
    TimeSpan? NetworkTimeout { get; set; }              // tabandan; "Timeout" DEĞİL
}

// Microsoft.Extensions.AI.OpenAI
static IChatClient AsIChatClient(this ChatClient chatClient);                       // temiz
static IChatClient AsIChatClient(this ResponsesClient c, string? defaultModelId);   // [OPENAI001]

// Microsoft.Agents.AI.OpenAI — sunucu tarafı depolamayı kapatır
static IChatClient AsIChatClientWithStoredOutputDisabled(                           // [OPENAI001][MAAI001]
    this ResponsesClient c, string? model = null, bool includeReasoningEncryptedContent = true);

// Microsoft.Extensions.AI — boru hattı
static ChatClientBuilder AsBuilder(this IChatClient inner);
static ChatClientBuilder UseFunctionInvocation(this ChatClientBuilder b, ILoggerFactory? lf, Action<FunctionInvokingChatClient>? cfg);
static ChatClientBuilder UseOpenTelemetry(this ChatClientBuilder b, ILoggerFactory? lf, string? sourceName, Action<OpenTelemetryChatClient>? cfg);

// ChatOptions.Reasoning — ModelBinding.ReasoningEffort buraya bağlanır
sealed class ReasoningOptions { ReasoningEffort? Effort; ReasoningOutput? Output; }
enum ReasoningEffort { None, Low, Medium, High, ExtraHigh }

// AIFunctionFactory — AddToolsFrom bunu kullanır
static AIFunction Create(MethodInfo method, object? target, AIFunctionFactoryOptions options);
static AIFunction Create(MethodInfo method, Func<AIFunctionArguments, object> createInstanceFunc, AIFunctionFactoryOptions? options);
sealed class AIFunctionArguments { IServiceProvider? Services { get; set; } }   // örnek metotları için
```

🚨 **Responses API ile `ChatHistoryProvider` birlikte kullanılamaz.** Sunucu tarafı depolama açıkken OpenAI bir konuşma kimliği döndürür ve `ChatClientAgent` şu hatayı atar: *"Only ConversationId or ChatHistoryProvider may be used, but not both."* `UsePostgreSql()` her derlenen agent'a bir `ChatHistoryProvider` bağladığı için AgentPrism Responses yolunda **her zaman** `AsIChatClientWithStoredOutputDisabled` kullanır. Geçmiş bizim veritabanımızda kalır. Karar K-030.

### Faz 4'te kullanılanlar

Reflection ile doğrulandı: `Microsoft.Agents.AI.Hosting` 1.16.0-preview.260730.1,
`Microsoft.Agents.AI.Hosting.OpenAI` 1.16.0-alpha.260730.1.

```csharp
// Microsoft.Agents.AI.Hosting — ON SURUM, K-008 geregi yalniz AgentPrism.AspNetCore
public abstract class AgentSessionStore
{
    public abstract ValueTask SaveSessionAsync(AIAgent agent, string sessionStoreId, AgentSession session, CancellationToken ct = default);
    public abstract ValueTask<AgentSession> GetSessionAsync(AIAgent agent, string sessionStoreId, CancellationToken ct = default);
    public abstract ValueTask DeleteSessionAsync(AIAgent agent, string sessionStoreId, CancellationToken ct = default);
}
// AgentPrismAgentSessionStore bunu AgentSessionManager'a delege eder (K-026).

// Microsoft.Agents.AI.Hosting.OpenAI — PUBLIC yardimci. /v1/responses bunun uzerine kurulu.
public static class OpenAIResponses
{
    static OpenAIResponsesRunRequest ToAgentRunRequest(JsonElement body, OpenAIResponsesMapOptions? mapOptions = null);
    static string?  GetSessionStoreId(OpenAIResponsesRunRequest request);   // conversation ?? previous_response_id
    static string   CreateResponseId();                                     // "resp_..."
    static JsonElement WriteResponse(AgentResponse response, string responseId, string? conversationId = null);
    static IAsyncEnumerable<string> WriteResponseStreamAsync(                // HAZIR SSE cerceveleri
        IAsyncEnumerable<AgentResponseUpdate> updates, string responseId, string? conversationId = null, CancellationToken ct = default);
}

public sealed class OpenAIResponsesRunRequest
{
    string? ConversationId { get; }   IList<ChatMessage> Messages { get; }
    AgentRunOptions? Options { get; }  string? PreviousResponseId { get; }
    // DIKKAT: agent adi TASIMAZ. Govdeden kendimiz okuruz (model / metadata.entity_id).
}

// Microsoft.Agents.AI.Abstractions — oturum gecmisini okumanin public yolu
public abstract class ChatHistoryProvider
{
    public ValueTask<IEnumerable<ChatMessage>> InvokingAsync(InvokingContext ctx, CancellationToken ct = default);
    public sealed class InvokingContext                                     // [MAAI001]
    {
        public InvokingContext(AIAgent agent, AgentSession? session, IEnumerable<ChatMessage> requestMessages);
    }
}
```

🚨 **`IConversationStorage`, `IAgentConversationIndex`, `IResponsesService`, `IResponseExecutor`
`internal`'dır.** Ölçüldü: `AddOpenAIResponses()` bunları `TryAddSingleton` ile kaydediyor ancak
servis tipleri dışarıdan **adlandırılamaz** (`svcPublic=False`), `InternalsVisibleTo` yalnız
Microsoft'un test derlemesine açık. Bu yüzden MAF'ın `MapOpenAIResponses()` /
`MapOpenAIConversations()` uçları kullanılmadı — kalıcılığı değiştirmek mümkün değil.
Kararlar K-036 ve bölüm 1.

### Faz 6 için hazır olanlar

```csharp
// Microsoft.Agents.AI.Hosting — cok kiracililik  [Faz 6]
IsolationKeyScopedAgentSessionStore · SessionIsolationKeyProvider
// Tuketici kendi AgentSessionStore'unu MapAgentPrism'den once kaydederse onunki kazanir.
```

---

## 5. Veri Modeli (`agentprism` şeması)

Ayrı şema kullanılır. Tüketici uygulamanın `public` şemasına **hiç dokunulmaz**.

```mermaid
erDiagram
    tenants ||..o{ agent_definitions : "tenant_id (FK YOK)"
    tenants ||..o{ sessions : "tenant_id (FK YOK)"
    tenants ||..o{ runs : "tenant_id (FK YOK)"
    tenants ||..o{ conversations : "tenant_id (FK YOK)"
    tenants ||..o{ audit_log : "tenant_id (FK YOK)"

    agent_definitions ||--o{ agent_definition_versions : "agent_id"
    conversations ||--o{ conversation_items : "conversation_id"
    conversations ||--o{ responses : "conversation_id"
    runs ||--o{ run_events : "(run_id, seq) PK"
    runs ||--o{ tool_invocations : "run_id"
    runs ||--o| traces : "run_id"
    traces ||--o{ spans : "trace_id"

    tenants {
        uuid id PK
        text slug UK
    }
    agent_definitions {
        uuid id PK
        text name "UK (tenant_id, name)"
        integer version
        jsonb definition "GIN index"
    }
    agent_definition_versions {
        uuid id PK
        integer version "UK (agent_id, version)"
        jsonb definition "değişmez geçmiş"
    }
    sessions {
        text id PK
        text agent_name
        json state "OPAK · jsonb DEĞİL (K-027)"
        integer schema_version
    }
    conversations {
        uuid id PK
        text agent_name
        jsonb metadata
    }
    conversation_items {
        uuid id PK
        bigint seq "UK (conversation_id, seq)"
        json item "POLİMORFİK · jsonb DEĞİL (K-027)"
    }
    responses {
        uuid id PK
        jsonb payload "BOŞ · Faz 4 sessions kullanır"
    }
    runs {
        uuid id PK
        text agent_name
        text session_id
        smallint status
        bigint total_tokens
    }
    run_events {
        uuid run_id PK
        bigint seq PK
        smallint type
        text payload "text · geçerli JSON olmayabilir"
    }
    tool_invocations {
        uuid id PK
        text tool_name
        integer duration_ms "BOŞ · Faz 6 doldurur"
    }
    traces {
        uuid id PK
        text trace_id "BOŞ · Faz 6"
    }
    spans {
        uuid id PK
        text name "BOŞ · Faz 6"
    }
    audit_log {
        uuid id PK
        text action
        jsonb before
        jsonb after
    }
```

> Kesikli çizgiler (`..`) **yabancı anahtar olmayan** mantıksal bağı gösterir.
> `tenant_id` sütunlarına FK konmadı — gerekçe karar defterinde.


| Tablo | İçerik |
|-------|--------|
| `__migrations` | Uygulanmış migration'lar, checksum ile |
| `tenants` | Kiracı kaydı; tek kiracıda tek varsayılan satır |
| `agent_definitions` | Agent tanımının güncel hali |
| `agent_definition_versions` | Değişmez versiyon geçmişi, geri alma için |
| `sessions` | Serileştirilmiş `AgentSession` (**`json`**) + agent adı + kiracı + `schema_version` |
| `conversations` | Konuşma başlığı; `PostgresChatHistoryProvider` yazar |
| `conversation_items` | Konuşma mesajları, sıralı (**`json`**) |
| `responses` | **Boş.** `/v1/responses` ve `/v1/conversations` durumu `sessions` tablosunda tutulur (K-036, K-043); ayrı bir yanıt kaydı yazılmadı |
| `runs` | Çalıştırma özeti: agent, oturum, durum, token, süre, maliyet |
| `run_events` | Append-only olay akışı, `(run_id, seq)` birincil anahtar |
| `tool_invocations` | Tool çağrıları: ad, argüman, sonuç, süre, hata |
| `traces` / `spans` | OpenTelemetry span'leri (Faz 6) |
| `audit_log` | Kim, ne zaman, hangi tanımı değiştirdi |

Kurallar:

- Zaman alanları `timestamptz`, her zaman UTC
- **Sorgulanan** serbest yapılı alanlar `jsonb`, sorgulanan yollarda GIN index
- 🚨 **Opak ve polimorfik yükler `json`, `jsonb` DEĞİL.** `jsonb` nesne anahtarlarını yeniden sıralar; System.Text.Json'ın `$type` ayracı ilk özellik olmak zorundadır. `sessions.state` ve `conversation_items.item` bu yüzden `json`. Karar K-027.
- `run_events.payload` `text` — `RunEventWriter` argümanları AOT uyumlu kalmak için elle biçimlendirir, çıktı geçerli JSON olmayabilir
- Birincil anahtarlar `uuid` v7 — zaman sıralı, index dostu; uygulama üretir (`AgentPrismId.NewId()`), `gen_random_uuid()` **kullanılmaz**
- `RunStatus` ve `RunEventType` `smallint` olarak saklanır; enum değerleri kararlıdır
- Her tabloda `tenant_id` (`text`); `tenants` tablosuna **yabancı anahtar yoktur** — kısıt Faz 6'da kiracı yönetimiyle gelir
- Şema adı yapılandırılabilir (`AgentPrismPostgreSqlOptions.SchemaName`); `.sql` dosyalarındaki `{schema}` yer tutucusu katı doğrulamadan sonra değiştirilir (karar K-029)
- `run_events` partition'a **aday** (`created_at`); açılırsa birincil anahtarın o sütunu da içermesi gerekir

---

## 6. Çalıştırma Yolu

```mermaid
flowchart TD
    C["İstemci"]
    H["<b>AgentPrism.AspNetCore</b> — erişim filtresi<br/>loopback / bearer / policy"]
    V1["/v1/* eşlemesi<br/>agent adı = model ?? metadata.entity_id<br/>oturum = conversation ?? previous_response_id ?? yeni yanıt kimliği<br/>güvenilmez kimlikte kiracı sahipliği doğrulanır"]
    R["IAgentCatalog.ResolveAsync(name)"]
    SRC["Kaynaklar önceliğe göre<br/>CodeAgentSource 0 → MAF köprüsü 10 → DefinitionStoreAgentSource 100"]
    COMP["CompiledAgentCache.GetOrAdd(name, version)<br/>AgentDefinitionCompiler.Compile(definition)"]
    DEC["IAgentDecorator[] — Order'a göre, büyük olan dışta"]
    REC["<b>RunRecordingAgent</b> : DelegatingAIAgent<br/>RunEventWriter sıra numarasını üretir<br/>depo hatası çalıştırmayı KESMEZ"]
    RUN["AIAgent.RunAsync / RunStreamingAsync"]
    CHP["PostgresChatHistoryProvider<br/>geçmişi conversation_items'tan yükler, sonunda geri yazar"]
    LLM["IChatClient → OpenAI<br/>UseFunctionInvocation · UseOpenTelemetry"]

    C -->|"POST /api/agents/{name}/run"| H
    C -->|"POST /v1/responses · /v1/chat/completions"| H
    H --> V1 --> R --> SRC
    SRC -->|"bildirimsel tanım"| COMP --> DEC
    SRC -->|"fabrika agent'ı"| DEC
    DEC --> REC --> RUN
    RUN --> CHP --> LLM

    classDef faz4 fill:#1f4f7a,stroke:#0d2740,color:#ffffff
    classDef faz1 fill:#1f6f4a,stroke:#0d3b27,color:#ffffff
    classDef faz2 fill:#5a3a7a,stroke:#2c1c3d,color:#ffffff
    classDef faz3 fill:#7a4a1f,stroke:#3d250f,color:#ffffff
    class H,V1 faz4
    class R,SRC,COMP,DEC,REC faz1
    class CHP faz2
    class LLM faz3
```

Derleyicinin içi (`AgentDefinitionCompiler.Compile`):

```mermaid
flowchart LR
    D["AgentDefinition"] --> M["IModelProviderRegistry<br/>→ IChatClient"]
    D --> T["IToolRegistry<br/>→ AIFunction[]"]
    D --> H{"Harness var mı?"}
    T -.->|"bilinmeyen tool adı"| E["AgentPrismCompilationException"]
    M -.->|"bilinmeyen sağlayıcı"| E
    H -->|"evet"| HA["AsHarnessAgent"]
    H -->|"hayır"| CA["AsAIAgent"]

    classDef hata fill:#7a1f1f,stroke:#3d0f0f,color:#ffffff
    class E hata
```

Çalıştırma olayları, `RunEventWriter` tarafından boşluksuz sıra numarasıyla yazılır:

```mermaid
stateDiagram-v2
    [*] --> RunStarted
    RunStarted --> MessageDelta
    RunStarted --> ToolInvoking
    MessageDelta --> MessageDelta
    MessageDelta --> ToolInvoking
    ToolInvoking --> ToolInvoked
    ToolInvoking --> ToolFailed
    ToolInvoked --> MessageDelta
    ToolFailed --> RunFailed
    MessageDelta --> MessageCompleted
    MessageCompleted --> RunCompleted
    RunCompleted --> [*]
    RunFailed --> [*]
```

**Oturum yolu** (Faz 2 ✅) — çalıştırmadan bağımsız, çağıran tarafından yönetilir:

```mermaid
sequenceDiagram
    autonumber
    participant Cagiran as Çağıran
    participant Mgr as AgentSessionManager
    participant Store as ISessionStore
    participant Agent as AIAgent
    participant Rec as RunRecordingAgent

    Cagiran->>Mgr: GetOrCreateSessionAsync(agent, sessionId)
    Mgr->>Store: GetAsync(sessionId)
    alt kayıt var
        Store-->>Mgr: SessionRecord
        Mgr->>Agent: DeserializeSessionAsync(record.State)
    else kayıt yok
        Store-->>Mgr: null
        Mgr->>Agent: CreateSessionAsync()
    end
    Agent-->>Mgr: AgentSession
    Mgr->>Mgr: AgentSessionIdentity.SetId → StateBag damgası
    Mgr-->>Cagiran: AgentSession

    Cagiran->>Agent: RunAsync(message, session)
    Agent->>Rec: çalıştırma sarmalayıcısı
    Rec->>Rec: AgentSessionIdentity.GetId → RunRecord.SessionId

    Cagiran->>Mgr: SaveSessionAsync(agent, session)
    Mgr->>Agent: SerializeSessionAsync(session)
    Mgr->>Store: SaveAsync(record)
```

Damga oturumun `StateBag` alanında yaşar ve `SerializeSessionAsync` çıktısına dahildir; bu yüzden geri yüklenen bir oturum kendi kimliğini bilir.

**Neden `DelegatingAIAgent`, neden middleware değil?**
MAF middleware zinciri agent'a özgüdür ve `HarnessAgent` kendi iç dekoratörlerini ekler. Dış sarmalayıcı, harness dahil **her** agent tipinde aynı şekilde çalışır.

**Neden sıra numarasını yazıcı üretir?**
Tek bir yazıcıdan gelen numaralar deterministik sıra garantiler. Canlı akış (SSE) ve geçmişe dönük yeniden oynatma aynı sonucu verir; istemci `Last-Event-ID` ile kaldığı yerden devam edebilir.

---

## 7. Güvenlik Modeli

```mermaid
flowchart TD
    REQ["Gelen istek"] --> META{"yol = {prefix}/api/meta ?"}
    META -->|evet| OK["Uç çalışır"]
    META -->|hayır| POL{"AuthorizationPolicy tanımlı mı?"}
    POL -->|evet, başarısız| F403["403 Forbidden"]
    POL -->|"hayır ya da başarılı"| LB{"AllowRemoteAccess kapalı<br/>ve istek loopback dışı mı?"}
    LB -->|evet| F403b["403 Forbidden<br/>ProblemDetails"]
    LB -->|hayır| TOK{"AuthToken tanımlı mı?"}
    TOK -->|"evet, başlık geçersiz"| F401["401 Unauthorized<br/>WWW-Authenticate: Bearer"]
    TOK -->|"hayır ya da geçerli"| OK

    classDef red fill:#7a1f1f,stroke:#3d0f0f,color:#ffffff
    classDef green fill:#1f6f4a,stroke:#0d3b27,color:#ffffff
    class F403,F403b,F401 red
    class OK green
```

Üç katman, sırayla uygulanır:

1. **Loopback kısıtı** — `AllowRemoteAccess = false` (varsayılan). Loopback dışı istek `403` alır. Kaza ile açılmaya karşı koruma.
2. **Bearer token** — `AuthToken` doluysa `Authorization: Bearer` başlığı sabit zamanlı karşılaştırma ile denetlenir.
3. **Authorization policy** — `RequireAuthorization("policy")` ile ASP.NET Core kimlik doğrulama boru hattına bağlanır. Üretimde kullanılan yol budur.

`{prefix}/api/meta` kimlik doğrulaması olmadan erişilebilir. Arayüzün hangi kimlik yöntemini kullanacağını öğrenmesi için gereklidir; hiçbir hassas veri döndürmez.

Ek sınırlar:

- Sırlar (`ApiKey`, bağlantı dizesi) **hiçbir zaman** veritabanına yazılmaz, API'den dönmez, arayüzde gösterilmez
- Tüm tanım değişiklikleri `audit_log`'a yazılır
- `previous_response_id` ve `conversation_id` güvenilmez girdi kabul edilir; her zaman kiracı sahipliği doğrulanır

---

## 8. Sürüm Politikası

`Microsoft.Agents.AI.Hosting` (preview) ve `Microsoft.Agents.AI.Hosting.OpenAI` (alpha) hâlâ ön sürümdür. NuGet, ön sürüm bağımlılığı olan bir paketi kararlı olarak yayınlamayı engellemez ancak bu yanıltıcı olur.

Bu yüzden:

- AgentPrism, bu iki paket GA olana kadar `1.0.0-preview.N` olarak yayınlanır
- Ön sürüm bağımlılığı **yalnızca** `AgentPrism.AspNetCore` içinde toplanır
- `Abstractions`, `Core`, `PostgreSql`, `OpenAI` yalnız GA paketlere bağlıdır

Sonuç: MAF GA'ya geçtiğinde tek bir pakette sürüm güncellemesi yeterlidir.

---

## 9. Trim ve AOT

| Paket | AOT uyumlu | Neden |
|-------|-----------|-------|
| `AgentPrism.Abstractions` | Evet | Saf sözleşmeler |
| `AgentPrism.Core` | Evet | Yansımaya dayanan tek yol `AddToolsFrom` / `AddTool(Delegate)`; ikisi de `[RequiresUnreferencedCode]` + `[RequiresDynamicCode]` ile işaretli — uyarı bastırılmaz, çağırana iletilir |
| `AgentPrism.PostgreSql` | Evet | Npgsql AOT uyumlu |
| `AgentPrism.OpenAI` | Evet | Ölçüldü (Faz 3): `IsAotCompatible=true` ile sıfır uyarı. `OPENAI001`/`MAAI001` deneysel API tanılarıdır, AOT tanısı değil |
| `AgentPrism.AspNetCore` | Hayır | Minimal API delege yönlendirmesi reflection kullanır. Bayrak `Directory.Build.targets` içinde türetilir — `src/Directory.Build.props` csproj'dan önce yüklendiği için orada türetmek `false` tercihini yok sayardı (K-006) |
| `AgentPrism.UI` | Hayır | Gömülü varlık tarama + ASP.NET Core bağlantısı |

Bu ayrım `AgentPrismAotCompatible` özelliği ile uygulanır: varsayılan `src/Directory.Build.props` içinde verilir, `IsAotCompatible` türetmesi ise `Directory.Build.targets` içinde yapılır (csproj okunduktan **sonra**).

AOT uyumluluğu Faz 1'de üç somut kısıt getirdi:

| Kısıt | Çözüm |
|-------|-------|
| `ValidateDataAnnotations()` yansıma kullanır | Elle yazılmış `AgentPrismOptionsValidator` |
| `optionsBuilder.Bind()` yansıma kullanır | `EnableConfigurationBindingGenerator=true` (kaynak üreteci) |
| Tool argümanlarını JSON'a çevirme | Elle biçimlendirme; `JsonSerializer` kullanılmaz |

`AgentPrism.PostgreSql` (Faz 2) `jsonb` alanlarını serileştirirken **System.Text.Json kaynak üreteci** kullanmalıdır (`JsonSerializerContext`); yansımaya dayanan aşırı yüklemeler AOT vaadini bozar.

---

## 10. İlgili Dokümanlar

| Doküman | İçerik |
|---------|--------|
| [KARARLAR.md](KARARLAR.md) | Karar defteri — gerekçeleriyle kalıcı tercihler |
| [00-ALTYAPI.md](00-ALTYAPI.md) | Faz 0 — build ve paketleme altyapısı |
| [01-CEKIRDEK-SOYUTLAMALAR.md](01-CEKIRDEK-SOYUTLAMALAR.md) | Faz 1 — sözleşmeler ve runtime |
| [02-POSTGRESQL-KALICILIK.md](02-POSTGRESQL-KALICILIK.md) | Faz 2 — kalıcılık katmanı |
| [03-SAGLAYICI-VE-DERLEYICI.md](03-SAGLAYICI-VE-DERLEYICI.md) | Faz 3 — OpenAI ve agent derleyici |
| [04-HTTP-API.md](04-HTTP-API.md) | Faz 4 — HTTP katmanı |
| [05-AGENTPRISM-UI.md](05-AGENTPRISM-UI.md) | Faz 5 — arayüz |
| [06-GOZLEMLENEBILIRLIK.md](06-GOZLEMLENEBILIRLIK.md) | Faz 6 — telemetri, workflows, çok kiracılılık |
| [07-SAGLAMLASTIRMA-VE-YAYIN.md](07-SAGLAMLASTIRMA-VE-YAYIN.md) | Faz 7 — sağlamlaştırma ve yayın |
