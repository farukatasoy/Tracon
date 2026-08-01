# AgentPrism.AspNetCore

AgentPrism HTTP katmani.

`MapAgentPrism` ile iki uc grubunu baglar:

- **Yonetim API'si** — agent CRUD, oturumlar, calistirmalar, tool'lar, modeller
- **OpenAI uyumlu uclar** — `/v1/responses`, `/v1/conversations`, `/v1/chat/completions`

Katmanli erisim korumasi icerir: varsayilan olarak yalnizca loopback, opsiyonel bearer token, ve ASP.NET Core authorization policy kancasi.

```csharp
app.MapAgentPrism("/agentprism", options =>
{
    options.RequireAuthorization("AgentPrismAdmin");
});
```

> **Onsurum notu:** Bu paket `Microsoft.Agents.AI.Hosting` (preview) ve `Microsoft.Agents.AI.Hosting.OpenAI` (alpha) paketlerine baglidir. AgentPrism'in tum onsurum bagimliligi bilerek bu tek pakette toplanmistir.

## Kurulum

```bash
dotnet add package AgentPrism.AspNetCore
```

## Baglanti

- Depo ve tam dokumantasyon: <https://github.com/farukatasoy/AgentPrism>
- Mimari: [docs/MIMARI.md](https://github.com/farukatasoy/AgentPrism/blob/main/docs/MIMARI.md)

Lisans: MIT
