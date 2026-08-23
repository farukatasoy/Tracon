# Faz 3 — Sağlayıcı Katmanı ve Agent Derleyici

> **Durum:** ✅ Tamamlandı (2026-08-02)
> **Önkoşul:** [02-POSTGRESQL-KALICILIK.md](02-POSTGRESQL-KALICILIK.md) — tamamlandı
> **Sonraki:** [04-HTTP-API.md](04-HTTP-API.md)
> **Paket:** `AgentPrism.OpenAI` (+ `Core` ve `Abstractions`'a küçük eklemeler)

---

> ### ⚗️ Damıtılmış kayıt
> Bu dosya fazın **planını** değil, fazın bıraktığı **kalıcı bilgiyi**
> taşır. Plan gövdesi, planlanan/gerçekleşen API, dosya listesi, risk ve
> açık soru bölümleri kapanışta düştü — **silinmedi, git geçmişindedir.**
>
> Tam metin — kopyala, çalıştır:
>
> ```bash
> git show 7f1833e:docs/arsiv/fazlar/03-SAGLAYICI-VE-DERLEYICI.md
> ```
>
> Damıtıldı 2026-08-23 · `scripts/dokuman-bakim.py faz-damit`

---

## Amaç ve Sonuç

OpenAI'ı bağlamak ve `AgentDefinition` → çalışan `AIAgent` yolunu uçtan uca tamamlamak. **Sonuç:** Veritabanındaki bir tanım gerçek bir OpenAI yanıtı üretiyor, tool çağırıyor ve çağrı `run_events` tablosuna yazılıyor. Arayüz ve HTTP katmanı olmadan. Elle doğrulandı; çıktılar aşağıda. ---

## Plandan Sapmalar

Beş sapma var. Hepsi ölçüme dayanıyor.

### S1 — `UseOpenAI()` iki sağlayıcı kaydeder *(kullanıcı kararı)*

**Plan:** "Seçim `ModelBinding` üzerinden yapılır."
**Sorun:** `ModelBinding`'de böyle bir alan yoktu ve eklemek `Abstractions`'a sağlayıcıya özgü bir alan sızdırırdı.
**Yapılan:** Tek `UseOpenAI()` çağrısı iki sağlayıcı kaydeder — `openai` ve `openai-responses`. Seçim `ModelBinding.Provider` ile yapılır, `Abstractions` değişmez, iki yüzey de `/models` çıktısında görünür.

### S2 — Responses API sunucu tarafı depolamayı **kapatır** *(kullanıcı kararı)*

**Plan:** "Responses API; geçmişi servis yönetir."
**Ölçüm:** Sunucu tarafı depolama açıkken OpenAI bir konuşma kimliği döndürüyor ve MAF çalışma anında hata veriyor:

```
System.InvalidOperationException: Only ConversationId or ChatHistoryProvider may be used, but not both.
  at Microsoft.Agents.AI.ChatClientAgent.UpdateSessionConversationId(...)
```

`UsePostgreSql()` her derlenen agent'a bir `ChatHistoryProvider` bağlar (Faz 2). İki yol aynı anda çalışamaz. Bu hata **yalnızca PostgreSQL açıkken** ortaya çıkar; bellek içi kurulumda sessizce çalışır.

**Yapılan:** `AsIChatClientWithStoredOutputDisabled(model)` kullanılıyor. Geçmiş AgentPrism'in PostgreSQL'inde kalır; denetim izi, kiracı yalıtımı ve replay vaadi korunur. Doğrulandı (aşağıdaki çıktı, adım 7).

### S3 — Yerleşik model kataloğu **yok** *(kullanıcı kararı)*

**Plan:** "`ModelProviderDescriptor` her model için: ad, context penceresi, ... fiyat metadata'sı."
**Ölçüm:** Faz 3 sırasında bilgiye dayanarak yazılan yerleşik liste (`gpt-5.1`, `gpt-5`, `gpt-4.1`, `gpt-4o`, `o3`, `o4-mini`) gerçek bir hesabın erişebildiği modellerin **hiçbirini** içermiyordu. `gpt-4.1-mini` çağrısı `HTTP 403 model_not_found` döndü. Gerçek liste: `gpt-5.4-mini`, `gpt-5.6-luna`, `gpt-5.6-terra`.

**Yapılan:** `OpenAIModelCatalog.Models` kaldırıldı. Katalog tamamen `AgentPrism:Providers:OpenAI:Models` ayarından gelir. Bir NuGet paketi model listesini güncel tutamaz — OpenAI modelleri sürümlerden hızlı değişir. Karar K-032.

### S4 — `ReasoningEffort` derleyiciye bağlandı *(kullanıcı kararı)*

`ModelBinding.ReasoningEffort` Faz 1'den beri hiçbir yerde okunmuyordu. Artık `AgentDefinitionCompiler.BuildChatOptions` içinde `ChatOptions.Reasoning`'e çevriliyor. Geçersiz değer sessizce yok sayılmaz; `AgentPrismCompilationException` ile reddedilir ve geçerli değerler listelenir.

### S5 — `AddToolsFrom` iki aşırı yükleme + attribute *(kullanıcı kararı)*

**Plan:** `AddToolsFrom<T>()`.
**Sorun:** C# statik sınıfları tür argümanı olarak kabul etmez (`CS0718`). Plandaki `OrderTools` örneği tam olarak bir `static class`.
**Yapılan:** `AddToolsFrom(Type)` aşırı yüklemesi eklendi. Ayrıca tarama `[AgentPrismTool]` ile açık işaretleme ister; işaretsiz metotlar tool olmaz. Bu, K2 güvenlik sınırının doğal devamıdır — bir sınıfa metot eklemek onu kazara agent'a açmaz.

---

## `IChatClient` Boru Hattı

Her sağlayıcı aynı hattı kurar:

```csharp
inner.AsBuilder()
     .UseFunctionInvocation(loggerFactory)                                    // tool dongusu MAF'ta
     .UseOpenTelemetry(loggerFactory, AgentPrismDiagnostics.ActivitySourceName)  // Faz 6'nin kaynagi
     .Build();
```

`inner` seçimi:

| Yüzey | Çağrı | Geçmiş nerede |
|-------|-------|---------------|
| `openai` | `GetChatClient(model).AsIChatClient()` | AgentPrism (PostgreSQL veya bellek) |
| `openai-responses` | `GetResponsesClient().AsIChatClientWithStoredOutputDisabled(model)` | AgentPrism (aynı) |

İki yüzey de **tek** `OpenAIClient` örneğini, dolayısıyla tek HTTP bağlantı havuzunu paylaşır.

---

## Bitiş Ölçütleri (DoD)

Elle doğrulama: PostgreSQL 18 (Docker) + gerçek OpenAI anahtarı, model `gpt-5.4-mini`.

| Ölçüt | Durum | Kanıt |
|-------|-------|-------|
| `UseOpenAI(apiKey)` zincire eklenir (`AddModelProvider` ile, `Replace` **değil**) | ✅ | `OpenAIProviderExtensionsTests` |
| Veritabanındaki bir tanımdan agent derlenir ve gerçek OpenAI yanıtı üretir | ✅ | aşağıdaki çıktı, adım 1–3 |
| Tool çağrısı çalışır ve `run_events` içinde `ToolInvoking`/`ToolInvoked` görünür | ✅ | aşağıdaki çıktı, adım 4 |
| Bilinmeyen tool adı anlaşılır hata verir | ✅ | aşağıdaki çıktı, adım 5 |
| API anahtarı hiçbir log, API yanıtı veya veritabanı kaydında görünmez | ✅ | adım 8 + `SecretLeakTests` + sır taraması boş |
| Harness ayarlı bir agent bağlam sıkıştırması ile çalışır | ✅ | `/agents/arastirmaci/run` gerçek yanıt üretti |
| Dört doğrulama kapısı temiz | ✅ | `build` / `test` / `pack` / `format` → 0 uyarı, 0 hata |

### Gerçek çıktı — veritabanı tanımı + gerçek OpenAI

```
1) Tanim veritabanina yazildi.
2) Katalogdan cozuldu: RunRecordingAgent
3) OpenAI yaniti: ORD-9 siparişiniz kargoya verilmiş. Tahmini teslim: 2 gün.
4) run: agent=db-destek durum=Completed token=442
   seq=0 tip=RunStarted
   seq=1 tip=ToolInvoking     tool=get_order_status  payload=orderId=ORD-9
   seq=2 tip=ToolInvoked                             payload=ORD-9 numarali siparis kargoya verildi...
   seq=3 tip=MessageDelta
   seq=4 tip=MessageCompleted
   seq=5 tip=RunCompleted
5) Beklenen hata: 'db-bozuk' agent'i su tool'lara isaret ediyor ancak bunlar kodda kayitli degil:
   silinmis_tool. Kayitli tool'lar: get_order_status. Tool'lar yalnizca kodda tanimlanir;
   `builder.AddAgentPrism().AddTool(...)` ile kaydedin.
6) Beklenen hata: 'db-saglayicisiz' agent'i derlenemedi: 'yok-boyle' adinda bir model saglayicisi
   kayitli degil. Kayitli saglayicilar: openai, openai-responses.
7) Responses API yaniti: Ankara
8) Calistirma kayitlarinda anahtar var mi: False
```

### Gerçek çıktı — örnek API

```bash
$ curl -s localhost:5085/health
{"status":"healthy","phase":"3 - saglayici ve derleyici",
 "storage":{"persistent":false,"runStore":"InMemoryRunStore","sessionStore":"InMemorySessionStore"},
 "provider":{"openAI":true,"model":"gpt-5.4-mini","name":"openai"}}

$ curl -s -X POST localhost:5085/agents/support/run \
       -H 'Content-Type: application/json' \
       -d '{"message":"ORD-7 siparisim nerede","sessionId":"son-kontrol"}'
{"text":"ORD-7 siparişiniz **kargoya verilmiş**.  \n**Tahmini teslim:** 2 gün.","sessionId":"son-kontrol"}

$ curl -s localhost:5085/models      # iki saglayici, katalog yapilandirmadan
[{"name":"openai","models":[{"name":"gpt-5.4-mini",...},{"name":"gpt-5.6-luna",...},{"name":"gpt-5.6-terra",...}]},
 {"name":"openai-responses","models":[ ... ayni liste ... ]}]

$ curl -s localhost:5085/tools
[{"name":"get_order_status","description":"Bir siparisin kargo durumunu dondurur.",
  "jsonSchema":"{\"type\":\"object\",\"properties\":{\"orderId\":{\"type\":\"string\"}},\"required\":[\"orderId\"]}",
  "requiresApproval":false}, ...]
```

Paket bağımlılıkları (nuspec, `net8.0`): `AgentPrism.Core`, `Microsoft.Agents.AI.OpenAI`, `Microsoft.Extensions.AI.OpenAI`, `OpenAI` — **4 doğrudan bağımlılık**, geçişli sızıntı yok.

---

## Kullanım

```csharp
builder.AddAgentPrism()
       .AddToolsFrom(typeof(OrderTools))                    // [AgentPrismTool] ile isaretli metotlar
       .UseOpenAI(configuration.GetSection(OpenAIProviderOptions.SectionName))
       .UsePostgreSql(connectionString)
       .AddAgent(new AgentDefinition
       {
           Name = "support",
           Instructions = "Sen bir destek asistanisin.",
           Model = new ModelBinding
           {
               Provider = OpenAIProviderNames.ChatCompletions,   // veya .Responses
               Model = "gpt-5.4-mini",
               ReasoningEffort = "medium",                       // destekleyen modellerde
           },
           ToolNames = ["get_order_status"],
       });
```

```json
{
  "AgentPrism": {
    "Providers": {
      "OpenAI": {
        "ApiKey": "",
        "DefaultModel": "gpt-5.4-mini",
        "Endpoint": "",
        "Organization": "",
        "Timeout": "",
        "Models": [
          { "Name": "gpt-5.4-mini", "DisplayName": "GPT-5.4 mini",
            "ContextWindowTokens": 400000, "InputCostPerMillionTokens": 0.25 }
        ]
      }
    }
  }
}
```

`ApiKey` **asla** bu dosyaya yazılmaz — `dotnet user-secrets` kullanılır.

---

## Faz 4'e Devreden Notlar

**1. `tool_invocations` tablosu hâlâ boş.** Faz 2'de kuruldu, yazan yok. Faz 6'ya bırakıldı (süre hesabı `ToolInvoking`/`ToolInvoked` korelasyonu gerektirir). Faz 3'te tool çağrıları `run_events` üzerinden doğrulanıyor.

**2. 🚨 Responses API ile `ChatHistoryProvider` birlikte kullanılamaz.** Faz 4 `/v1/responses` uçlarını kuracak ve `IConversationStorage` / `IResponsesService` implementasyonlarını yazacak. O katman kendi konuşma durumunu PostgreSQL'de tutacağı için `openai-responses` sağlayıcısı ile aynı çatışmayı yaşayabilir. `OpenAIChatClientFactory.CreateInnerChatClient` içindeki `AsIChatClientWithStoredOutputDisabled` çağrısı bu yüzden vardır — kaldırmadan önce sapma S2'yi okuyun.

**3. Model kataloğu kodda yok.** `/api/models` ucu (Faz 4) boş liste dönebilir; bu bir hata değildir. Arayüz (Faz 5) boş kataloğu ele almalı ve kullanıcıyı `AgentPrism:Providers:OpenAI:Models` ayarına yönlendirmelidir.

**4. `OpenAIProviderOptions` bir `class`, `record` değil.** Bilinçlidir: `record`'un ürettiği `ToString` tüm özellikleri yazar ve API anahtarını ilk günlük satırında ifşa ederdi. `SecretLeakTests` bunu korur. Yeni ayar sınıfları için aynı kural geçerlidir.

**5. Yeni ayar eklerken üç yer güncellenir:** `OpenAIProviderOptions`, `OpenAIProviderExtensions.Bind`, `OpenAIProviderOptionsValidator`. Elle bağlama karar K-021'in bedelidir.

**6. `AgentPrism.OpenAI` AOT uyumlu kaldı.** Chat Completions yolu `IsAotCompatible=true` ile sıfır uyarı verir. `AddToolsFrom` yansıma kullanır ama `AgentPrism.Core` içindedir ve `[RequiresUnreferencedCode]` + `[RequiresDynamicCode]` ile işaretlidir — uyarı bastırılmaz, çağırana iletilir.

---
