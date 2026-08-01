# AgentPrism — Mimari

> Bu doküman AgentPrism'in kalıcı mimari resmidir. Faz dokümanları (`00`–`07`) uygulama sırasını anlatır; bu doküman **ne** inşa ettiğimizi anlatır.
>
> **Bu dosya her fazın sonunda güncellenir.** Gerçekleşen tasarım ile bu doküman arasında fark varsa doküman yanlıştır — koda göre düzeltilir.

## Güncel Durum (2026-08-02)

| Paket | Durum | Faz |
|-------|-------|-----|
| `AgentPrism.Abstractions` | ✅ Tamamlandı | 1 |
| `AgentPrism.Core` | ✅ Tamamlandı | 1 · 2 (oturum yönetimi) |
| `AgentPrism.PostgreSql` | ✅ Tamamlandı | 2 |
| `AgentPrism.OpenAI` | ⬜ İskelet | 3 |
| `AgentPrism.AspNetCore` | ⬜ İskelet | 4 |
| `AgentPrism.UI` | ⬜ İskelet | 5 |
| `AgentPrism` (meta) | ✅ Paketleniyor | 0 |

Testler: **130 test geçiyor** — 42 birim testi + 88 entegrasyon testi (Testcontainers, gerçek PostgreSQL).
Build, test, pack ve format kapıları sıfır uyarı.

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

```
┌──────────────────────────────────────────────────────────────┐
│  Tüketici uygulama (ASP.NET Core)                            │
│    builder.AddAgentPrism().UsePostgreSql(..).UseOpenAI(..)   │
│    app.MapAgentPrism("/agentprism")                          │
└───────────────────────────┬──────────────────────────────────┘
                            │
┌───────────────────────────▼──────────────────────────────────┐
│  AgentPrism.UI            gömülü React SPA + middleware       │
├──────────────────────────────────────────────────────────────┤
│  AgentPrism.AspNetCore    MapAgentPrism, yönetim API'si,      │
│                           OpenAI uyumlu uçlar, erişim filtresi│
├────────────────────┬─────────────────────┬───────────────────┤
│ AgentPrism.        │ AgentPrism.         │                   │
│ PostgreSql         │ OpenAI              │                   │
│ (kalıcılık)        │ (sağlayıcı)         │                   │
├────────────────────┴─────────────────────┴───────────────────┤
│  AgentPrism.Core                                              │
│    IAgentCatalog ◄─ IAgentSource[]   (kod · MAF · veritabanı) │
│                  └─ IAgentDecorator[] (çalıştırma kaydı)      │
│    AgentDefinitionCompiler · CompiledAgentCache               │
│    AgentSessionManager · AgentSessionIdentity                 │
│    ToolRegistry · ModelProviderRegistry · InMemory*Store      │
├──────────────────────────────────────────────────────────────┤
│  AgentPrism.Abstractions  sözleşmeler                         │
└───────────────────────────┬──────────────────────────────────┘
                            │
┌───────────────────────────▼──────────────────────────────────┐
│  Microsoft Agent Framework                                    │
│    AIAgent · AgentSession · ChatClientAgent · HarnessAgent    │
│    ChatHistoryProvider · AgentSessionStore · Workflows        │
└──────────────────────────────────────────────────────────────┘
```

**Bağımlılık yönü tek yönlüdür ve döngü içermez:**

```
Abstractions ◄── Core ◄── PostgreSql
                  ▲   ◄── OpenAI
                  └────── AspNetCore ◄── UI
                                         ▲
                                  AgentPrism (meta)
```

Bu grafiği bozan bir referans eklemek yasaktır. `AgentPrism.Core.UnitTests` içindeki mimari testi bunu Faz 1'den itibaren zorlar.

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

### Faz 4 ve sonrası için hazır olanlar

```csharp
// Microsoft.Agents.AI.Hosting/AgentSessionStore.cs — ÖN SÜRÜM, K-008 gereği Faz 4  [Faz 4]
public abstract class AgentSessionStore
{
    public abstract ValueTask SaveSessionAsync(AIAgent agent, string sessionStoreId, AgentSession session, CancellationToken ct = default);
    public abstract ValueTask<AgentSession> GetSessionAsync(AIAgent agent, string sessionStoreId, CancellationToken ct = default);
    public abstract ValueTask DeleteSessionAsync(AIAgent agent, string sessionStoreId, CancellationToken ct = default);
}
// Uygulaması AgentPrism.Core'daki AgentSessionManager'a delege eder (karar K-026).

// Microsoft.Agents.AI.Hosting — çok kiracılılık  [Faz 6]
IsolationKeyScopedAgentSessionStore · SessionIsolationKeyProvider
```

**Kritik bulgu:** `Microsoft.Agents.AI.Hosting.OpenAI` depolama servislerini `TryAddSingleton` ile kaydeder. `AddOpenAIResponses()` çağrılmadan **önce** kendi implementasyonumuzu kaydedersek MAF'ın bellek içi sürümleri devre dışı kalır. Değiştirdiğimiz arayüzler: `IConversationStorage`, `IAgentConversationIndex`, `IResponsesService`, `IResponseExecutor`.

---

## 5. Veri Modeli (`agentprism` şeması)

Ayrı şema kullanılır. Tüketici uygulamanın `public` şemasına **hiç dokunulmaz**.

| Tablo | İçerik |
|-------|--------|
| `__migrations` | Uygulanmış migration'lar, checksum ile |
| `tenants` | Kiracı kaydı; tek kiracıda tek varsayılan satır |
| `agent_definitions` | Agent tanımının güncel hali |
| `agent_definition_versions` | Değişmez versiyon geçmişi, geri alma için |
| `sessions` | Serileştirilmiş `AgentSession` (**`json`**) + agent adı + kiracı + `schema_version` |
| `conversations` | Konuşma başlığı; `PostgresChatHistoryProvider` yazar, Faz 4'te Conversations API'si de kullanır |
| `conversation_items` | Konuşma mesajları, sıralı (**`json`**) |
| `responses` | Responses API yanıt kayıtları |
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

```
İstemci
  │  POST /agentprism/api/agents/{name}/run   (veya /v1/responses)     [Faz 4]
  ▼
AgentPrism.AspNetCore — erişim filtresi (loopback / token / policy)    [Faz 4]
  ▼
IAgentCatalog.ResolveAsync(name)                                       [Faz 1 ✅]
  │
  ├─ kaynaklar önceliğe göre denenir
  │    CodeAgentSource (0) → MAF köprüsü (10) → DefinitionStoreAgentSource (100)
  │
  ├─ bildirimsel tanım ise → CompiledAgentCache.GetOrAdd(name, version)
  │     └─ AgentDefinitionCompiler.Compile(definition)
  │          ├─ IModelProviderRegistry → IChatClient
  │          ├─ IToolRegistry          → AIFunction[]   (bilinmeyen ad → hata)
  │          └─ Harness? AsHarnessAgent : AsAIAgent
  │
  └─ IAgentDecorator[] uygulanır (Order'a göre, büyük olan dışta)
       └─ RunRecordingAgentDecorator → RunRecordingAgent
  ▼
RunRecordingAgent : DelegatingAIAgent                                  [Faz 1 ✅]
  │  RunEventWriter sıra numarasını üretir
  │  run.started → message.delta → tool.invoking → tool.invoked → run.completed
  │  FunctionCallContent / FunctionResultContent içeriklerden okunur
  │  DEPO HATASI ÇALIŞTIRMAYI KESMEZ — yazıcı devre dışı kalır, loglanır
  ▼
AIAgent.RunAsync / RunStreamingAsync
  │  PostgresChatHistoryProvider geçmişi conversation_items'tan yükler  [Faz 2 ✅]
  │  ve çalıştırma sonunda geri yazar
  ▼
IChatClient → OpenAI                                                   [Faz 3]
```

**Oturum yolu** (Faz 2 ✅) — çalıştırmadan bağımsız, çağıran tarafından yönetilir:

```
AgentSessionManager.GetOrCreateSessionAsync(agent, sessionId)
  │  ISessionStore.GetAsync(sessionId)          → sessions tablosu
  │  kayıt varsa  → agent.DeserializeSessionAsync(record.State)
  │  kayıt yoksa  → agent.CreateSessionAsync()
  └─ AgentSessionIdentity.SetId(session, sessionId)   → StateBag'e damga
  ▼
agent.RunAsync(message, session)
  │  RunRecordingAgent → AgentSessionIdentity.GetId(session) → RunRecord.SessionId
  │  PostgresChatHistoryProvider → ProviderSessionState → conversation_id
  ▼
AgentSessionManager.SaveSessionAsync(agent, session)
     agent.SerializeSessionAsync(session) → ISessionStore.SaveAsync()
```

Damga oturumun `StateBag` alanında yaşar ve `SerializeSessionAsync` çıktısına dahildir; bu yüzden geri yüklenen bir oturum kendi kimliğini bilir.

**Neden `DelegatingAIAgent`, neden middleware değil?**
MAF middleware zinciri agent'a özgüdür ve `HarnessAgent` kendi iç dekoratörlerini ekler. Dış sarmalayıcı, harness dahil **her** agent tipinde aynı şekilde çalışır.

**Neden sıra numarasını yazıcı üretir?**
Tek bir yazıcıdan gelen numaralar deterministik sıra garantiler. Canlı akış (SSE) ve geçmişe dönük yeniden oynatma aynı sonucu verir; istemci `Last-Event-ID` ile kaldığı yerden devam edebilir.

---

## 7. Güvenlik Modeli

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
| `AgentPrism.Core` | Evet | Reflection kullanılmaz; JSON için source generator |
| `AgentPrism.PostgreSql` | Evet | Npgsql AOT uyumlu |
| `AgentPrism.OpenAI` | Evet | — |
| `AgentPrism.AspNetCore` | Hayır | Minimal API delege yönlendirmesi reflection kullanır |
| `AgentPrism.UI` | Hayır | Gömülü varlık tarama + ASP.NET Core bağlantısı |

Bu ayrım `src/Directory.Build.props` içindeki `AgentPrismAotCompatible` özelliği ile uygulanır.

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
