# Faz 3 — Sağlayıcı Katmanı ve Agent Derleyici

> **Durum:** 🔜 Sıradaki
> **Önkoşul:** [02-POSTGRESQL-KALICILIK.md](02-POSTGRESQL-KALICILIK.md) — tamamlandı
> **Sonraki:** [04-HTTP-API.md](04-HTTP-API.md)
> **Paket:** `AgentPrism.OpenAI`
>
> ⚠️ **Faz 1 bu fazın işinin bir kısmını zaten yaptı.** `AgentDefinitionCompiler`, `IToolRegistry`, `IModelProviderRegistry` ve `ModelProviderRegistry` **tamamlandı ve testli**. Bu faza kalan iş: `IModelProvider` arayüzünün OpenAI uygulaması ve model kataloğu. Ayrıntı: aşağıdaki "Faz 1'de tamamlananlar" tablosu.

---

## Bu Faza Başlarken

Önce şunları bu sırayla okuyun:

1. [`MIMARI.md`](MIMARI.md) — özellikle bölüm 4 (MAF genişleme noktaları) ve bölüm 6 (çalıştırma yolu)
2. [`KARARLAR.md`](KARARLAR.md) — kapatılmış tartışmaları yeniden açmayın
3. [`02-POSTGRESQL-KALICILIK.md`](02-POSTGRESQL-KALICILIK.md) — "Gerçekleşen Public API" ve "Faz 3'e Devreden Notlar"
4. [`../MEMORY.md`](../MEMORY.md) — önceki oturumların keşfettiği tuzaklar
5. Bu doküman

Skill'ler: `.agents/skills/maf-api-kesfi/` (MAF imzalarını doğrulama), `.agents/skills/faz-tamamlama/` (faz sonu protokolü).

---

## Devraldığınız Sözleşmeler

Bu imzalar **tamamlandı ve testli**. Faz 3 bunları değiştirmez, kullanır.

```csharp
// AgentPrism.Abstractions — bu fazda uygulanacak TEK arayüz
public interface IModelProvider
{
    string Name { get; }                                 // "openai" — karşılaştırma harfe duyarsız
    IReadOnlyList<ModelDescriptor> Models { get; }
    IChatClient CreateChatClient(ModelBinding binding);
}

// AgentPrism.Core — derleyicinin gerçek imzası (Faz 2'de 5. parametre eklendi)
public sealed class AgentDefinitionCompiler
{
    public AgentDefinitionCompiler(
        IModelProviderRegistry models,
        IToolRegistry tools,
        ILoggerFactory? loggerFactory = null,
        IServiceProvider? services = null,
        ChatHistoryProvider? chatHistoryProvider = null);

    public AIAgent Compile(AgentDefinition definition);
}

// AgentPrism.Core — oturum yaşam döngüsü (Faz 2)
public sealed class AgentSessionManager
{
    public ValueTask<AgentSession> GetOrCreateSessionAsync(AIAgent agent, string sessionId, CancellationToken ct = default);
    public ValueTask<string>       SaveSessionAsync(AIAgent agent, AgentSession session, CancellationToken ct = default);
    public ValueTask<bool>         DeleteSessionAsync(string sessionId, CancellationToken ct = default);
    public ValueTask<IReadOnlyList<SessionRecord>> QuerySessionsAsync(SessionQuery query, CancellationToken ct = default);
}
```

Davranış sözleşmeleri (mevcut testlerin zorladığı kurallar):

| Kural | Nerede doğrulanıyor |
|-------|--------------------|
| Bilinmeyen tool adı `AgentPrismCompilationException` atar ve kayıtlı tool'ları listeler | `AgentDefinitionCompilerTests` |
| Bilinmeyen sağlayıcı adı derleme hatası verir | aynı |
| `Harness` doluysa `AsHarnessAgent`, boşsa `AsAIAgent` | aynı |
| Derleyici DI'daki `ChatHistoryProvider`'ı üretilen agent'a bağlar | `ServiceRegistrationTests.Derlenen_agent_sohbet_gecmisini_veritabanina_yazar` |
| Sürüm artınca derlenmiş agent önbelleği doğal olarak geçersizleşir | `CompiledAgentCacheTests` |
| Kayıtlar `TryAdd*` ile yapılır; tüketicinin kaydı kazanır | `AgentPrismServiceCollectionExtensions` |

---

## 🚨 Bilinen Tuzaklar

Faz 2'de keşfedilenler; bu fazı doğrudan etkiler.

**1. `UseOpenAI()` `Replace` KULLANMAZ.** `UsePostgreSql()` mevcut bir depoyu değiştirir, bu yüzden `Replace` gerekir (karar K-025). `UseOpenAI()` ise yeni bir sağlayıcı **ekler** — `AddModelProvider(...)` yeterlidir. Bu ikisini karıştırmayın.

**2. Yerleşik DI kabı varsayılan değer taşıyan kurucu parametrelerini doldurmaz.** `AgentSessionManager` ve `AgentDefinitionCompiler` bu yüzden açık fabrika ile kaydedilir. `OpenAIModelProvider` isteğe bağlı parametre alacaksa aynısını yapın.

**3. `dotnet format`, `dotnet build`'den fazlasını yakalar.** Dört kapıyı da çalıştırın.

**4. `MA0004` (ConfigureAwait) kütüphane kodunda hata seviyesindedir** ve `await using` ifadelerini de kapsar. Kalıp: `var x = ...; await using (x.ConfigureAwait(false)) { ... }`. Faz 2'de tekrarı azaltmak için `NpgsqlHelpers` yazıldı; benzer bir ihtiyaç doğarsa aynı yolu izleyin.

**5. AOT: yansımasız JSON zorunlu.** `AgentPrism.OpenAI` AOT uyumlu işaretlidir. Serileştirme gerekiyorsa `JsonSerializerContext` kaynak üreteci kullanın; `JsonSerializer.Serialize(object)` aşırı yüklemeleri `IL2026` üretir ve build'i kırar. Örnek: `AgentPrism.PostgreSql/AgentPrismJsonContext.cs`.

**6. API anahtarı loglanmamalıdır.** Npgsql'in bağlantı dizesini loglaması Faz 2'de sorun olmadı çünkü Npgsql parolayı maskeler. OpenAI istemcisinde aynı garanti yoktur — `SecretLeakTests` bunu zorlamalıdır.

---

## Amaç

OpenAI'ı bağlamak ve `AgentDefinition` → çalışan `AIAgent` yolunu uçtan uca tamamlamak. Bu fazın sonunda veritabanındaki bir tanım gerçek bir model yanıtı üretir — arayüz ve HTTP katmanı olmadan.

---

## Tasarım Kararları

### Sağlayıcı soyutlaması, tek sağlayıcı implementasyonu

`IModelProviderRegistry` birden çok sağlayıcıyı ada göre tutar. Bugün yalnız `openai` kayıtlıdır.

Gerekçe: yarın `AgentPrism.Anthropic` veya `AgentPrism.AzureOpenAI` eklemek **breaking change olmamalıdır**. Modüler paketleme kararının ana sebebi budur. Soyutlama bugün tek implementasyonla bile bedelini öder.

### Faz 1'de tamamlananlar — yeniden yazmayın

| Bileşen | Durum | Konum |
|---------|-------|-------|
| `IModelProvider` arayüzü | ✅ | `AgentPrism.Abstractions/Models/` |
| `IModelProviderRegistry` + `ModelProviderRegistry` | ✅ | `Abstractions` + `Core/Models/` |
| `AgentDefinitionCompiler` (model + tool + harness) | ✅ | `Core/Compilation/` |
| `IToolRegistry` + `ToolRegistry` | ✅ | `Abstractions/Tools/` + `Core/Tools/` |
| `IAgentPrismBuilder.AddTool` / `AddModelProvider` | ✅ | `Core/` |
| Bilinmeyen tool → `AgentPrismCompilationException` | ✅ testli | `AgentDefinitionCompilerTests` |

Bu faza kalan iş:

```csharp
// AgentPrism.OpenAI içinde uygulanacak tek arayüz:
public interface IModelProvider
{
    string Name { get; }                                 // "openai"
    IReadOnlyList<ModelDescriptor> Models { get; }       // model kataloğu
    IChatClient CreateChatClient(ModelBinding binding);  // istemci üretimi
}
```

Referans uygulama: `samples/AgentPrism.Api/EchoModelProvider.cs` ve `tests/AgentPrism.PostgreSql.IntegrationTests/Infrastructure/EchoModelProvider.cs` — ağa çıkmayan, çalışan iki örnek.

### Faz 2'de tamamlananlar — yeniden yazmayın

| Bileşen | Durum | Konum |
|---------|-------|-------|
| `IAgentDefinitionStore` PostgreSQL uygulaması | ✅ | `PostgreSql/Stores/PostgresAgentDefinitionStore.cs` |
| `IRunStore` PostgreSQL uygulaması | ✅ | `PostgreSql/Stores/PostgresRunStore.cs` |
| `ISessionStore` PostgreSQL uygulaması | ✅ | `PostgreSql/Stores/PostgresSessionStore.cs` |
| `ChatHistoryProvider` PostgreSQL uygulaması + derleyici bağlantısı | ✅ | `PostgreSql/Stores/PostgresChatHistoryProvider.cs` |
| Gömülü migration runner (advisory lock + checksum) | ✅ | `PostgreSql/Migrations/` |
| Oturum yaşam döngüsü (`AgentSessionManager`) | ✅ | `Core/Sessions/` |
| Kiracı yalıtımı (tanım / çalıştırma / oturum) | ✅ testli | `TenantIsolationTests` |

### API anahtarı asla veritabanına yazılmaz

`AgentDefinition` yalnız `ModelBinding` taşır — sağlayıcı adı ve model adı. Kimlik bilgisi `AgentPrism:Providers:OpenAI` bölümünden, yani yapılandırmadan gelir.

Faz 2'de doğrulandı: `agent_definitions.definition` sütununa yazılan `AgentDefinitionPayload` yalnız `DisplayName`, `Description`, `Instructions`, `Model`, `ToolNames`, `Harness` ve `Metadata` alanlarını taşır. `ModelBinding` içinde kimlik bilgisi alanı **yoktur** ve eklenmeyecektir.

Yapılandırma bölümü deseni (Faz 2'de yerleşti, karar K-028):

```
AgentPrism:PostgreSql:ConnectionString
AgentPrism:Providers:OpenAI:ApiKey        ← Faz 3
```

`AgentPrismOptions` gibi ayarlar **elle bağlanır**, `Bind()` kullanılmaz (karar K-021). Yeni ayar eklerken bağlama metoduna da ekleyin; örnek: `AgentPrismPostgreSqlBuilderExtensions.Bind`.

Sonuç: veritabanı dökümü sızsa bile API anahtarı sızmaz. Arayüzden agent oluşturan kişi anahtarı göremez.

### Tool kayıt defteri güvenlik sınırıdır

```csharp
builder.AddAgentPrism()
       .AddTool(GetOrderStatus)
       .AddTool(SearchKnowledgeBase, name: "search", description: "Bilgi bankasında arar")
       .AddToolsFrom<OrderTools>();
```

`IToolRegistry` her tool için bir `ToolDescriptor` üretir: ad, açıklama, JSON şema, onay gereksinimi. Arayüz bu listeyi gösterir; agent tanımına yalnız **ad** yazılır.

Kodda bulunmayan bir tool adı derleme sırasında hata verir. Bu, tasarım kuralı K2'nin uygulama noktasıdır ve gevşetilmeyecektir.

### `IChatClient` boru hattı

```csharp
chatClient
    .AsBuilder()
    .UseFunctionInvocation()
    .UseOpenTelemetry()
    .Build();
```

`UseFunctionInvocation` tool çağrı döngüsünü MAF'a bırakır — kendi döngümüzü yazmayız. `UseOpenTelemetry` Faz 6'daki telemetri toplamanın kaynağıdır.

---

## `AgentPrism.OpenAI`

```csharp
public static IAgentPrismBuilder UseOpenAI(
    this IAgentPrismBuilder builder,
    string apiKey,
    Action<OpenAIProviderOptions>? configure = null);

public sealed class OpenAIProviderOptions
{
    public string? DefaultModel { get; set; }
    public Uri? Endpoint { get; set; }          // OpenAI uyumlu ara sunucular için
    public string? Organization { get; set; }
    public TimeSpan? Timeout { get; set; }
}
```

`OpenAIChatClientFactory` iki yolu destekler:

| Yol | Kullanım |
|-----|----------|
| `GetChatClient(model).AsIChatClient()` | Chat Completions; geçmiş yerel olarak tutulur |
| `GetOpenAIResponseClient(model)` | Responses API; geçmişi servis yönetir |

Seçim `ModelBinding` üzerinden yapılır. Varsayılan Chat Completions'tır — geçmiş bizim PostgreSQL'imizde durur ve tam kontrol bizde kalır.

### Model kataloğu

`ModelProviderDescriptor` her model için: ad, context penceresi, maksimum çıktı token'ı, akış desteği, tool desteği, fiyat metadata'sı.

Arayüz bu bilgiyi model seçim ekranında ve maliyet hesabında kullanır.

---

## Derleyicinin Gerçek Sağlayıcı ile Doğrulanması

`AgentDefinitionCompiler` Faz 1'de tamamlandı ve sahte `IChatClient` ile test edildi. Bu fazda gerçek OpenAI istemcisiyle uçtan uca doğrulanır.

```
AgentDefinition
   ├─ ModelBinding    → IModelProviderRegistry.CreateChatClient()
   │                     └─ OpenAIChatClientFactory
   ├─ ToolNames       → IToolRegistry.TryGet() → AIFunction[]
   │                     └─ bulunamayan ad → AgentPrismCompilationException
   └─ HarnessSettings
        ├─ dolu  → AsHarnessAgent(new HarnessAgentOptions { .. })
        └─ boş   → AsAIAgent(new ChatClientAgentOptions { .. })
```

### Harness ayarları

`HarnessSettings`, MAF'ın `HarnessAgentOptions` yapısını yansıtır: bağlam sıkıştırma, todo takibi, dosya erişimi, dosya belleği, tool otomatik onayı.

`HarnessSettings` → `HarnessAgentOptions` eşlemesi Faz 1'de yazıldı (`AgentDefinitionCompiler.CompileHarnessAgent`). Yeni bir harness ayarı eklerken:

1. `HarnessSettings` içine özellik ekle (`AgentPrism.Abstractions`)
2. `CompileHarnessAgent` içindeki eşlemeye ekle
3. `MAAI001` bastırması zaten o blokta — yeni üye de kapsanır

**Bu fazda kapalı kalan yetenekler:** shell erişimi ve arka plan agent'ları. Bunlar ayrı bir güvenlik değerlendirmesi gerektirir ve Faz 6'da ele alınır.

---

## Dosya Listesi

```
src/AgentPrism.OpenAI/
├── OpenAIProviderExtensions.cs        (UseOpenAI — IAgentPrismBuilder uzantısı)
├── OpenAIProviderOptions.cs
├── OpenAIModelProvider.cs             (IModelProvider uygulaması)
├── OpenAIChatClientFactory.cs
└── OpenAIModelCatalog.cs              (ModelDescriptor listesi)
```

> `UseOpenAI()` yeni bir sağlayıcı **ekler**, mevcut bir servisi değiştirmez. Bu yüzden `AddModelProvider(...)` yeterlidir; `Replace` gerekmez. (Faz 2'nin `UsePostgreSql()` durumundan farklıdır — orada mevcut depo değiştirilir.)

Faz 3'te ayrıca `AddToolsFrom<T>()` eklenir (attribute taramalı tool kaydı). Yansıma kullandığı için `[RequiresUnreferencedCode]` + `[RequiresDynamicCode]` ile işaretlenmelidir — bkz. `IAgentPrismBuilder.AddTool(Delegate, ...)` deseni.

---

## Test Stratejisi

| Test | Neyi doğrular |
|------|---------------|
| `ModelProviderRegistryTests` | Ada göre çözüm; bilinmeyen sağlayıcı → hata |
| `OpenAIChatClientFactoryTests` | Seçenek eşlemesi; anahtar loglanmaz |
| `ToolDescriptorFactoryTests` | JSON şema üretimi doğru |
| `CompilerIntegrationTests` | Tanım → çalışan agent (sahte `IChatClient`) |
| `SecretLeakTests` | API anahtarı hiçbir serileştirme çıktısında görünmez |

**Gerçek OpenAI çağrısı yapan test yoktur.** Manuel doğrulama örnek API üzerinden yapılır.

---

## Bitiş Ölçütleri (DoD)

- [ ] `UseOpenAI(apiKey)` zincire eklenir (`AddModelProvider` ile — `Replace` **değil**)
- [ ] Veritabanındaki bir tanımdan agent derlenir ve gerçek OpenAI yanıtı üretir
- [ ] Tool çağrısı çalışır ve `run_events` içinde `ToolInvoking`/`ToolInvoked` olarak görünür
- [ ] Bilinmeyen tool adı anlaşılır hata verir
- [ ] API anahtarı hiçbir log, API yanıtı veya veritabanı kaydında görünmez
- [ ] Harness ayarlı bir agent bağlam sıkıştırması ile çalışır
- [ ] Dört doğrulama kapısı temiz (`build` / `test` / `pack` / `format`)

> **Not:** `tool_invocations` tablosu Faz 2'de oluşturuldu ancak **yazan yok**. Doldurulması Faz 6'ya bırakıldı (süre hesabı `ToolInvoking`/`ToolInvoked` çiftinin korelasyonunu gerektirir). Faz 3'te tool çağrıları `run_events` üzerinden doğrulanır.

Manuel doğrulama:

```bash
cd samples/AgentPrism.Api
dotnet user-secrets set "AgentPrism:PostgreSql:ConnectionString" "Host=...;Database=AgentPrism;..."
dotnet user-secrets set "AgentPrism:Providers:OpenAI:ApiKey" "sk-..."
dotnet run                                    # http://localhost:5080
curl -X POST localhost:5080/agents/support/run \
     -H 'Content-Type: application/json' \
     -d '{"message":"1234 numarali siparisim nerede","sessionId":"demo"}'
curl localhost:5080/runs                      # usage token sayilari dolu olmali
```

---

## Riskler

| Risk | Önlem |
|------|-------|
| OpenAI model adları ve fiyatları değişir | Model kataloğu yapılandırmadan genişletilebilir; kod değişikliği gerekmez |
| `HarnessAgentOptions` API'si MAF'ta değişebilir | Kullanım tek dosyada toplanır |
| Tool JSON şema üretimi AOT'ta reflection gerektirir | `AIFunctionFactory` şemayı kendisi üretir; kendi reflection'ımızı yazmayız |
