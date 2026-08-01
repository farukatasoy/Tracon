# AgentPrism

**Microsoft Agent Framework için üretim seviyesi agent kontrol düzlemi.**

AgentPrism, [Microsoft Agent Framework](https://learn.microsoft.com/en-us/agent-framework/overview/) üzerine kurulu bir .NET paket ailesidir. Projesine ekleyen geliştirici kendi AI harness'ini kolay ama esnek şekilde kurar ve `/agentprism` arayüzünden yönetir.

> **Durum:** Faz 2 tamamlandı — çekirdek runtime ve PostgreSQL kalıcılığı çalışıyor. Agent tanımlanır, çalıştırılır, oturum ve sohbet geçmişi yeniden başlatmayı atlatır. Veritabanı hâlâ **zorunlu değildir**; yapılandırılmazsa depolama bellek içine düşer. Sağlayıcı Faz 3'te, HTTP katmanı Faz 4'te, arayüz Faz 5'te gelir.

**Hedef** (Faz 5 sonunda):

```csharp
builder.AddAgentPrism()
       .UsePostgreSql(connectionString)
       .UseOpenAI(apiKey)
       .AddTool(GetOrderStatus);

app.MapAgentPrism("/agentprism");
```

İki satır. Çalışan bir agent, kalıcı oturumlar ve tarayıcıda bir kontrol düzlemi.

**Bugün çalışan** (Faz 2):

```csharp
builder.AddAgentPrism()
       .AddTool(OrderTools.GetOrderStatus, name: "get_order_status", description: "Kargo durumunu döndürür.")
       .AddModelProvider(new MyModelProvider())
       .AddAgent(new AgentDefinition
       {
           Name = "support",
           Instructions = "Sen bir destek asistanısın.",
           Model = new ModelBinding { Provider = "openai", Model = "gpt-5.4-mini" },
           ToolNames = ["get_order_status"],
       })
       .UsePostgreSql(connectionString);      // ← Faz 2; isteğe bağlı

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
| `AgentPrism.OpenAI` | ⬜ OpenAI sağlayıcı adaptörü (Faz 3) |
| `AgentPrism.AspNetCore` | ⬜ HTTP katmanı — yönetim API'si + OpenAI uyumlu uçlar (Faz 4) |
| `AgentPrism.UI` | ⬜ Gömülü React arayüzü (Faz 5) |

**Hedef framework:** `net8.0`, `net9.0`, `net10.0` · **Lisans:** MIT

---

## Kurulum

```bash
dotnet add package AgentPrism --prerelease
```

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
| [3](docs/03-SAGLAYICI-VE-DERLEYICI.md) | OpenAI sağlayıcısı ve agent derleyici | 🔜 Sıradaki |
| [4](docs/04-HTTP-API.md) | HTTP API katmanı | Planlandı |
| [5](docs/05-AGENTPRISM-UI.md) | AgentPrismUI | Planlandı |
| [6](docs/06-GOZLEMLENEBILIRLIK.md) | Gözlemlenebilirlik, workflows, çok kiracılılık | Planlandı |
| [7](docs/07-SAGLAMLASTIRMA-VE-YAYIN.md) | Sağlamlaştırma ve yayın | Planlandı |

### Sürüm politikası

`Microsoft.Agents.AI.Hosting` (preview) ve `Microsoft.Agents.AI.Hosting.OpenAI` (alpha) hâlâ ön sürümdür. AgentPrism, bu iki paket GA olana kadar `1.0.0-preview.N` olarak yayınlanır.

Ön sürüm bağımlılığı yalnızca `AgentPrism.AspNetCore` içindedir. Diğer paketler yalnız GA paketlere bağlıdır.

---

## Geliştirme

```bash
dotnet build  AgentPrism.slnx -c Release              # 0 uyarı bekleniyor
dotnet test   AgentPrism.slnx -c Release --no-build   # 130 test (42 birim + 88 entegrasyon)
dotnet pack   AgentPrism.slnx -c Release --no-build
dotnet format AgentPrism.slnx --verify-no-changes
```

`TreatWarningsAsErrors` açıktır — uyarı yoktur, hata vardır.

Gereksinimler: .NET SDK 10.0.100+, **Docker** (entegrasyon testleri Testcontainers ile gerçek PostgreSQL kaldırır), Node.js 20.19+ (Faz 5'ten itibaren arayüz build'i için).

Örnek uygulamayı çalıştırma:

```bash
cd samples/AgentPrism.Api
dotnet run           # http://localhost:5080
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
