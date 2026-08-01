# AgentPrism.Core

AgentPrism cekirdek calisma zamani.

Agent katalogu, tanim derleyicisi, tool kayit defteri ve calistirma kaydi. Veritabani gerektirmez: yapilandirma yapilmazsa tum depolama bellek icinde calisir.

```csharp
builder.AddAgentPrism()
       .AddTool(GetOrderStatus);
```

## Kurulum

```bash
dotnet add package AgentPrism.Core
```

## Baglanti

- Depo ve tam dokumantasyon: <https://github.com/farukatasoy/AgentPrism>
- Mimari: [docs/MIMARI.md](https://github.com/farukatasoy/AgentPrism/blob/main/docs/MIMARI.md)

Lisans: MIT
