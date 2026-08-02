# AgentPrism

**Microsoft Agent Framework için üretim seviyesi agent kontrol düzlemi.**

AgentPrism, [Microsoft Agent Framework](https://learn.microsoft.com/en-us/agent-framework/overview/) üzerine kurulu bir .NET paket ailesidir. Projesine ekleyen geliştirici kendi AI harness'ini kolay ama esnek şekilde kurar ve `/agentprism` arayüzünden yönetir.

> **Durum:** Faz 6 tamamlandı — AgentPrism artık **işletilebilir**. Her çalıştırmanın span ağacı ve metriği var, geri alınamaz tool'lar kullanıcı onayı bekliyor, tool'lar uzak MCP sunucularından da gelebiliyor ve kiracı istekten çözülüyor. `app.MapAgentPrism()` yönetim API'sini, OpenAI uyumlu çalıştırma uçlarını ve gömülü yönetim arayüzünü tek prefix altına bağlar. Veritabanı hâlâ **zorunlu değildir**; yapılandırılmazsa depolama bellek içine düşer.

```csharp
builder.AddAgentPrism()
       .UsePostgreSql(connectionString)
       .UseOpenAI(apiKey)
       .AddTool(GetOrderStatus)
       .UseUI();

app.MapAgentPrism("/agentprism");
```

İki satır. Çalışan bir agent, kalıcı oturumlar ve `http://localhost:5080/agentprism`
adresinde çalışan bir kontrol düzlemi.

### Arayüz

Yedi ekran: **Agents** (katalog, tanım editörü, versiyon geçmişi, geri alma),
**Playground** (akışlı sohbet, tool kartları), **Sessions**, **Runs** (olay olay zaman
çizelgesi), **Tools**, **Models**, **Settings**.

React 19 + TypeScript ile yazılır, Vite ile derlenir ve assembly'ye **Brotli
sıkıştırılmış gömülür**. Tüketici projede hiçbir JavaScript bağımlılığı oluşmaz;
`node_modules` klasörü gerekmez. JavaScript bütçesi **88 KB gzip** (kapı: 250 KB).

Arayüz herhangi bir prefix altında çalışır (`/agentprism`, `/panel`, …) ve prefix'i
çalışma anında öğrenir. Açık ve koyu tema; varsayılan işletim sistemi tercihidir.

**Bugün çalışan HTTP yüzeyi** (Faz 4):

```csharp
// Tek giris noktasi. Erisim varsayilan olarak loopback ile sinirli.
app.MapAgentPrism("/agentprism", options =>
{
    options.RequireAuthorization("AgentPrismAdmin");   // uretimde kullanilan yol
});
```

```
GET    /agentprism/api/meta                    surum · kimlik yontemi · aktif depolar  [kimlik dogrulamasi YOK]
GET    /agentprism/api/agents                  katalog (kod + veritabani)
POST   /agentprism/api/agents                  yeni tanim        · PUT · DELETE · /versions · /rollback
POST   /agentprism/api/agents/{name}/run       SSE akisli deneme calistirmasi
GET    /agentprism/api/sessions[/{id}]         oturumlar ve sohbet gecmisi   · DELETE
GET    /agentprism/api/runs[/{id}]             calistirma kayitlari
GET    /agentprism/api/runs/{id}/events        SSE; canli veya replay, Last-Event-ID ile devam
GET    /agentprism/api/tools · /api/models · /api/stats

POST   /agentprism/v1/responses                OpenAI Responses API uyumlu
POST   /agentprism/v1/chat/completions         OpenAI Chat Completions API uyumlu
POST   /agentprism/v1/conversations            konusma ac · GET · DELETE · /items
```

Stok OpenAI SDK'si ile:

```python
from openai import OpenAI

client = OpenAI(base_url="https://app.example.com/agentprism/v1", api_key="...")

# 'model' alani agent adini tasir - ek alan gerekmez.
r = client.responses.create(model="support", input="ORD-3 siparisim nerede")
print(r.output_text)          # ORD-3 siparisiniz kargoya verilmis. Tahmini teslimat: 2 gun.

# Konusma zincirleme — iki yol da calisir
r2 = client.responses.create(model="support", input="Peki ya ORD-9?", previous_response_id=r.id)

conv = client.conversations.create()
client.responses.create(model="support", conversation=conv.id, input="Merhaba")
for item in client.conversations.items.list(conv.id):
    print(item.type, getattr(item, "role", ""))    # message / function_call / function_call_output
```

Tool döngüsü sunucuda tamamlanır; her çalıştırma olay olay kaydedilir ve SSE ile geri
oynatılabilir.

**Bugün çalışan** (Faz 6):

```csharp
builder.AddAgentPrism()
       .AddToolsFrom(typeof(OrderTools))      // [AgentPrismTool] ile işaretli metotlar
       .UseOpenAI(apiKey)                     // ← Faz 3
       .AddAgent(new AgentDefinition
       {
           Name = "support",
           Instructions = "Sen bir destek asistanısın.",
           Model = new ModelBinding { Provider = OpenAIProviderNames.ChatCompletions, Model = "gpt-5.4-mini" },
           ToolNames = ["get_order_status", "cancel_order"],
       })
       .UsePostgreSql(connectionString)       // ← Faz 2; isteğe bağlı
       .UseMcp()                              // ← Faz 6; uzak MCP tool'ları, isteğe bağlı
       .UseUI();                              // ← Faz 5; gömülü arayüz

// Kalıcı oturumla çalıştır
var agent = await catalog.ResolveAsync("support");
var session = await sessions.GetOrCreateSessionAsync(agent!, "musteri-42");

var response = await agent!.RunAsync("Siparişim nerede?", session);
await sessions.SaveSessionAsync(agent, session);

// Çalıştırmayı olay olay oku
await foreach (var e in runStore.ReadEventsAsync(runId))
{
    Console.WriteLine($"#{e.Sequence} {e.Type} {e.Text}");
}
```

Tool sınıfı:

```csharp
internal static class OrderTools
{
    [AgentPrismTool("get_order_status", "Bir siparişin kargo durumunu döndürür.")]
    public static string GetOrderStatus(string orderId) => ...;

    public static string Helper() => "...";   // işaretsiz — tool olmaz
}
```

`UseOpenAI()` **iki** sağlayıcı kaydeder: `openai` (Chat Completions) ve `openai-responses` (Responses API). Seçim `ModelBinding.Provider` ile yapılır. Her iki yolda da konuşma geçmişi AgentPrism'in veritabanında kalır.

`UseOpenAICompatible(ad, ...)` aynı paketle **herhangi bir** OpenAI uyumlu uca bağlanır — OpenRouter, Groq, vLLM, yerel Ollama/LM Studio (← Faz 8). Yerel sunucular `ApiKey` istemez. Her sağlayıcı `GET {endpoint}/models` ile ücretsiz denetlenir (`/agentprism/api/models/health`) ve ardışık hata veren bir sağlayıcı devre kesici tarafından geçici olarak durdurulur.

`UsePostgreSql()` çağrılmazsa depolama bellek içine düşer ve hiçbir şey kırılmaz. Şema, gömülü SQL migration'ları ile ayrı bir `agentprism` şemasında oluşur; uygulamanızın `public` şemasına dokunulmaz.

Çalışan örnek: [`samples/AgentPrism.Api`](samples/AgentPrism.Api).

---

## Neden?

Microsoft Agent Framework 1.16.0 ile GA oldu. Güçlü bir agent runtime sunar. Ancak resmî geliştirici arayüzü **DevUI** hâlâ preview ve dokümanı açıkça şunu söyler:

> "DevUI is a **sample app** to help you visualize and debug your agents and workflows during development. It is **not** intended for production use."

AgentPrism bu boşluğu doldurur. DevUI'nin yerine geçmez — bıraktığı yerden devam eder.

| | DevUI | AgentPrism |
|---|-------|------------|
| Amaç | Geliştirme sırasında görselleştirme | Üretimde çalışan kontrol düzlemi |
| Kalıcılık | Bellek içi | PostgreSQL (ayrı `agentprism` şeması) |
| Erişim | Loopback + sabit token | Loopback + token + authorization policy |
| Agent tanımı | Salt okunur | Kod + veritabanı, versiyonlu, geri alınabilir |
| Çok kiracılılık | Yok | Her sorguda `tenant_id` |
| Denetim izi | Yok | `audit_log` |
| .NET dokümanı | "Coming soon" | Var |

---

## Paketler

| Paket | Ne yapar |
|-------|----------|
| `AgentPrism` | Meta paket — hepsini tek referansla getirir |
| `AgentPrism.Abstractions` | ✅ Sözleşmeler; kendi implementasyonunuzu yazacaksanız yeterli |
| `AgentPrism.Core` | ✅ Çalışma zamanı, katalog, tanım derleyicisi, tool defteri, oturum yönetimi. **Veritabanı gerektirmez.** |
| `AgentPrism.PostgreSql` | ✅ Kalıcılık — gömülü SQL migration'ları, ayrı `agentprism` şeması |
| `AgentPrism.OpenAI` | ✅ OpenAI sağlayıcı adaptörü — Chat Completions + Responses, tool çağrısı, OpenTelemetry |
| `AgentPrism.Mcp` | ✅ Uzak MCP sunucularından tool keşfi — yalnız HTTP, varsayılan onaylı |
| `AgentPrism.AspNetCore` | ✅ HTTP katmanı — yönetim API'si + OpenAI uyumlu uçlar + çok kiracılılık |
| `AgentPrism.UI` | ✅ Gömülü React arayüzü — sekiz ekran, sıfır JavaScript bağımlılığı |

**Hedef framework:** `net8.0`, `net9.0`, `net10.0` · **Lisans:** MIT

---

## Kurulum

```bash
dotnet add package AgentPrism --prerelease
```

### Model kataloğu

AgentPrism **yerleşik model listesi taşımaz**. OpenAI model adları ve fiyatları bir NuGet paketinin yayın sıklığından hızlı değişir; koda gömülü bir liste kısa sürede yanıltıcı olur. Katalog yapılandırmadan gelir:

```json
{
  "AgentPrism": { "Providers": { "OpenAI": {
    "DefaultModel": "gpt-5.4-mini",
    "Models": [
      { "Name": "gpt-5.4-mini", "DisplayName": "GPT-5.4 mini",
        "ContextWindowTokens": 400000, "InputCostPerMillionTokens": 0.25 }
    ]
  }}}
}
```

Katalog bir **doğrulama listesi değildir**: burada olmayan bir model adı da kullanılabilir. Liste yalnızca arayüzün model seçim ekranını ve maliyet hesabını besler.

### Sırları ayarlayın

Bağlantı dizesi ve API anahtarı repoya **hiç girmez**. `dotnet user-secrets` kullanılır:

```bash
cd <projeniz>
dotnet user-secrets init
dotnet user-secrets set "AgentPrism:PostgreSql:ConnectionString" "Host=...;Port=5432;Database=AgentPrism;Username=...;Password=..."
dotnet user-secrets set "AgentPrism:Providers:OpenAI:ApiKey"     "sk-..."
```

`appsettings.json` yalnız şemayı gösterir, değer taşımaz.

---

## Tasarım Kuralları

Bunlar dört değişmez kuraldır. Ayrıntı: [docs/MIMARI.md](docs/MIMARI.md).

**1. Sıfır sürpriz.** `AddAgentPrism()` tek başına çalışır. PostgreSQL yapılandırılmazsa depolama bellek içine düşer. Veritabanı zorunlu değildir.

**2. Tool'lar yalnız kodda tanımlanır.** Arayüzden agent oluşturulabilir, ancak tool **kodu** yazılamaz. Arayüz sadece kodda kayıtlı tool'lardan seçim yaptırır. Bu bir güvenlik sınırıdır.

**3. MAF nesneleri sızdırılır, sarmalanmaz.** `AIAgent`, `AgentSession`, `ChatMessage` doğrudan kullanılır. AgentPrism bir kontrol düzlemidir, bir soyutlama katmanı değil.

**4. Her genişleme noktası değiştirilebilir.** Tüm servisler `TryAdd*` ile kaydedilir. Kendi implementasyonunuzu önce kaydederseniz sizinki kazanır.

---

## Yol Haritası

| Faz | Konu | Durum |
|-----|------|-------|
| [0](docs/00-ALTYAPI.md) | Build ve paketleme altyapısı | ✅ Tamamlandı |
| [1](docs/01-CEKIRDEK-SOYUTLAMALAR.md) | Çekirdek soyutlamalar ve runtime | ✅ Tamamlandı |
| [2](docs/02-POSTGRESQL-KALICILIK.md) | PostgreSQL kalıcılık katmanı | ✅ Tamamlandı |
| [3](docs/03-SAGLAYICI-VE-DERLEYICI.md) | OpenAI sağlayıcısı ve agent derleyici | ✅ Tamamlandı |
| [4](docs/04-HTTP-API.md) | HTTP API katmanı | ✅ Tamamlandı |
| [5](docs/05-AGENTPRISM-UI.md) | AgentPrismUI | ✅ Tamamlandı |
| [6](docs/06-GOZLEMLENEBILIRLIK.md) | Gözlemlenebilirlik, tool onayı, MCP, çok kiracılılık | ✅ Tamamlandı |
| [7](docs/07-SAGLAMLASTIRMA-VE-YAYIN.md) | Sağlamlaştırma ve yayın | ⏸ Beklemede — yayın zamanı kullanıcı kararı (K-068) |
| [8](docs/08-SAGLAYICI-GENISLEMESI.md) | Sağlayıcı genişlemesi ve sağlık denetimi (OpenAI uyumlu herhangi bir uç, devre kesici) | ✅ Tamamlandı |
| [9](docs/09-YONETISIM-VE-DENETIM-IZI.md) | Yönetişim: rol tabanlı yetkilendirme (Reader/Operator/Admin) ve denetim izi | ✅ Tamamlandı |
| [10](docs/10-AGENT-SKILLERI.md) | Agent skill'leri: markdown talimatlar, kaynaklar ve MAF onayı | ✅ Tamamlandı |
| [11](docs/11-SKILL-SCRIPT-CALISTIRMA.md) | Skill script çalıştırma: sandbox, izin kaydı ve denetim izi | ✅ Tamamlandı |
| [—](docs/IKINCI-FAZ-YOL-HARITASI.md) | İkinci faz yol haritası (Faz 11–30) | 📋 Planlandı — sıradaki Faz 12 |
| [—](docs/BEYIN-FIRTINASI.md) | İkinci faz hammaddesi — 29 aday yetenek | Tamamı planlandı |

> Faz 6, planındaki Workflows kalemini **yapmadı**; ertelendi ve gerekçesi
> [K-054](docs/KARARLAR.md) ile kayıt altına alındı. Ayrıntı:
> [06-GOZLEMLENEBILIRLIK.md](docs/06-GOZLEMLENEBILIRLIK.md) sapma S1.

### ⚠️ Skill script çalıştırma ve izolasyon sınırı

Faz 11, skill script'lerinin **sunucuda** çalıştırılmasına izin verir. Özellik
varsayılan olarak **kapalıdır** ve yalnız kodda açılır:

```csharp
builder.Services.AddAgentPrism()
    .UseSkillScripts(options =>
    {
        options.PlatformIsolationAcknowledged = true;
        options.Interpreters["py"] = "python3";
    });
```

**AgentPrism işletim sistemi seviyesinde yalıtım sağlamaz.** Script, AgentPrism
sürecinin kullanıcı hakları ve ağ erişimiyle çalışır.

| Sağlanan | Sağlanmayan |
|----------|-------------|
| Yorumlayıcı beyaz listesi | Dosya sistemi hapsi |
| Ortam değişkeni beyaz listesi | Ağ kısıtı |
| Zaman aşımı + süreç ağacı öldürme | Bellek ve CPU kotası |
| Çıktı kırpma, eşzamanlılık sınırı | Hak düşürme |
| Kiracı bazlı izin kaydı + denetim izi | |

Sağlanmayanlar barındırma ortamında kurulmalıdır: **container** içinde,
**ayrıcalıksız bir kullanıcı** ile ve **kısıtlı ağ** ile çalıştırın.
`PlatformIsolationAcknowledged` bayrağı bu tabloyu görmeden özelliğin
açılmasını engeller; eksikse uygulama **açılışta** hata verir.

### Sürüm politikası

`Microsoft.Agents.AI.Hosting` (preview) ve `Microsoft.Agents.AI.Hosting.OpenAI` (alpha) hâlâ ön sürümdür. AgentPrism, bu iki paket GA olana kadar `1.0.0-preview.N` olarak yayınlanır.

Ön sürüm bağımlılığı yalnızca `AgentPrism.AspNetCore` içindedir. Diğer paketler yalnız GA paketlere bağlıdır.

---

## Geliştirme

```bash
dotnet build  AgentPrism.slnx -c Release              # 0 uyarı bekleniyor
dotnet test   AgentPrism.slnx -c Release --no-build   # 310 test
dotnet pack   AgentPrism.slnx -c Release --no-build
dotnet format AgentPrism.slnx --verify-no-changes
```

`TreatWarningsAsErrors` açıktır — uyarı yoktur, hata vardır.

Gereksinimler: .NET SDK 10.0.100+, **Node.js 20.19+** (arayüz derlemesi), **Docker**
(entegrasyon testleri Testcontainers ile gerçek PostgreSQL kaldırır). Arayüz E2E
testleri Chromium'u ilk çalıştırmada kendisi indirir.

`dotnet build` arayüzü de derler: `npm ci` → tip denetimi → 42 Vitest testi →
Vite → Brotli sıkıştırma → bundle bütçesi kapısı. Adımlar artımsaldır; kaynak
değişmediyse atlanır. Hızlı bir iç döngü için `-p:AgentPrismFrontendEnabled=false`.

Örnek uygulamayı çalıştırma:

```bash
cd samples/AgentPrism.Api
dotnet run           # http://localhost:5080/agentprism
```

Yalnız arayüz üzerinde çalışıyorsanız Vite geliştirme sunucusu daha hızlıdır:

```bash
cd src/AgentPrism.UI/frontend
npm run dev          # http://localhost:5173 — /agentprism/* istekleri 5080'e vekillenir
```

---

## Dokümantasyon

| Kaynak | İçerik |
|--------|--------|
| [docs/MIMARI.md](docs/MIMARI.md) | Mimari — katmanlar, veri modeli, MAF genişleme noktaları, güvenlik modeli |
| [docs/KARARLAR.md](docs/KARARLAR.md) | Karar defteri — reddedilen yaklaşımlar ve kalıcı tercihler, gerekçeleriyle |
| [docs/](docs/) | Faz dokümanları (00–07) — kapsam, tasarım kararları, DoD |
| [.agents/skills/](.agents/skills/) | Tekrarlanan iş akışları — faz tamamlama protokolü, MAF API keşfi |
| [AGENTS.md](AGENTS.md) | Merkezi agent talimatları — proje kuralları, faz akışı, doğrulama kapıları (`CLAUDE.md` buna symlink) |
| [MEMORY.md](MEMORY.md) | Agent'ların oturumlar arası biriktirdiği kurumsal bilgi notları |

---

## Lisans

MIT
