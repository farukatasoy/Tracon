# Faz 4 — HTTP API Katmanı

> **Durum:** 🔜 Sıradaki
> **Önkoşul:** [03-SAGLAYICI-VE-DERLEYICI.md](03-SAGLAYICI-VE-DERLEYICI.md) — tamamlandı
> **Sonraki:** [05-AGENTPRISM-UI.md](05-AGENTPRISM-UI.md)
> **Paket:** `AgentPrism.AspNetCore`

---

## Bu Faza Başlarken

Önce şunları bu sırayla okuyun:

1. [`MIMARI.md`](MIMARI.md) — bölüm 4 (MAF genişleme noktaları), bölüm 6 (çalıştırma yolu), bölüm 7 (güvenlik)
2. [`KARARLAR.md`](KARARLAR.md) — kapatılmış tartışmaları yeniden açmayın
3. [`03-SAGLAYICI-VE-DERLEYICI.md`](03-SAGLAYICI-VE-DERLEYICI.md) — "Gerçekleşen Public API" ve "Faz 4'e Devreden Notlar"
4. [`../MEMORY.md`](../MEMORY.md) — önceki oturumların keşfettiği tuzaklar
5. Bu doküman

Skill'ler: `.agents/skills/maf-api-kesfi/` (MAF imzalarını doğrulama), `.agents/skills/faz-tamamlama/` (faz sonu protokolü).

---

## Devraldığınız Sözleşmeler

Bu imzalar **tamamlandı ve testli**. Faz 4 bunları değiştirmez, kullanır.

```csharp
// AgentPrism.Core — katalog ve calistirma
IAgentCatalog        : ListAsync(ct) · ResolveAsync(name, ct)
AgentDefinitionCompiler.Compile(AgentDefinition) → AIAgent
AgentSessionManager  : GetOrCreateSessionAsync · SaveSessionAsync · DeleteSessionAsync · QuerySessionsAsync
IRunStore            : QueryRunsAsync(RunQuery) · ReadEventsAsync(runId, ...)
IToolRegistry        : List() → IReadOnlyList<ToolDescriptor> · TryGet(name, out AIFunction)
IModelProviderRegistry : List() → IReadOnlyList<ModelProviderDescriptor> · CreateChatClient(ModelBinding)

// AgentPrism.OpenAI — Faz 3
OpenAIProviderNames.ChatCompletions = "openai"
OpenAIProviderNames.Responses       = "openai-responses"
UseOpenAI(apiKey | IConfiguration | Action<OpenAIProviderOptions>)

// AgentPrism.Abstractions — Faz 3
[AgentPrismTool(name, description)] · IAgentPrismBuilder.AddToolsFrom<T>() / AddToolsFrom(Type)
```

Davranış sözleşmeleri (mevcut testlerin zorladığı kurallar):

| Kural | Nerede doğrulanıyor |
|-------|--------------------|
| Bilinmeyen tool adı `AgentPrismCompilationException` atar ve kayıtlı tool'ları listeler | `AgentDefinitionCompilerTests` |
| Bilinmeyen sağlayıcı adı derleme hatası verir ve kayıtlı sağlayıcıları listeler | `ModelProviderRegistryTests` |
| `UseOpenAI()` iki sağlayıcı kaydeder; ikinci çağrı çoğaltmaz | `OpenAIProviderExtensionsTests` |
| API anahtarı hiçbir serileştirme, günlük veya istisna çıktısında görünmez | `SecretLeakTests` |
| `UsePostgreSql()` bellek içi depoların yerini `Replace` ile alır | `ServiceRegistrationTests` |
| Kayıtlar `TryAdd*` ile yapılır; tüketicinin kaydı kazanır | `AgentPrismServiceCollectionExtensions` |

---

## Faz 2 ve 3'ten Devralınanlar

Bu fazın dayandığı, **tamamlanmış ve testli** parçalar:

| Ne | Nerede | Not |
|----|--------|-----|
| `conversations` + `conversation_items` tabloları | `0001_initial.sql` | `PostgresChatHistoryProvider` zaten yazıyor. OpenAI uyumlu Conversations API'si **aynı tabloları** kullanmalı; ikinci bir tablo açma. |
| `responses` tablosu | `0001_initial.sql` | Boş. `PostgresResponsesService` bu fazda doldurur. |
| `AgentSessionManager` | `Core/Sessions/` | MAF'ın `AgentSessionStore` uygulaması **buna delege eder** — kendi kalıcılık kodunu yazma. Karar K-026. |
| `AgentSessionIdentity` | `Core/Sessions/` | Oturum kimliği `AgentSession.StateBag` içinde. `AgentSessionStore.sessionStoreId` bu damgayla eşleşmelidir. |
| Kiracı yalıtımı | `Postgres*Store` + `ITenantContext` | Depolar zaten `ITenantContext.TenantId` ile sınırlı. HTTP katmanı yalnız doğru `ITenantContext`'i kaydetmekle yükümlü. |
| `/api/meta` için depo tipi bilgisi | — | Örnek API `/health` içinde `runStore.GetType().Name` döndürüyor; aynı desen `/api/meta` için kullanılabilir (karar K-018). |
| Geçici HTTP uçları | `samples/AgentPrism.Api/Program.cs` | `/agents`, `/tools`, `/models`, `/runs`, `/sessions` çalışıyor. `MapAgentPrism()` bunların yerini alır; sözleşmeleri örnek alın. |

---

## 🚨 Bilinen Tuzaklar

**1. `conversation_items.item` sütunu `json`, `jsonb` değil** (karar K-027). Polimorfik `$type` ayracı ilk özellik olmak zorundadır ve `jsonb` anahtarları yeniden sıralar. Bu fazda `responses.payload` için de aynı soruyu sorun: yük polimorfik mi?

**2. 🚨 Responses API sunucu durumu `ChatHistoryProvider` ile çakışır.** Faz 3'te ölçüldü:

```
System.InvalidOperationException: Only ConversationId or ChatHistoryProvider may be used, but not both.
```

`UsePostgreSql()` her derlenen agent'a bir `ChatHistoryProvider` bağlar. Bu yüzden `OpenAIChatClientFactory` Responses yolunda `AsIChatClientWithStoredOutputDisabled` kullanır. Faz 4 kendi `IConversationStorage` implementasyonunu yazarken aynı çatışmayı tekrar değerlendirin — konuşma durumu **iki kez** yönetilmemelidir.

**3. `MAAI001` ve `OPENAI001` derlemeyi kırar.** MAF ve OpenAI kitaplıklarının bazı üyeleri "evaluation purposes only" işaretlidir; `TreatWarningsAsErrors` ile hata olur. Bastırma gerekçeyle ve **tek dosyada** yapılır. Mevcut örnekler: `AgentDefinitionCompiler.CompileHarnessAgent` (K-020), `OpenAIChatClientFactory.CreateInnerChatClient` (K-030, K-031).

**4. `dotnet format`, `dotnet build`'den fazlasını yakalar.** Dört kapıyı da çalıştırın.

**5. `MA0004` (ConfigureAwait) kütüphane kodunda hata seviyesindedir** ve `await using` ifadelerini de kapsar. Kalıp: `var x = ...; await using (x.ConfigureAwait(false)) { ... }`.

**6. Ayar sınıfları `record` OLMAMALIDIR.** `record`'un ürettiği `ToString` tüm özellikleri yazar ve sırları günlüğe ifşa eder. `AgentPrismUiOptions.AuthToken` bir sırdır; `SecretLeakTests` desenini bu faza taşıyın.

**7. Model kataloğu boş olabilir.** AgentPrism yerleşik model listesi taşımaz (K-032). `/api/models` boş liste dönebilir; bu bir hata değildir.

---

## Amaç

Arayüzün ve dış istemcilerin konuşacağı yüzeyi kurmak. Tek giriş noktası: `app.MapAgentPrism()`.

---

## Giriş Noktası

```csharp
app.MapAgentPrism("/agentprism", options =>
{
    options.RequireAuthorization("AgentPrismAdmin");
});
```

Varsayılan prefix `/agentprism`. Herhangi bir prefix'e bağlanabilir; arayüz bunu çalışma anında öğrenir.

---

## Uç Grupları

### A) OpenAI uyumlu çalıştırma uçları

`Microsoft.Agents.AI.Hosting.OpenAI` paketi kullanılır, ancak depolama bizim PostgreSQL implementasyonlarımızdır.

```
POST   {prefix}/v1/responses
POST   {prefix}/v1/conversations
GET    {prefix}/v1/conversations/{id}
POST   {prefix}/v1/chat/completions
```

Bu sayede mevcut OpenAI SDK'ları (Python, JavaScript, .NET) AgentPrism'e doğrudan bağlanır:

```python
client = OpenAI(base_url="https://app.example.com/agentprism/v1", api_key="...")
response = client.responses.create(metadata={"entity_id": "support"}, input="Merhaba")
```

**Kritik uygulama detayı.** MAF depolama servislerini `TryAddSingleton` ile kaydeder. Kendi implementasyonlarımız `AddOpenAIResponses()` çağrısından **önce** kaydedilmelidir:

```csharp
services.TryAddSingleton<IConversationStorage, PostgresConversationStorage>();
services.TryAddSingleton<IAgentConversationIndex, PostgresAgentConversationIndex>();
services.TryAddSingleton<IResponsesService, PostgresResponsesService>();
// ancak bundan sonra:
builder.AddOpenAIResponses();
```

Sıra bozulursa MAF'ın bellek içi implementasyonları kazanır ve kalıcılık sessizce devre dışı kalır. Bu, bir entegrasyon testi ile korunur.

### B) Yönetim API'si

```
GET    {prefix}/api/meta                      kimlik yöntemi, sürüm, aktif store'lar   [kimlik doğrulaması YOK]

GET    {prefix}/api/agents                    katalog (kod + DB)
POST   {prefix}/api/agents                    yeni tanım
GET    {prefix}/api/agents/{name}
PUT    {prefix}/api/agents/{name}             yeni versiyon üretir
DELETE {prefix}/api/agents/{name}
GET    {prefix}/api/agents/{name}/versions
POST   {prefix}/api/agents/{name}/rollback    versiyona geri dön
POST   {prefix}/api/agents/{name}/run         SSE akışlı test çalıştırma

GET    {prefix}/api/sessions                  filtreli liste
GET    {prefix}/api/sessions/{id}             mesaj geçmişi
DELETE {prefix}/api/sessions/{id}

GET    {prefix}/api/runs                      filtreli liste
GET    {prefix}/api/runs/{id}
GET    {prefix}/api/runs/{id}/events          SSE; canlı veya replay

GET    {prefix}/api/tools                     kayıtlı tool'lar + JSON şemaları
GET    {prefix}/api/models                    sağlayıcılar ve modeller
GET    {prefix}/api/stats                     token, maliyet, hata oranı
```

Minimal API + typed results. `ProblemDetails` ile tek tip hata sözleşmesi. `Microsoft.AspNetCore.OpenApi` ile OpenAPI belgesi üretilir.

---

## Güvenlik — `AgentPrismEndpointFilter`

Üç katman, sırayla:

```csharp
public sealed class AgentPrismUiOptions
{
    public bool AllowRemoteAccess { get; set; }          // varsayılan: false
    public string? AuthToken { get; set; }               // varsayılan: null
    public string? AuthorizationPolicy { get; private set; }
    public void RequireAuthorization(string policyName);
}
```

1. **Loopback kısıtı** — `AllowRemoteAccess = false` iken loopback dışı istek `403`. Kaza ile açılmaya karşı koruma. DevUI'nin davranışı ile aynı.
2. **Bearer token** — `AuthToken` doluysa `Authorization: Bearer` başlığı **sabit zamanlı** karşılaştırma ile denetlenir (`CryptographicOperations.FixedTimeEquals`).
3. **Authorization policy** — `RequireAuthorization("policy")` ASP.NET Core kimlik boru hattına bağlanır. Üretimde kullanılan yol budur.

`{prefix}/api/meta` her zaman kimlik doğrulaması olmadan erişilebilir. Arayüzün hangi kimlik yöntemini kullanacağını öğrenmesi için gereklidir. Yalnız şunları döner: sürüm, kimlik yöntemi, aktif store tipleri. Hiçbir hassas veri içermez.

### Güvenilmez girdi

`previous_response_id` ve `conversation_id` istemciden gelir ve **güvenilmez** kabul edilir. MAF dokümanının uyarısı:

> Treat `previous_response_id` and `conversation_id` as untrusted input. Authenticate and authorize the caller before using either ID to load or save a session or checkpoint.

Her yükleme öncesi kiracı sahipliği doğrulanır.

---

## Akış (Streaming)

Tüm akışlı uçlar Server-Sent Events kullanır. `Last-Event-ID` başlığı desteklenir: bağlantı koparsa istemci kaldığı sıra numarasından devam eder.

`run_events` append-only olduğu için replay ve canlı akış aynı kod yolundan geçer.

---

## Dosya Listesi

```
src/AgentPrism.AspNetCore/
├── AgentPrismEndpointRouteBuilderExtensions.cs   (MapAgentPrism)
├── AgentPrismUiOptions.cs
├── Security/AgentPrismEndpointFilter.cs
├── Security/LoopbackGuard.cs
├── Security/BearerTokenValidator.cs
├── Endpoints/MetaEndpoints.cs
├── Endpoints/AgentEndpoints.cs
├── Endpoints/SessionEndpoints.cs
├── Endpoints/RunEndpoints.cs
├── Endpoints/ToolEndpoints.cs
├── Endpoints/ModelEndpoints.cs
├── Endpoints/StatsEndpoints.cs
├── OpenAICompat/OpenAICompatRegistration.cs      (TryAdd sırası burada)
├── Streaming/SseWriter.cs
└── Contracts/*.cs                                (istek/yanıt DTO'ları)

src/AgentPrism.PostgreSql/Stores/
├── PostgresConversationStorage.cs
├── PostgresAgentConversationIndex.cs
└── PostgresResponsesService.cs
```

> `AgentPrism.PostgreSql` bu fazda `Microsoft.Agents.AI.Hosting.OpenAI` arayüzlerini implemente eder. Bu, o pakete bağımlılık getirir. **Alternatif:** bu üç sınıfı `AgentPrism.AspNetCore` içine koymak ve `PostgreSql` paketini GA-only tutmak. Faz 4 başında karar verilir ve `KARARLAR.md`'ye işlenir.

---

## Test Stratejisi

> **Bu faz `tests/AgentPrism.AspNetCore.FunctionalTests` projesini oluşturur** (`Microsoft.AspNetCore.Mvc.Testing` ile). Faz 0 yalnız `AgentPrism.Core.UnitTests`'i kurdu; test projeleri test edecekleri şeyle birlikte gelir.

`tests/AgentPrism.AspNetCore.FunctionalTests` — `WebApplicationFactory`.

| Test | Neyi doğrular |
|------|---------------|
| `LoopbackGuardTests` | Uzak IP `403`; loopback geçer |
| `BearerTokenTests` | Yanlış token `401`; sabit zamanlı karşılaştırma |
| `AuthorizationPolicyTests` | Policy başarısız → `403` |
| `MetaEndpointTests` | Kimlik doğrulaması olmadan erişilir; sır içermez |
| `AgentCrudTests` | CRUD + versiyonlama + geri alma |
| `RunStreamingTests` | SSE olay sırası; `Last-Event-ID` ile devam |
| `OpenAICompatTests` | OpenAI SDK ile uçtan uca çağrı |
| `StorageOverrideTests` | **PostgreSQL store'ları aktif, bellek içi olanlar değil** |
| `ProblemDetailsTests` | Tüm hatalar tek tip sözleşme |
| `SecretLeakTests` | `AuthToken` ve OpenAI anahtarı hiçbir yanıtta, `/api/meta` çıktısında veya günlükte görünmez |

Faz 3'ün `SecretLeakTests` sınıfı örnek alınabilir: `tests/AgentPrism.OpenAI.UnitTests/SecretLeakTests.cs`. `RecordingLoggerProvider` günlük satırlarını bellekte toplar ve sır taramasını mümkün kılar.

---

## Bitiş Ölçütleri (DoD)

- [ ] `MapAgentPrism()` her iki uç grubunu bağlar
- [ ] Üç güvenlik katmanı ayrı ayrı test edilir
- [ ] OpenAI Python SDK ile `/v1/responses` çağrısı çalışır
- [ ] `StorageOverrideTests` PostgreSQL store'larının kazandığını doğrular
- [ ] SSE akışı kopan bağlantıdan devam eder
- [ ] OpenAPI belgesi üretilir

---

## Riskler

| Risk | Önlem |
|------|-------|
| `TryAdd` sırası bozulursa kalıcılık sessizce devre dışı kalır | `StorageOverrideTests`; ayrıca `/api/meta` aktif store tipini bildirir |
| `Hosting.OpenAI` alpha API'si değişebilir | Kullanım `OpenAICompatRegistration.cs` içinde toplanır |
| SSE arkasında ters vekil arabelleği | `X-Accel-Buffering: no` başlığı; dokümanda vekil ayarı anlatılır |
