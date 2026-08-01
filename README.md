# AgentPrism

**Microsoft Agent Framework için üretim seviyesi agent kontrol düzlemi.**

AgentPrism, [Microsoft Agent Framework](https://learn.microsoft.com/en-us/agent-framework/overview/) üzerine kurulu bir .NET paket ailesidir. Projesine ekleyen geliştirici kendi AI harness'ini kolay ama esnek şekilde kurar ve `/agentprism` arayüzünden yönetir.

> **Durum:** Faz 0 tamamlandı — build ve paketleme altyapısı hazır. Ürün kodu Faz 1'den itibaren gelir. Yol haritası aşağıda.

```csharp
builder.AddAgentPrism()
       .UsePostgreSql(connectionString)
       .UseOpenAI(apiKey)
       .AddTool(GetOrderStatus);

builder.AddAIAgent("support", "Sen bir destek asistanısın.");

app.MapAgentPrism("/agentprism");
```

İki satır. Çalışan bir agent, kalıcı oturumlar ve tarayıcıda bir kontrol düzlemi.

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
| `AgentPrism.Abstractions` | Sözleşmeler; kendi implementasyonunuzu yazacaksanız yeterli |
| `AgentPrism.Core` | Çalışma zamanı, katalog, tanım derleyicisi, tool defteri. **Veritabanı gerektirmez.** |
| `AgentPrism.PostgreSql` | Kalıcılık — gömülü SQL migration'ları ile |
| `AgentPrism.OpenAI` | OpenAI sağlayıcı adaptörü |
| `AgentPrism.AspNetCore` | HTTP katmanı — yönetim API'si + OpenAI uyumlu uçlar |
| `AgentPrism.UI` | Gömülü React arayüzü |

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
dotnet user-secrets set "AgentPrism:ConnectionString"        "Host=...;Port=5432;Database=AgentPrism;Username=...;Password=..."
dotnet user-secrets set "AgentPrism:Providers:OpenAI:ApiKey" "sk-..."
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
| [1](docs/01-CEKIRDEK-SOYUTLAMALAR.md) | Çekirdek soyutlamalar ve runtime | Planlandı |
| [2](docs/02-POSTGRESQL-KALICILIK.md) | PostgreSQL kalıcılık katmanı | Planlandı |
| [3](docs/03-SAGLAYICI-VE-DERLEYICI.md) | OpenAI sağlayıcısı ve agent derleyici | Planlandı |
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
dotnet build  AgentPrism.slnx -c Release      # 0 uyarı bekleniyor
dotnet test   AgentPrism.slnx -c Release
dotnet pack   AgentPrism.slnx -c Release --no-build
dotnet format AgentPrism.slnx --verify-no-changes
```

Gereksinimler: .NET SDK 10.0.100+, Node.js 20.19+ (Faz 5'ten itibaren arayüz build'i için), Docker (entegrasyon testleri için).

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
| [docs/](docs/) | Faz dokümanları (00–07) |
| [AGENTS.md](AGENTS.md) | Merkezi agent talimatları — proje kuralları, mimari konvansiyonlar (`CLAUDE.md` buna symlink) |
| [MEMORY.md](MEMORY.md) | Agent'ların oturumlar arası biriktirdiği kurumsal bilgi notları |

---

## Lisans

MIT
