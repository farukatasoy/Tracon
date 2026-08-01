# AgentPrism — Mimari

> Bu doküman AgentPrism'in kalıcı mimari resmidir. Faz dokümanları (`00`–`07`) uygulama sırasını anlatır; bu doküman **ne** inşa ettiğimizi anlatır.

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
│  AgentPrism.Core          katalog, derleyici, tool defteri,   │
│                           çalıştırma kaydı, bellek içi store  │
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

Aşağıdaki imzalar MAF kaynak kodundan alınmıştır (`microsoft/agent-framework`, `dotnet/src/`).

```csharp
// Microsoft.Agents.AI.Hosting/AgentSessionStore.cs — soyut, değiştirilebilir
public abstract class AgentSessionStore
{
    public abstract ValueTask SaveSessionAsync(AIAgent agent, string sessionStoreId, AgentSession session, CancellationToken ct = default);
    public abstract ValueTask<AgentSession> GetSessionAsync(AIAgent agent, string sessionStoreId, CancellationToken ct = default);
    public abstract ValueTask DeleteSessionAsync(AIAgent agent, string sessionStoreId, CancellationToken ct = default);
}

// Microsoft.Agents.AI/ChatHistoryProvider.cs — özel kalıcılık için taban sınıf
protected virtual ValueTask<IEnumerable<ChatMessage>> ProvideChatHistoryAsync(InvokingContext ctx, CancellationToken ct);
protected virtual ValueTask StoreChatHistoryAsync(InvokedContext ctx, CancellationToken ct);

// Microsoft.Agents.AI/ProviderSessionState<T> — session içinde tipli durum
// ÖNEMLİ: ChatHistoryProvider örneği tüm oturumlarda paylaşılır.
// Oturuma özgü hiçbir durum alan olarak tutulamaz; AgentSession içinde saklanır.

// Microsoft.Agents.AI.Hosting/HostApplicationBuilderAgentExtensions.cs
public static IHostedAgentBuilder AddAIAgent(this IHostApplicationBuilder builder, string name, string? instructions, ...);
public static IHostedAgentBuilder AddAIAgent(this IHostApplicationBuilder builder, string name, Func<IServiceProvider, string, AIAgent> createAgentDelegate, ...);

// Microsoft.Agents.AI.Hosting — çok kiracılılık için hazır yapılar
IsolationKeyScopedAgentSessionStore
SessionIsolationKeyProvider
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
| `sessions` | Serileştirilmiş `AgentSession` (jsonb) + agent adı + kiracı |
| `conversations` | OpenAI uyumlu konuşma kaydı |
| `conversation_items` | Konuşma mesajları, sıralı |
| `responses` | Responses API yanıt kayıtları |
| `runs` | Çalıştırma özeti: agent, oturum, durum, token, süre, maliyet |
| `run_events` | Append-only olay akışı, `(run_id, seq)` birincil anahtar |
| `tool_invocations` | Tool çağrıları: ad, argüman, sonuç, süre, hata |
| `traces` / `spans` | OpenTelemetry span'leri (Faz 6) |
| `audit_log` | Kim, ne zaman, hangi tanımı değiştirdi |

Kurallar:

- Zaman alanları `timestamptz`, her zaman UTC
- Serbest yapılı alanlar `jsonb`; sorgulanan yollarda GIN index
- Birincil anahtarlar `uuid` v7 — zaman sıralı, index dostu
- Her tabloda `tenant_id`; tek kiracıda sabit varsayılan
- `run_events` partition'a hazır (`created_at`), partition Faz 6'da açılır

---

## 6. Çalıştırma Yolu

```
İstemci
  │  POST /agentprism/api/agents/{name}/run   (veya /v1/responses)
  ▼
AgentPrism.AspNetCore
  │  erişim filtresi → loopback / token / policy
  ▼
IAgentCatalog.ResolveAsync(name)
  │  kod agent'ı mı, DB tanımı mı?
  ├─ kod   → MAF DI'sından çözülür
  └─ DB    → AgentDefinitionCompiler
              ├─ IModelProviderRegistry → IChatClient
              ├─ IToolRegistry          → AIFunction[]
              └─ HarnessSettings?       → AsHarnessAgent / AsAIAgent
  ▼
RunRecordingAgent  (DelegatingAIAgent)
  │  run.started → message.delta → tool.invoking → tool.invoked → run.completed
  │  her olay IRunStore'a append edilir
  ▼
AIAgent.RunStreamingAsync(..., AgentSession)
  │  ChatHistoryProvider oturum geçmişini yükler ve yazar
  ▼
IChatClient → OpenAI
```

**Neden `DelegatingAIAgent`, neden middleware değil?**
MAF middleware zinciri agent'a özgüdür ve `HarnessAgent` kendi iç dekoratörlerini ekler. Dış sarmalayıcı, harness dahil **her** agent tipinde aynı şekilde çalışır. Bu yüzden çalıştırma kaydı bir sarmalayıcıdır.

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
