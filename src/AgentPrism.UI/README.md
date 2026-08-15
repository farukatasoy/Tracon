# AgentPrism.UI

Embedded management UI.

Single-page application written with React 19 + TypeScript. Built with Vite and
embedded in the assembly **Brotli-compressed**. No JavaScript dependency is created
in the consumer's project; no `node_modules` folder is required.

## Installation

```bash
dotnet add package AgentPrism.UI --prerelease
```

```csharp
builder.AddAgentPrism()
       .UseOpenAI(apiKey)
       .UseUI();

app.MapAgentPrism("/agentprism");
```

There is no separate mapping call. `MapAgentPrism` finds the registration and binds
the UI under the same prefix; the prefix is written in one place.

## Screens

| Screen | Content |
|-------|--------|
| Agents | Catalog (code / database), definition editor, version history, rollback |
| Playground | Streaming chat; tool calls as cards with arguments and results |
| Sessions | Session list, chat history, raw state, deletion |
| Runs | Run list, summary, event-by-event timeline |
| Tools | Registered tools and their JSON schemas |
| Models | Providers, models, capability flags |
| Settings | Version, prefix, auth method, active stores, theme |

## Notes

- The UI works under any prefix (`/agentprism`, `/panel`, ...) and learns the prefix
  at runtime
- Light and dark theme; the default is the operating system preference
- Tools are defined only in code. An agent can be created from the UI, but tool
  **code** cannot be written — this is a security boundary
- The UI shell is exempt from the bearer token layer; the loopback restriction and
  authorization policy apply instead. Rationale: a browser cannot add an
  `Authorization` header to a script request
- JavaScript budget: 250 KB gzip (build gate). Current size ~88 KB

## Links

- Repository and full documentation: <https://github.com/farukatasoy/AgentPrism>
- Architecture: [docs/MIMARI.md](https://github.com/farukatasoy/AgentPrism/blob/main/docs/MIMARI.md)
- UI phase: [docs/05-AGENTPRISM-UI.md](https://github.com/farukatasoy/AgentPrism/blob/main/docs/05-AGENTPRISM-UI.md)

License: MIT
