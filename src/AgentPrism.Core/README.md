# AgentPrism.Core

The AgentPrism core run time.

It contains the agent catalog, definition compiler, tool registry, and run recording.
It does not require a database. Without configuration, all storage runs in memory.

```csharp
builder.AddAgentPrism()
       .AddTool(GetOrderStatus);
```

## Installation

```bash
dotnet add package AgentPrism.Core
```

## Links

- Repository and full documentation: <https://github.com/farukatasoy/AgentPrism>
- Architecture: [docs/MIMARI.md](https://github.com/farukatasoy/AgentPrism/blob/main/docs/MIMARI.md)

License: MIT
