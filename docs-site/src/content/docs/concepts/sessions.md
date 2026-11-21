---
title: Sessions and conversations
description: Learn how sessions carry conversation state, how branching copies history, and how attachments are owned and removed.
sidebar:
  order: 4
---

A **session** is where a conversation's state lives between turns. A **run** is one
turn. They are separate on purpose: a session has many runs, and a run can happen
without one.

## The lifecycle

```mermaid
sequenceDiagram
    accTitle: Session conversation lifecycle
    accDescr: A caller sends a session id, Tracon loads conversation history, invokes the agent, appends new items, and returns the response.
    autonumber
    participant Caller
    participant Manager as AgentSessionManager
    participant Store as ISessionStore
    participant Agent as AIAgent

    Caller->>Manager: GetOrCreateSessionAsync(agent, sessionId)
    Manager->>Store: GetAsync(sessionId)
    alt a record exists
        Store-->>Manager: SessionRecord
        Manager->>Agent: DeserializeSessionAsync(state)
    else no record
        Store-->>Manager: null
        Manager->>Agent: CreateSessionAsync()
    end
    Agent-->>Manager: AgentSession
    Manager->>Manager: stamp the id into the session state
    Manager-->>Caller: AgentSession

    Caller->>Agent: RunAsync(message, session)
    Caller->>Manager: SaveSessionAsync(agent, session)
    alt first save of this session
        Manager->>Store: TryCreateAsync(record)
    else every later save
        Manager->>Store: TryUpdateAsync(record, versionRead)
    end
```

The caller drives it. Tracon does not decide when a conversation starts or ends.

What the next turn finds depends on the save. A **new** session exists only in memory until you save it: a turn
that runs on it and is never saved leaves a conversation no record names, and the next
`GetOrCreateSessionAsync` opens a session with an empty history. For a session that already has a record, a SQL
provider writes each turn to the conversation as the turn ends, so a later turn you did not save is still there; with
the in-memory default the history is part of the saved state, and a turn you did not save is lost. A turn that is
canceled writes nothing ([below](#a-canceled-turn)). The checks in front of the HTTP endpoints (run authorization,
session owner, quota) are not part of these calls: ask [`RunAdmission`](/guides/embedding/#ask-the-run-gates-before-you-run-an-agent).

The identity stamp matters: the session id is written into the session's own state
bag, so it survives serialization. A restored session knows which session it is, which
is how the recording layer can put the right session id on a run without being told.

## Two turns at once

Neither save is unconditional, and both refuse rather than overwrite.

| Situation | What the store is asked | If another writer got there first |
|---|---|---|
| First save of a new session | `TryCreateAsync` | `TraconSessionConflictException` |
| Every later save of that session | `TryUpdateAsync(record, versionRead)` | `TraconSessionConflictException` |

`SessionRecord.Version` is the record's write generation. A save replaces the exact
generation it read; if another turn advanced it meanwhile, the write is refused and
the caller is told. The alternative — overwriting — loses a turn that the run record
already reports as successful, with nothing anywhere saying so.

Over HTTP that surfaces as `409 Conflict` on both `/api/agents/{name}/run` (a
problem document whose `errorType` is `session_conflict`) and the OpenAI-compatible
`/v1/responses` (an error envelope whose `error.type` is the same value). **Retry the
turn** only after you have looked at what the conversation holds; the conflict means the conversation moved on, not that anything is broken. On
a streaming request the response headers are already sent, so the same condition
arrives as an `error` event in the stream instead of a status code.

A refused save does not undo the turn. A SQL provider writes the turn's messages to the conversation as the turn ends,
before the save, so the turn that lost the race is in the conversation the next turn reads. That is why a retry can send
a message the conversation already holds: before you retry, read the history
(`AgentSessionManager.ReadHistoryAsync`) and look. Two concurrent first turns of a new session each open their own
conversation, and the loser's conversation is not reachable from any record.

A request that **answers a pending call** (a tool approval, a tool input or a client tool
result) takes the session's generation before it applies the answer. The call is pending in
the history the session holds, and the session is written when a run ends, so two requests
that read the same generation would both find it pending and both run the tool. The one that
takes the generation first goes on; the other gets the same `409` before it applies a decision
or runs anything. The request that applies a tool approval or a tool input also writes what it
answered into the session before the run starts, because a run whose client disconnects is
cancelled and saves nothing: the same answer sent again is refused (`409` on the management
API, an invalid resume on AG-UI) and the tool does not run twice. A decision made at
`POST /api/approvals/{id}/decide` does the same: the job that applies it writes the mark before the
tool runs, so answering the same request over `/run` while that job runs, or after the worker that
ran it died, is refused with `409` as well. The unanswered request the
cancelled run leaves in the history is rejected by the agent framework on the next run of the
session, so the user can carry on with a new message. That request is still in the history,
though its answer was applied, so `GET /api/sessions/{id}` lists the requests that still wait in
`pendingRequestIds`: a request in `messages` that is not in the list was answered. The console
reads it and shows such an approval as answered instead of asking for the decision again.

Saving a session under a **different** id than it was read from — what
`/v1/responses` does when it chains with `previous_response_id` — writes a new record
rather than replacing the source one, so no generation applies and the write is
unconditional.

## Persisted payload compatibility

`SessionRecord.State` is the Microsoft Agent Framework's own serialized
format; Tracon does not interpret it. `SessionRecord.StateSchemaVersion`
(Tracon's own envelope generation) and `StateMafVersion` (the Microsoft
Agent Framework package version that wrote `State`) are stamped on every
save so that a failed restore can report exactly what was recorded instead
of guessing. See [Versions and upgrades](/reference/versioning/#persisted-session-and-checkpoint-state)
for the full compatibility policy and what to do when a session cannot be
restored after an upgrade.

## Reading a conversation back

`GET /api/sessions/{sessionId}` returns metadata plus `messages`, the conversation in order. The messages are
read through the registered chat history provider, with in-memory storage and with a SQL provider alike, so
they are what the agent itself sees, tool calls and tool results included. `messages` is `null` only when the
history could not be read: the agent no longer exists, or it can no longer restore the stored state. An agent
that builds its own chat history instead of taking the registered provider shows no messages on SQL storage.
`state` always carries the raw session state, and it is not a chat log.

The messages come back in sequence order. That ordering is not cosmetic: the index of a message **is** the
sequence number the branch endpoint takes.

From your own code, ask the session manager. It reads the same history, a page at a time:

```csharp
var sessions = provider.GetRequiredService<AgentSessionManager>();

var page = await sessions.ReadHistoryAsync(agent, "chat-42", skip: 0, take: 50);

foreach (var message in page ?? [])
{
    Console.WriteLine($"{message.Role}: {message.Text}");
}
```

The result is `null` when the current tenant has no session with that id. `skip` counts messages from the start of
the conversation. The call checks the tenant, not the owner: session ownership is a gate in front of the HTTP
endpoints, so a host that shows a conversation to a user asks
[`RunAdmission`](/guides/embedding/#ask-the-run-gates-before-you-run-an-agent) first. Reading does not claim the
session or save it.

A provider-hosted live voice session writes into this same history: when it closes,
the conversation's transcript is appended as ordinary user and assistant messages, so
`GET /api/sessions/{sessionId}` shows what was actually said. That write is a
retention decision and can be switched off — see
[voice privacy and retention](/guides/voice/#privacy-and-retention).

## A canceled turn

A turn writes its messages to the history when it ends. A turn that is canceled does not end, so it writes nothing: the
user's message and the part of the answer that streamed are not in the history, and the next turn answers as if the
message had never been sent. Saving the session after the cancellation does not bring them back, because there is
nothing to save. The run is recorded as `Canceled`.

To keep such a turn, turn on `Tracon:Sessions:KeepCanceledTurns`. A **streamed** turn the client cancels after the model
began to answer is then written: the user's message, and the text that had streamed with a note that the answer was cut
off, so the next turn sees what was asked and what the user saw. A cancel before the first piece of the answer writes
nothing, because the message has not been accepted yet and the content guards have not necessarily screened it. A turn
that is not streamed writes nothing. The kept text is user content: it is subject to the same retention and erasure as the
rest of the history.

A host that runs agents from its own code decides for itself and calls `AgentSessionManager.RecordCanceledTurnAsync` from the
place that catches the cancellation:

```csharp
var sessions = provider.GetRequiredService<AgentSessionManager>();
var session = await sessions.GetOrCreateSessionAsync(agent, "chat-42");

try
{
    await agent.RunAsync("Summarize it.", session);
}
catch (OperationCanceledException)
{
    // The write goes around the content guards: only keep a message the model had started to answer, or screen it yourself.
    await sessions.RecordCanceledTurnAsync(
        agent,
        session,
        [new ChatMessage(ChatRole.User, "Summarize it.")],
        partialAnswer: null);
}
```

## Branching

`POST /api/sessions/{sessionId}/branch` copies items up to and including a sequence
number into a **new** conversation and opens a session on it.

```mermaid
flowchart LR
    accTitle: Conversation branch operation
    accDescr: Branching copies parent conversation items through a selected sequence into a new conversation and opens a new session on that copy.
    P["parent conversation<br/>items 0..9"] -->|"branch at 4"| B["new conversation<br/>copy of items 0..4"]
    B --> S["new session"]
    P -.->|"provenance only"| B
```

The items are **copied**, not shared. Writing to the branch never changes the parent,
and the pointer back to the parent is provenance, nothing more. This is what "try the
same conversation with a different agent from turn five" looks like.

Branching needs a SQL provider. On in-memory storage there are no addressable items to
copy, and the endpoint answers `501` rather than pretending.

:::note[Only the session screen can branch mid-conversation]
The playground folds its transcript from a live event stream, and those events carry
no sequence numbers. Branching from the playground therefore copies the **whole**
conversation. The session screen reads stored items and can branch at any point.
:::

## OpenAI-compatible conversations

`/v1/conversations` maps onto the same sessions. Two behaviours are worth knowing:

- A conversation id is a **reservation**. An id that has never carried a call is still
  valid and answers `200`. A conversation id is also **local to the calling tenant**:
  two tenants can use the same id and each reaches only its own session. An id that
  only another tenant uses answers exactly like one nobody has used — `200` for a
  read, an empty item list, `deleted: false` for a delete — so the API never confirms
  what another tenant holds. A `404` means the caller is refused (your
  authorization handler, or the session belongs to another user).
- `previous_response_id` and `conversation_id` are treated as untrusted input. A
  response call that names an id only another tenant uses opens a new session under
  it in the caller's own tenant and leaves the other tenant's session untouched.

## A2A conversations

An agent published over A2A keeps a session for each `contextId`. The session identity
is not the `contextId` the caller sends: it is `a2a:` followed by a digest of the
tenant, the agent, the caller, and the `contextId`. The digest has a fixed length, so a
`contextId` of any length is stored, and two callers that send the same value do not
share a conversation. The Sessions page lists these conversations under the exposed
agent. [Publish agents through A2A](/guides/external-agents/#a2a-conversations) states
who counts as the caller and what a conversation costs.

## Who can access a session

By default everything above is tenant-scoped: any caller with the
`Reader`/`Operator` role in a tenant can list, read, delete, and branch every
session in that tenant, regardless of which user opened it.

There are two ways to draw a narrower line, and they compose. **Session
ownership** is built in and needs no code. **`IRunAuthorizationHandler`** hands
the decision to your own policy.

### Session ownership

Turning ownership on makes Tracon record which user a session belongs to,
and narrow the session list to that user:

```json
{
  "Tracon": {
    "SessionOwnership": {
      "Enabled": true
    }
  }
}
```

The owner comes from `IRunAttributionContext` — the same interface that names
the user on a cost report — and is read at the moment a session is **opened**.
It is never read from a request body: a `userId` field there would let any
client open a session under someone else's name.

| With ownership on | What happens |
|---|---|
| `GET /api/sessions` | Only the caller's own sessions, narrowed **before** paging, so `take=3` returns three of *their* sessions |
| `GET`/`DELETE`/`POST …/branch` on another user's session | `404`, byte for byte identical to a session that does not exist |
| Starting a run against another user's session over HTTP | `403` — continuing a conversation reads its whole history back, so the run surface is guarded too. A run you start from .NET code is not compared with the owner; ask [`RunAdmission`](/guides/embedding/#ask-the-run-gates-before-you-run-an-agent) first |
| A branch of your own session | The copy inherits **your** ownership; branching is a copy, not a handover |
| No identity can be resolved | The session is not opened at all: `403` with `errorType` `session_owner_required` |

Three properties are worth knowing before you turn it on:

- **It is not retroactive.** Sessions written before you enabled it have no
  owner. Tracon cannot invent one for a conversation it did not watch being
  opened. Those sessions stay readable by id, so nothing that was live at the
  moment of the flip breaks — but they no longer appear in any user's list. Once
  they no longer matter, `RefuseUnownedSessions` closes that door too; see
  below.
- **Someone still needs the whole list.** A caller who satisfies
  `Tracon:SessionOwnership:ManagementPolicy` (default: the `Operator` role
  policy) gets the unfiltered tenant listing, including those unowned rows. If
  the policy is not registered, *nobody* gets the unfiltered list — the failure
  direction is deliberate. Set it to `""` to state that outright. If your
  policy provider or requirement handler throws, the caller also gets the
  narrow list, and Tracon writes an `Error` line in the
  `Tracon.SessionOwnership` category; an unregistered policy writes nothing.
- **Ownership is drawn under the tenant, never across it.** The same person in
  two tenants still has two independent data spaces.
- **The owner is an opaque identity and letter case counts.** `Alice` and `alice` are two
  owners: the session list of one never includes the sessions of the other, whichever store
  holds them. On SQL Server the owner column compares by code point (`Latin1_General_BIN2`), as
  the other stores do. SQL Server still ignores trailing spaces, so `Alice` and `Alice ` are one
  owner there; an identity provider does not hand those out.

`RequireAuthenticatedOwner` (default `true`) is what turns an unresolvable
identity into a refusal. Turning it off lets unowned sessions be opened again,
which is only useful while migrating: such a session is invisible in its own
caller's list from the moment it is written.

Sessionless runs are unaffected — there is nothing to own.

#### Refusing the unowned rows too

An unowned row is not discoverable — it appears in no user's list — but by
default it is still readable by anyone in the tenant who knows its id.
`RefuseUnownedSessions` (default `false`) turns that into a refusal:

```json
{
  "Tracon": {
    "SessionOwnership": {
      "Enabled": true,
      "RefuseUnownedSessions": true
    }
  }
}
```

| With strict mode on | What happens |
|---|---|
| `GET`/`DELETE`/`POST …/branch` on an unowned session | `404`, byte for byte identical to a session that does not exist |
| `GET`/`DELETE` `/v1/conversations/{id}` on an unowned session | The same `404` — the OpenAI-compatible routes reach the same sessions |
| A voice socket on an unowned session | Refused with `404` |
| Starting a run against an unowned session | `403` with `errorType` `session_owner_required` |
| A caller who satisfies `ManagementPolicy` reading one | Still `200` — support keeps the access it already had in the management listing |
| A caller who satisfies `ManagementPolicy` starting a run on one | `403` — that exemption covers reading a conversation, never appending to it |
| A session that **does not exist yet** | Unchanged: the first turn opens it and claims it |

The last row is the one to hold on to. "Never created" and "created without an
owner" are different rows and get different answers; if they were folded
together, the first turn of every new conversation would be refused.

The setting does nothing on its own — with `Enabled` off, no owner is ever
read. Default `false` because turning it on strands every conversation that was
live at the moment you enabled ownership. Turn it on once those conversations
no longer matter, or from day one in a deployment that has no unowned rows at
all.

### Your own authorization handler

Ownership answers "which user", and only for sessions. For anything else —
per-project rules, shared conversations, an external policy service — bind
`IRunAuthorizationHandler`:

```csharp
public sealed class YourRunAuthorizationHandler(IYourOwnershipService ownership) : IRunAuthorizationHandler
{
    public ValueTask<RunAuthorizationResult> AuthorizeRunAsync(
        RunAuthorizationRequest request, CancellationToken cancellationToken = default)
        => new(RunAuthorizationResult.Allow());

    public async ValueTask<RunAuthorizationResult> AuthorizeSessionAsync(
        SessionAuthorizationRequest request, CancellationToken cancellationToken = default)
    {
        // request.SessionId is null only for SessionAccess.List.
        if (request.Access == SessionAccess.List)
        {
            return RunAuthorizationResult.Allow();
        }

        return await ownership.OwnsAsync(request.TenantId, request.UserId, request.SessionId!, cancellationToken)
            ? RunAuthorizationResult.Allow()
            : RunAuthorizationResult.Deny("This session belongs to a different user.");
    }
}
```

The response shape follows the same "don't confirm what shouldn't be seen"
rule the OpenAI-compatible conversations above already use: a denied
**read**, **delete**, or **branch** returns `404` with the exact same body a
genuinely missing session gets, and a denied **list** returns `403`. Note the
difference from ownership: a handler answers a yes/no question, so a "no" on a
list rejects the whole call rather than quietly returning fewer rows. Ownership
is a filter and does narrow the list. See
[Embedding: run and session authorization](/guides/embedding/#6--run-and-session-authorization)
for the run-starting half of the same contract.

With ownership on, a handler no longer has to reject a whole listing just to
keep users apart — the listing arrives already narrowed, and the handler is
free to answer the questions ownership cannot.

## Attachments

Attachments are uploaded independently and referenced from messages; the bytes live in
storage and only a small reference travels with a message. The upload's type is
decided by inspecting its magic bytes, not by the `Content-Type` the client claims.

Deleting a session deletes the attachments it owns, its chat history, the conversation memory records it produced
(see [Context and memory](/guides/context-and-memory/#remember-a-user-across-sessions)), and the files of
its [session workspace](/guides/context-and-memory/#give-an-agent-files-in-each-session). On SQL storage the chat
history is a conversation of its own, and only the state of the session says which one, so Tracon deletes it, with its
messages, before the session record goes; in-memory storage keeps the history inside the record. Every way to delete
a session does the same: `DELETE /api/sessions/{id}`, `DELETE /v1/conversations/{id}`, and the session store
that Tracon registers for the Microsoft Agent Framework. The session record goes last, so a step that fails
leaves the session in place and a repeat request finishes the work. A session that does not exist deletes
nothing, and the attachments uploaded to its id stay.

Two histories are not reached this way. A run that has no session leaves a conversation of its own that no session
names, so deleting sessions does not delete it; the retention period for `conversations`, or a
[data subject erasure](/concepts/governance/#data-subject-rights) that names the conversation, does. And a run that
is still going when you delete its session writes its conversation again, with only that turn in it, so cancel the
run before you delete the session.

You can also call
`DELETE /api/attachments/{id}` for one attachment, and the orphan-attachment retention
target cleans uploads that never become part of a session. Individual deletion is a
hard delete: an older message that still contains the reference will no longer be able
to download the bytes. The link is deliberately not a database foreign key because an
upload can exist before its session does.

Downloads are served with `Content-Disposition: attachment` and
`X-Content-Type-Options: nosniff` together, so uploaded HTML can never execute in the
console's origin.

## Read next

- [Runs and recording](/concepts/runs/) — inspect recorded status, events, tokens, and recording limits.
- [Attachments and multimodal input](/guides/multimodal/) — attach validated files, images, and audio to a conversation.
- [Workflows](/concepts/workflows/) — coordinate multiple agents with checkpoints and human input.
