# Tracon.Embedded

`samples/Tracon.Api` is a green-field setup: it shows Tracon's two-line
promise (`AddTracon()` + `MapTracon()`) working in an empty application.
This sample shows the other side — embedding Tracon into a host that
already has its own tenants, users, permissions, event bus, and object
storage. Full explanation:
[Embedding into a host application](https://tracon.dev/guides/embedding/).

## Run it

```bash
dotnet run --project samples/Tracon.Embedded
```

No API key or connection string is required — the sample uses an in-memory
model provider and in-memory persistence, the same "zero surprises" pattern
`samples/Tracon.Api` uses.

## What it binds

Tracon has seven embedding points. This sample binds six of them, all
registered **before** `AddTracon()` so they win over Tracon's built-in
default (every one uses `TryAdd`):

| Contract | This sample's implementation |
|---|---|
| `ITenantContext` | `Tenancy/EmbeddedTenantContext.cs` — reads `X-Host-Tenant`, falls back to `AmbientTenantScope` |
| `ITenantStore` | `Tenancy/EmbeddedTenantStore.cs` — in-memory stand-in for the host's own tenant directory |
| `IRunAttributionContext` | `Attribution/EmbeddedRunAttributionContext.cs` — reads `X-Host-User`, falls back to `AmbientRunAttributionScope` |
| `IToolAuthorizationHandler` | `Authorization/EmbeddedToolAuthorizationHandler.cs` — a fixed per-tenant permission map |
| `IRunAuthorizationHandler` | `Authorization/EmbeddedRunAuthorizationHandler.cs` — denies starting a run, and reaching a run's resources, for a tenant the host's directory does not know about |
| `IRunEventSink` | `Events/BoundedChannelRunEventSink.cs` — an 8-item bounded channel that **drops** on backpressure, drained by `Events/RunEventBridgeWorker.cs` |
| `IAttachmentStorage` | `Attachments/InMemoryBufferAttachmentStorage.cs` — stands in for an external object store |

Confirm those six took over the built-in default:

```bash
curl -s http://localhost:5082/tracon/api/diagnostics | jq '.extensionPoints'
```

Six of the seven entries read `"isBuiltInDefault": false` here; the seventh,
`IToolApprovalPresenter`, stays on its built-in default on purpose — see
`samples/Tracon.Api`, which binds that one (`OrderApprovalPresenter`) plus
`IRunAttributionContext` (`DemoRunAttributionContext`, for its per-user cost
demo) and so reports `true` for the remaining five.

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
curl -s -H 'X-Host-Tenant: acme' http://localhost:5082/tracon/api/runs | jq '.[0]'
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

## Optional: one connection pool shared with your own EF Core `DbContext`

Without a connection string this sample runs fully in memory, as above. Give it
one and `Persistence/HostDbContext.cs` (the host's own, entirely separate,
schema) and Tracon's own store layer share a single `NpgsqlDataSource` —
proving the pattern
[Two connection planes: EF Core and Tracon](https://tracon.dev/guides/ef-core/)
describes actually runs, not just reads:

```bash
dotnet user-secrets set "Tracon:PostgreSql:ConnectionString" \
  "Host=localhost;Database=tracon_embedded;Username=postgres;Password=..." \
  --project samples/Tracon.Embedded

dotnet run --project samples/Tracon.Embedded
```

```bash
curl -s -X POST http://localhost:5082/tickets \
  -H 'Content-Type: application/json' \
  -d '{"tenantId":"acme","userId":"user-42","subject":"How do I reset my password?"}'

# {"id": "...", "tenantId": "acme", "subject": "...", "runId": "...", "createdAt": "..."}
# The ticket carries only the RunId; read the run's own content back through
# Tracon's own store:
curl -s -H 'X-Host-Tenant: acme' "http://localhost:5082/tracon/api/runs/<runId>"
```

`POST /tickets` (`Program.cs`) generates the run's identifier up front
(`TraconRunOptions.RunId`) so it can save the ticket without waiting for —
or copying — anything Tracon records for that run itself. `/tickets` is
only mapped when persistence is configured.

## What this sample deliberately does not add

Neither `InMemoryBufferAttachmentStorage` nor `BoundedChannelRunEventSink` is
a Tracon type — they live only here. Tracon takes no dependency on
any specific object store or messaging library; a real deployment derives its
own implementation from whichever client it already uses (S3, Azure Blob,
Kafka, SQS, an internal bus).
