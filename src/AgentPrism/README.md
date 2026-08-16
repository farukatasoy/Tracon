# AgentPrism

The AgentPrism meta package.

A production-grade agent control plane built on the Microsoft Agent Framework, backed by PostgreSQL, with an embedded management UI.

This package brings in all AgentPrism components with a single reference:

| Package | What it does |
|-------|----------|
| `AgentPrism.Abstractions` | Contracts |
| `AgentPrism.Core` | Runtime, catalog, compiler |
| `AgentPrism.PostgreSql` | Persistence |
| `AgentPrism.OpenAI` | OpenAI provider |
| `AgentPrism.AspNetCore` | HTTP API |
| `AgentPrism.UI` | Embedded UI |

If you only need a subset, install the relevant package individually.

## Setup

```bash
dotnet add package AgentPrism
```

## Links

- Repository and full documentation: <https://github.com/farukatasoy/AgentPrism>
- Architecture: [docs/MIMARI.md](https://github.com/farukatasoy/AgentPrism/blob/main/docs/MIMARI.md)

License: MIT
