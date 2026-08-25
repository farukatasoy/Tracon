---
title: Governance
description: Govern agents with tenant isolation, audit, approvals, quotas, retention, content guards, API keys, and webhooks.
sidebar:
  order: 8
---

Governance is explicit and visible. Tenancy, quotas, rate limits, retention cleanup,
and content guards need configuration. Audit decorators and their default store are
registered by `AddAgentPrism()`; authentication only changes which actor name they
can record.

## Multi-tenancy

Off by default. Turned on, the tenant is resolved in a fixed order:

```mermaid
flowchart TD
    accTitle: Tenant resolution order
    accDescr: AgentPrism first uses an API key tenant, then configured claim or header tenancy, and otherwise resolves the built-in default tenant.
    K{"authenticated with an API key?"} -->|yes| KT["the key's tenant"]
    K -->|no| S{"tenancy enabled?"}
    S -->|no| D["default tenant"]
    S -->|yes| C{"claim type configured?"}
    C -->|yes| AU{"request authenticated?"}
    AU -->|yes| CL["the claim's value"]
    AU -->|no| D
    C -->|no| H{"header resolution allowed?"}
    H -->|yes| HD["the header's value"]
    H -->|no| D
    CL --> V{"valid format · allowlisted?"}
    HD --> V
    V -->|yes| T["tenant resolved"]
    V -->|no| D
```

Two rules are worth reading twice.

**An API key outranks everything.** A key *proves* a secret; a claim or a header only
*asserts* one. If a key and a header disagree, the request is refused with `403`
before it reaches any endpoint.

**If a claim type is configured, the header is never read.** Otherwise an
authenticated user could reach another tenant's data by adding a header. The header
path also has to be enabled explicitly — an HTTP header is not proof of identity.

Isolation is enforced by contract tests that check it in both directions, across the
in-memory store and all three SQL providers, with a coverage gate requiring every
public store method to be either tested or exempted with a documented reason.

Isolation lives in the application layer, and that is a deliberate choice. Every
query carries the resolved tenant; AgentPrism does not create database row level
security policies, and it does not assume your database has them. Two reasons: the
coverage gate above already makes an untested store method a build failure, and
SQLite has no row level security at all, so adding it would make the three
providers behave differently. You are free to add such policies in your own
database. If you do, keep the tenant that AgentPrism resolves and the tenant your
policy binds to the connection in agreement — they are two separate mechanisms.

Rate limits are not an isolation boundary. `AgentPrism:RateLimit` and the inbound
trigger limit count in the memory of one process, so a `Tenant` partition splits
that instance's own window per tenant rather than a window shared across the
deployment. What binds a tenant's total consumption is a quota, and quotas are
counted in the database.

### Attributing spend below the tenant

The tenant answers "whose data is this". Two further questions — which **user**
spent this, and which **job** it was spent on — are answered by
`IRunAttributionContext`, the sibling interface described in
[Runs](/concepts/runs/#who-ran-it-and-for-what).

The security property is the same one the tenant header has: the value is never
taken from the run request body. A `userId` field there would let any client
write spend against another user's name and forge the cost record outright, so
the body is not a source of attribution at all — the server resolves it from your
identity pipeline, and a `userId` sent in the body is ignored.

The recorded user id is an **opaque string**. AgentPrism does not resolve it,
does not validate it, and stores no personal detail of its own; what it
identifies is your application's decision.

:::caution
Erasure does **not** match on `runs.user_id`. `IDataSubjectResolver` is the only
thing that knows which subject a value belongs to — AgentPrism deliberately
holds no mapping — so a resolver must return those runs itself. Find them with
`GET /api/runs?userId={id}&includeChildren=true` and include their ids in the
scope's `RunIds`. Erasing a run row removes its `user_id` along with everything
else on it.
:::

## Per-tenant provider credentials and egress

By default every tenant shares the model provider credential a `Use...()` call
registered at startup — one key, one bill, one usage pool. A tenant can instead bring
its own key (BYOK): its usage and its bill stay separate. A record for this binds a
tenant and a provider to the **name** of a configuration key, never to the key's
value — the value is read from `IConfiguration` only at call time and is never
written to a database, a log line, or an HTTP response. A tenant with no binding for
a provider keeps using the shared setup-time credential; nothing changes until an
administrator writes one, and a binding whose configuration key carries no value does
not fall back to the shared key silently — the run fails with a clear error, so a
misconfigured tenant is never billed against the wrong account.

An egress policy narrows which providers a tenant's agents may call at all. A tenant
with no saved policy is unrestricted; saving one is an additive restriction. Naming a
forbidden provider in an agent definition is rejected **at compile time** — before any
request reaches the network — and writing a credential binding for a forbidden
provider is rejected too, so the two surfaces cannot disagree.

Both are managed under `/api/tenants/{tenantId}/providers` and
`/api/tenants/{tenantId}/egress`, guarded by the `SecurityAdmin` API key scope. See
[Per-tenant credentials](/guides/model-providers/#per-tenant-credentials-byok)
for the full HTTP contract.

## The audit trail

Who changed what, when, and from what to what. Agent definitions, MCP servers,
tenants, approval rules, quotas, retention policies, and approval decisions all land
in it.

Runs do **not** — the run history already holds the full record. The one exception is
a content guard's block decision, which is a governance decision rather than a run
detail and must stay traceable after retention deletes the run.

Writes happen in store **decorators**, not in endpoints, so no code path can change
one of those entities without an entry.

The actor comes from your authentication. With none configured the actor is `null`,
and that is not hidden.

Before anything is written it passes a secret filter: any field whose name contains
`apiKey`, `authorization`, `password`, `secret`, or a singular `token` has its value
replaced with `***`. Plural `tokens` — count fields like `maxOutputTokens` — is
deliberately excluded.

```bash
curl 'http://localhost:5081/agentprism/api/audit?action=agent.update'
curl 'http://localhost:5081/agentprism/api/audit/quota:{id}'
```

### Tamper detection

Every entry carries a hash of its own content and the hash of the entry before it,
chained per tenant. `GET /api/audit/verify` walks the chain and reports one of three
outcomes:

| Status | Meaning |
|---|---|
| `Valid` | Every entry's hash matches its content and links to the one before it |
| `Broken` | An entry's stored hash no longer matches its content — it was altered after it was written |
| `Gap` | A link between two entries is missing — a row was deleted, or a write never completed |

`Broken` and `Gap` both name the first entry where the chain fails.

```bash
curl 'http://localhost:5081/agentprism/api/audit/verify'
# {"status":"Valid","entriesChecked":42,"firstFailingEntryId":null}
```

:::note[Entries written before this feature ships have no hash]
They are excluded from the walk rather than misreported as tampered — a chain starts
at the first entry written after upgrading, not retroactively.
:::

## Approvals

Two shapes, matching how the run was started.

**In-band.** A streaming run that hits a tool needing approval carries the request in
its stream and the decision in the next turn.

**The mailbox.** A queued run stops at `AwaitingApproval` and the request waits in
`GET /api/approvals/pending`, with the tool's recorded arguments for the approver to
read and an absolute expiry.

Deciding either way **resumes** the run — the model has to see a result or a refusal
and continue. And the decision opens a **new** run; the one that stopped is never
rewritten.

:::note[The audit entry is written before the decision is applied]
Everywhere else an audit failure is swallowed. Not here: an approval decision that
cannot be recorded is not applied at all. Approvals and skill scripts are the only two
places with that inversion, and both are places where the missing record would be the
whole problem.
:::

Standing decisions are **approval rules** — a pre-approval for a tool. They do not
expire. `GET /api/approvals/rules` is the list to review periodically, because each
entry is a tool call that will never ask again.

A rule narrows its scope one of three ways: to one exact set of arguments (a hash,
written by the "don't ask again" flow above), to a set of argument **conditions** (for
example `amount <= 100`, written with `POST /api/approvals/rules`), or not at all —
matching every call of the tool. A rule carries a hash or conditions, never both.

Conditions are comparisons, never expressions: a dotted path into the arguments, one
operator from a closed set (`Equals`, `NotEquals`, `GreaterThan`,
`GreaterThanOrEqual`, `LessThan`, `LessThanOrEqual`, `In`, `NotIn`), and a value. All
of a rule's conditions must match — there is no `OR`; write two rules instead. A
condition fails closed: an unresolved path, a missing argument, or a type mismatch
(text `"100"` does not satisfy a numeric rule) all mean the call still asks for
approval.

```mermaid
flowchart TD
    accTitle: Approval decision order
    accDescr: A code-defined policy runs first and can force or waive approval; only when it is undecided do the persisted data rules decide, falling back to asking the user.
    A["tool call requiring approval"] --> B{"code policy registered?"}
    B -->|"no"| D["data rules"]
    B -->|"yes"| P["policy runs"]
    P --> R{"result"}
    R -->|"Required"| ASK["ask the user"]
    R -->|"NotRequired"| GO["run without asking"]
    R -->|"Undecided"| D
    D --> M{"a rule matches?"}
    M -->|"yes"| GO
    M -->|"no"| ASK
```

A **code-defined policy**, registered with
`builder.AddToolApprovalPolicy("refund_order", context => ...)`, runs before the data
rules and can override them in both directions. Code is a security boundary; the data
rules are writable from the UI and are not allowed to loosen a policy that says
`Required`. An unhandled exception in a policy is treated as `Required` and logged —
a broken policy never silently releases a tool from approval.

### Tool authorization

Approval and authorization answer different questions. Approval asks "is this call okay
this time" and stops to wait for a person; authorization asks "can this caller call this
tool at all" and answers instantly from `IToolAuthorizationHandler` — your own policy,
checked before approval and before the call's timeout even starts. A denied call does not
fail the run: the model gets the reason as an ordinary tool result and continues its
turn. See [Tools, skills, and MCP](/concepts/tools/#authorization-and-timeout)
for the interface and an example.

## Quotas and rate limits

Two different mechanisms, deliberately not merged. Rate limits work at second and
minute scale in memory; quotas work at day and month scale in the database.

Neither refuses anything by default: rate limiting is off, and quotas are empty until
a rule exists. An agent with no matching rule is unlimited.

A rule sets any of three limits — runs, tokens, or cost — over a period, scoped to the
tenant or to one agent. Exceeding one returns `429` with which quota was hit and when
the counter resets.

:::caution[Counters are approximate]
The check happens **before** a run starts; consumption is written **after** it
finishes. A run already in progress is never cut off mid-flight, so brief overshoot is
possible by design.
:::

## Retention

Recorded runs accumulate. A retention policy sets an age or row limit per target — run
events, tool calls, traces, jobs, webhook deliveries, eval results, checkpoints,
attachments, sessions, and more.

A database policy takes precedence. When none exists and retention is enabled in
configuration, AgentPrism falls back to its target defaults, including 30 days for
run events and 14 days for spans. With retention disabled, nothing is removed. A
policy with `enabled: false` is configured but paused.

When a policy has `archive: true`, cleanup first sends its batch to the registered
`IArchiveSink`. If no sink exists, no rows are deleted. This fail-safe trades storage
growth for protection from silent data loss.

No endpoint deletes synchronously. Preview first — it is the only way to see the size
of a deletion before it happens — then run, which queues a job.

```bash
curl 'http://localhost:5081/agentprism/api/retention/preview'
curl -X POST 'http://localhost:5081/agentprism/api/retention/run'
curl 'http://localhost:5081/agentprism/api/retention/history'
```

The history of what was deleted is itself never cleaned up.

## Data subject rights

Retention removes data by **age**. Export and erasure remove it by **identity** — a
data subject's own sessions, runs, and conversations, on request (a GDPR-style
"right to erasure").

AgentPrism does not store personal identity itself: `sessions.id` is a value your own
application chose, and only your application knows which session, run, or
conversation belongs to which end user. You supply that mapping by registering an
`IDataSubjectResolver`:

```csharp
public sealed class MyResolver : IDataSubjectResolver
{
    public ValueTask<DataSubjectScope> ResolveAsync(
        string subjectId, string tenantId, CancellationToken cancellationToken = default)
        => new(new DataSubjectScope
        {
            SessionIds = LookUpSessionIds(subjectId),
            RunIds = LookUpRunIds(subjectId),
            ConversationIds = LookUpConversationIds(subjectId),
        });
}

builder.Services.AddSingleton<IDataSubjectResolver, MyResolver>();
```

Without a resolver registered, both endpoints return `409` — never a silent empty
result that could be misread as "already erased".

```bash
curl 'http://localhost:5081/agentprism/api/data-subjects/user-42/export'
curl -X DELETE 'http://localhost:5081/agentprism/api/data-subjects/user-42'
curl -X DELETE 'http://localhost:5081/agentprism/api/data-subjects/user-42?dryRun=false'
```

:::caution[`dryRun` defaults to `true`]
A bare `DELETE` previews the row counts per target and deletes nothing.
`?dryRun=false` is required to actually erase.
:::

Erasure removes the session, run, conversation, attachment, score, and voice-session
rows that belong to the subject — including summarized conversation messages, which
retention otherwise keeps forever. It never touches the audit trail: an audit record
is "who did what", not the subject's own data, and stays intact and verifiable after
an erasure. The erasure itself **is** written there, with the row count per target; if
that write fails, the whole erasure rolls back.

Export returns every matching row, keyed by target, as one JSON document.
Attachment file bytes are not included — only their metadata.

## Content protection

`AddContentProtection(...)` encrypts session state, chat history, run inputs and
events, tool arguments/results, agent files, and attachments with AES-256-GCM
before they reach the database, and decrypts them transparently on read. Off by
default, like the content guards below — turning it on is a deliberate call, and
only new writes are protected: a row's own content, not configuration, decides
whether it needs decrypting. See
[at-rest content protection](/getting-started/security/#at-rest-content-protection)
for the key configuration and its limits.

## Content guards

An `IContentGuard` inspects content going to and coming from the model. Decisions are
`Allow`, `Mask`, or `Block`, and **the strictest decision wins**.

Off by default: with no guard registered the wrapper is never added and the measured
cost is zero.

The guard sits **inside** the tool-call loop, above the raw client. A tool result
re-enters the model on a second call, and a guard outside the loop would never see it.

Blocked content never reaches the provider network and does not trip the circuit
breaker. Recording stores the placeholder `[content_blocked]`, while the audit entry
records the guard, rule, and direction without the blocked text.

:::note[Masked content stays masked]
Input preview runs before the recording path. When a guard returns `Mask`, the model,
recorded input, and run events receive the masked value. AgentPrism does not retain a
hidden raw copy for later inspection.
:::

## The document channel

`documents` on `POST /api/agents/{name}/run` attaches reference text to a run,
wrapped in a delimiter and marked apart from the agent's instructions:

```json
{
  "message": "Summarize the attached policy.",
  "documents": [{ "name": "policy.md", "content": "Refunds within 30 days." }]
}
```

:::caution[Not a security guarantee]
This is a **convention and an audit trail, not a security guarantee**. No provider
gives a hard promise that content wrapped this way is never treated as an
instruction — a capable-enough model can still be steered by content inside a
document. The value is in keeping the data channel visibly separate in the
transcript and in the run record, and in the boundary marker surviving content that
tries to imitate it: a document containing the literal delimiter has that occurrence
defanged, so it can never forge the end of the document and make the model treat
what follows as a new set of instructions.
:::

The run record keeps the document's **name and size**, in its own event — never the
content, which already lives with the rest of the run's recorded input, subject to
the same [content protection](#content-protection) and retention settings as
everything else.

## Webhooks

Subscribe to events and AgentPrism posts them to your endpoint.

The signature is `HMAC-SHA256(timestamp + "." + body, secret)`, with the timestamp
inside the signature so a replay cannot be reused. Your receiver decides the tolerance
window.

The secret is never stored: the subscription carries the **name** of the configuration
key it is read from. Delivery goes through the job queue, so a `test` call reports
that it was queued, not how it went. The delivery history has one entry per event
carrying the latest status and attempt count — retries update that entry rather than
adding rows.

Address validation happens inside the socket connect callback, so the address
validated is the address connected to. See
[securing the endpoints](/getting-started/security/) for why.

## Read next

- [Securing the endpoints](/getting-started/security/)
- [The HTTP API](/http-api/)
