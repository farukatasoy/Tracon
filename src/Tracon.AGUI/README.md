# Tracon.AGUI

The AG-UI endpoint for Tracon. It connects a front end that speaks the [AG-UI protocol](https://docs.ag-ui.com)
(CopilotKit and others) to a Tracon agent.

An AG-UI request is a Tracon run: it is recorded, the tenant is resolved, the roles are enforced, the quota is counted
and the cost is written. Session ownership applies when `Tracon:SessionOwnership` is on (it is off by default); until
then a `threadId` is whatever the front end chooses, and any operator of the tenant can continue it. Only the dialect on
the wire is different.

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
your authorization policy and the `Operator` role. An API key needs the `ChatRun` or the `RunsWrite` scope; a key that holds
only `ChatRun` opens this endpoint and no other, so it is the key to give a browser front end.

The endpoint is `POST /tracon/agui/{agentName}`. The body is an AG-UI `RunAgentInput` and the answer is
`text/event-stream`, one `data:` frame per AG-UI event.

```bash
curl -N -X POST http://localhost:5081/tracon/agui/assistant \
  -H 'Content-Type: application/json' \
  -d '{"threadId":"t1","runId":"r1","messages":[{"id":"m1","role":"user","content":"hello"}],
       "tools":[{"name":"open_page","description":"Opens a page in the browser.","parameters":{"type":"object","properties":{"url":{"type":"string"}}}}],
       "context":[]}'
```

## How a request is read

| AG-UI field | What Tracon does |
|---|---|
| `threadId` | The session. One to 200 characters with no whitespace at either end; dots, colons and every other character are allowed, and it is not the narrower rule of a workflow execution id. It belongs to the tenant that opened it, and with `Tracon:SessionOwnership` on also to the user. |
| `messages` | Only the messages after the last assistant message are new. A user message is the run's message; a tool message answers a pending client-side tool call. Everything before is already in the session. System and developer messages are not read. |
| `tools` | Must name every client-side tool the agent carries. The schema is always the code's. A tool the agent does not carry is ignored. |
| `resume` | Answers a tool approval (`{ "approved": true, "reason": "...", "remember": false }`) or a request for user input (the requested fields). |
| `context` | Becomes run documents, kept apart from the instructions. |
| `forwardedProps.parameters` | The values of the agent's run parameters, an object of strings. Read only when the host turned on `AcceptForwardedParameters` or set `ResolveParameters`, which reads it as untrusted input (see below). |
| `state`, the rest of `forwardedProps` | Not used. |

## Run parameters

An agent can declare parameters its instructions fill with values a run supplies. An AG-UI request cannot supply them
unless the host allows it, because a value becomes part of the agent's instructions, which the content guard does not read:

```csharp
app.MapTraconAGUI("/tracon/agui", options => options.AcceptForwardedParameters = true);
```

With the option on, the values are checked like the `parameters` of `POST /api/agents/{name}/run`: a name the schema does not
declare, a missing required value and a value over `MaxParameterValueLength` are each a `400`, and so is a value that is not a
string. The values are recorded with the run, so a replay repeats them. The tenant, the session and the agent are never read
from `forwardedProps`. Turn the option on only when the host accepts that the browser writes part of an agent's instructions,
and declare only the parameters the front end needs.

A host that can produce the values on its own server sets `ResolveParameters` instead, which replaces the option:

```csharp
app.MapTraconAGUI("/tracon/agui", options =>
    options.ResolveParameters = async (request, cancellationToken) =>
        new Dictionary<string, string> { ["articleId"] = await LookupAsync(request.ThreadId, request.User, cancellationToken) });
```

It is called once per request, after the gates, with the agent, the thread, the caller and what the front end sent
(untrusted). Only what it returns reaches the run, checked like any other values, and a tool of the run reads them from
`TraconRunContext.Current.Parameters` with the source `ParameterValueSource.Host`. A resolver that throws ends the request
with `500` and the error type `parameter_resolution_failed`, and no run starts.

## Mapping more than one pattern

The endpoint can be mapped under more than one pattern (`app.MapTraconAGUI("/partner/agui")`). The patterns share one
rate-limit window. Mapping one pattern twice fails when it is mapped, with a message that names the pattern.

## What comes back

The first event after `RUN_STARTED` is a `CUSTOM` event named `tracon.run` that carries the Tracon run id. A tool that waits for
approval, and an MCP server that asks the user for input, end the run with an interrupt in `RUN_FINISHED`. A failure after the
stream began is a `RUN_ERROR` event that carries a correlation id the server log has. No event carries `rawEvent`: the provider's
raw update does not reach the browser. When the model asks for a server tool and a client-side tool in one answer, Tracon
holds the client call back and the stream carries a `CUSTOM` event named `tracon.client_call_withheld` with the call's
`toolCallId` and `toolName`, before `RUN_FINISHED`; a server tool that waits for approval ends the run at the approval and
the held client call does not run in that turn.

## Notes

- A tool is defined only in code. An AG-UI request cannot add a tool or change a schema.
- The package takes `AGUI.Server` and `AGUI.Abstractions` (MIT, published by the AG-UI protocol project). It is not part of the
  `Tracon` meta package, so an application whose front end does not speak AG-UI carries neither.
- It does not support Native AOT.

License: MIT. The text ships in the package as LICENSE.md.
Details: <https://tracon.dev/reference/licensing/>
