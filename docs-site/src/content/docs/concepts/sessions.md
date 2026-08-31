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
    accDescr: A caller sends a session id, AgentPrism loads conversation history, invokes the agent, appends new items, and returns the response.
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

The caller drives it. AgentPrism does not decide when a conversation starts or ends.

The identity stamp matters: the session id is written into the session's own state
bag, so it survives serialization. A restored session knows which session it is, which
is how the recording layer can put the right session id on a run without being told.

## Two turns at once

Neither save is unconditional, and both refuse rather than overwrite.

| Situation | What the store is asked | If another writer got there first |
|---|---|---|
| First save of a new session | `TryCreateAsync` | `AgentPrismSessionConflictException` |
| Every later save of that session | `TryUpdateAsync(record, versionRead)` | `AgentPrismSessionConflictException` |

`SessionRecord.Version` is the record's write generation. A save replaces the exact
generation it read; if another turn advanced it meanwhile, the write is refused and
the caller is told. The alternative — overwriting — loses a turn that the run record
already reports as successful, with nothing anywhere saying so.

Over HTTP that surfaces as `409 Conflict` on both `/api/agents/{name}/run` (a
problem document whose `errorType` is `session_conflict`) and the OpenAI-compatible
`/v1/responses` (an error envelope whose `error.type` is the same value). **Retry the
turn**; the conflict means the conversation moved on, not that anything is broken. On
a streaming request the response headers are already sent, so the same condition
arrives as an `error` event in the stream instead of a status code.

Saving a session under a **different** id than it was read from — what
`/v1/responses` does when it chains with `previous_response_id` — writes a new record
rather than replacing the source one, so no generation applies and the write is
unconditional.

## Reading a conversation back

`GET /api/sessions/{sessionId}` returns metadata plus `messages` — but `messages` is
`null` when the configured storage cannot expose a readable history. With in-memory
storage the history lives inside an opaque provider blob; `state` always carries that
raw blob, and it is not a chat log.

With a SQL provider the history is stored as ordered items, so messages come back in
sequence order. That ordering is not cosmetic: the index of a message **is** the
sequence number the branch endpoint takes.

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
  valid and answers `200`. So a `404` means "not yours", not "never used" — an id
  owned by another tenant is reported as missing rather than forbidden, so the API
  does not confirm that it exists.
- `previous_response_id` and `conversation_id` are treated as untrusted input. Tenant
  ownership is verified on every use.

## Attachments

Attachments are uploaded independently and referenced from messages; the bytes live in
storage and only a small reference travels with a message. The upload's type is
decided by inspecting its magic bytes, not by the `Content-Type` the client claims.

Deleting a session deletes the attachments it owns. You can also call
`DELETE /api/attachments/{id}` for one attachment, and the orphan-attachment retention
target cleans uploads that never become part of a session. Individual deletion is a
hard delete: an older message that still contains the reference will no longer be able
to download the bytes. The link is deliberately not a database foreign key because an
upload can exist before its session does.

Downloads are served with `Content-Disposition: attachment` and
`X-Content-Type-Options: nosniff` together, so uploaded HTML can never execute in the
console's origin.

## Read next

- [Runs and recording](/concepts/runs/)
- [Attachments and multimodal input](/guides/multimodal/)
- [Workflows](/concepts/workflows/)
