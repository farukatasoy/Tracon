# Tracon.AGUI

The AG-UI endpoint for Tracon. It connects a front end that speaks the [AG-UI protocol](https://docs.ag-ui.com)
(CopilotKit and others) to a Tracon agent.

An AG-UI request is a Tracon run: it is recorded, the tenant is resolved, the roles and the session owner are
enforced, the quota is counted and the cost is written. Only the dialect on the wire is different.

## Installation

```bash
dotnet add package Tracon.AGUI --prerelease
```

```csharp
builder.AddTracon()
       .UseOpenAI(apiKey)
       .AddClientTool("open_page", "Opens a page in the user's browser.", schema)
       .AddAgent(new AgentDefinition
       {
           Name = "assistant",
           Instructions = "You help the user.",
           Model = new ModelBinding { Provider = "openai", Model = "gpt-4o-mini" },
           ToolNames = ["open_page"],
       });

app.MapTracon();
app.MapTraconAGUI();
```

`MapTraconAGUI` needs `MapTracon` first. It inherits the access protection from there: loopback by default, the bearer token,
your authorization policy, the `Operator` role and the `runs:write` API key scope.

The endpoint is `POST /tracon/agui/{agentName}`. The body is an AG-UI `RunAgentInput` and the answer is
`text/event-stream`, one `data:` frame per AG-UI event.

```bash
curl -N -X POST http://localhost:5081/tracon/agui/assistant \
  -H 'Content-Type: application/json' \
  -d '{"threadId":"t1","runId":"r1","messages":[{"id":"m1","role":"user","content":"hello"}],"tools":[],"context":[]}'
```

## How a request is read

| AG-UI field | What Tracon does |
|---|---|
| `threadId` | The session. At most 200 characters. |
| `messages` | Only the messages after the last assistant message are new. A user message is the run's message; a tool message answers a pending client-side tool call. Everything before is already in the session. System and developer messages are not read. |
| `tools` | Must name every client-side tool the agent carries. The schema is always the code's. A tool the agent does not carry is ignored. |
| `resume` | Answers a tool approval (`{ "approved": true, "reason": "...", "remember": false }`) or a request for user input (the requested fields). |
| `context` | Becomes run documents, kept apart from the instructions. |
| `state`, `forwardedProps` | Not used. |

## What comes back

The first event after `RUN_STARTED` is a `CUSTOM` event named `tracon.run` that carries the Tracon run id. A tool that waits for
approval, and an MCP server that asks the user for input, end the run with an interrupt in `RUN_FINISHED`. A failure after the
stream began is a `RUN_ERROR` event that carries a correlation id the server log has.

## Notes

- A tool is defined only in code. An AG-UI request cannot add a tool or change a schema.
- The package takes `AGUI.Server` and `AGUI.Abstractions` (MIT, published by the AG-UI protocol project). It is not part of the
  `Tracon` meta package, so an application whose front end does not speak AG-UI carries neither.
- It does not support Native AOT.
