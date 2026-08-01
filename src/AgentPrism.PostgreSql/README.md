# AgentPrism.PostgreSql

AgentPrism icin PostgreSQL kalicilik katmani.

Agent tanimlari, oturumlar, konusmalar, calistirmalar ve olaylar ayri bir `agentprism` semasinda saklanir. Tuketici uygulamanin `public` semasina dokunulmaz.

Gomulu SQL migration'lari uygulama basladiginda `pg_advisory_lock` korumasi altinda uygulanir.

```csharp
builder.AddAgentPrism()
       .UsePostgreSql(connectionString);
```

## Kurulum

```bash
dotnet add package AgentPrism.PostgreSql
```

## Baglanti

- Depo ve tam dokumantasyon: <https://github.com/farukatasoy/AgentPrism>
- Mimari: [docs/MIMARI.md](https://github.com/farukatasoy/AgentPrism/blob/main/docs/MIMARI.md)

Lisans: MIT
