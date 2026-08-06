# AgentPrism

**Microsoft Agent Framework için üretim seviyesi agent kontrol düzlemi.**

AgentPrism, [Microsoft Agent Framework](https://learn.microsoft.com/en-us/agent-framework/overview/) üzerine kurulu bir .NET paket ailesidir. Geliştirici kendi AI harness'ini kurar ve `/agentprism` arayüzünden yönetir.

> **Durum:** Faz 39 tamamlandı — AgentPrism **işletilebilir bir kontrol düzlemidir**, `dotnet new agentprism-api` ile başlatılabilir; `AgentPrism.Testing` ile gerçek model çağırmadan test edilebilir. Çalıştırmalar span/metrik/maliyetle kaydedilir. Veritabanı/model isteğe bağlı. Faz 8-39 bitti; [kalan](docs/UCUNCU-FAZ-YOL-HARITASI.md) planlı.

```csharp
builder.AddAgentPrism()
       .UsePostgreSql(connectionString)
       .UseOpenAI(apiKey)
       .AddTool(GetOrderStatus)
       .UseUI();

app.MapAgentPrism("/agentprism");
```

İki satır: çalışan bir agent, kalıcı oturumlar, `http://localhost:5080/agentprism`
adresinde bir kontrol düzlemi.

### Arayüz

Agents, Playground, Sessions, Runs, Workflows, Jobs, Evals, Experiments, Tools,
Models, MCP, Audit, Diagnostics, Settings — her faz için ayrı bir ekran.

React 19 + TypeScript ile yazılır, Vite ile derlenir ve assembly'ye **Brotli
sıkıştırılmış gömülür**. Tüketici projede hiçbir JavaScript bağımlılığı oluşmaz;
`node_modules` klasörü gerekmez. JavaScript bütçesi **~155 KB gzip** (kapı: 250 KB).

Arayüz herhangi bir prefix altında çalışır (`/agentprism`, `/panel`, …) ve prefix'i
çalışma anında öğrenir. Açık ve koyu tema; varsayılan işletim sistemi tercihidir.

**HTTP yüzeyinden bir kesit:**

```csharp
// Tek giris noktasi; erisim varsayilan olarak loopback ile sinirli.
app.MapAgentPrism("/agentprism", options => options.RequireAuthorization("AgentPrismAdmin"));
```

```
GET    /agentprism/api/meta                    surum · kimlik yontemi · aktif depolar  [kimliksiz]
GET    /agentprism/api/agents                  katalog (kod + veritabani)
POST   /agentprism/api/agents                  yeni tanim        · PUT · DELETE · /versions · /rollback
POST   /agentprism/api/agents/{name}/run       SSE akisli deneme calistirmasi
GET    /agentprism/api/sessions[/{id}]         oturumlar ve sohbet gecmisi · DELETE
GET    /agentprism/api/runs[/{id}]             calistirma kaydi
GET    /agentprism/api/runs/{id}/events        SSE; canli veya replay, Last-Event-ID ile devam
GET    /agentprism/api/tools · /api/models · /api/stats · /api/diagnostics
POST   /agentprism/api/attachments             ek yukle · GET/DELETE

POST   /agentprism/v1/responses                OpenAI Responses API uyumlu
POST   /agentprism/v1/chat/completions         OpenAI Chat Completions API uyumlu
POST   /agentprism/v1/conversations            konusma ac · GET/DELETE · /items
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
| `AgentPrism.SqlServer` | ✅ SQL Server 2019+ / Azure SQL kalıcılığı — aynı şema, kendi migration seti. **Meta pakete dâhil değil**. Sözleşme testleri `azure-sql-edge` ile doğrulandı; `mssql/server` koşmadı |
| `AgentPrism.Sqlite` | ✅ SQLite kalıcılığı — tek dosya, tablo öneki, kendi migration seti. **Meta pakete dâhil değil**. Tek yazıcılıdır; çok örnekli dağıtımda kullanılmaz |
| `AgentPrism.OpenAI` | ✅ OpenAI sağlayıcı adaptörü — Chat Completions + Responses, tool çağrısı, OpenTelemetry |
| `AgentPrism.Anthropic` | ✅ Anthropic (Claude) sağlayıcı adaptörü — resmî SDK, prompt caching, genişletilmiş düşünme. **Meta pakete dâhil değil** |
| `AgentPrism.Google` | ✅ Google Gemini sağlayıcı adaptörü — resmî SDK, güvenlik eşikleri, düşünme bütçesi. **Meta pakete dâhil değil**; geçişli `Google.Apis.Auth` zinciri gelir |
| `AgentPrism.Azure` | ✅ Azure OpenAI sağlayıcı adaptörü — deployment tabanlı model çözümü, API anahtarı veya Entra kimliği. **Meta pakete dâhil değil**; `Azure.Identity` **yoktur**, kimlik fabrikası tüketiciden gelir |
| `AgentPrism.Voice` | ✅ Ses tool'ları: `speak`, `transcribe`, `list_voices`. Ölçüm `tool_invocations`'a yazılır. **Sıfır NuGet bağımlılığı**; meta pakete dâhil değil. Gerçek zamanlı konuşma `Core`'dadır: `UseVoiceConversation()` |
| `AgentPrism.Mcp` | ✅ Uzak MCP sunucularından tool keşfi — yalnız HTTP, onay varsayılan |
| `AgentPrism.Workflows` | ✅ Workflow yürütme — beş desen, kontrol noktası, sürdürme, human-in-the-loop |
| `AgentPrism.AspNetCore` | ✅ HTTP katmanı — yönetim API'si, OpenAI uyumlu uçlar, çok kiracılılık |
| `AgentPrism.UI` | ✅ Gömülü React arayüzü — sekiz ekran, sıfır JavaScript bağımlılığı |
| `AgentPrism.Templates` | ✅ `dotnet new agentprism-api` şablonu — meta pakete dâhil değil |
| `AgentPrism.Testing` | ✅ `FakeModelProvider`/`AgentPrismTestHost`/`RunAssertions`; çerçeveden bağımsız, meta pakete dâhil değil |

**Hedef framework:** `net8.0`/`net9.0`/`net10.0` · **Lisans:** MIT

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

# veya SQL Server, veya SQLite (üçü AYNI ANDA verilmez; verilirse son kayıt kazanır ve uyarı loglanır)
dotnet user-secrets set "AgentPrism:SqlServer:ConnectionString" "Server=...,1433;Database=AgentPrism;User Id=...;Password=...;TrustServerCertificate=true"
dotnet user-secrets set "AgentPrism:Sqlite:ConnectionString"    "Data Source=agentprism.db"
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
| [12](docs/12-AGENT-CAGRI-GRAFIGI.md) | Agent'ın agent'ı çağırması: çağrı grafiği, çalıştırma ağacı ve paylaşılan bütçe | ✅ Tamamlandı |
| [13](docs/13-BAGLAM-SIKISTIRMA-VE-BELLEK.md) | Bağlam sıkıştırma (5 strateji + pipeline) ve bellek sağlayıcıları (dosya, todo, metin araması) | ✅ Tamamlandı |
| [14](docs/14-COK-MODLULUK.md) | Çok modluluk: görsel/dosya eki, kalıcı agent dosya belleği | ✅ Tamamlandı |
| [15](docs/15-WORKFLOWS-YURUTME.md) | Workflows: beş desenle yürütme, kontrol noktası ve sürdürme | ✅ Tamamlandı |
| [16](docs/16-WORKFLOWS-ARAYUZ.md) | Workflows: graf görselleştirme, human-in-the-loop, Magentic plan onayı | ✅ Tamamlandı |
| [17](docs/17-TOPLU-VE-ZAMANLANMIS-CALISTIRMA.md) | Toplu ve zamanlanmış çalıştırma: PostgreSQL iş kuyruğu (`SKIP LOCKED`), cron zamanlama, Jobs ekranı | ✅ Tamamlandı |
| [18](docs/18-DEGERLENDIRME.md) | Değerlendirme (eval): takım/vaka/koşu, Faz 17'nin iş kuyruğu üzerinde, Evals ekranı | ✅ Tamamlandı |
| [19](docs/19-SURUM-KARSILASTIRMA-VE-AB.md) | Sürüm karşılaştırma (diff) ve A/B deneyleri: oturum bazlı deterministik trafik bölme, Experiments ekranı | ✅ Tamamlandı |
| [20](docs/20-MALIYET-VE-GOSTERGE-PANELI.md) | Maliyet raporlaması ve gösterge paneli: fiyat kataloğu/yapılandırması, Dashboard giriş ekranı | ✅ Tamamlandı |
| [—](docs/IKINCI-FAZ-YOL-HARITASI.md) | İkinci faz yol haritası (Faz 21–30) | ✅ Tamamı bitti |
| [31](docs/31-GERI-BILDIRIM-VE-PUANLAMA.md) | Geri bildirim ve puanlama (çalıştırma/mesaj puanı) | ✅ Tamamlandı |
| [32](docs/32-CALISTIRMA-IPTALI.md) | Çalıştırma iptali (`POST /api/runs/{id}/cancel`) | ✅ Tamamlandı |
| [33](docs/33-SAGLIK-DENETIMI-VE-TESHIS.md) | Sağlık denetimi (`/health`) ve yapılandırma teşhisi (`/api/diagnostics`) | ✅ Tamamlandı |
| [34](docs/34-TANIM-DOGRULAMA-UCU.md) | Tanım doğrulama ucu | ✅ Tamamlandı |
| [35](docs/35-MALIYET-VE-KOTA-METRIKLERI.md) | Maliyet ve kota OTel metrikleri | ✅ Tamamlandı |
| [—](docs/UCUNCU-FAZ-YOL-HARITASI.md) | Üçüncü faz yol haritası (Faz 36–52) | Faz 36–37 bitti; kalanı 📋 [adaylarda](docs/UCUNCU-FAZ-ADAYLARI.md) |
| [—](docs/BEYIN-FIRTINASI.md) | İkinci faz hammaddesi — 29 aday yetenek | Tamamı planlandı |


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
| [docs/MIMARI.md](docs/MIMARI.md) | Mimari — katmanlar, veri modeli, çalıştırma yolu, güvenlik modeli |
| [docs/MAF-GENISLEME-NOKTALARI.md](docs/MAF-GENISLEME-NOKTALARI.md) | Kullandığımız ve bilerek kullanmadığımız MAF genişleme noktaları |
| [docs/KARARLAR-INDEKS.md](docs/KARARLAR-INDEKS.md) · [-REDDEDILEN](docs/KARARLAR-INDEKS-REDDEDILEN.md) | Karar/red indeksleri (üretilen) |
| [docs/KARARLAR.md](docs/KARARLAR.md) | Karar defteri — reddedilen yaklaşımlar ve kalıcı tercihler, gerekçeleriyle |
| [docs/](docs/) | Faz dokümanları (00–32) — kapsam, tasarım kararları, DoD |
| [docs/hafiza/](docs/hafiza/) | Alan bazlı kurumsal bilgi — codepath'ler, desenler, tuzaklar |
| [docs/arsiv/](docs/arsiv/) | Faz anlatısı ve paket×faz birikimi (tarihsel kayıt) |
| [.agents/skills/](.agents/skills/) | Tekrarlanan iş akışları — faz başlangıç/tamamlama protokolü, MAF API keşfi |
| [AGENTS.md](AGENTS.md) | Merkezi agent talimatları — okuma protokolü, proje kuralları, doğrulama kapıları (`CLAUDE.md` buna symlink) |
| [MEMORY.md](MEMORY.md) | Hafıza yönlendirmesi + her oturumda geçerli tuzaklar |
| [scripts/dokuman-bakim.py](scripts/dokuman-bakim.py) | Karar indeksini üretir, doküman bütçelerini denetler |

---

## Lisans

MIT
