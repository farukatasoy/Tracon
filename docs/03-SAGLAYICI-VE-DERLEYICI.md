# Faz 3 — Sağlayıcı Katmanı ve Agent Derleyici

> **Durum:** Planlandı
> **Önkoşul:** [02-POSTGRESQL-KALICILIK.md](02-POSTGRESQL-KALICILIK.md)
> **Sonraki:** [04-HTTP-API.md](04-HTTP-API.md)
> **Paket:** `AgentPrism.OpenAI`
>
> ⚠️ **Faz 1 bu fazın işinin bir kısmını zaten yaptı.** `AgentDefinitionCompiler`, `IToolRegistry`, `IModelProviderRegistry` ve `ModelProviderRegistry` **tamamlandı ve testli**. Bu faza kalan iş: `IModelProvider` arayüzünün OpenAI uygulaması ve model kataloğu. Ayrıntı: aşağıdaki "Faz 1'de tamamlananlar" tablosu.

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

Referans uygulama: `samples/AgentPrism.Api/EchoModelProvider.cs` — ağa çıkmayan, çalışan bir örnek.

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
