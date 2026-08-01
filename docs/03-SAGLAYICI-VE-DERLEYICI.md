# Faz 3 — Sağlayıcı Katmanı ve Agent Derleyici

> **Durum:** Planlandı
> **Önkoşul:** [02-POSTGRESQL-KALICILIK.md](02-POSTGRESQL-KALICILIK.md)
> **Sonraki:** [04-HTTP-API.md](04-HTTP-API.md)
> **Paket:** `AgentPrism.OpenAI` (+ `AgentPrism.Core` içinde derleyici tamamlama)

---

## Amaç

OpenAI'ı bağlamak ve `AgentDefinition` → çalışan `AIAgent` yolunu uçtan uca tamamlamak. Bu fazın sonunda veritabanındaki bir tanım gerçek bir model yanıtı üretir — arayüz ve HTTP katmanı olmadan.

---

## Tasarım Kararları

### Sağlayıcı soyutlaması, tek sağlayıcı implementasyonu

`IModelProviderRegistry` birden çok sağlayıcıyı ada göre tutar. Bugün yalnız `openai` kayıtlıdır.

Gerekçe: yarın `AgentPrism.Anthropic` veya `AgentPrism.AzureOpenAI` eklemek **breaking change olmamalıdır**. Modüler paketleme kararının ana sebebi budur. Soyutlama bugün tek implementasyonla bile bedelini öder.

### API anahtarı asla veritabanına yazılmaz

`AgentDefinition` yalnız `ModelBinding` taşır — sağlayıcı adı ve model adı. Kimlik bilgisi `AgentPrismOptions.Providers` altından, yani yapılandırmadan gelir.

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

## Derleyici Tamamlama

Faz 1'de iskeleti kurulan `AgentDefinitionCompiler` bu fazda gerçek sağlayıcı ile tamamlanır.

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

**Bu fazda kapalı kalan yetenekler:** shell erişimi ve arka plan agent'ları. Bunlar ayrı bir güvenlik değerlendirmesi gerektirir ve Faz 6'da ele alınır.

---

## Dosya Listesi

```
src/AgentPrism.OpenAI/
├── OpenAIProviderExtensions.cs        (UseOpenAI)
├── OpenAIProviderOptions.cs
├── OpenAIChatClientFactory.cs
├── OpenAIModelCatalog.cs
└── OpenAIModelDescriptors.cs

src/AgentPrism.Core/
├── Compilation/AgentDefinitionCompiler.cs     (tamamlanır)
├── Compilation/AgentPrismCompilationException.cs
├── Providers/ModelProviderRegistry.cs
└── Tools/ToolDescriptorFactory.cs             (JSON şema üretimi)
```

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

- [ ] `UseOpenAI(apiKey)` zincire eklenir
- [ ] Veritabanındaki bir tanımdan agent derlenir ve gerçek OpenAI yanıtı üretir
- [ ] Tool çağrısı çalışır ve `tool_invocations` tablosuna yazılır
- [ ] Bilinmeyen tool adı anlaşılır hata verir
- [ ] API anahtarı hiçbir log, API yanıtı veya veritabanı kaydında görünmez
- [ ] Harness ayarlı bir agent bağlam sıkıştırması ile çalışır

Manuel doğrulama:

```bash
cd samples/AgentPrism.Api
dotnet user-secrets set "AgentPrism:Providers:OpenAI:ApiKey" "sk-..."
dotnet run
# örnek uygulama başlangıçta bir tanım oluşturur ve çalıştırır
```

---

## Riskler

| Risk | Önlem |
|------|-------|
| OpenAI model adları ve fiyatları değişir | Model kataloğu yapılandırmadan genişletilebilir; kod değişikliği gerekmez |
| `HarnessAgentOptions` API'si MAF'ta değişebilir | Kullanım tek dosyada toplanır |
| Tool JSON şema üretimi AOT'ta reflection gerektirir | `AIFunctionFactory` şemayı kendisi üretir; kendi reflection'ımızı yazmayız |
