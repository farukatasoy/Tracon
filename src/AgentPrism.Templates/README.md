# AgentPrism.Templates

`dotnet new` şablonu — çalışan bir [AgentPrism](https://www.nuget.org/packages/AgentPrism) kontrol düzlemi üretir.

## Kurulum

```bash
dotnet new install AgentPrism.Templates
```

## Kullanım

```bash
dotnet new agentprism-api -n Benim.Agent
cd Benim.Agent
dotnet user-secrets init
dotnet user-secrets set "AgentPrism:Providers:OpenAI:ApiKey" "sk-..."
dotnet run
```

`http://localhost:5081/agentprism` adresinde çalışan bir kontrol düzlemi açılır.

## Seçenekler

| Seçenek | Değerler | Varsayılan | Ne yapar |
|---|---|---|---|
| `--persistence` | `memory`, `postgres`, `sqlite`, `sqlserver` | `memory` | Kalıcılık sağlayıcısı |
| `--provider` | `openai`, `anthropic`, `google`, `azure` | `openai` | Model sağlayıcısı |
| `--ui` | `true`, `false` | `true` | Gömülü kontrol düzlemi arayüzü |

```bash
dotnet new agentprism-api -n Benim.Agent --persistence postgres --provider anthropic --ui true
```

Üretilen `appsettings.json` yalnız boş placeholder taşır — hiçbir `secret` içermez. Bağlantı dizesi ve API anahtarı `dotnet user-secrets` ile ayarlanır; üretilen `README.md` bunu ilk adım olarak anlatır.

Ayrıntı: [AgentPrism deposu](https://github.com/farukatasoy/AgentPrism), [`docs/37-PROJE-SABLONU.md`](https://github.com/farukatasoy/AgentPrism/blob/main/docs/37-PROJE-SABLONU.md).
