# Tracon.UI

Embedded management UI.

Single-page application written with React 19 + TypeScript. Built with Vite and
embedded in the assembly **Brotli-compressed**. No JavaScript dependency is created
in the consumer's project; no `node_modules` folder is required.

## Installation

```bash
dotnet add package Tracon.UI --prerelease
```

```csharp
builder.AddTracon()
       .UseOpenAI(apiKey)
       .UseUI();

app.MapTracon("/tracon");
```

There is no separate mapping call. `MapTracon` finds the registration and binds
the UI under the same prefix; the prefix is written in one place.

## Screens

30 screens behind 36 routes. The console groups them into **Operate** — what the
agents are doing right now — and **Configure** — what they are allowed to do.
By the area they manage:

| Area | Screens |
|-------|--------|
| Agents | Catalog (code / database), definition editor, detail, version history, rollback |
| Skills | Skill list, skill editor |
| Playground | Streaming chat; tool calls as cards with arguments and results |
| Sessions | Session list, chat history, raw state, deletion |
| Runs | Run list, summary, event-by-event timeline |
| Workflows | Workflow list, graph detail, editor |
| Jobs | Queue list, job detail, retry and cancellation |
| Evals | Suite list, suite detail, eval run detail |
| Experiments | Experiment list, A/B variant comparison |
| Approvals | Pending tool approvals, approve and reject |
| Triggers | Inbound trigger list, trigger editor |
| Observability | Dashboard, audit trail, diagnostics |
| Catalog | Tools and their JSON schemas, providers and models, MCP servers |
| Settings | Version, prefix, auth method, active stores, theme |

## Notes

- The UI works under any prefix (`/tracon`, `/panel`, ...) and learns the prefix
  at runtime
- Dark and light themes. A fresh console opens dark; Settings also offers
  "follow system", which then tracks the operating system preference
- Tools are defined only in code. An agent can be created from the UI, but tool
  **code** cannot be written — this is a security boundary
- The UI shell is exempt from the bearer token layer; the loopback restriction and
  authorization policy apply instead: a browser cannot add an
  `Authorization` header to a script request
- JavaScript budget: 250 KB gzip (build gate). Current size 184.1 KB
- Four run-time JavaScript dependencies, and a build gate on that list. Dialogs,
  menus, tooltips and the command palette are written against the platform
- Colour, contrast and density come from one token set, checked on every build:
  text at 5.2:1 or better and non-text at 3.5:1 or better, in both themes

## Links

- Full documentation: <https://tracon.dev>
- Console guide: <https://tracon.dev/ui/>

License: PolyForm Small Business 1.0.0 - free below 100 people and 1,000,000 USD
(2019, inflation adjusted) revenue; a commercial licence applies above that. Terms
ship in the package as LICENSE.md. Details: <https://tracon.dev/reference/licensing/>
