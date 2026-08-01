# AgentPrism

AgentPrism meta paketi.

Microsoft Agent Framework uzerine kurulu, PostgreSQL destekli, gomulu yonetim arayuzu olan uretim seviyesi agent kontrol duzlemi.

Bu paket tum AgentPrism bilesenlerini tek referansla getirir:

| Paket | Ne yapar |
|-------|----------|
| `AgentPrism.Abstractions` | Sozlesmeler |
| `AgentPrism.Core` | Calisma zamani, katalog, derleyici |
| `AgentPrism.PostgreSql` | Kalicilik |
| `AgentPrism.OpenAI` | OpenAI saglayicisi |
| `AgentPrism.AspNetCore` | HTTP API |
| `AgentPrism.UI` | Gomulu arayuz |

Yalnizca bir alt kumeye ihtiyaciniz varsa ilgili paketi tek tek kurun.

## Kurulum

```bash
dotnet add package AgentPrism
```

## Baglanti

- Depo ve tam dokumantasyon: <https://github.com/farukatasoy/AgentPrism>
- Mimari: [docs/MIMARI.md](https://github.com/farukatasoy/AgentPrism/blob/main/docs/MIMARI.md)

Lisans: MIT
