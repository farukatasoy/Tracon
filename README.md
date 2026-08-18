# AgentPrism

**Microsoft Agent Framework için üretim seviyesi agent kontrol düzlemi.**

AgentPrism, [Microsoft Agent Framework](https://learn.microsoft.com/en-us/agent-framework/overview/) üzerine kurulu bir .NET paket ailesidir. Geliştirici AI harness'ini kurar, `/agentprism` üzerinden yönetir.

> **Durum:** Faz 61 tamamlandı — AgentPrism **işletilebilirdir**, kaynak kodu **İngilizce**dir, [ürün dokümantasyonu yayınlanmıştır](https://farukatasoy.github.io/AgentPrism) ve **public API kapısı** (`EnablePublicApiTracking`) yayın kararından bağımsız olarak açıktır — kayıtsız bir yüzey değişikliği derlemeyi kırar. `dotnet new agentprism-api` ile başlatılır, `AgentPrism.Testing` ile model çağırmadan test edilir. Çalıştırmalar span/metrik/maliyetle kaydedilir, kiracı yalıtılır, agent'lar MCP/A2A ile dışa açılır, `pgvector` ile anlamsal arama yapılır, API anahtarıyla erişim daralır, A/B deneyleri kanarya kuralıyla otomatik geri alınır. Bir tool'un gövdesi tarayıcıda çalışabilir (`AddClientTool`) ve gömülebilir bir sohbet bileşeni üçüncü taraf sayfalara CORS ile açılabilir. Faz 8–61 bitti; kod dili birleştirme, doküman düzeni, ürün dokümantasyon sitesi, public API kapısı ve istemci tarafı tool'lar tamamlandı.

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

Dashboard, Agents, Skills, Playground, Sessions, Runs, Workflows, Jobs, Evals,
Experiments, Approvals, Tools, Models, MCP, Audit, Diagnostics, Settings —
**27 ekran, 33 route**; liste ve düzenleyiciler dâhil.

React 19 + TypeScript ile yazılır, Vite ile derlenir ve assembly'ye **Brotli
sıkıştırılmış gömülür**. Tüketici projede hiçbir JavaScript bağımlılığı oluşmaz;
`node_modules` klasörü gerekmez. JavaScript bütçesi **165,8 KB gzip** (kapı: 250 KB).

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

# Konusma zincirleme: previous_response_id VEYA conversation — ikisi de calisir.
r2 = client.responses.create(model="support", input="Peki ya ORD-9?", previous_response_id=r.id)
```

Tool döngüsü sunucuda tamamlanır; her çalıştırma olay olay kaydedilir ve SSE ile geri
oynatılabilir.

**Kodda agent ve tool tanımı:**

```csharp
builder.AddAgentPrism()
       .AddToolsFrom(typeof(OrderTools))      // [AgentPrismTool] ile işaretli metotlar
       .UseOpenAI(apiKey)
       .AddAgent(new AgentDefinition
       {
           Name = "support",
           Instructions = "Sen bir destek asistanısın.",
           Model = new ModelBinding { Provider = OpenAIProviderNames.ChatCompletions, Model = "gpt-5.4-mini" },
           ToolNames = ["get_order_status", "cancel_order"],
       })
       .UsePostgreSql(connectionString)       // isteğe bağlı
       .UseMcp()                              // uzak MCP tool'ları, isteğe bağlı
       .UseUI();                              // gömülü arayüz

// Kalıcı oturumla çalıştır
var agent = await catalog.ResolveAsync("support");
var session = await sessions.GetOrCreateSessionAsync(agent!, "musteri-42");
var response = await agent!.RunAsync("Siparişim nerede?", session);
await sessions.SaveSessionAsync(agent, session);
```

```csharp
internal static class OrderTools
{
    [AgentPrismTool("get_order_status", "Bir siparişin kargo durumunu döndürür.")]
    public static string GetOrderStatus(string orderId) => ...;

    public static string Helper() => "...";   // işaretsiz — tool olmaz
}
```

`UseOpenAI()` **iki** sağlayıcı kaydeder: `openai` (Chat Completions) ve
`openai-responses`; seçim `ModelBinding.Provider` ile yapılır.
`UseOpenAICompatible(ad, ...)` aynı paketle **herhangi bir** OpenAI uyumlu uca
bağlanır — OpenRouter, Groq, vLLM, yerel Ollama/LM Studio (yerel sunucular
`ApiKey` istemez). Her sağlayıcı `GET {endpoint}/models` ile ücretsiz denetlenir
ve ardışık hata veren bir sağlayıcıyı devre kesici geçici olarak durdurur.

`UsePostgreSql()` çağrılmazsa depolama bellek içine düşer ve hiçbir şey kırılmaz.
Şema, gömülü SQL migration'ları ile ayrı bir `agentprism` şemasında oluşur;
uygulamanızın `public` şemasına dokunulmaz.

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
| `AgentPrism.SqlServer` | ✅ SQL Server 2019+ / Azure SQL kalıcılığı — aynı şema, kendi migration seti. **Meta pakete dâhil değil**. Sözleşme testleri gerçek `mssql/server` ile doğrulandı (K-386) |
| `AgentPrism.Sqlite` | ✅ SQLite kalıcılığı — tek dosya, tablo öneki, kendi migration seti. **Meta pakete dâhil değil**. Tek yazıcılıdır; çok örnekli dağıtımda kullanılmaz |
| `AgentPrism.OpenAI` | ✅ OpenAI sağlayıcı adaptörü — Chat Completions + Responses, tool çağrısı, OpenTelemetry |
| `AgentPrism.Anthropic` | ✅ Anthropic (Claude) sağlayıcı adaptörü — resmî SDK, prompt caching, genişletilmiş düşünme. **Meta pakete dâhil değil** |
| `AgentPrism.Google` | ✅ Google Gemini sağlayıcı adaptörü — resmî SDK, güvenlik eşikleri, düşünme bütçesi. **Meta pakete dâhil değil**; geçişli `Google.Apis.Auth` zinciri gelir |
| `AgentPrism.Azure` | ✅ Azure OpenAI sağlayıcı adaptörü — deployment tabanlı model çözümü, API anahtarı veya Entra kimliği. **Meta pakete dâhil değil**; `Azure.Identity` **yoktur**, kimlik fabrikası tüketiciden gelir |
| `AgentPrism.Voice` | ✅ Ses tool'ları: `speak`, `transcribe`, `list_voices`. Ölçüm `tool_invocations`'a yazılır. **Sıfır NuGet bağımlılığı**; meta pakete dâhil değil. Gerçek zamanlı konuşma `Core`'dadır: `UseVoiceConversation()` |
| `AgentPrism.Mcp` | ✅ Uzak MCP sunucularından tool keşfi — yalnız HTTP, onay varsayılan |
| `AgentPrism.Workflows` | ✅ Workflow yürütme — beş desen, kontrol noktası, sürdürme, human-in-the-loop |
| `AgentPrism.AspNetCore` | ✅ HTTP katmanı — yönetim API'si, OpenAI uyumlu uçlar, çok kiracılılık |
| `AgentPrism.UI` | ✅ Gömülü React arayüzü — 27 ekran / 33 route, sıfır JavaScript bağımlılığı |
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

**Faz 0–63 bitti** (7 hariç — yayın zamanı kullanıcı kararı, K-068).

| Dalga | Fazlar | Konu | Durum |
|-------|--------|------|-------|
| 1 | 0–20 | Çekirdek, kalıcılık, HTTP, arayüz, workflows, eval, maliyet | ✅ Bitti (7 beklemede) |
| 2 | 21–30 | Kota, MCP, SQL Server, SQLite, saklama, sağlayıcılar, ses | ✅ Bitti |
| 3 | 31–52 | Puanlama, iptal, teşhis, şablon, guardrail, RAG, üreteç | ✅ Bitti |
| 4 | 53–56 | API anahtarı, öksüz çalıştırma, onay kutusu, kanarya | ✅ Bitti |
| 5 | 57–60 | Kod dili, doküman düzeni, ürün dokümantasyonu, public API kapısı | ✅ Bitti |
| 6 | 61 | İstemci tarafı tool'lar ve gömülebilir sohbet | ✅ Bitti |
| 7 | 62–66 | Model yedek zinciri, onay politikası, denetim zinciri ve veri hakları, BYOK ve egress, gelen tetikleyiciler | 🔄 62–63 bitti · 64–66 planlı |
| 8 | 67–72 | `pgvector` opt-in, çalıştırma kimliği ve token kırılımı, tool yetkilendirmesi ve timeout, olay hedefi, workflow kod düğümü, çok dilli talimat | 📋 Planlandı |
| 9 | 73 | Tüketici agent desteği: derleme anı tanıları, üretilen yetenek haritası, kapsam kapısı | 📋 Planlandı |

**Tam liste: [`docs/YOL-HARITASI.md`](docs/YOL-HARITASI.md)** — her fazı tek tek
listeler ve fazların kendi dokümanlarından **üretilir**, elle yazılmaz. Dalgaların
sıralama gerekçesi (kapandı): [`docs/arsiv/`](docs/arsiv/).
Seçilmemiş adaylar: [`docs/ADAYLAR.md`](docs/ADAYLAR.md).

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
sürecinin kullanıcı hakları ve ağ erişimiyle çalışır. AgentPrism yorumlayıcı ve
ortam değişkeni beyaz listesi, zaman aşımı + süreç ağacı öldürme, çıktı kırpma,
eşzamanlılık sınırı ve kiracı bazlı izin kaydı + denetim izi sağlar; **dosya
sistemi hapsi, ağ kısıtı, bellek/CPU kotası ve hak düşürme sağlamaz.** Bunlar
barındırma ortamında kurulmalıdır: **container** içinde, **ayrıcalıksız bir
kullanıcı** ile ve **kısıtlı ağ** ile çalıştırın.
`PlatformIsolationAcknowledged` bayrağı bu sınırı görmeden özelliğin açılmasını
engeller; eksikse uygulama **açılışta** hata verir. Ayrıntı:
[`docs/11-SKILL-SCRIPT-CALISTIRMA.md`](docs/11-SKILL-SCRIPT-CALISTIRMA.md).

### İçerik denetimi (guardrails)

Varsayılan **kapalıdır**: `AddAgentPrism()` hiç guard kaydetmez ve model boru
hattına halka eklenmez. Açmak açık bir tercihtir — yerleşik desen guard'ı için
`.AddPatternContentGuard(o => o.MaskedPii = PiiPatterns.CreditCard)`, kendi
kural kümeniz için `IContentGuard` + `.AddContentGuard<T>()`. Birden çok guard
sırayla çalışır ve **en sert karar kazanır**; engellenen içerik hiçbir yere
yazılmaz. Ayrıntı: [`docs/48-GUARDRAILS.md`](docs/48-GUARDRAILS.md).

### Sürüm politikası

`Microsoft.Agents.AI.Hosting` (preview) ve `Microsoft.Agents.AI.Hosting.OpenAI` (alpha) hâlâ ön sürümdür. AgentPrism, bu iki paket GA olana kadar `1.0.0-preview.N` olarak yayınlanır.

Ön sürüm bağımlılığı yalnızca `AgentPrism.AspNetCore` içindedir. Diğer paketler yalnız GA paketlere bağlıdır.

---

## Geliştirme

```bash
dotnet build  AgentPrism.slnx -c Release              # 0 uyarı bekleniyor
dotnet test   AgentPrism.slnx -c Release --no-build   # 3695 test, 16 proje
dotnet pack   AgentPrism.slnx -c Release --no-build
dotnet format AgentPrism.slnx --verify-no-changes
```

`TreatWarningsAsErrors` açıktır — uyarı yoktur, hata vardır.

Gereksinimler: .NET SDK 10.0.100+, **Node.js 20.19+** (arayüz derlemesi), **Docker**
(entegrasyon testleri Testcontainers ile gerçek veritabanı kaldırır). Arayüz E2E
testleri Chromium'u ilk çalıştırmada kendisi indirir.

`dotnet build` arayüzü de derler: `npm ci` → tip denetimi → Vitest → Vite →
Brotli sıkıştırma → bundle bütçesi kapısı. Adımlar artımsaldır; kaynak
değişmediyse atlanır. Hızlı bir iç döngü için `-p:AgentPrismFrontendEnabled=false`.

```bash
cd samples/AgentPrism.Api && dotnet run     # http://localhost:5080/agentprism

# Yalnız arayüz: Vite geliştirme sunucusu daha hızlıdır (5173 → 5080'e vekil)
cd src/AgentPrism.UI/frontend && npm run dev
```

---

## Dokümantasyon

**Kullanıcıya dönük ürün dokümantasyonu ayrı bir sitededir ve İngilizce'dir:**
<https://farukatasoy.github.io/AgentPrism> — kurulum, ilk agent, kavramlar, arayüz
turu, HTTP API (143 operasyon) ve 588 public tipin API referansı. Kaynağı
[`docs-site/`](docs-site/); `main`'e her push'ta yayınlanır.

Aşağıdaki tablo **geliştirme dokümantasyonudur** (Türkçe, repo içi). İkisi
karıştırılmaz: `docs/` geliştirme günlüğüdür, `docs-site/` ürün dokümantasyonudur.

| Kaynak | İçerik |
|--------|--------|
| [docs/MIMARI.md](docs/MIMARI.md) | Mimari — katmanlar, veri modeli, çalıştırma yolu, güvenlik modeli |
| [docs/MAF-GENISLEME-NOKTALARI.md](docs/MAF-GENISLEME-NOKTALARI.md) | Kullandığımız ve bilerek kullanmadığımız MAF genişleme noktaları |
| [docs/KARARLAR.md](docs/KARARLAR.md) · [indeks](docs/KARARLAR-INDEKS.md) · [reddedilen](docs/arsiv/KARARLAR-INDEKS-REDDEDILEN.md) | Karar defteri — kalıcı tercihler ve reddedilen yaklaşımlar, gerekçeleriyle |
| [docs/](docs/) `NN-*.md` · [YOL-HARITASI.md](docs/YOL-HARITASI.md) · [ADAYLAR.md](docs/ADAYLAR.md) | Faz dokümanları (kapsam, tasarım, DoD) · faz durumu (üretilen) · seçilmemiş adaylar |
| [docs/manuel-test/](docs/manuel-test/) | Elle koşulan kabul testi spesifikasyonu; koşumu `manuel-test-kosumu` skill'i yürütür |
| [docs/hafiza/](docs/hafiza/) · [docs/arsiv/](docs/arsiv/) | Alan bazlı tuzaklar · kapanmış kayıt (faz anlatısı, koşum turları) |
| [AGENTS.md](AGENTS.md) · [MEMORY.md](MEMORY.md) · [.agents/skills/](.agents/skills/) | Agent talimatları, hafıza yönlendirmesi, iş akışı skill'leri (`CLAUDE.md` → `AGENTS.md` symlink) |
| [docs-site/](docs-site/) · [docfx/](docfx/) | **Ürün sitesi** (İngilizce, Astro Starlight) ve API referansı üreteci. Ayrı yayın hattı; `dotnet build`'e bağlanmaz. Node 22.12+ gerekir |
| [scripts/dokuman-bakim.py](scripts/dokuman-bakim.py) | Karar indeksini üretir, doküman bütçelerini denetler |

---

## Lisans

MIT — ticari katman planı ve "bugün MIT olan hiçbir şey ücretli olmayacak"
taahhüdü için [COMMERCIAL.md](COMMERCIAL.md).
