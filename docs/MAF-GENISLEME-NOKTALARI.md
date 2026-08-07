# Kullandığımız MAF Genişleme Noktaları

> **Canlı referans.** Microsoft Agent Framework'ün hangi genişleme noktasını
> nasıl kullandığımızı ve hangilerini bilerek kullanmadığımızı anlatır.
> `MIMARI.md`'den ayrıldı çünkü her oturumda değil, **yalnız MAF'a dokunurken**
> okunur.
>
> Bir tipin gerçek imzasını doğrulamadan kullanma — `maf-api-kesfi` skill'i.
> Tuzaklar: [`hafiza/maf-api.md`](hafiza/maf-api.md).

---

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

**`InvokedAsync` ile geçmişe ELLE yazma (Faz 29).** Akışlı çalıştırma iptal
edildiğinde MAF geçmişi yazmaz: kesilen yanıt kaybolur ve model bir sonraki
turda kendi yarım cümlesini görmez. Konuşma katmanı barge-in'de
`chatHistory.InvokedAsync(new ChatHistoryProvider.InvokedContext(agent, session,
[], [kısmî yanıt]))` çağırır — yazmanın başka public yolu yoktur
(`StoreChatHistoryAsync` `protected`'tir). Kurucu `MAAI001` işaretlidir;
bastırma `VoiceConversationDriver.RecordInterruptionAsync` içinde tek noktadadır
(`ChatHistoryReader` ile aynı gerekçe, K-037).

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
// 🚨 Bu iki cagri YALNIZ ModelProviderRegistry.CreateChatClient icinde yapilir (Faz 48, K-320).
// Saglayici paketleri HAM istemci dondurur. Gerekce: FunctionInvokingChatClient tool cagri
// dongusunu surer ve o dongunun DISINDA duran bir halka ara turlari (tool sonuclari) GORMEZ.

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

### Faz 5'te kullanılanlar

Faz 5 yeni bir MAF tipi kullanmadı; tek genişletme kendi tipimizdir.

```csharp
// AgentPrism.Abstractions — MAF'in AgentRunOptions tipinden turer
public sealed class AgentPrismRunOptions : AgentRunOptions
{
    public Guid? RunId { get; init; }
    public override AgentRunOptions Clone();   // RunId'yi KORUR
}

// Reflection ile dogrulandi: AgentRunOptions sealed DEGILDIR,
// public parametresiz ctor'u ve protected kopya ctor'u vardir.
//   ctor()
//   protected ctor(AgentRunOptions options)
//   virtual AgentRunOptions Clone()
```

`RunRecordingAgent` bu tipi `options as AgentPrismRunOptions` ile okur; boşsa kimliği
kendisi üretir. Tip `ChatOptions` **taşımaz** — örnekleme ayarları gerekiyorsa MAF'ın
`ChatClientAgentRunOptions` tipi kullanılır ve ikisi birlikte kullanılamaz. AgentPrism
kendi uçlarında örnekleme ayarlarını agent tanımından çözdüğü için pratikte kısıt
oluşturmaz.

### Faz 6'da kullanılanlar

```csharp
// Microsoft.Agents.AI — telemetri
OpenTelemetryAgent(AIAgent innerAgent, string sourceName, bool autoWireChatClient)
// [MAAI001] · autoWireChatClient: false — sohbet istemcisi boru hattinda zaten
// UseOpenTelemetry("AgentPrism") var; otomatik baglama cift span uretirdi.

// Microsoft.Agents.AI — tool onayi
ToolApprovalAgent(AIAgent innerAgent, ToolApprovalAgentOptions options)
ToolApprovalAgentOptions { IEnumerable<Func<ToolAutoApprovalRuleContext, ValueTask<bool>>> AutoApprovalRules }
ToolAutoApprovalRuleContext { FunctionCallContent · Agent · Session · RequestMessages · RunOptions }

// Microsoft.Extensions.AI — onay mekanizmasinin KALBI
ApprovalRequiredAIFunction(AIFunction innerFunction)     // DelegatingAIFunction
ToolApprovalRequestContent(string requestId, ToolCallContent toolCall)
    → ToolApprovalResponseContent CreateResponse(bool approved, string reason)
ToolApprovalResponseContent { bool Approved · string Reason · ToolCallContent ToolCall }

// Microsoft.Agents.AI.Harness — telemetri kaynagi
HarnessAgentOptions.OpenTelemetrySourceName = "AgentPrism"
// Verilmezse harness ic span'leri MAF'in kendi kaynagina gider ve waterfall'da eksik kalir.

// ModelContextProtocol.Core 2.0.0 — MCP istemcisi
McpClient.CreateAsync(IClientTransport, McpClientOptions?, ILoggerFactory?, CancellationToken)
McpClient.ListToolsAsync(RequestOptions?, CancellationToken) → IList<McpClientTool>
McpClientTool : AIFunction                     // MAF'a dogrudan takilir, adaptor GEREKMEZ
McpClientTool.WithName(string)                 // {sunucu}_{tool} onegi icin
HttpClientTransport(HttpClientTransportOptions, ILoggerFactory)
HttpClientTransportOptions { Endpoint · AdditionalHeaders · TransportMode · OAuth }
```

**Nasıl bir arada çalışıyor:** onay gereken tool `ToolRegistry` içinde
`ApprovalRequiredAIFunction` ile sarılır → `FunctionInvokingChatClient` onu
çalıştırmak yerine `ToolApprovalRequestContent` üretir → `ToolApprovalAgent`
otomatik onay kurallarını dener → kural eşleşmezse istek yanıtta yüzeye çıkar ve
çalıştırma biter → karar bir **sonraki turun** girdisi olarak gelir.

### Faz 13'te kullanılanlar

```csharp
// Microsoft.Agents.AI.Compaction
sealed class CompactionProvider : AIContextProvider { CompactionProvider(CompactionStrategy, string?, ILoggerFactory?); }
abstract class CompactionStrategy {
    protected CompactionStrategy(CompactionTrigger trigger, CompactionTrigger? target);
    public ValueTask<bool> CompactAsync(CompactionMessageIndex, ILogger, CancellationToken); // PUBLIC, sanal değil
    protected virtual ValueTask<bool> CompactCoreAsync(CompactionMessageIndex, ILogger, CancellationToken);
}
sealed class SlidingWindowCompactionStrategy(CompactionTrigger, int minimumPreservedTurns, CompactionTrigger?) : CompactionStrategy;
sealed class TruncationCompactionStrategy(CompactionTrigger, int minimumPreservedGroups, CompactionTrigger?) : CompactionStrategy;
sealed class ToolResultCompactionStrategy(CompactionTrigger, int minimumPreservedGroups, CompactionTrigger?) : CompactionStrategy;
sealed class SummarizationCompactionStrategy(IChatClient, CompactionTrigger, int minimumPreservedGroups, string? prompt, CompactionTrigger?) : CompactionStrategy;
sealed class ContextWindowCompactionStrategy(int maxContextWindowTokens, int maxOutputTokens, double, double) : CompactionStrategy; // tetikleyici YOK
sealed class PipelineCompactionStrategy(IEnumerable<CompactionStrategy>) : CompactionStrategy;                                       // tetikleyici YOK
sealed class CompactionMessageIndex(IList<CompactionMessageGroup>, Tokenizer);  // Microsoft.ML.Tokenizers — gecisli bagimlilik, yeni paket YOK
delegate bool CompactionTrigger(CompactionMessageIndex);
static class CompactionTriggers { TokensExceed/MessagesExceed/TurnsExceed/GroupsExceed/HasToolCalls/TokensBelow/All/Any }

// Microsoft.Agents.AI — bellek
sealed class FileMemoryProvider(AgentFileStore, Func<AgentSession,FileMemoryState>?, FileMemoryProviderOptions?) : AIContextProvider;
sealed class TodoProvider(TodoProviderOptions?) : AIContextProvider;
sealed class TextSearchProvider(Func<string,CancellationToken,Task<IEnumerable<TextSearchResult>>>, TextSearchProviderOptions?, ILoggerFactory?) : MessageAIContextProvider;
abstract class AgentFileStore { ReadAsync/WriteAsync/ListChildrenAsync/SearchAsync/DeleteAsync/CreateDirectoryAsync/FileExistsAsync }
sealed class InMemoryAgentFileStore : AgentFileStore;   // varsayilan (bellek ici; AddAgentPrism)
// PostgresAgentFileStore : AgentFileStore                // Faz 14 — UsePostgreSql() bunu koyar (K-110, K-114)
// Agent adi arayuzde parametre olmadigi icin ambient AgentPrismRunContext.Current?.AgentName'den okunur.

// HarnessAgentOptions'ta bu fazda baglanan uyeler
CompactionStrategy CompactionStrategy; bool DisableCompaction;
AgentFileStore FileMemoryStore; bool DisableFileMemory; bool DisableTodoProvider;
IEnumerable<AIContextProvider> AIContextProviders;  // TextSearchProvider bu yoldan eklendi
```

**🚨 Sapma:** `ChatHistoryMemoryProvider` kullanılmadı. Gerçek kurucusu
`(VectorStore, string collectionName, int vectorDimensions, ...)` istiyor —
vektör tabanlı anlamsal arama, basit oturum-içi bellek değil. Karar K-105
(kullanıcı onayladı): depoda somut bir `VectorStore` implementasyonu olmadan
kapsam dışı.

### Hâlâ kullanılmayan MAF genişleme noktaları

```csharp
// Microsoft.Agents.AI.Hosting — cok kiracili oturum deposu
IsolationKeyScopedAgentSessionStore · SessionIsolationKeyProvider
// Faz 6 kiraciyi ITenantContext ile cozdu ve her sorguya filtre koydu; MAF'in
// oturum deposu sarmalayicisi gerekmedi. Tuketici kendi AgentSessionStore'unu
// MapAgentPrism'den once kaydederse onunki kazanir.

// Microsoft.Agents.AI — degerlendirme, skill, arka plan agent'lari, vektor bellek
AgentSkill · AgentSkillsProvider                       → Faz 10 (F-09)
BackgroundAgentsProvider · HarnessAgentOptions.BackgroundAgents → Faz 12 (F-10, K-062)
ChatHistoryMemoryProvider · VectorStore                → planlanmadi (K-105 — VectorStore karari verilince)
EvalItem · EvalCheck · LocalEvaluator · IAgentEvaluator → Faz 18 (F-14)
AIJudgeLoopEvaluator · LoopAgent                       → planlanmadi (eval'den AYRI kavram)
Microsoft.Agents.AI.Workflows                          → Faz 15 TAMAMLANDI (F-27, K-054)
  AgentWorkflowBuilder.Build{Sequential,Concurrent}      → hazir desen fabrikalari
  AgentWorkflowBuilder.Create{Handoff,GroupChat,Magentic}BuilderWith
  InProcessExecution.{Run,Resume}StreamingAsync          → yurutme
  StreamingRun.TrySendMessageAsync(TurnToken)            → 🚨 ZORUNLU, yoksa graf calismaz
  CheckpointManager.CreateJson(ICheckpointStore<JsonElement>, opts)
  WorkflowVisualizer.ToMermaidString · Workflow.Reflect* → Faz 16 TAMAMLANDI (graf cizimi)
  RequestPort · ExternalRequest/Response                 → Faz 16 TAMAMLANDI (human-in-the-loop)
  StreamingRun.SendResponseAsync                         → 🚨 yanit sonrasi akis YENIDEN acilmali
  MagenticWorkflowBuilder.RequirePlanSignoff(true)       → Faz 16 TAMAMLANDI (plan onayi)
Microsoft.Agents.AI.Workflows.Declarative               → ALINMADI (K-129: +19 paket, Responses API sarti)
```

**2026-08-02'de reflection ile doğrulanan ve ikinci faz planına giren bulgular:**

| Bulgu | Etkisi |
|-------|--------|
| `AgentSkillsProvider`, `CompactionProvider`, `BackgroundAgentsProvider` **`AIContextProvider`'dır** | Üçü de `ChatClientAgentOptions.AIContextProviders` ile düz agent'a takılır — harness zorunlu **değildir**. K-053'ün harness kusuru bu yolla aşılır |
| `AgentSkillsProviderOptions.Disable*Approval` varsayılanı **`false`** | Skill yükleme, kaynak okuma ve script çalıştırma Faz 6'nın onay akışından **zaten** geçer |
| `AgentFileSkillScriptRunner` bir **delegedir**; MAF hiçbir script'i kendi çalıştırmaz | Sandbox, zaman aşımı ve denetim izi tamamen AgentPrism'in sorumluluğudur (Faz 11) |
| `AgentFileStore` bir **soyutlamadır**, dosya sistemi değil | Veritabanı destekli uygulama, agent'a "dosya" verirken diske hiç dokunmaz (K-062 endişesini ortadan kaldırır) |
| `WorkflowVisualizer.ToMermaidString(workflow)` **var** | Graf metni MAF'tan gelir; arayüz onu **çizmez**, dışa aktarır — mermaid.js ~100 KB gzip eder (K-132) |
| `AIAgent.Id` sanal değil ama arka alanı salt-okunur **değil** (Faz 16) | Kalıcı executor kimliği bu alana yazılarak kuruldu; kontrol noktaları süreç ömrünü aşar (K-127) |
| Kontrol noktası bekleyen isteği taşır ve sürdürmede **aynı `RequestId` ile yeniden yayınlanır** (Faz 16) | Yanıt saklanan bir nesneyle değil, yeniden yayınlanan istekle eşleştirilir (K-128) |
| `HarnessAgentOptions` üyeleri: `AgentSkillsSource`, `CompactionStrategy`, `FileMemoryStore`, `LoopEvaluators`, `BackgroundAgents` | Harness zaten bunları içeride kullanıyor; düz agent için açığa çıkarmak gerekir |
| `ChatHistoryMemoryProvider` **`VectorStore` istiyor**, basit bellek değil (Faz 13) | Kapsam dışı bırakıldı (K-105); vektör deposu kararı verilince ayrı bir faz |
| `CompactionProvider` **tokenizer parametresi almaz**, MAF içeride kendi çözer (Faz 13) | `Microsoft.ML.Tokenizers.Data.*` gibi bir veri paketi gerekmedi; gerçek çalıştırmayla doğrulandı |
| `CompactionStrategy.CompactAsync` **public ve sanal değil**, `CompactCoreAsync` korumalı (Faz 13) | Bir sarmalayıcı iç stratejiyi ancak `CompactAsync` ile çağırabilir — C#'ta korumalı üyeye kardeş tip üzerinden erişilemez |
| `AIContextProvider`'ın **kendi alt sınıfını yazmak mümkün** — üç filtre parametreli `protected ctor`'un hepsinde varsayılan değer var, `: base()` yeterli (Faz 22) | `McpResourceContextProvider` (Mod A) bu deseni kullanan **ilk** AgentPrism-yazımı `AIContextProvider`; önceki tüm kullanımlar MAF'ın kendi tipleriydi (`CompactionProvider`, `TodoProvider`, ...). Ezilecek metot `ProvideAIContextAsync(InvokingContext, CancellationToken = default)` — `= default` atlanırsa `MA0061` |
| `ClientOAuthOptions.RedirectUri` (`ModelContextProtocol.Core`) **zorunlu**; SDK yalnız Authorization Code (+PKCE) destekler (Faz 22) | client_credentials gibi etkileşimsiz bir OAuth modu bu SDK sürümünde **yok** — planlanan "Mod 0" (K-168) bu yüzden terk edildi |

---

