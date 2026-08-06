# AgentPrism.Starter

`dotnet new agentprism-api` ile üretildi. Çalışan bir [AgentPrism](https://github.com/farukatasoy/AgentPrism) kontrol düzlemidir.

## 1. Sırları ayarlayın

Bağlantı dizesi ve API anahtarı bu depoya **hiç girmez**. `appsettings.json` yalnız boş placeholder taşır. `dotnet user-secrets` kullanın:

```bash
dotnet user-secrets init
```

**Kalıcılık** — üretim sırasında seçtiğiniz `--persistence` değerine göre yalnız *birini* ayarlayın:

```bash
dotnet user-secrets set "AgentPrism:PostgreSql:ConnectionString" "Host=...;Port=5432;Database=AgentPrism;Username=...;Password=..."
dotnet user-secrets set "AgentPrism:SqlServer:ConnectionString"  "Server=...,1433;Database=AgentPrism;User Id=...;Password=...;TrustServerCertificate=true"
dotnet user-secrets set "AgentPrism:Sqlite:ConnectionString"     "Data Source=agentprism.db"
```

Hiçbiri ayarlanmazsa depolama bellek içine düşer — hiçbir şey kırılmaz, veri süreçle birlikte biter.

**Model sağlayıcısı** — seçtiğiniz `--provider` değerine göre yalnız *birini* ayarlayın:

```bash
dotnet user-secrets set "AgentPrism:Providers:OpenAI:ApiKey"        "sk-..."
dotnet user-secrets set "AgentPrism:Providers:Anthropic:ApiKey"     "sk-ant-..."
dotnet user-secrets set "AgentPrism:Providers:Google:ApiKey"        "AIza..."
dotnet user-secrets set "AgentPrism:Providers:AzureOpenAI:Endpoint" "https://<kaynak>.openai.azure.com/"
dotnet user-secrets set "AgentPrism:Providers:AzureOpenAI:ApiKey"   "..."
```

API anahtarı ayarlanmazsa uygulama yine ayağa kalkar; yalnızca modeli gerçekten çağıran çalıştırmalar hata döner.

## 2. Model adını seçin

AgentPrism **yerleşik model listesi taşımaz** — model adları ve fiyatları bir NuGet paketinin yayın sıklığından hızlı değişir. [`Program.cs`](Program.cs) içindeki `MODEL_ADINI_BURAYA_YAZIN` placeholder'ını sağlayıcının bugünkü belgesindeki gerçek model adıyla değiştirin (örn. OpenAI için `gpt-5.4-mini`).

## 3. Çalıştırın

```bash
dotnet run
```

`http://localhost:5081/agentprism` adresinde çalışan bir kontrol düzlemi açılır. Katalog, deneme çalıştırması ve OpenAI uyumlu uçlar için:

```bash
curl http://localhost:5081/agentprism/api/agents
```

## Tool ekleyin

[`Tools/OrderTools.cs`](Tools/OrderTools.cs) içinde bir örnek tool var. Yeni bir tool eklemek için `[AgentPrismTool]` ile işaretli **statik** bir metot yazın ve `Program.cs`'te `AddToolsFrom(typeof(...))` ile kaydedin. Tool'lar yalnızca kodda tanımlanır — bu bir güvenlik sınırıdır; arayüz yalnızca kayıtlı tool'lardan seçim yaptırır.

## Daha fazla

- [AgentPrism deposu](https://github.com/farukatasoy/AgentPrism)
- [`docs/MIMARI.md`](https://github.com/farukatasoy/AgentPrism/blob/main/docs/MIMARI.md)
