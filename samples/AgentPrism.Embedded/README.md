# AgentPrism.Embedded

`samples/AgentPrism.Api` is a green-field setup: it shows AgentPrism's two-line
promise (`AddAgentPrism()` + `MapAgentPrism()`) working in an empty application.
This sample shows the other side — embedding AgentPrism into a host that
already has its own tenants, users, permissions, event bus, and object
storage. Full explanation:
[Embedding into a host application](https://agentprism.doayen.web.tr/guides/embedding/).

## Run it

```bash
dotnet run --project samples/AgentPrism.Embedded
```

No API key or connection string is required — the sample uses an in-memory
model provider and in-memory persistence, the same "zero surprises" pattern
`samples/AgentPrism.Api` uses.

## What it binds

Five contracts, all registered **before** `AddAgentPrism()` so they win over
AgentPrism's built-in default (every one uses `TryAdd`):

| Contract | This sample's implementation |
|---|---|
| `ITenantContext` | `Tenancy/EmbeddedTenantContext.cs` — reads `X-Host-Tenant`, falls back to `AmbientTenantScope` |
| `ITenantStore` | `Tenancy/EmbeddedTenantStore.cs` — in-memory stand-in for the host's own tenant directory |
| `IRunAttributionContext` | `Attribution/EmbeddedRunAttributionContext.cs` — reads `X-Host-User`, falls back to `AmbientRunAttributionScope` |
| `IToolAuthorizationHandler` | `Authorization/EmbeddedToolAuthorizationHandler.cs` — a fixed per-tenant permission map |
| `IRunEventSink` | `Events/BoundedChannelRunEventSink.cs` — an 8-item bounded channel that **drops** on backpressure, drained by `Events/RunEventBridgeWorker.cs` |
| `IAttachmentStorage` | `Attachments/InMemoryBufferAttachmentStorage.cs` — stands in for an external object store |

Confirm all five took over the built-in default:

```bash
curl -s http://localhost:5082/agentprism/api/diagnostics | jq '.extensionPoints'
```

Every entry reads `"isBuiltInDefault": false` here — contrast with
`samples/AgentPrism.Api`, which reports `true` for four of the five. Its
fifth, `IRunAttributionContext`, is already bound to its own
`DemoRunAttributionContext` (Phase 68, for its per-user cost demo) — a
pre-existing binding this phase did not add.

## The mandatory scenario: background work

`Jobs/EmbeddedJobWorker.cs` runs an agent with **no HTTP request** behind it —
the scenario a queued email digest or a nightly summary job hits. It opens
`AmbientTenantScope` and `AmbientRunAttributionScope` directly around the run,
in its own method body:

```bash
curl -s -X POST http://localhost:5082/jobs \
  -H 'Content-Type: application/json' \
  -d '{"tenantId":"acme","userId":"user-42","message":"What is my account balance?"}'

# The run appears with tenantId "acme" and userId "user-42" even though
# nothing in this request path is an HTTP request by the time the agent runs.
curl -s -H 'X-Host-Tenant: acme' http://localhost:5082/agentprism/api/runs | jq '.[0]'
```

## The event bridge: drop, never block

Enqueue several jobs quickly and the 8-item channel overflows — the bridge
drops the newest events rather than slowing the run down to match a queue
depth:

```bash
for i in $(seq 1 10); do
  curl -s -X POST http://localhost:5082/jobs \
    -H 'Content-Type: application/json' \
    -d "{\"tenantId\":\"acme\",\"message\":\"request $i\"}" > /dev/null
done

curl -s http://localhost:5082/jobs/bridge-state
# {"received": ..., "dropped": ...}
```

## What this sample deliberately does not add

Neither `InMemoryBufferAttachmentStorage` nor `BoundedChannelRunEventSink` is
an AgentPrism type — they live only here. AgentPrism takes no dependency on
any specific object store or messaging library; a real deployment derives its
own implementation from whichever client it already uses (S3, Azure Blob,
Kafka, SQS, an internal bus).
