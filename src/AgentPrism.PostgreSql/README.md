# AgentPrism.PostgreSql

PostgreSQL persistence layer for AgentPrism.

Agent definitions, sessions, conversations, runs, and events are stored in a separate `agentprism` schema. The consuming application's `public` schema is left untouched.

Embedded SQL migrations are applied at application startup under `pg_advisory_lock` protection.

```csharp
builder.AddAgentPrism()
       .UsePostgreSql(connectionString);
```

## Setup

```bash
dotnet add package AgentPrism.PostgreSql
```

## Links

- Repository and full documentation: <https://github.com/farukatasoy/AgentPrism>
- Architecture: [docs/MIMARI.md](https://github.com/farukatasoy/AgentPrism/blob/main/docs/MIMARI.md)

License: MIT
